using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Фоновый сервис периодической очистки скриншотов Vision Agent.
    /// Раз в <c>CleanupIntervalMinutes</c> (default 30 мин) удаляет папки
    /// задач старше <c>Privacy.WorkspaceRetentionHours</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.6). См. DESIGN § 6.5.
    /// </para>
    /// <para>
    /// По образцу <c>AuditRetentionService</c> / <c>ChatRetentionService</c>.
    /// Первый запуск — через 5 минут после старта, чтобы не конкурировать
    /// с прогревом приложения.
    /// </para>
    /// </remarks>
    public sealed class VisionRetentionService : BackgroundService
    {
        /// <summary>Первый запуск через 5 минут после старта приложения.</summary>
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

        /// <summary>Интервал между прогонами — 30 минут.</summary>
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly VisionAgentOptions _options;
        private readonly ILogger<VisionRetentionService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="scopeFactory">Фабрика scope (для Scoped <see cref="IVisionScreenshotCleaner"/>).</param>
        /// <param name="options">Настройки Vision Agent.</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр null.</exception>
        public VisionRetentionService(
            IServiceScopeFactory scopeFactory,
            IOptions<VisionAgentOptions> options,
            ILogger<VisionRetentionService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1. Enabled=VisionAgent + SaveToWorkspace=false → ничего не делаем.
            var enabled = _options.Enabled;
            var saveToWorkspace = _options.Privacy?.SaveToWorkspace ?? false;

            if (!enabled || !saveToWorkspace)
            {
                _logger.LogInformation(
                    "VisionRetentionService отключён (Enabled={Enabled}, SaveToWorkspace={Save})",
                    enabled, saveToWorkspace);
                return;
            }

            _logger.LogInformation(
                "VisionRetentionService запущен: интервал={Interval}мин, retention={Hours}ч",
                (int)CleanupInterval.TotalMinutes,
                _options.Privacy.WorkspaceRetentionHours);

            // 2. Первый запуск — через 5 минут.
            try
            {
                await Task.Delay(InitialDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // 3. Цикл через PeriodicTimer.
            using var timer = new PeriodicTimer(CleanupInterval);
            do
            {
                try
                {
                    await RunCleanupAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "VisionRetentionService: ошибка прогона cleanup");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        /// <summary>
        /// Один прогон cleanup в новом scope.
        /// </summary>
        private async Task RunCleanupAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var cleaner = scope.ServiceProvider
                .GetRequiredService<IVisionScreenshotCleaner>();

            await cleaner.CleanupAsync(cancellationToken);
        }
    }
}