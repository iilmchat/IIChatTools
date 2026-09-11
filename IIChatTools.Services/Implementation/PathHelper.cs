using System;
using System.IO;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Вспомогательный класс для безопасной работы с путями.
    /// Предотвращает path traversal и выход за пределы workspace.
    /// </summary>
    public static class PathHelper
    {
        /// <summary>
        /// Пытается получить безопасный абсолютный путь внутри workspace.
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
                var root = Path.GetFullPath(workspaceRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                string combined;
                if (string.IsNullOrWhiteSpace(userPath))
                {
                    combined = root;
                }
                else if (Path.IsPathRooted(userPath))
                {
                    combined = Path.GetFullPath(userPath);
                }
                else
                {
                    combined = Path.GetFullPath(Path.Combine(root, userPath));
                }

                // Проверяем, что combined либо равен root, либо начинается с root + разделитель
                if (combined.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                    combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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

            var root = Path.GetFullPath(workspaceRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var rel = fullPath.Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return rel.Replace('\\', '/');
            }
            return fullPath.Replace('\\', '/');
        }
    }
}