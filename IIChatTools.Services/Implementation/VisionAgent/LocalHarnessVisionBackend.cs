using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Локальный backend Vision Agent — управляет текущей Windows-машиной
    /// через GDI-скриншоты (<c>System.Drawing.Common</c>), <c>SendInput</c> для
    /// мыши/клавиатуры и Win32 P/Invoke для whitelist процессов.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф2.1-Ф2.2). См. DESIGN § 3.1, § 7.2.
    /// </para>
    /// <para>
    /// <b>Платформа:</b> только Windows. Использует GDI (через
    /// <c>System.Drawing.Common</c>) + Win32 P/Invoke (<c>SendInput</c>,
    /// <c>GetSystemMetrics</c>). Атрибут <see cref="SupportedOSPlatformAttribute"/>
    /// отключает CA1416 — анализатор корректно понимает, что класс
    /// Windows-only, и не требует явных проверок <c>IsOSPlatform</c> в каждом
    /// методе.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class LocalHarnessVisionBackend : IVisionBackend
    {
        /// <inheritdoc />
        public string Name => "local-harness";

        private readonly VisionAgentOptions _options;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LocalHarnessVisionBackend> _logger;

        /// <summary>
        /// Идентификатор экземпляра backend'а (для имени Chrome-профиля).
        /// Генерируется один раз при создании (Scoped → на каждую задачу).
        /// </summary>
        private readonly string _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>Запущенный процесс Chrome / Edge. Null, если браузер не открыт.</summary>
        private Process _chromeProcess;

        /// <summary>Путь к временному Chrome-профилю (fresh, без cookies / паролей).</summary>
        private string _chromeProfileDir;

        /// <summary>
        /// Создаёт backend.
        /// </summary>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent</c>).</param>
        /// <param name="configuration">Конфигурация приложения (для <c>BrowserLocator.Resolve</c>).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null.</exception>
        public LocalHarnessVisionBackend(
            IOptions<VisionAgentOptions> options,
            IConfiguration configuration,
            ILogger<LocalHarnessVisionBackend> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ============================================================
        // IVisionBackend — lifecycle (Ф2.3+)
        // ============================================================

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.3): проверяет домен по whitelist, запускает
        /// Chrome / Edge с fresh-профилем (<c>--user-data-dir</c> в
        /// <c>%TEMP%\vision-profile-{instanceId}</c>), ждёт стабилизации.
        /// </remarks>
        public async Task OpenAsync(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL не может быть пустым.", nameof(url));
            }

            // 1. Whitelist-проверка.
            if (!VisionWhitelistValidator.IsAllowed(url, _options.Whitelist, out var error))
            {
                _logger.LogWarning("VisionAgent: OpenAsync отклонён — {Error}", error);
                throw new InvalidOperationException(error);
            }

            // 2. Если Chrome уже запущен — закрываем (одна задача — один браузер).
            if (_chromeProcess != null && !_chromeProcess.HasExited)
            {
                CloseBrowser();
            }

            // 3. Резолвим Chrome / Edge (переиспользуем BrowserLocator).
            var browserPath = BrowserLocator.Resolve(_configuration);
            if (string.IsNullOrEmpty(browserPath))
            {
                throw new InvalidOperationException(
                    "Не найден Chromium-совместимый браузер (Edge или Chrome). " +
                    "Укажите путь в Browser:ExecutablePath.");
            }

            // 4. Fresh-профиль в %TEMP%.
            _chromeProfileDir = Path.Combine(
                Path.GetTempPath(),
                $"vision-profile-{_instanceId}");
            Directory.CreateDirectory(_chromeProfileDir);

            // 5. Аргументы запуска.
            // ВАЖНО: --start-maximized для более стабильного viewport.
            // --disable-blink-features=AutomationControlled — анти-детект.
            // БЕЗ --headless (мы видим окно и управляем им).
            var args = new List<string>
            {
                $"--user-data-dir={_chromeProfileDir}",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-blink-features=AutomationControlled",
                "--disable-features=Translate,OptimizationHints",
                "--start-maximized",
                url
            };

            var psi = new ProcessStartInfo
            {
                FileName = browserPath,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            _chromeProcess = Process.Start(psi);
            _logger.LogInformation(
                "VisionAgent: Chrome запущен (pid={Pid}, url={Url}, profile={Profile})",
                _chromeProcess?.Id ?? 0, url, _chromeProfileDir);

            // 6. Пауза на первичную загрузку страницы (PageStabilityCheckMs × 4).
            // Полноценное ожидание «страница готова» — Ф2.9 (через GetForegroundWindow
            // + win-title). Пока — консервативная пауза.
            var waitMs = Math.Max(1000, _options.Limits.PageStabilityCheckMs * 4);
            await Task.Delay(waitMs, cancellationToken).ConfigureAwait(false);
        }

        // ============================================================
        // IVisionBackend — screenshot (Ф2.4)
        // ============================================================

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.4): GDI-скриншот всего экрана (через
        /// <c>Graphics.CopyFromScreen</c>), сохранение в PNG-байты.
        /// Downscale до <c>MaxImageWidth × MaxImageHeight</c> — Ф2.8.
        /// На Linux бросает <c>PlatformNotSupportedException</c> (System.Drawing.Common
        /// deprecated .NET 7+ — работает только на Windows).
        /// </remarks>
        public Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Размеры экрана.
            var width = GetSystemMetrics(SM_CXSCREEN);
            var height = GetSystemMetrics(SM_CYSCREEN);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(
                    $"Некорректные размеры экрана: {width}×{height}.");
            }

            // 2. Захват экрана через GDI.
            byte[] pngBytes;
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(
                        sourceX: 0, sourceY: 0,
                        destinationX: 0, destinationY: 0,
                        blockRegionSize: new Size(width, height),
                        copyPixelOperation: CopyPixelOperation.SourceCopy);
                }

                // 3. Кодируем в PNG.
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                pngBytes = ms.ToArray();
            }

            // 4. Проверка лимита.
            if (pngBytes.Length > _options.Limits.MaxScreenshotBytes)
            {
                throw new InvalidOperationException(
                    $"Скриншот {pngBytes.Length} байт превышает лимит " +
                    $"{_options.Limits.MaxScreenshotBytes} байт.");
            }

            _logger.LogDebug(
                "VisionAgent: скриншот {W}×{H}, {Bytes} байт",
                width, height, pngBytes.Length);

            return Task.FromResult(pngBytes);
        }

        /// <summary>Получение метрики системы (Win32 <c>GetSystemMetrics</c>).</summary>
        private const int SM_CXSCREEN = 0;
        /// <summary>Получение метрики системы (Win32 <c>GetSystemMetrics</c>).</summary>
        private const int SM_CYSCREEN = 1;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        /// <summary>
        /// Закрывает запущенный Chrome и удаляет временный профиль.
        /// v1.12.0 (KI-131, Ф2.3).
        /// </summary>
        private void CloseBrowser()
        {
            try
            {
                if (_chromeProcess != null && !_chromeProcess.HasExited)
                {
                    // Kill(true) — закрывает всё дерево процессов Chrome.
                    _chromeProcess.Kill(entireProcessTree: true);
                    _chromeProcess.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VisionAgent: ошибка закрытия Chrome");
            }
            finally
            {
                _chromeProcess?.Dispose();
                _chromeProcess = null;
            }

            try
            {
                if (!string.IsNullOrEmpty(_chromeProfileDir) && Directory.Exists(_chromeProfileDir))
                {
                    Directory.Delete(_chromeProfileDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VisionAgent: не удалось удалить профиль {Profile}", _chromeProfileDir);
            }
            finally
            {
                _chromeProfileDir = null;
            }
        }

        // ============================================================
        // Mouse helpers (v1.12.0, KI-131, Ф2.5)
        // ============================================================

        /// <summary>
        /// Отправляет Move+LeftClick или Move+RightClick.
        /// </summary>
        /// <param name="x">X в пикселях.</param>
        /// <param name="y">Y в пикселях.</param>
        /// <param name="rightClick">Правая кнопка (контекстное меню) или левая.</param>
        private void SendMouseClick(int x, int y, bool rightClick)
        {
            var (nx, ny) = NormalizeCoordinates(x, y);

            var downFlag = rightClick
                ? Win32Interop.MOUSEEVENTF_RIGHTDOWN
                : Win32Interop.MOUSEEVENTF_LEFTDOWN;
            var upFlag = rightClick
                ? Win32Interop.MOUSEEVENTF_RIGHTUP
                : Win32Interop.MOUSEEVENTF_LEFTUP;

            // Move (absolute) + Down + Up — три события за один SendInput call.
            var inputs = new[]
            {
                MakeMouseInput(nx, ny, Win32Interop.MOUSEEVENTF_MOVE | Win32Interop.MOUSEEVENTF_ABSOLUTE),
                MakeMouseInput(nx, ny, downFlag),
                MakeMouseInput(nx, ny, upFlag)
            };

            SendInputBatch(inputs);
        }

        /// <summary>
        /// Отправляет только Move (без клика).
        /// </summary>
        private void SendMouseMove(int x, int y)
        {
            var (nx, ny) = NormalizeCoordinates(x, y);
            var inputs = new[]
            {
                MakeMouseInput(nx, ny, Win32Interop.MOUSEEVENTF_MOVE | Win32Interop.MOUSEEVENTF_ABSOLUTE)
            };
            SendInputBatch(inputs);
        }

        /// <summary>
        /// Создаёт INPUT с MOUSEINPUT.
        /// </summary>
        private static Win32Interop.INPUT MakeMouseInput(int nx, int ny, uint flags)
        {
            return new Win32Interop.INPUT
            {
                type = Win32Interop.INPUT_MOUSE,
                U = new Win32Interop.InputUnion
                {
                    mi = new Win32Interop.MOUSEINPUT
                    {
                        dx = nx,
                        dy = ny,
                        mouseData = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        /// <summary>
        /// Отправляет массив INPUT в <c>SendInput</c>. Проверяет результат.
        /// </summary>
        private void SendInputBatch(Win32Interop.INPUT[] inputs)
        {
            var sent = Win32Interop.SendInput(
                (uint)inputs.Length,
                inputs,
                System.Runtime.InteropServices.Marshal.SizeOf<Win32Interop.INPUT>());

            if (sent != inputs.Length)
            {
                var err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                _logger.LogWarning(
                    "VisionAgent: SendInput отправил {Sent}/{Total} (win32err={Err})",
                    sent, inputs.Length, err);
                throw new InvalidOperationException(
                    $"SendInput: отправлено {sent} из {inputs.Length} (Win32 error {err}).");
            }
        }

        /// <summary>
        /// Нормализует пиксельные координаты в 0..65535 для Win32 SendInput.
        /// </summary>
        private static (int nx, int ny) NormalizeCoordinates(int x, int y)
        {
            var screenWidth = Win32Interop.GetSystemMetrics(Win32Interop.SM_CXSCREEN);
            var screenHeight = Win32Interop.GetSystemMetrics(Win32Interop.SM_CYSCREEN);
            return VisionMouseCoordinates.NormalizeToAbsolute(x, y, screenWidth, screenHeight);
        }

        // ============================================================
        // IVisionBackend — mouse (Ф2.5)
        // ============================================================

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.5): <c>SendInput</c> — Move (absolute) + LeftDown + LeftUp.
        /// </remarks>
        public Task ClickAsync(int x, int y, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendMouseClick(x, y, rightClick: false);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.5): два клика с паузой 50 мс (порог double-click Windows).
        /// </remarks>
        public async Task DoubleClickAsync(int x, int y, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendMouseClick(x, y, rightClick: false);
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            SendMouseClick(x, y, rightClick: false);
        }

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.5): Move + RightDown + RightUp.
        /// </remarks>
        public Task RightClickAsync(int x, int y, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendMouseClick(x, y, rightClick: true);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.5): только Move без клика (hover).
        /// </remarks>
        public Task MoveMouseAsync(int x, int y, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendMouseMove(x, y);
            return Task.CompletedTask;
        }

        // ============================================================
        // IVisionBackend — keyboard (Ф2.6)
        // ============================================================

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.6): <c>SendInput</c> с юникод-сканированием
        /// (KEYEVENTF_UNICODE) — работает для кириллицы / эмодзи.
        /// </remarks>
        public Task TypeAsync(string text, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "LocalHarnessVisionBackend.TypeAsync — реализация в Ф2.6 (DESIGN § 7.2).");
        }

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.6): маппинг имени (<c>Enter</c>, <c>Tab</c>, ...)
        /// в virtual-key code + <c>SendInput</c>.
        /// </remarks>
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "LocalHarnessVisionBackend.PressKeyAsync — реализация в Ф2.6 (DESIGN § 7.2).");
        }

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.6): последовательное нажатие модификаторов +
        /// финальной клавиши (<c>Ctrl+C</c>, <c>Alt+F4</c>).
        /// </remarks>
        public Task HotkeyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "LocalHarnessVisionBackend.HotkeyAsync — реализация в Ф2.6 (DESIGN § 7.2).");
        }

        // ============================================================
        // IVisionBackend — scroll / wait (Ф2.7)
        // ============================================================

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.7): <c>SendInput</c> с
        /// <c>MOUSEEVENTF_WHEEL</c> (deltaY &gt; 0 — вниз / &lt; 0 — вверх).
        /// </remarks>
        public Task ScrollAsync(int deltaY, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "LocalHarnessVisionBackend.ScrollAsync — реализация в Ф2.7 (DESIGN § 7.2).");
        }

        /// <inheritdoc />
        public Task WaitAsync(int milliseconds, CancellationToken cancellationToken = default)
        {
            if (milliseconds <= 0) return Task.CompletedTask;

            // Реализация доступна сразу — простая пауза.
            // Используется в основном loop'е Vision Agent для ожидания
            // стабилизации страницы после действия.
            return Task.Delay(milliseconds, cancellationToken);
        }

        // ============================================================
        // IAsyncDisposable
        // ============================================================

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            // v1.12.0 (KI-131, Ф2.3): закрываем Chrome + чистим временный профиль.
            CloseBrowser();
            return ValueTask.CompletedTask;
        }
    }
}