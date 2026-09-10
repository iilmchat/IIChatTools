using System;
using System.IO;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Вспомогательный класс для безопасной работы с путями
    /// </summary>
    public static class PathHelper
    {
        /// <summary>
        /// Пытается получить безопасный абсолютный путь внутри workspace, предотвращая path traversal
        /// </summary>
        /// <param name="userPath">Путь, указанный пользователем</param>
        /// <param name="workspaceRoot">Корневая директория рабочего пространства</param>
        /// <param name="safePath">Результирующий безопасный абсолютный путь</param>
        /// <returns>true, если путь безопасен и находится внутри workspaceRoot, иначе false</returns>
        public static bool TryGetSafeFullPath(string userPath, string workspaceRoot, out string safePath)
        {
            safePath = null;

            if (string.IsNullOrEmpty(userPath) || string.IsNullOrEmpty(workspaceRoot))
                return false;

            try
            {
                // Нормализуем корень
                var root = Path.GetFullPath(workspaceRoot);
                // Комбинируем с пользовательским путём и получаем полный
                var combined = Path.GetFullPath(Path.Combine(root, userPath));

                // Проверяем, что combined начинается с root (с учётом разделителей)
                if (combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
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
    }
}