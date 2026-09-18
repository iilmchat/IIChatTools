using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.HealthChecks
{
    /// <summary>
    /// Health-check каталога рабочего пространства.
    /// Проверяет, что <c>Workspace:RootPath</c> задан, существует и доступен на запись.
    /// Регистрируется с тегами <c>workspace</c> и <c>ready</c> — участвует в <c>/health/ready</c>.
    /// </summary>
    public class WorkspaceHealthCheck : IHealthCheck
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<WorkspaceHealthCheck> _logger;

        /// <summary>
        /// Создаёт health-check workspace.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <param name="logger">Логгер</param>
        public WorkspaceHealthCheck(
            IConfiguration configuration,
            ILogger<WorkspaceHealthCheck> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var raw = _configuration["Workspace:RootPath"];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy("Workspace:RootPath не задан в конфигурации"));
            }

            // Раскрываем переменные окружения (%USERPROFILE% на Windows, $HOME на *nix)
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(expanded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Некорректный путь Workspace:RootPath: {Path}", expanded);
                return Task.FromResult(
                    HealthCheckResult.Unhealthy($"Некорректный путь: {expanded}", ex));
            }

            if (!Directory.Exists(fullPath))
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy($"Каталог не существует: {fullPath}"));
            }

            // Проверка на запись — создаём и сразу удаляем temp-файл
            try
            {
                var probe = Path.Combine(fullPath, $".healthcheck-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return Task.FromResult(
                    HealthCheckResult.Healthy($"Workspace доступен на запись: {fullPath}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workspace недоступен на запись: {Path}", fullPath);
                return Task.FromResult(
                    HealthCheckResult.Unhealthy($"Каталог недоступен на запись: {fullPath}", ex));
            }
        }
    }
}