using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса статистики.
    /// </summary>
    public class StatusService : IStatusService
    {
        private readonly AppDbContext _dbContext;
        private readonly IDependencyChecker _dependencyChecker;
        private readonly AppUptimeTracker _uptimeTracker;
        private readonly ILogger<StatusService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="dependencyChecker">Сервис проверки зависимостей</param>
        /// <param name="uptimeTracker">Трекер времени работы</param>
        /// <param name="logger">Логгер</param>
        public StatusService(
            AppDbContext dbContext,
            IDependencyChecker dependencyChecker,
            AppUptimeTracker uptimeTracker,
            ILogger<StatusService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _dependencyChecker = dependencyChecker ?? throw new ArgumentNullException(nameof(dependencyChecker));
            _uptimeTracker = uptimeTracker ?? throw new ArgumentNullException(nameof(uptimeTracker));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<StatusSnapshot> GetSnapshotAsync()
        {
            var snapshot = new StatusSnapshot
            {
                AppVersion = AppVersionHolder.Current,
                StartedAtUtc = _uptimeTracker.StartedAtUtc,
                UptimeSeconds = (long)_uptimeTracker.GetUptime().TotalSeconds,
                Dependencies = new Dictionary<string, string>(),
                RecentActions = new List<RecentActionDto>()
            };

            try
            {
                snapshot.DatabaseOnline = await _dbContext.Database.CanConnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось проверить подключение к БД");
                snapshot.DatabaseOnline = false;
            }

            if (snapshot.DatabaseOnline)
            {
                try
                {
                    snapshot.TotalUsers = await _dbContext.Users.CountAsync();
                    snapshot.ActiveUsers = await _dbContext.Users.CountAsync(u => u.IsActive);
                    snapshot.TotalActions = await _dbContext.AuditLogs.CountAsync();

                    var now = DateTime.UtcNow;
                    snapshot.PendingApprovals = await _dbContext.PendingActions
                        .CountAsync(a => a.Status == "Pending" && a.ExpiresAt > now);

                    var recent = await _dbContext.AuditLogs
                        .AsNoTracking()
                        .OrderByDescending(a => a.CreatedAt)
                        .Take(20)
                        .Select(a => new RecentActionDto
                        {
                            Id = a.Id,
                            UserName = a.User != null ? (a.User.FullName ?? a.User.Email) : "(аноним)",
                            ToolName = a.ToolName,
                            Status = a.Status,
                            DurationMs = a.DurationMs,
                            CreatedAt = a.CreatedAt
                        })
                        .ToListAsync();

                    snapshot.RecentActions = recent;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка сбора статистики из БД");
                }
            }

            try
            {
                snapshot.Dependencies = await _dependencyChecker.CheckAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось получить информацию о зависимостях");
            }

            return snapshot;
        }
    }

    /// <summary>
    /// Хранит текущую версию приложения без зависимости слоя Services от слоя API.
    /// </summary>
    public static class AppVersionHolder
    {
        /// <summary>
        /// Текущая версия приложения (устанавливается при старте из API-слоя).
        /// </summary>
        public static string Current { get; set; } = "1.0.0";
    }
}