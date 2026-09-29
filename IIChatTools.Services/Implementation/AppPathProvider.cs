using System;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Простая реализация <see cref="IAppPathProvider"/>
    /// (v1.7.0, KI-097, KI-100).
    /// Регистрируется в <c>Startup.cs</c> API как Singleton
    /// на основе <c>IWebHostEnvironment.ContentRootPath</c>.
    /// </summary>
    public class AppPathProvider : IAppPathProvider
    {
        /// <inheritdoc/>
        public string ContentRootPath { get; }

        /// <summary>
        /// Создаёт провайдер.
        /// </summary>
        /// <param name="contentRootPath">Абсолютный путь к ContentRoot</param>
        /// <exception cref="ArgumentNullException">
        /// Если <paramref name="contentRootPath"/> = null или пусто.
        /// </exception>
        public AppPathProvider(string contentRootPath)
        {
            if (string.IsNullOrWhiteSpace(contentRootPath))
                throw new ArgumentNullException(nameof(contentRootPath));
            ContentRootPath = contentRootPath;
        }
    }
}