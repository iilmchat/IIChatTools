using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.VisionOverlay.Ipc
{
    /// <summary>
    /// NamedPipe-сервер overlay. Слушает <c>iichattools-vision-overlay-{taskId}</c>,
    /// принимает команды от IIChatTools.API (<c>progress</c> / <c>final_status</c> /
    /// <c>close</c>) и применяет их к <see cref="MainWindow"/>.
    /// В обратную сторону отправляет <c>stop</c>, когда пользователь нажал STOP / ESC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.7). См. DESIGN § 4.6.
    /// </para>
    /// <para>
    /// <b>Один клиент.</b> <c>MaxAllowedServerInstances = 1</c>: overlay обслуживает
    /// одну задачу. Если API отключился и переподключился — сервер принимает
    /// новое соединение (внешний цикл).
    /// </para>
    /// <para>
    /// <b>Идемпотентный Dispose.</b> Безопасно вызывать несколько раз.
    /// </para>
    /// </remarks>
    internal sealed class OverlayPipeServer : IDisposable
    {
        /// <summary>Максимум символов в taskId (защита от абсурдно длинных имён pipe).</summary>
        private const int MaxTaskIdLength = 64;

        /// <summary>Whitelist символов в taskId. Всё остальное — в «_».</summary>
        private static readonly Regex TaskIdSanitizer =
            new Regex("[^a-zA-Z0-9_-]", RegexOptions.Compiled);

        /// <summary>Кодировка pipe — UTF-8 без BOM.</summary>
        private static readonly Encoding PipeEncoding = new UTF8Encoding(false);

        private readonly string _pipeName;
        private readonly MainWindow _window;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        private NamedPipeServerStream _server;
        private StreamWriter _writer;
        private volatile bool _disposed;

        /// <summary>
        /// Создаёт сервер.
        /// </summary>
        /// <param name="taskId">ID задачи. Нормализуется перед вставкой в имя pipe.</param>
        /// <param name="window">Окно overlay, к которому применяются команды.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="window"/> = null.</exception>
        public OverlayPipeServer(string taskId, MainWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));

            var safeTaskId = SanitizeTaskId(taskId);
            _pipeName = "iichattools-vision-overlay-" + safeTaskId;
        }

        /// <summary>
        /// Имя pipe (для логов / отладки).
        /// </summary>
        public string PipeName => _pipeName;

        /// <summary>
        /// Основной цикл: слушает подключения, читает команды, применяет к окну.
        /// Не бросает — все ошибки логируются в <see cref="Console"/> (у overlay нет ILogger).
        /// </summary>
        public async Task RunAsync()
        {
            // Внешний цикл: при потере клиента (API отключился) — ждём новое подключение.
            while (!_disposed)
            {
                try
                {
                    _server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    Console.WriteLine($"[overlay] pipe слушает: {_pipeName}");
                    await _server.WaitForConnectionAsync().ConfigureAwait(false);

                    if (_disposed)
                    {
                        break;
                    }

                    Console.WriteLine("[overlay] клиент подключён (API)");

                    using var reader = new StreamReader(_server, PipeEncoding, false, 4096, leaveOpen: true);
                    _writer = new StreamWriter(_server, PipeEncoding, 4096, leaveOpen: true)
                    {
                        AutoFlush = true,
                        NewLine = "\n"
                    };

                    await ReadLoopAsync(reader).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Dispose во время ожидания — нормальное завершение.
                    break;
                }
                catch (IOException ex)
                {
                    if (!_disposed)
                    {
                        Console.WriteLine($"[overlay] IO-ошибка pipe: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    if (!_disposed)
                    {
                        Console.WriteLine($"[overlay] неожиданная ошибка pipe: {ex.Message}");
                    }
                }
                finally
                {
                    CleanupConnection();
                }
            }
        }

        /// <summary>
        /// Отправляет команду <c>stop</c> в API-сторону (пользователь нажал STOP / ESC).
        /// Если клиент не подключён — no-op.
        /// </summary>
        public void TrySendStop()
        {
            if (_disposed) return;

            try
            {
                var json = OverlayJson.Serialize(new OverlayCommand { Type = "stop" });

                // Сериализуем запись: TrySendStop может вызываться из UI-потока,
                // а close — из pipe-loop'а.
                _writeLock.Wait();
                try
                {
                    _writer?.WriteLine(json);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[overlay] ошибка отправки stop: {ex.Message}");
            }
        }

        /// <summary>
        /// Закрывает сервер и освобождает ресурсы. Идемпотентно.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CleanupConnection();

            try { _writeLock.Dispose(); } catch { /* ignore */ }
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Читает команды построчно до EOF или <c>close</c>.
        /// </summary>
        private async Task ReadLoopAsync(StreamReader reader)
        {
            while (!_disposed)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                {
                    // EOF — клиент отключился (нормально при закрытии задачи).
                    Console.WriteLine("[overlay] клиент отключился (EOF)");
                    return;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                var cmd = OverlayJson.Deserialize<OverlayCommand>(line);
                if (cmd?.Type == null) continue;

                if (await ApplyCommandAsync(cmd).ConfigureAwait(false) == false)
                {
                    // Пришла close — выходим из цикла; окно закроется само.
                    return;
                }
            }
        }

        /// <summary>
        /// Применяет команду к окну. Возвращает <c>false</c>, если после неё
        /// нужно закрыть окно (команда <c>close</c>).
        /// </summary>
        private async Task<bool> ApplyCommandAsync(OverlayCommand cmd)
        {
            switch (cmd.Type)
            {
                case "progress":
                    try
                    {
                        _window.UpdateProgress(
                            cmd.StepIndex ?? 0,
                            cmd.MaxSteps ?? 0,
                            cmd.Action ?? string.Empty);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[overlay] progress error: {ex.Message}");
                    }
                    return true;

                case "final_status":
                    try
                    {
                        _window.SetFinalStatus(
                            cmd.Summary ?? (cmd.Success == true ? "Готово" : "Ошибка"),
                            cmd.Success ?? false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[overlay] final_status error: {ex.Message}");
                    }
                    // Даём пользователю ~800 мс увидеть финальный статус перед закрытием.
                    // Не блокируем pipe-loop: Task.Delay с ConfigureAwait(false).
                    await Task.Delay(800).ConfigureAwait(false);
                    return true;

                case "close":
                    Console.WriteLine("[overlay] получена команда close — закрываем окно");
                    try
                    {
                        _window.Dispatcher.Invoke(() =>
                        {
                            try { _window.Close(); }
                            catch { /* ignore */ }
                        });
                    }
                    catch { /* ignore */ }
                    return false;

                default:
                    Console.WriteLine($"[overlay] неизвестная команда: {cmd.Type}");
                    return true;
            }
        }

        /// <summary>
        /// Закрывает server + writer. Безопасно при любом состоянии.
        /// </summary>
        private void CleanupConnection()
        {
            try { _writer?.Dispose(); } catch { /* ignore */ }
            _writer = null;

            try { _server?.Dispose(); } catch { /* ignore */ }
            _server = null;
        }

        /// <summary>
        /// Нормализует taskId: только <c>[a-zA-Z0-9_-]</c>, длина ≤ 64.
        /// Пустой → «unknown».
        /// </summary>
        private static string SanitizeTaskId(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return "unknown";

            var trimmed = taskId.Trim();
            if (trimmed.Length > MaxTaskIdLength)
            {
                trimmed = trimmed.Substring(0, MaxTaskIdLength);
            }

            var sanitized = TaskIdSanitizer.Replace(trimmed, "_");
            return string.IsNullOrEmpty(sanitized) ? "unknown" : sanitized;
        }
    }
}