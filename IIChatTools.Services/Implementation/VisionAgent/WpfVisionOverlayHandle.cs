using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Handle одного экземпляра overlay'я. Связывает основной процесс с
    /// <c>IIChatTools.VisionOverlay.exe</c> через NamedPipe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.7). См. DESIGN § 4.6, § 6.4.
    /// </para>
    /// <para>
    /// <b>Потокобезопасность:</b>
    /// <list type="bullet">
    ///   <item>запись в pipe (progress / final_status / close) — через <see cref="SemaphoreSlim"/>;</item>
    ///   <item><see cref="StopToken"/> — резолвится из <c>CancellationTokenSource</c>,</item>
    ///   <item>фоновый <c>ReadLoop</c> — единственный читатель pipe.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal sealed class WpfVisionOverlayHandle : IVisionOverlayHandle
    {
        /// <summary>Кодировка pipe — UTF-8 без BOM (совпадает с overlay-сервером).</summary>
        private static readonly Encoding PipeEncoding = new UTF8Encoding(false);

        /// <summary>
        /// Опции JSON (camelCase, case-insensitive, omit-null).
        /// Должны совпадать с <c>OverlayJson.Options</c> в overlay-проекте.
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private readonly Process _process;
        private readonly NamedPipeClientStream _pipe;
        private readonly string _taskId;
        private readonly ILogger _logger;

        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();

        private Task _readLoopTask;
        private volatile bool _disposed;

        /// <summary>
        /// Создаёт handle. Запускает фоновый ReadLoop.
        /// </summary>
        /// <param name="process">Процесс overlay (уже запущен).</param>
        /// <param name="pipe">Подключённый <see cref="NamedPipeClientStream"/>.</param>
        /// <param name="taskId">Нормализованный taskId (для логов).</param>
        /// <param name="logger">Логгер родительского класса.</param>
        /// <exception cref="ArgumentNullException">Если параметр null.</exception>
        public WpfVisionOverlayHandle(
            Process process,
            NamedPipeClientStream pipe,
            string taskId,
            ILogger logger)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
            _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
            _taskId = taskId ?? string.Empty;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _writer = new StreamWriter(_pipe, PipeEncoding, 4096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            _reader = new StreamReader(_pipe, PipeEncoding, false, 4096, leaveOpen: true);

            // Фоновый ReadLoop: слушает 'stop' от overlay.
            _readLoopTask = Task.Run(ReadLoopAsync);
        }

        /// <inheritdoc />
        public CancellationToken StopToken => _stopCts.Token;

        /// <inheritdoc />
        public void UpdateProgress(int stepIndex, int maxSteps, string action)
        {
            if (_disposed) return;
            SendCommand(new
            {
                type = "progress",
                stepIndex,
                maxSteps,
                action = action ?? string.Empty
            });
        }

        /// <inheritdoc />
        public void SetFinalStatus(string summary, bool success)
        {
            if (_disposed) return;
            SendCommand(new
            {
                type = "final_status",
                summary = summary ?? string.Empty,
                success
            });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 1. Отправить close (best-effort).
            SendCommand(new { type = "close" });

            // 2. Разбудить ReadLoop — ждать завершения (коротко).
            try { _reader?.Dispose(); } catch { /* ignore */ }

            // 3. Дождаться exit процесса (до ProcessExitTimeoutMs).
            try
            {
                if (!_process.WaitForExit(2000))
                {
                    // Не успел — kill (например, пользователь не нажал OK / висит).
                    try
                    {
                        if (!_process.HasExited)
                        {
                            _process.Kill(entireProcessTree: true);
                        }
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }

            // 4. Освободить ресурсы.
            try { _writer?.Dispose(); } catch { /* ignore */ }
            try { _pipe?.Dispose(); } catch { /* ignore */ }
            try { _process?.Dispose(); } catch { /* ignore */ }
            try { _stopCts?.Dispose(); } catch { /* ignore */ }
            try { _writeLock?.Dispose(); } catch { /* ignore */ }

            _logger.LogDebug(
                "WpfVisionOverlayHandle: dispose завершён (taskId={TaskId})", _taskId);
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Фоновый цикл чтения pipe: слушает <c>stop</c> от overlay.
        /// </summary>
        private async Task ReadLoopAsync()
        {
            try
            {
                while (!_disposed)
                {
                    var line = await _reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) return;   // EOF — overlay закрылся.

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    OverlayClientCommand cmd;
                    try
                    {
                        cmd = JsonSerializer.Deserialize<OverlayClientCommand>(line, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (cmd?.Type == null) continue;

                    if (string.Equals(cmd.Type, "stop", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation(
                            "WpfVisionOverlayHandle: STOP от overlay (taskId={TaskId})",
                            _taskId);

                        try { _stopCts.Cancel(); }
                        catch (ObjectDisposedException) { /* Dispose уже был */ }

                        return;   // stop — финальная команда от клиента.
                    }

                    // Остальные команды (progress / final_status от overlay) не ожидаем.
                    _logger.LogDebug(
                        "WpfVisionOverlayHandle: неожидаемая команда от overlay: {Type}",
                        cmd.Type);
                }
            }
            catch (ObjectDisposedException)
            {
                // Dispose во время чтения — нормальное завершение.
            }
            catch (IOException ex)
            {
                if (!_disposed)
                {
                    _logger.LogDebug(ex,
                        "WpfVisionOverlayHandle: pipe закрыт (taskId={TaskId})", _taskId);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    _logger.LogWarning(ex,
                        "WpfVisionOverlayHandle: ошибка ReadLoop (taskId={TaskId})", _taskId);
                }
            }
        }

        /// <summary>
        /// Отправляет JSON-команду в overlay. Потокобезопасно.
        /// </summary>
        private void SendCommand(object command)
        {
            if (_writer == null) return;

            try
            {
                var json = JsonSerializer.Serialize(command, JsonOptions);

                _writeLock.Wait();
                try
                {
                    _writer.WriteLine(json);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // Dispose во время отправки — нормально.
            }
            catch (IOException ex)
            {
                if (!_disposed)
                {
                    _logger.LogDebug(ex,
                        "WpfVisionOverlayHandle: pipe закрыт при отправке (taskId={TaskId})",
                        _taskId);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    _logger.LogWarning(ex,
                        "WpfVisionOverlayHandle: ошибка отправки команды (taskId={TaskId})",
                        _taskId);
                }
            }
        }

        /// <summary>
        /// Минимальный DTO для команд, приходящих **от overlay** (только <c>stop</c>).
        /// </summary>
        private sealed class OverlayClientCommand
        {
            public string Type { get; set; }
        }
    }
}