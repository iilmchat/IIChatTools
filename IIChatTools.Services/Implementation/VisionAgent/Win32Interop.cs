using System;
using System.Runtime.InteropServices;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Win32 P/Invoke для Vision Agent: <c>SendInput</c>, <c>GetSystemMetrics</c>.
    /// Структуры INPUT / MOUSEINPUT / KEYBDINPUT / HARDWAREINPUT.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф2.5). См. DESIGN § 7.2.
    /// Внутренний класс — не для публичного API. Используется
    /// <c>LocalHarnessVisionBackend</c> (мышь) и Ф2.6 (клавиатура).
    /// </remarks>
    internal static class Win32Interop
    {
        // ============================================================
        // GetSystemMetrics
        // ============================================================

        /// <summary>Primary screen width, px.</summary>
        public const int SM_CXSCREEN = 0;

        /// <summary>Primary screen height, px.</summary>
        public const int SM_CYSCREEN = 1;

        /// <summary>
        /// Возвращает системную метрику (размеры экрана и т.п.).
        /// </summary>
        [DllImport("user32.dll", SetLastError = false)]
        public static extern int GetSystemMetrics(int nIndex);

        // ============================================================
        // SendInput
        // ============================================================

        /// <summary>Тип события — mouse.</summary>
        public const uint INPUT_MOUSE = 0;

        /// <summary>Тип события — keyboard (используется в Ф2.6).</summary>
        public const uint INPUT_KEYBOARD = 1;

        // Mouse flags.
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        /// <summary>Прокрутка колеса мыши (v1.12.0, KI-131, Ф2.7).</summary>
        public const uint MOUSEEVENTF_WHEEL = 0x0800;

        /// <summary>
        /// Один «щелчок» колеса в Win32. mouseData в MOUSEEVENTF_WHEEL
        /// должен быть кратен этой величине. v1.12.0 (KI-131, Ф2.7).
        /// </summary>
        public const int WHEEL_DELTA = 120;

        // Keyboard flags (v1.12.0, KI-131, Ф2.6).
        /// <summary>Клавиша была нажата (KeyUp), а не опущена (KeyDown).</summary>
        public const uint KEYEVENTF_KEYUP = 0x0002;
        /// <summary>Символ передаётся в <c>wScan</c> как Unicode (для не-ASCII ввода).</summary>
        public const uint KEYEVENTF_UNICODE = 0x0004;

        /// <summary>
        /// Отправляет массив событий INPUT в систему (мышь / клавиатура).
        /// Возвращает число успешно обработанных событий (0 при ошибке).
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(
            uint nInputs,
            [In] INPUT[] pInputs,
            int cbSize);

        // ============================================================
        // Struct definitions (для SendInput)
        // ============================================================

        /// <summary>
        /// Обёртка события для <c>SendInput</c>. Размер на x64 = 40 байт.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            /// <summary>Тип события: <c>INPUT_MOUSE</c> / <c>INPUT_KEYBOARD</c>.</summary>
            public uint type;

            /// <summary>Union: mouse / keyboard / hardware.</summary>
            public InputUnion U;
        }

        /// <summary>
        /// Union всех вариантов INPUT (перекрытие по offset 0).
        /// C# не поддерживает union напрямую, используем <see cref="FieldOffsetAttribute"/>.
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            /// <summary>Событие мыши.</summary>
            [FieldOffset(0)]
            public MOUSEINPUT mi;

            /// <summary>Событие клавиатуры (Ф2.6).</summary>
            [FieldOffset(0)]
            public KEYBDINPUT ki;

            /// <summary>Hardware-событие (не используется).</summary>
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        /// <summary>Событие мыши (<c>SendInput</c>).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            /// <summary>Абсолютная X (если <c>MOUSEEVENTF_ABSOLUTE</c>), иначе — delta.</summary>
            public int dx;
            /// <summary>Абсолютная Y (если <c>MOUSEEVENTF_ABSOLUTE</c>), иначе — delta.</summary>
            public int dy;
            /// <summary>Данные (wheel delta для scroll — Ф2.7).</summary>
            public uint mouseData;
            /// <summary>Флаги (<c>MOUSEEVENTF_*</c>).</summary>
            public uint dwFlags;
            /// <summary>Timestamp (0 = системное время).</summary>
            public uint time;
            /// <summary>Extra info (не используется).</summary>
            public IntPtr dwExtraInfo;
        }

        /// <summary>Событие клавиатуры (<c>SendInput</c>).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            /// <summary>Virtual-key code.</summary>
            public ushort wVk;
            /// <summary>Scan code (если <c>KEYEVENTF_UNICODE</c> — Unicode-символ).</summary>
            public ushort wScan;
            /// <summary>Флаги (<c>KEYEVENTF_*</c>).</summary>
            public uint dwFlags;
            /// <summary>Timestamp.</summary>
            public uint time;
            /// <summary>Extra info.</summary>
            public IntPtr dwExtraInfo;
        }

        /// <summary>Hardware-событие (не используется, нужен для union).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            /// <summary>Сообщение.</summary>
            public uint uMsg;
            /// <summary>Param low.</summary>
            public ushort wParamL;
            /// <summary>Param high.</summary>
            public ushort wParamH;
        }
    }
}