using System;
using System.IO;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация резолвера рабочего пространства.
    /// Структура: {Workspace:RootPath}/users/{userId}
    /// </summary>
    public class WorkspaceResolver : IWorkspaceResolver
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<WorkspaceResolver> _logger;

        /// <summary>
        /// Создаёт экземпляр резолвера.
        /// </summary>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public WorkspaceResolver(IConfiguration configuration, ILogger<WorkspaceResolver> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task<string> GetWorkspacePathAsync(int userId)
        {
            var raw = _configuration["Workspace:RootPath"];
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException(
                    "Не задан Workspace:RootPath в конфигурации. " +
                    "Задайте его в appsettings.Development.json или через User Secrets: " +
                    "dotnet user-secrets set \"Workspace:RootPath\" \"<путь>\".");
            }

            // Раскрываем переменные окружения (%USERPROFILE% на Windows, $HOME на *nix).
            // Это позволяет использовать кросс-платформенный placeholder
            // %USERPROFILE%\IIChatToolsWorkspace в appsettings.Development.json
            // как безопасный fallback для новых разработчиков.
            var expanded = Environment.ExpandEnvironmentVariables(raw);

            var basePath = Path.GetFullPath(expanded);
            var userPath = Path.Combine(basePath, "users", userId.ToString());

            if (!Directory.Exists(userPath))
            {
                Directory.CreateDirectory(userPath);
                _logger.LogInformation(
                    "Создано рабочее пространство {Path} для пользователя {UserId}",
                    userPath, userId);
            }

            return Task.FromResult(userPath);
        }
    }
}