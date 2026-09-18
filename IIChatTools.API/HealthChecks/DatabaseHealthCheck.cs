using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.HealthChecks
{
    /// <summary>
    /// Health-check подключения к базе данных.
    /// Проверяет доступность БД через <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.CanConnectAsync"/>.
    /// Регистрируется с тегами <c>db</c> и <c>ready</c> — участвует в <c>/health/ready</c>.
    /// </summary>
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DatabaseHealthCheck> _logger;

        /// <summary>
        /// Создаёт health-check БД.
        /// </summary>
        /// <param name="scopeFactory">Фабрика scope для разрешения scoped-зависимости <see cref="AppDbContext"/></param>
        /// <param name="logger">Логгер</param>
        public DatabaseHealthCheck(
            IServiceScopeFactory scopeFactory,
            ILogger<DatabaseHealthCheck> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var canConnect = await db.Database.CanConnectAsync(cancellationToken);
                if (canConnect)
                {
                    return HealthCheckResult.Healthy("База данных доступна");
                }

                _logger.LogWarning("Health-check: CanConnectAsync вернул false");
                return HealthCheckResult.Unhealthy("База данных недоступна");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health-check БД завершился ошибкой");
                return HealthCheckResult.Unhealthy("Ошибка подключения к БД", ex);
            }
        }
    }
}