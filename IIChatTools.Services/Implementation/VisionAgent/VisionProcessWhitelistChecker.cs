using System;
using System.Collections.Generic;
using System.Linq;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Проверка «процесс в фокусе — разрешён ли?».
    /// Чистая логика (без Win32) — тестируется в CI ubuntu-latest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф2.9). См. DESIGN § 6.1 (<c>AllowedProcesses</c>).
    /// </para>
    /// <para>
    /// <b>Поведение при пустом whitelist:</b> считается «whitelist не задан» →
    /// разрешаем любой процесс. Это безопаснее по UX: dev забыл настроить —
    /// не получает сломанное поведение. Если нужно «запретить всё» — используйте
    /// невозможное имя в списке (например, <c>["__none__"]</c>).
    /// </para>
    /// <para>
    /// <b>Нормализация:</b> имя процесса сравнивается без учёта регистра и без
    /// расширения <c>.exe</c> (см. <see cref="NormalizeProcessName"/>).
    /// <c>chrome.exe</c> == <c>chrome</c> == <c>CHROME</c>.
    /// </para>
    /// </remarks>
    public static class VisionProcessWhitelistChecker
    {
        /// <summary>
        /// Проверяет, разрешён ли процесс по списку.
        /// </summary>
        /// <param name="processName">
        /// Имя процесса из <c>Process.ProcessName</c> (без <c>.exe</c>) или
        /// произвольное. Нормализуется через <see cref="NormalizeProcessName"/>.
        /// </param>
        /// <param name="allowedProcesses">
        /// Whitelist из конфига. <c>null</c> или пустой список → «без ограничений»
        /// (возвращает <c>true</c>).
        /// </param>
        /// <param name="error">
        /// Сообщение об ошибке (заполнено только при <c>false</c>).
        /// Содержит имя процесса и список разрешённых.
        /// </param>
        /// <returns><c>true</c> — разрешён; <c>false</c> — запрещён.</returns>
        public static bool IsProcessAllowed(
            string processName,
            IEnumerable<string> allowedProcesses,
            out string error)
        {
            error = null;

            // 1. Whitelist не задан / пустой → без ограничений.
            if (allowedProcesses == null)
            {
                return true;
            }

            var list = allowedProcesses
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (list.Count == 0)
            {
                return true;
            }

            // 2. Имя процесса пустое → разрешить (не можем проверить, не блокируем).
            //    Ситуация редкая: Process.GetProcessById упал, либо это системный
            //    процесс без имени. Не считаем это нарушением.
            if (string.IsNullOrWhiteSpace(processName))
            {
                return true;
            }

            // 3. Сверка.
            var normalized = NormalizeProcessName(processName);
            foreach (var allowed in list)
            {
                if (string.Equals(
                        normalized,
                        NormalizeProcessName(allowed),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            error = $"Процесс «{processName}» не в whitelist. " +
                    $"Разрешены: {string.Join(", ", list)}.";
            return false;
        }

        /// <summary>
        /// Нормализует имя процесса: trim + strip <c>.exe</c> (case-insensitive).
        /// </summary>
        /// <param name="name">Имя процесса. Может быть <c>null</c> / пустым.</param>
        /// <returns>Нормализованное имя. Пустая строка, если вход был пуст.</returns>
        public static string NormalizeProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var trimmed = name.Trim();
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 4);
            }
            return trimmed;
        }
    }
}