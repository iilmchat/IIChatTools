using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.BackgroundJobs;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.BackgroundJobs
{
    public class BackgroundJobRunner : BackgroundService
    {
        private readonly IBackgroundJobQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IJobCancellationRegistry _cancellationRegistry;
        private readonly ILogger<BackgroundJobRunner> _logger;
        private readonly BackgroundJobOptions _options;
        private readonly SemaphoreSlim _semaphore;

        public BackgroundJobRunner(
            IBackgroundJobQueue queue,
            IServiceScopeFactory scopeFactory,
            IJobCancellationRegistry cancellationRegistry,
            ILogger<BackgroundJobRunner> logger,
            IOptions<BackgroundJobOptions> options)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _cancellationRegistry = cancellationRegistry;
            _logger = logger;
            _options = options.Value;
            _semaphore = new SemaphoreSlim(_options.MaxConcurrentJobs);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackgroundJobRunner started. Max concurrent jobs: {Max}", _options.MaxConcurrentJobs);

            while (!stoppingToken.IsCancellationRequested)
            {
                var jobId = await _queue.DequeueAsync(stoppingToken);
                await _semaphore.WaitAsync(stoppingToken);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessJobAsync(jobId, stoppingToken);
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }, stoppingToken);
            }
        }

        private async Task ProcessJobAsync(int jobId, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();
            var toolRegistry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();
            var workspaceResolver = scope.ServiceProvider.GetRequiredService<IWorkspaceResolver>();

            var job = await jobService.GetAsync(jobId);
            if (job == null || job.Status == "Cancelled") return;

            // KI-259: per-job CTS. Связываем с stoppingToken хоста — отмена
            // задачи или graceful shutdown останавливают tool.
            var jobCts = _cancellationRegistry.GetOrCreate(jobId);

            Channel<ChatStreamEvent> eventChannel = null;
            Task pumpTask = null;
            CancellationTokenSource linkedCts = null;

            try
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, jobCts.Token);
                var effectiveToken = linkedCts.Token;

                // Между проверкой job и созданием CTS отмена могла уже прийти
                // (job.Status = Cancelled). Повторно проверяем, чтобы не
                // запускать tool после отмены.
                var latestBeforeStart = await jobService.GetAsync(jobId);
                if (latestBeforeStart == null || latestBeforeStart.Status == "Cancelled")
                {
                    _logger.LogInformation("Job {JobId}: cancelled before start", jobId);
                    return;
                }

                _logger.LogInformation("Starting job {JobId} of type {Type}", jobId, job.Type);
                await jobService.UpdateStatusAsync(jobId, "Running", 0, "Инициализация...");

                // Параметры, которые передал LLM при запуске
                var parameters = JObject.Parse(job.ParametersJson);
                
                // Формируем контекст для тула. CancellationToken передается внутри контекста!
                // WorkspaceRoot обязателен для file-system/screenshot-инструментов.
                var workspaceRoot = await workspaceResolver.GetWorkspacePathAsync(job.UserId);
                var context = new ToolExecutionContext
                {
                    UserId = job.UserId,
                    ChatId = job.ChatId,
                    WorkspaceRoot = workspaceRoot,
                    CancellationToken = effectiveToken
                };

                ToolResult result = null;
                string toolName = "";

                // Маршрутизация по типу задачи
                switch (job.Type)
                {
                    case "Orchestrator":
                        toolName = "orchestrate_task";
                        break;
                    case "Vision":
                        toolName = "vision_agent";
                        break;
                    case "Smoke":
                        toolName = "browser_session_control";
                        parameters["command"] = "run_smoke";
                        parameters["sessionId"] = parameters["sessionId"]?.ToString() ?? "";
                        break;
                    case "Reflection":
                        toolName = "reflect_task";
                        break;
                    default:
                        throw new InvalidOperationException($"Неизвестный тип задачи: {job.Type}");
                }

                _logger.LogInformation("Job {JobId}: executing tool {ToolName}", jobId, toolName);
                await jobService.UpdateStatusAsync(jobId, "Running", 10, $"Выполнение {toolName}...");

                // v2.1.0 Фаза 4: Orchestrator умеет писать orchestrator_* в EventWriter.
                // Мост превращает их в progress-обновления; у Vision/Smoke событий нет,
                // поэтому остаётся 10% → 100%.
                eventChannel = Channel.CreateUnbounded<ChatStreamEvent>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
                context.EventWriter = eventChannel.Writer;
                pumpTask = PumpProgressAsync(eventChannel.Reader, jobId, effectiveToken);

                // ВАЖНО: KI-260 — мы выполняем тул в фоне. Если тул требует approval,
                // он либо упадёт, либо выполнится (в зависимости от реализации). 
                // Пока просто вызываем. (CancellationToken передается в context, а не 4-м аргументом).
                result = await toolRegistry.ExecuteAsync(toolName, context, parameters);

                // Дождаться последних progress-событий до финального статуса.
                eventChannel.Writer.TryComplete();
                await pumpTask;
                pumpTask = null;

                // Пользователь мог нажать «Отмена», пока tool ещё работал
                // (Фаза 4: POST /api/jobs/{id}/cancel). Не перетираем Cancelled.
                var latest = await jobService.GetAsync(jobId);
                if (latest == null || latest.Status == "Cancelled")
                {
                    _logger.LogInformation(
                        "Job {JobId}: cancelled while running — результат не сохраняем", jobId);
                    return;
                }

                if (result.Success)
                {
                    await jobService.UpdateStatusAsync(jobId, "Completed", 100, "Готово", result.Data?.ToString());
                }
                else
                {
                    // KI-258: инструмент мог кооперативно вернуть
                    // Fail("Операция отменена.") вместо OperationCanceledException.
                    // В этом случае job — Cancelled, а не Failed.
                    if (jobCts.Token.IsCancellationRequested)
                    {
                        _logger.LogInformation(
                            "Job {JobId}: tool вернул Fail после отмены — статус Cancelled", jobId);
                        await jobService.UpdateStatusAsync(jobId, "Cancelled", 0, "Отменено");
                        return;
                    }

                    await jobService.UpdateStatusAsync(jobId, "Failed", 100, "Ошибка", error: result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                await jobService.UpdateStatusAsync(jobId, "Cancelled", 0, "Отменено");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed.", jobId);

                // Отмена могла прийти до исключения — не перетираем Cancelled.
                var latest = await jobService.GetAsync(jobId);
                if (latest != null && latest.Status == "Cancelled") return;

                await jobService.UpdateStatusAsync(jobId, "Failed", 0, "Ошибка", error: ex.Message);
            }
            finally
            {
                if (eventChannel != null) eventChannel.Writer.TryComplete();
                if (pumpTask != null)
                {
                    try { await pumpTask; }
                    catch (OperationCanceledException) { /* job cancel / host shutdown */ }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Job {JobId}: progress pump failed", jobId);
                    }
                }

                linkedCts?.Dispose();
                _cancellationRegistry.Remove(jobId);
            }
        }

        /// <summary>
        /// Переводит legacy SSE-события Orchestrator в progress-обновления job.
        /// Работает в отдельном DI-scope, чтобы не делить AppDbContext с tool'ом.
        /// </summary>
        private async Task PumpProgressAsync(
            ChannelReader<ChatStreamEvent> reader, int jobId, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();
            var completedRounds = 0;

            try
            {
                await foreach (var evt in reader.ReadAllAsync(ct))
                {
                    switch (evt?.Type)
                    {
                        case "orchestrator_started":
                            await UpdateProgressIfActiveAsync(jobs, jobId, 5, "Декомпозиция...");
                            break;

                        case "orchestrator_round":
                            completedRounds++;
                            {
                                var dto = evt.Data as ChatOrchestratorRoundDto;
                                var progress = Math.Min(90, 10 + completedRounds * 15);
                                var message = dto == null
                                    ? $"Подзадача {completedRounds}"
                                    : $"{dto.Agent}: {dto.Status}";
                                await UpdateProgressIfActiveAsync(jobs, jobId, progress, message);
                            }
                            break;

                        case "orchestrator_completed":
                            await UpdateProgressIfActiveAsync(jobs, jobId, 95, "Агрегация...");
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Job отменён / host shutdown.
            }
        }

        /// <summary>
        /// Обновляет прогресс, только если задача ещё активна
        /// (не перетирает Completed/Failed/Cancelled).
        /// </summary>
        private static async Task UpdateProgressIfActiveAsync(
            IBackgroundJobService jobs, int jobId, int progress, string message)
        {
            var current = await jobs.GetAsync(jobId);
            if (current == null) return;
            if (current.Status == "Completed"
                || current.Status == "Failed"
                || current.Status == "Cancelled")
            {
                return;
            }

            var next = Math.Max(current.Progress, Math.Min(100, progress));
            await jobs.UpdateStatusAsync(jobId, "Running", next, message);
        }
    }
}