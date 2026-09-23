using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using IIChatTools.Services.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Фоновый сервис удаления старых чатов (retention policy).
    /// Раз в <c>Chat:Retention:CleanupIntervalHours</c> удаляет чаты,
    /// у которых <c>UpdatedAt</c> старше <c>Chat:Retention:DefaultDays</c>.
    /// Файлы чатов нет — только БД (Chats + ChatMessages, cascade delete).
    /// Использует <see cref="IChatService.DeleteOldChatsAsync"/> (bulk DELETE
    /// через <c>ExecuteDeleteAsync</c> в EF Core 7+).
    /// </summary>
    public class ChatRetentionService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ChatRetentionService> _logger;
        private readonly ChatRetentionOptions _options;

        /// <summary>
        /// Создаёт фоновый сервис retention чатов.
        /// </summary>
        /// <param name="scopeFactory">Фабрика scope (для scoped <see cref="IChatService"/>)</param>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatRetentionService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ChatRetentionService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            _options = new ChatRetentionOptions();
            configuration.GetSection("Chat:Retention").Bind(_options);
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation(
                    "ChatRetentionService отключён (Chat:Retention:Enabled = false)");
                return;
            }

            // Ограничиваем срок хранения сверху (защита от опечаток в конфиге).
            var retentionDays = Math.Min(
                Math.Max(1, _options.DefaultDays),
                Math.Max(1, _options.MaxDays));

            // Первый запуск — через 2 минуты после старта (дать приложению подняться).
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var interval = TimeSpan.FromHours(Math.Max(1, _options.CleanupIntervalHours));
            _logger.LogInformation(
                "ChatRetentionService запущен: интервал={Interval}ч, срок={Days}д (max={MaxDays}д)",
                interval.TotalHours, retentionDays, _options.MaxDays);

            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    await RunCleanupAsync(retentionDays, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка retention-чистки чатов");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        /// <summary>
        /// Один прогон чистки: удаляет чаты старше <paramref name="retentionDays"/>.
        /// </summary>
        /// <param name="retentionDays">Срок хранения в днях</param>
        /// <param name="cancellationToken">Токен отмены</param>
        private async Task RunCleanupAsync(int retentionDays, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

                var deleted = await chatService.DeleteOldChatsAsync(retentionDays, cancellationToken);

                if (deleted > 0)
                {
                    var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
                    _logger.LogInformation(
                        "Retention чатов: удалено {Count} чатов старше {Cutoff:u}",
                        deleted, cutoff);

                    AppMetrics.ChatCleanupTotal.WithLabels("retention").Inc(deleted);
                }
                else
                {
                    _logger.LogDebug(
                        "Retention чатов: нечего удалять (срок {Days}д)",
                        retentionDays);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка retention-чистки чатов");
            }
        }
    }
}