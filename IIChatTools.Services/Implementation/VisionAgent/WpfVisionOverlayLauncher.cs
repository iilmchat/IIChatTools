using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Launcher реального WPF-приложения «on-screen indicator» Vision Agent
    /// (<c>IIChatTools.VisionOverlay.exe</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.7). См. DESIGN § 4.6, § 6.4.
    /// </para>
    /// <para>
    /// <b>Архитектура:</b> overlay — отдельный процесс (WinExe, net10.0-windows).
    /// IPC — NamedPipe (<c>iichattools-vision-overlay-{taskId}</c>):
    /// overlay — сервер, API — клиент. Контракт — JSON-строки, разделитель <c>\n</c>.
    /// </para>
    /// <para>
    /// <b>Доступность:</b> <see cref="IsAvailable"/> = <c>true</c>, если ОС = Windows
    /// и <c>IIChatTools.VisionOverlay.exe</c> найден рядом с API-бинарём.
    /// На Linux / при отсутствии exe'шника возвращает <c>false</c> → DI регистрирует
    /// <c>NoopVisionOverlayLauncher</c> вместо этого класса.
    /// </para>
    /// <para>
    /// <b>Singleton.</b> Stateless — хранит только <c>ILogger</c>. Все ресурсы
    /// создаются на задачу в <see cref="StartAsync"/> и живут в
    /// <see cref="WpfVisionOverlayHandle"/>.
    /// </para>
    /// </remarks>
    public sealed class WpfVisionOverlayLauncher : IVisionOverlayLauncher
    {
        /// <summary>
        /// Имя exe'шника overlay (без пути). Должен совпадать с
        /// <c>AssemblyName</c> в <c>IIChatTools.VisionOverlay.csproj</c>.
        /// </summary>
        private const string OverlayExeName = "IIChatTools.VisionOverlay.exe";

        /// <summary>
        /// Префикс имени pipe. Совпадает с <c>OverlayPipeServer</c> overlay-проекта
        /// (см. <c>IIChatTools.VisionOverlay/Ipc/OverlayPipeServer.cs</c>).
        /// </summary>
        private const string PipeNamePrefix = "iichattools-vision-overlay-";

        /// <summary>
        /// Общее время ожидания подключения к pipe (мс). Overlay — WPF-процесс,
        /// стартует ~300-500 мс; 5 сек — комфортный запас.
        /// </summary>
        private const int PipeConnectTimeoutMs = 5000;

        /// <summary>Шаг retry при подключении к pipe (мс).</summary>
        private const int PipeConnectRetryStepMs = 100;

        /// <summary>Ожидание exit процесса overlay при Dispose (мс).</summary>
        private const int ProcessExitTimeoutMs = 2000;

        /// <summary>Максимум символов в taskId (совпадает с сервером overlay).</summary>
        private const int MaxTaskIdLength = 64;

        /// <summary>
        /// Нормализация taskId. Должна совпадать с <c>OverlayPipeServer.TaskIdSanitizer</c>
        /// в overlay-проекте — иначе имена pipe разъедутся.
        /// </summary>
        private static readonly Regex TaskIdSanitizer =
            new Regex("[^a-zA-Z0-9_-]", RegexOptions.Compiled);

        /// <summary>
        /// Опции JSON. Должны совпадать с <c>OverlayJson.Options</c> в overlay-проекте
        /// (camelCase, case-insensitive, omit-null).
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private readonly ILogger<WpfVisionOverlayLauncher> _logger;

        /// <summary>
        /// Создаёт launcher.
        /// </summary>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="logger"/> = null.</exception>
        public WpfVisionOverlayLauncher(ILogger<WpfVisionOverlayLauncher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsAvailable
        {
            get
            {
                if (!OperatingSystem.IsWindows()) return false;
                return File.Exists(ResolveOverlayExePath());
            }
        }

        /// <inheritdoc />
        public async Task<IVisionOverlayHandle> StartAsync(
            string taskId,
            int maxSteps,
            CancellationToken cancellationToken = default)
        {
            // 1. Проверка доступности (страховка — вызывающий уже проверил IsAvailable).
            var exePath = ResolveOverlayExePath();
            if (!OperatingSystem.IsWindows() || !File.Exists(exePath))
            {
                _logger.LogWarning(
                    "WpfVisionOverlayLauncher: overlay недоступен (OS={OS}, exe={Exe})",
                    Environment.OSVersion.Platform, exePath);
                return NoopVisionOverlayLauncher.NoopHandleFactory();
            }

            var safeTaskId = SanitizeTaskId(taskId);
            var pipeName = PipeNamePrefix + safeTaskId;

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Запуск процесса overlay.
            Process process;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
                };

                // RULES § 1.10: только ArgumentList, без конкатенации.
                psi.ArgumentList.Add("--task-id=" + safeTaskId);
                psi.ArgumentList.Add("--max-steps=" + maxSteps.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

                process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogWarning(
                        "WpfVisionOverlayLauncher: Process.Start вернул null (exe={Exe})",
                        exePath);
                    return NoopVisionOverlayLauncher.NoopHandleFactory();
                }

                _logger.LogDebug(
                    "WpfVisionOverlayLauncher: overlay запущен (pid={Pid}, taskId={TaskId})",
                    process.Id, safeTaskId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "WpfVisionOverlayLauncher: не удалось запустить overlay ({Exe})",
                    exePath);
                return NoopVisionOverlayLauncher.NoopHandleFactory();
            }

            // 3. Подключение к pipe (retry-loop).
            NamedPipeClientStream pipeClient = null;
            try
            {
                pipeClient = await ConnectWithRetryAsync(pipeName, cancellationToken)
                    .ConfigureAwait(false);

                if (pipeClient == null)
                {
                    _logger.LogWarning(
                        "WpfVisionOverlayLauncher: не удалось подключиться к pipe '{Pipe}' за {Timeout} мс",
                        pipeName, PipeConnectTimeoutMs);
                    TryKillProcess(process);
                    return NoopVisionOverlayLauncher.NoopHandleFactory();
                }

                _logger.LogDebug(
                    "WpfVisionOverlayLauncher: pipe '{Pipe}' подключён (pid={Pid})",
                    pipeName, process.Id);

                return new WpfVisionOverlayHandle(
                    process, pipeClient, safeTaskId, _logger);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                pipeClient?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "WpfVisionOverlayLauncher: ошибка подключения к pipe '{Pipe}'",
                    pipeName);
                TryKillProcess(process);
                pipeClient?.Dispose();
                return NoopVisionOverlayLauncher.NoopHandleFactory();
            }
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Возвращает абсолютный путь к <c>IIChatTools.VisionOverlay.exe</c>.
        /// Ищет в подпапке <c>VisionOverlay/</c> рядом с основным бинарём
        /// (см. Target <c>CopyVisionOverlayToOutput</c> в IIChatTools.API.csproj).
        /// </summary>
        private static string ResolveOverlayExePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "VisionOverlay", OverlayExeName);
        }

        /// <summary>
        /// Подключается к pipe с retry (шаг <see cref="PipeConnectRetryStepMs"/>,
        /// общий таймаут <see cref="PipeConnectTimeoutMs"/>). Возвращает <c>null</c>
        /// при исчерпании таймаута.
        /// </summary>
        private static async Task<NamedPipeClientStream> ConnectWithRetryAsync(
            string pipeName,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(PipeConnectTimeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                NamedPipeClientStream client = null;
                try
                {
                    client = new NamedPipeClientStream(
                        serverName: ".",
                        pipeName: pipeName,
                        direction: PipeDirection.InOut,
                        options: PipeOptions.Asynchronous);

                    // Ждём минимум 100 мс на попытку (или до конца дедлайна).
                    var waitMs = (int)Math.Min(
                        PipeConnectRetryStepMs,
                        Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));

                    client.Connect(waitMs);
                    return client;
                }
                catch (TimeoutException)
                {
                    // Overlay ещё не поднял pipe-сервер — ждём и пробуем снова.
                    client?.Dispose();
                }
                catch (IOException)
                {
                    // pipe существует, но busy / не готов — тоже retry.
                    client?.Dispose();
                }
                catch
                {
                    client?.Dispose();
                    throw;
                }

                try
                {
                    await Task.Delay(PipeConnectRetryStepMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }

            return null;
        }

        /// <summary>
        /// Пытается kill'нуть процесс overlay без выброса исключений.
        /// </summary>
        private static void TryKillProcess(Process process)
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { /* ignore */ }
            finally
            {
                try { process.Dispose(); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Нормализует taskId (только <c>[a-zA-Z0-9_-]</c>, длина ≤ 64).
        /// Пустой → «unknown». Синхронизировано с overlay-сервером.
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