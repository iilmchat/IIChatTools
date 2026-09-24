using System;
using System.Collections.Generic;
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
    /// <para>
    /// v1.4.x (KI-067): поддерживает per-user override через
    /// <c>UserSettings</c> — <c>Chat.RetentionDays</c> (int) и
    /// <c>Chat.DoNotDelete</c> (bool).
    /// </para>
    /// Использует bulk DELETE через <c>ExecuteDeleteAsync</c> в EF Core 7+
    /// (см. <c>IChatService.DeleteOldChatsAsync</c>).
    /// </summary>
    public class ChatRetentionService : BackgroundService
    {
        /// <summary>KI-067: ключ per-user override срока хранения (int).</summary>
        private const string KeyRetentionDays = "Chat.RetentionDays";

        /// <summary>KI-067: ключ per-user override «не удалять чаты» (bool).</summary>
        private const string KeyDoNotDelete = "Chat.DoNotDelete";

        /// <summary>Префикс ключей per-user настроек, относящихся к чатам.</summary>
        private const string KeyPrefixChat = "Chat.";

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
        /// Один прогон чистки: удаляет чаты старше <paramref name="globalRetentionDays"/>,
        /// с учётом per-user override (v1.4.x, KI-067):
        /// <list type="bullet">
        ///   <item><c>Chat.DoNotDelete = true</c> — пользователь исключается;</item>
        ///   <item><c>Chat.RetentionDays = N</c> — свой срок хранения.</item>
        /// </list>
        /// </summary>
        /// <param name="globalRetentionDays">Глобальный срок хранения (из appsettings)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        private async Task RunCleanupAsync(int globalRetentionDays, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
                var userSettings = scope.ServiceProvider.GetRequiredService<IUserSettingsService>();

                // 1. Получаем все per-user настройки Chat.* (один SQL-запрос).
                var allChatSettings = await userSettings.GetAllWithKeyPrefixAsync(
                    KeyPrefixChat, cancellationToken);

                var excludedUserIds = new HashSet<int>();
                var retentionOverrides = new Dictionary<int, int>();

                foreach (var s in allChatSettings)
                {
                    if (string.Equals(s.Key, KeyDoNotDelete, StringComparison.Ordinal)
                        && bool.TryParse(s.Value, out var doNotDelete) && doNotDelete)
                    {
                        excludedUserIds.Add(s.UserId);
                    }
                    else if (string.Equals(s.Key, KeyRetentionDays, StringComparison.Ordinal)
                             && int.TryParse(s.Value, out var days) && days > 0)
                    {
                        retentionOverrides[s.UserId] = days;
                    }
                }

                var totalDeleted = 0;

                // 2. Per-user overrides (каждый — отдельный bulk-DELETE).
                foreach (var kv in retentionOverrides)
                {
                    if (excludedUserIds.Contains(kv.Key))
                    {
                        // DoNotDelete побеждает RetentionDays.
                        continue;
                    }

                    var userDays = Math.Min(kv.Value, _options.MaxDays);
                    var deletedForUser = await chatService.DeleteOldChatsForUserAsync(
                        kv.Key, userDays, cancellationToken);

                    if (deletedForUser > 0)
                    {
                        _logger.LogInformation(
                            "Retention чатов (per-user): userId={UserId}, срок={Days}д, удалено={Count}",
                            kv.Key, userDays, deletedForUser);
                    }

                    totalDeleted += deletedForUser;
                }

                // 3. Общий путь — для всех остальных (без override + не excluded).
                //    Если overrides нет — это тот же путь, что и раньше (обратная совместимость).
                var deletedGlobal = await chatService.DeleteOldChatsAsync(
                    globalRetentionDays, excludedUserIds, cancellationToken);

                totalDeleted += deletedGlobal;

                // 4. Метрика + лог.
                if (totalDeleted > 0)
                {
                    var cutoff = DateTime.UtcNow.AddDays(-globalRetentionDays);
                    _logger.LogInformation(
                        "Retention чатов: всего удалено {Count} (per-user={PerUser}, global={Global}, excluded={Excluded}), срок={Days}д, cutoff={Cutoff:u}",
                        totalDeleted, retentionOverrides.Count, deletedGlobal, excludedUserIds.Count,
                        globalRetentionDays, cutoff);

                    AppMetrics.ChatCleanupTotal.WithLabels("retention").Inc(totalDeleted);
                }
                else
                {
                    _logger.LogDebug(
                        "Retention чатов: нечего удалять (срок {Days}д, overrides={Overrides}, excluded={Excluded})",
                        globalRetentionDays, retentionOverrides.Count, excludedUserIds.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка retention-чистки чатов");
            }
        }
    }
}