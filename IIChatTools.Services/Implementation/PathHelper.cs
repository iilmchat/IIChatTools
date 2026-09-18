using System;
using System.IO;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Вспомогательный класс для безопасной работы с путями.
    /// Предотвращает path traversal и выход за пределы workspace.
    /// Кроссплатформенный: корректно обрабатывает оба разделителя ('/' и '\')
    /// на любой ОС, устраняя обход через «чужой» разделитель (KI-040).
    /// </summary>
    public static class PathHelper
    {
        /// <summary>
        /// Сравнение путей: на Windows — без учёта регистра (ФС регистронезависима),
        /// на Linux — с учётом регистра (разные имена — разные пути).
        /// </summary>
        private static StringComparison PathComparison =>
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        /// <summary>
        /// Пытается получить безопасный абсолютный путь внутри workspace.
        /// Разделители '/' и '\' нормализуются к текущей ОС до валидации,
        /// что исключает path traversal через backslash на Linux.
        /// </summary>
        /// <param name="userPath">Путь, указанный пользователем (относительный или абсолютный)</param>
        /// <param name="workspaceRoot">Корневая директория рабочего пространства</param>
        /// <param name="safePath">Результирующий безопасный абсолютный путь</param>
        /// <returns>true, если путь безопасен и находится внутри workspaceRoot</returns>
        public static bool TryGetSafeFullPath(string userPath, string workspaceRoot, out string safePath)
        {
            safePath = null;

            if (string.IsNullOrWhiteSpace(workspaceRoot))
                return false;

            try
            {
                var root = NormalizeFullPath(workspaceRoot);

                string combined;
                if (string.IsNullOrWhiteSpace(userPath))
                {
                    combined = root;
                }
                else
                {
                    // Нормализуем оба разделителя ДО Path.GetFullPath —
                    // это критично на Linux, где '\' не считается разделителем.
                    var normalized = NormalizeSeparators(userPath);

                    combined = Path.IsPathRooted(normalized)
                        ? NormalizeFullPath(normalized)
                        : NormalizeFullPath(Path.Combine(root, normalized));
                }

                // Проверяем, что combined либо равен root, либо начинается с root + разделитель
                if (combined.Equals(root, PathComparison) ||
                    combined.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
                {
                    safePath = combined;
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Нормализует относительный путь для отображения в результатах (с прямыми слэшами).
        /// </summary>
        /// <param name="fullPath">Абсолютный путь</param>
        /// <param name="workspaceRoot">Корень workspace</param>
        /// <returns>Относительный путь с прямыми слэшами</returns>
        public static string ToRelative(string fullPath, string workspaceRoot)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(workspaceRoot))
                return fullPath;

            var root = NormalizeFullPath(workspaceRoot);

            if (fullPath.StartsWith(root, PathComparison))
            {
                var rel = fullPath.Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return rel.Replace('\\', '/');
            }
            return fullPath.Replace('\\', '/');
        }

        /// <summary>
        /// Заменяет оба разделителя ('/' и '\') на DirectorySeparatorChar текущей ОС.
        /// Это устраняет cross-platform обход: на Linux '\' перестаёт быть «обычным символом».
        /// </summary>
        /// <param name="path">Исходный путь</param>
        /// <returns>Путь с нормализованными разделителями</returns>
        private static string NormalizeSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            var sep = Path.DirectorySeparatorChar;
            return path.Replace('\\', sep).Replace('/', sep);
        }

        /// <summary>
        /// Возвращает канонический абсолютный путь без завершающего разделителя.
        /// Использует <see cref="Path.TrimEndingDirectorySeparator(string)"/>,
        /// который корректно сохраняет корень ("/" остаётся "/", а не превращается в "").
        /// </summary>
        /// <param name="path">Путь для нормализации</param>
        /// <returns>Канонический путь</returns>
        private static string NormalizeFullPath(string path)
        {
            var full = Path.GetFullPath(path);
            return Path.TrimEndingDirectorySeparator(full);
        }
    }
}