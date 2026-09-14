using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Определяет путь к исполняемому файлу Chromium-совместимого браузера.
    /// Приоритет: явный путь из конфигурации → Edge → Chrome.
    /// </summary>
    public static class BrowserLocator
    {
        /// <summary>
        /// Возвращает путь к исполняемому файлу браузера.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <returns>Полный путь к EXE или null, если браузер не найден</returns>
        public static string Resolve(IConfiguration configuration)
        {
            // 1. Явно указанный путь
            var explicitPath = configuration?["Browser:ExecutablePath"];
            if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
                return explicitPath;

            // 2. Microsoft Edge (встроен в Windows 10/11)
            var edgePaths = new[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
            };
            foreach (var path in edgePaths)
                if (File.Exists(path)) return path;

            // 3. Google Chrome
            var chromePaths = new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
            };
            foreach (var path in chromePaths)
                if (File.Exists(path)) return path;

            return null;
        }
    }
}