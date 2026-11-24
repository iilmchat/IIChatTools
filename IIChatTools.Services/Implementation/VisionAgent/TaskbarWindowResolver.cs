using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// D12 / KI-166: поиск окна по подстроке заголовка (аналог Win+1 по
    /// названию задачи). Возвращает список видимых окон с совпадением —
    /// Vision-агент может выбрать target по имени, не по иконке.
    /// </summary>
    internal static class TaskbarWindowResolver
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        /// <summary>
        /// Возвращает заголовки видимых окон, содержащие <paramref name="query"/>.
        /// </summary>
        /// <param name="query">Подстрока (регистронезависимо)</param>
        /// <returns>Список заголовков (может быть пустым)</returns>
        public static IReadOnlyList<string> Find(string query)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(query)) return result;

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                var sb = new StringBuilder(512);
                GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (!string.IsNullOrWhiteSpace(title)
                    && title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(title);
                return true;
            }, IntPtr.Zero);

            return result;
        }
    }
}
