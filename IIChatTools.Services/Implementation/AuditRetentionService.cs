using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;                                // AppDbContext
using IIChatTools.Services.Interfaces;                 // IAuditRetentionService
using Microsoft.EntityFrameworkCore;                    // ← ДОБАВИТЬ (Where, ExecuteDeleteAsync)
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IIChatTools.Services.Metrics;
using System.Linq;                     // AppMetrics (после переноса)

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Фоновый сервис очистки устаревших записей аудита.
    /// Раз в <c>Audit:CleanupIntervalHours</c> удаляет:
    /// - записи из <c>AuditLogs</c> старше <c>Audit:DatabaseRetentionDays</c>;
    /// - JSONL-файлы старше <c>Audit:FileRetentionDays</c>.
    /// НЕ использует <see cref="IAuditService"/> — чтобы не создавать рекурсивный аудит.
    /// </summary>
    public class AuditRetentionService : BackgroundService, IAuditRetentionService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuditRetentionService> _logger;
        private readonly AuditRetentionOptions _options;

        /// <summary>
        /// Создаёт фоновый сервис retention аудита.
        /// </summary>
        /// <param name="scopeFactory">Фабрика scope (для scoped AppDbContext)</param>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public AuditRetentionService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<AuditRetentionService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _options = new AuditRetentionOptions();
            configuration.GetSection("Audit").Bind(_options);
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Первый запуск — через 1 минуту после старта (дать приложению подняться).
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { return; }

            var interval = TimeSpan.FromHours(Math.Max(1, _options.CleanupIntervalHours));
            _logger.LogInformation(
                "AuditRetentionService запущен: интервал={Interval}ч, БД={DbDays}д, файлы={FileDays}д",
                interval.TotalHours, _options.DatabaseRetentionDays, _options.FileRetentionDays);

            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    await RunCleanupAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка retention-чистки аудита");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        /// <inheritdoc />
        public async Task RunCleanupAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            if (_options.CleanupDatabase && _options.DatabaseRetentionDays > 0)
            {
                await CleanupDatabaseAsync(now, cancellationToken);
            }

            if (_options.CleanupFiles && _options.FileRetentionDays > 0)
            {
                CleanupFiles(now);
            }
        }

        /// <summary>
        /// Удаляет записи из <c>AuditLogs</c> старше retention-периода.
        /// </summary>
        private async Task CleanupDatabaseAsync(DateTime now, CancellationToken cancellationToken)
        {
            var cutoff = now.AddDays(-_options.DatabaseRetentionDays);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // EF Core 7+: ExecuteDeleteAsync — bulk DELETE без загрузки в память.
                var deleted = await db.AuditLogs
                    .Where(a => a.CreatedAt < cutoff)
                    .ExecuteDeleteAsync(cancellationToken);

                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Retention БД: удалено {Count} записей аудита старше {Cutoff:u}",
                        deleted, cutoff);
                    AppMetrics.AuditCleanupTotal.WithLabels("database").Inc(deleted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка retention-чистки БД");
            }
        }

        /// <summary>
        /// Удаляет JSONL-файлы старше retention-периода.
        /// Формат имени: <c>audit-YYYY-MM-DD.jsonl</c>.
        /// </summary>
        private void CleanupFiles(DateTime now)
        {
            try
            {
                var dir = Path.IsPathRooted(_options.LogDirectory)
                    ? _options.LogDirectory
                    : Path.Combine(AppContext.BaseDirectory, _options.LogDirectory);

                if (!Directory.Exists(dir))
                {
                    _logger.LogDebug("Каталог аудита не существует: {Dir}", dir);
                    return;
                }

                var cutoffDate = now.Date.AddDays(-_options.FileRetentionDays);
                var deleted = 0;

                foreach (var file in Directory.EnumerateFiles(dir, "audit-*.jsonl"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);  // audit-YYYY-MM-DD
                    var datePart = name.Length >= 16 ? name.Substring(6, 10) : null;

                    if (datePart == null ||
                        !DateTime.TryParseExact(datePart, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fileDate))
                    {
                        continue;
                    }

                    if (fileDate.Date < cutoffDate)
                    {
                        try
                        {
                            File.Delete(file);
                            deleted++;
                        }
                        catch (Exception exFile)
                        {
                            _logger.LogWarning(exFile, "Не удалось удалить файл аудита: {File}", file);
                        }
                    }
                }

                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Retention файлов: удалено {Count} JSONL-файлов старше {Cutoff:yyyy-MM-dd}",
                        deleted, cutoffDate);
                    AppMetrics.AuditCleanupTotal.WithLabels("file").Inc(deleted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка retention-чистки файлов");
            }
        }
    }
}