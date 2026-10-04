using System;
using System.Runtime.InteropServices;

namespace IIChatTools.VisionOverlay
{
    /// <summary>
    /// Win32 P/Invoke для overlay: установка extended window style
    /// (<c>WS_EX_NOACTIVATE</c> / <c>WS_EX_TOOLWINDOW</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.x (KI-149). Overlay <b>не должен</b> перехватывать фокус
    /// при клике — иначе на следующем шаге
    /// <c>EnsureForegroundProcessAllowed</c> в API отклоняет действие
    /// (процесс <c>IIChatTools.VisionOverlay</c> не в whitelist Chrome).
    /// </para>
    /// <para>
    /// <b>WS_EX_NOACTIVATE</b> — окно не активируется при клике, не появляется
    /// на панели задач. <b>WS_EX_TOOLWINDOW</b> — не показывается в Alt+Tab.
    /// </para>
    /// <para>
    /// Внутренний класс — не для публичного API. Используется
    /// <c>MainWindow.xaml.cs</c> в <c>OnSourceInitialized</c>.
    /// </para>
    /// </remarks>
    internal static class OverlayWin32
    {
        /// <summary>Индекс extended window style в <c>GetWindowLongPtr</c>.</summary>
        public const int GWL_EXSTYLE = -20;

        /// <summary>Окно не активируется при клике; не появляется на панели задач.</summary>
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        /// <summary>Окно не появляется в Alt+Tab (tool window).</summary>
        public const long WS_EX_TOOLWINDOW = 0x00000080L;

        // ==== Get / SetWindowLongPtr (x64 / x86) ====
        // user32.dll экспортирует GetWindowLongPtrW / GetWindowLongPtrW только на x64.
        // На x86 используем GetWindowLongW / SetWindowLongW.

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// Читает extended window style. Кроссплатформенно для x64 / x86.
        /// </summary>
        public static long GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex).ToInt64()
                : GetWindowLong32(hWnd, nIndex);
        }

        /// <summary>
        /// Устанавливает extended window style. Кроссплатформенно для x64 / x86.
        /// </summary>
        public static void SetWindowLongPtr(IntPtr hWnd, int nIndex, long dwNewLong)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong));
            }
            else
            {
                SetWindowLong32(hWnd, nIndex, unchecked((int)dwNewLong));
            }
        }
    }
}