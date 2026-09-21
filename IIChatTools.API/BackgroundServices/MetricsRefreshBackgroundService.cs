using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IIChatTools.Services.Metrics;

namespace IIChatTools.API.BackgroundServices
{
    /// <summary>
    /// Фоновый сервис: раз в 30 секунд обновляет gauge-метрики
    /// (<c>PendingApprovals</c>, <c>ActiveUsers</c>) из базы данных.
    /// </summary>
    public class MetricsRefreshBackgroundService : BackgroundService
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MetricsRefreshBackgroundService> _logger;

        /// <summary>
        /// Создаёт фоновый сервис.
        /// </summary>
        /// <param name="scopeFactory">Фабрика scope для разрешения scoped <see cref="AppDbContext"/></param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public MetricsRefreshBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<MetricsRefreshBackgroundService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(RefreshInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;

                    var pending = await db.PendingActions
                        .CountAsync(a => a.Status == "Pending" && a.ExpiresAt > now, stoppingToken);
                    AppMetrics.PendingApprovals.Set(pending);

                    var active = await db.Users.CountAsync(u => u.IsActive, stoppingToken);
                    AppMetrics.ActiveUsers.Set(active);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка обновления gauge-метрик");
                }

                try
                {
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}