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

            // 4. Downscale до MaxImageWidth × MaxImageHeight (v1.12.0, KI-131, Ф2.8).
            //    VL-модель не нуждается в полном 4K-скриншоте — 1024×768 достаточно,
            //    и это критично снижает размер payload'а (base64 в HTTP).
            var beforeBytes = pngBytes.Length;
            pngBytes = VisionImageResizer.Resize(
                pngBytes,
                _options.VisionLlm.MaxImageWidth,
                _options.VisionLlm.MaxImageHeight);

            if (pngBytes.Length != beforeBytes)
            {
                _logger.LogDebug(
                    "VisionAgent: downscale {Before} → {After} байт",
                    beforeBytes, pngBytes.Length);
            }

            // 5. Проверка лимита (после downscale — на всякий случай).
            if (pngBytes.Length > _options.Limits.MaxScreenshotBytes)
            {
                throw new InvalidOperationException(
                    $"Скриншот {pngBytes.Length} байт превышает лимит " +
                    $"{_options.Limits.MaxScreenshotBytes} байт даже после downscale.");
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
        // Whitelist processes (v1.12.0, KI-131, Ф2.9)
        // ============================================================

        /// <summary>
        /// Проверяет, что процесс в фокусе — в whitelist
        /// (<c>VisionAgent:Backend:Local:AllowedProcesses</c>).
        /// Бросает <see cref="InvalidOperationException"/> при нарушении.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Вызывается <b>перед</b> каждым mouse / keyboard действием.
        /// Не вызывается перед <see cref="ScreenshotAsync"/> — read-only.
        /// </para>
        /// <para>
        /// Пропускается если:
        /// <list type="bullet">
        ///   <item><c>AllowNonBrowserProcesses = true</c> — dev-режим (DESIGN § 5.1);</item>
        ///   <item>whitelist не задан / пустой (DESIGN § 5.1);</item>
        ///   <item>нет foreground-окна (<c>GetForegroundWindow = 0</c>) — рабочий стол;</item>
        ///   <item>процесс не удалось получить (<c>Process.GetProcessById</c> упал).</item>
        /// </list>
        /// </para>
        /// </remarks>
        private void EnsureForegroundProcessAllowed()
        {
            // Dev-bypass: AllowNonBrowserProcesses = true → игнорируем whitelist.
            var localOptions = _options.Backend?.Local;
            if (localOptions != null && localOptions.AllowNonBrowserProcesses)
            {
                return;
            }

            var allowed = localOptions?.AllowedProcesses;
            if (allowed == null || allowed.Count == 0)
            {
                return;   // whitelist не задан — без ограничений.
            }

            var hwnd = Win32Interop.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;   // фокуса нет — не блокируем.

            Win32Interop.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return;

            string processName;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch
            {
                // Процесс завершился между вызовами — не блокируем.
                return;
            }

            if (!VisionProcessWhitelistChecker.IsProcessAllowed(processName, allowed, out var error))
            {
                _logger.LogWarning("VisionAgent: {Error}", error);
                throw new InvalidOperationException(error);
            }
        }

        // ============================================================
        // Keyboard helpers (v1.12.0, KI-131, Ф2.6)
        // ============================================================

        /// <summary>
        /// Отправляет Down или Up для заданного VK-кода (обычная клавиша).
        /// </summary>
        /// <param name="vkCode">Virtual-key code.</param>
        /// <param name="up">true — KeyUp, false — KeyDown.</param>
        private void SendVirtualKey(ushort vkCode, bool up)
        {
            var flags = up ? Win32Interop.KEYEVENTF_KEYUP : 0u;
            var inputs = new[]
            {
                new Win32Interop.INPUT
                {
                    type = Win32Interop.INPUT_KEYBOARD,
                    U = new Win32Interop.InputUnion
                    {
                        ki = new Win32Interop.KEYBDINPUT
                        {
                            wVk = vkCode,
                            wScan = 0,
                            dwFlags = flags,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                }
            };
            SendInputBatch(inputs);
        }

        /// <summary>
        /// Отправляет Unicode-символ (Down + Up) через <c>KEYEVENTF_UNICODE</c>.
        /// Работает для BMP и surrogate pairs (2 символа UTF-16 = 2 пары Down/Up).
        /// </summary>
        /// <param name="ch">Символ (UTF-16 code unit).</param>
        private void SendUnicodeChar(char ch)
        {
            var down = new Win32Interop.INPUT
            {
                type = Win32Interop.INPUT_KEYBOARD,
                U = new Win32Interop.InputUnion
                {
                    ki = new Win32Interop.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = Win32Interop.KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            var up = new Win32Interop.INPUT
            {
                type = Win32Interop.INPUT_KEYBOARD,
                U = new Win32Interop.InputUnion
                {
                    ki = new Win32Interop.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = Win32Interop.KEYEVENTF_UNICODE | Win32Interop.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInputBatch(new[] { down, up });
        }

        /// <summary>
        /// Отправляет прокрутку колеса мыши (<c>MOUSEEVENTF_WHEEL</c>).
        /// v1.12.0 (KI-131, Ф2.7).
        /// </summary>
        /// <param name="deltaY">Желаемое значение (+ = вниз, − = вверх), в единицах WHEEL_DELTA.</param>
        private void SendMouseWheel(int deltaY)
        {
            // Defense in depth: основной clamp — в IVisionActionValidator (Ф5),
            // но и backend не даёт отправить абсурдно большие значения.
            var maxAbs = _options.ActionValidation?.MaxScrollDelta ?? 2000;
            var clamped = VisionScrollHelper.Clamp(deltaY, maxAbs);
            if (clamped == 0) return;

            // Win32 mouseData в WHEEL: положительный = scroll up (content down),
            // отрицательный = scroll down. User-facing deltaY: + = scroll down.
            // ToWheelMouseData инвертирует знак.
            var mouseData = VisionScrollHelper.ToWheelMouseData(clamped);

            var inputs = new[]
            {
                new Win32Interop.INPUT
                {
                    type = Win32Interop.INPUT_MOUSE,
                    U = new Win32Interop.InputUnion
                    {
                        mi = new Win32Interop.MOUSEINPUT
                        {
                            dx = 0,
                            dy = 0,
                            mouseData = unchecked((uint)mouseData),
                            dwFlags = Win32Interop.MOUSEEVENTF_WHEEL,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                }
            };
            SendInputBatch(inputs);
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
            EnsureForegroundProcessAllowed();
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
            EnsureForegroundProcessAllowed();
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
            EnsureForegroundProcessAllowed();
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
            EnsureForegroundProcessAllowed();
            SendMouseMove(x, y);
            return Task.CompletedTask;
        }

        // ============================================================
        // IVisionBackend — keyboard (Ф2.6)
        // ============================================================

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.6): Unicode через <c>KEYEVENTF_UNICODE</c> —
        /// работает для кириллицы / эмодзи / любых символов BMP и surrogate pairs.
        /// Перенос строки <c>\n</c> конвертируется в VK_RETURN (Down+Up).
        /// </remarks>
        public async Task TypeAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Проверка whitelist один раз перед всем вводом (не на каждый char).
            EnsureForegroundProcessAllowed();

            // Разбиваем по \n — между ними посылаем VK_RETURN.
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    // Перенос строки.
                    SendVirtualKey(0x0D, up: false);
                    SendVirtualKey(0x0D, up: true);
                }

                // Убираем \r в конце строки (если был \r\n).
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0) continue;

                // Каждый символ UTF-16 — Down + Up через KEYEVENTF_UNICODE.
                foreach (var ch in line)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SendUnicodeChar(ch);
                }
            }

            // Микро-пауза, чтобы приложение успело обработать очередь (не обязательно).
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.6): маппинг имени (<c>Enter</c>, <c>Tab</c>, <c>F5</c>, ...)
        /// в VK-код через <see cref="VisionKeyMapper"/>, затем Down + Up.
        /// </remarks>
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureForegroundProcessAllowed();

            var vk = VisionKeyMapper.GetVirtualKey(key);
            if (vk == null)
            {
                throw new ArgumentException(
                    $"Неизвестное имя клавиши: «{key}». " +
                    "Примеры: Enter, Tab, Escape, F1-F12, A-Z, 0-9, Left, Up, ...",
                    nameof(key));
            }

            SendVirtualKey(vk.Value, up: false);
            SendVirtualKey(vk.Value, up: true);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.6): <c>["Ctrl", "C"]</c> → Down(Ctrl) → Down(C) → Up(C) → Up(Ctrl).
        /// Последний элемент — финальная клавиша, все предыдущие — модификаторы.
        /// </remarks>
        public Task HotkeyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (keys == null || keys.Count == 0)
            {
                throw new ArgumentException("Список клавиш пустой.", nameof(keys));
            }

            if (keys.Count == 1)
            {
                // Один элемент — обычное нажатие клавиши (whitelist там же).
                return PressKeyAsync(keys[0], cancellationToken);
            }

            // Whitelist-проверка (для multi-key hotkey).
            EnsureForegroundProcessAllowed();

            // Все, кроме последнего — модификаторы.
            var modifiers = new List<ushort>();
            for (int i = 0; i < keys.Count - 1; i++)
            {
                var vk = VisionKeyMapper.GetModifierVirtualKey(keys[i]);
                if (vk == null)
                {
                    throw new ArgumentException(
                        $"«{keys[i]}» не является модификатором (ожидается Ctrl / Alt / Shift / Win).",
                        nameof(keys));
                }
                modifiers.Add(vk.Value);
            }

            // Последний элемент — финальная клавиша.
            var finalVk = VisionKeyMapper.GetVirtualKey(keys[keys.Count - 1]);
            if (finalVk == null)
            {
                throw new ArgumentException(
                    $"Неизвестное имя клавиши: «{keys[keys.Count - 1]}».",
                    nameof(keys));
            }

            // Down всех модификаторов.
            foreach (var m in modifiers) SendVirtualKey(m, up: false);
            // Down + Up финальной.
            SendVirtualKey(finalVk.Value, up: false);
            SendVirtualKey(finalVk.Value, up: true);
            // Up модификаторов (в обратном порядке).
            for (int i = modifiers.Count - 1; i >= 0; i--) SendVirtualKey(modifiers[i], up: true);

            return Task.CompletedTask;
        }

        // ============================================================
        // IVisionBackend — scroll / wait (Ф2.7)
        // ============================================================

        /// <inheritdoc />
        /// <remarks>
        /// v1.12.0 (KI-131, Ф2.7): <c>SendInput</c> с <c>MOUSEEVENTF_WHEEL</c>.
        /// deltaY в единицах <see cref="Win32Interop.WHEEL_DELTA"/> (120 = один щелчок).
        /// Значение clamp'ится к <c>[-MaxScrollDelta, MaxScrollDelta]</c>
        /// (defense in depth; основной валидатор — Ф5, IVisionActionValidator).
        /// </remarks>
        public Task ScrollAsync(int deltaY, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureForegroundProcessAllowed();
            SendMouseWheel(deltaY);
            return Task.CompletedTask;
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