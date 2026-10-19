using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.BackgroundJobs;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// REST + SSE для фоновых задач (v2.1.0, Фаза 4).
    ///
    /// <para>
    /// GET  /api/jobs?chatId={id}       — список задач чата/пользователя.
    /// GET  /api/jobs/{id}              — детали.
    /// POST /api/jobs/{id}/cancel       — отмена.
    /// GET  /api/jobs/{id}/stream       — SSE: job_progress / job_completed.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/jobs")]
    [Authorize]
    public class BackgroundJobsController : ControllerBase
    {
        private const int PollIntervalMs = 1500;
        private const int MaxListItems = 50;

        private readonly IBackgroundJobService _jobs;
        private readonly IJobCancellationRegistry _cancellationRegistry;
        private readonly ILogger<BackgroundJobsController> _logger;

        /// <summary>
        /// Создаёт контроллер.
        /// </summary>
        /// <param name="jobs">Сервис фоновых задач.</param>
        /// <param name="cancellationRegistry">Реестр токенов отмены (KI-259).</param>
        /// <param name="logger">Логгер.</param>
        public BackgroundJobsController(
            IBackgroundJobService jobs,
            IJobCancellationRegistry cancellationRegistry,
            ILogger<BackgroundJobsController> logger)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _cancellationRegistry = cancellationRegistry
                ?? throw new ArgumentNullException(nameof(cancellationRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Список задач пользователя (опционально — только выбранного чата).
        /// </summary>
        /// <param name="chatId">Идентификатор чата (null — все чаты).</param>
        /// <returns><c>{ success, data: BackgroundJobDto[] }</c>.</returns>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int? chatId)
        {
            var userId = GetCurrentUserId();
            var jobs = await _jobs.ListAsync(userId, chatId);

            var data = jobs
                .Take(MaxListItems)
                .Select(j => BackgroundJobDto.FromEntity(j))
                .ToList();

            return Ok(new { success = true, data });
        }

        /// <summary>
        /// Детали задачи (включая ResultJson).
        /// </summary>
        /// <param name="id">Идентификатор задачи.</param>
        /// <returns><c>{ success, data: BackgroundJobDto }</c> или 404.</returns>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var userId = GetCurrentUserId();
            var job = await _jobs.GetAsync(id);

            if (job == null || job.UserId != userId)
            {
                return NotFound(new { success = false, message = "Задача не найдена." });
            }

            return Ok(new
            {
                success = true,
                data = BackgroundJobDto.FromEntity(job, includeResult: true)
            });
        }

        /// <summary>
        /// Отменяет свою задачу. Для уже завершённой — возвращает текущий статус.
        /// </summary>
        /// <param name="id">Идентификатор задачи.</param>
        /// <returns><c>{ success, data: BackgroundJobDto }</c>.</returns>
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id)
        {
            var userId = GetCurrentUserId();
            var job = await _jobs.GetAsync(id);

            if (job == null || job.UserId != userId)
            {
                return NotFound(new { success = false, message = "Задача не найдена." });
            }

            if (IsTerminal(job.Status))
            {
                return Ok(new
                {
                    success = true,
                    data = BackgroundJobDto.FromEntity(job),
                    message = $"Задача уже завершена ({job.Status})."
                });
            }

            await _jobs.UpdateStatusAsync(
                id, "Cancelled", job.Progress, "Отменено пользователем");

            // KI-259: реально будим tool; иначе Orchestrator/Vision
            // продолжат работать до естественного завершения.
            _cancellationRegistry.Cancel(id);

            var updated = await _jobs.GetAsync(id);
            _logger.LogInformation(
                "Background job {JobId} cancelled by user {UserId}", id, userId);

            return Ok(new
            {
                success = true,
                data = BackgroundJobDto.FromEntity(updated ?? job),
                message = "Задача отменена."
            });
        }

        /// <summary>
        /// SSE-поток прогресса задачи.
        ///
        /// <para>
        /// Пока задача Pending/Running — опрашивает БД раз в 1.5 с и отправляет
        /// <c>job_progress</c>. На терминальном статусе отправляет
        /// <c>job_completed</c> и закрывает поток. Отключение клиента
        /// обрабатывается через <see cref="HttpContext.RequestAborted"/>.
        /// </para>
        /// </summary>
        /// <param name="id">Идентификатор задачи.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpGet("{id:int}/stream")]
        public async Task Stream(int id, CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            var initial = await _jobs.GetAsync(id);

            if (initial == null || initial.UserId != userId)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream; charset=utf-8";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Headers["Connection"] = "keep-alive";

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, HttpContext.RequestAborted);
            var ct = linked.Token;

            string lastStatus = null;
            var lastProgress = -1;
            string lastMessage = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var job = await _jobs.GetAsync(id);
                    if (job == null || job.UserId != userId) break;

                    var changed =
                        !string.Equals(job.Status, lastStatus, StringComparison.Ordinal) ||
                        job.Progress != lastProgress ||
                        !string.Equals(job.ProgressMessage, lastMessage, StringComparison.Ordinal);

                    if (changed)
                    {
                        if (IsTerminal(job.Status))
                        {
                            await WriteEventAsync("job_completed", new
                            {
                                jobId = job.Id,
                                type = job.Type,
                                status = job.Status,
                                progress = job.Progress,
                                message = job.ProgressMessage,
                                result = job.ResultJson,
                                error = job.Error,
                                completedAt = job.CompletedAt
                            }, ct);
                            break;
                        }

                        await WriteEventAsync("job_progress", new
                        {
                            jobId = job.Id,
                            type = job.Type,
                            status = job.Status,
                            progress = job.Progress,
                            message = job.ProgressMessage,
                            updatedAt = DateTime.UtcNow
                        }, ct);

                        lastStatus = job.Status;
                        lastProgress = job.Progress;
                        lastMessage = job.ProgressMessage;
                    }
                    else
                    {
                        // Keep-alive для прокси: пустой комментарий SSE.
                        await Response.WriteAsync(": ping\n\n", Encoding.UTF8, ct);
                        await Response.Body.FlushAsync(ct);
                    }

                    await Task.Delay(PollIntervalMs, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // F5 / закрытие вкладки / Stop — штатное завершение.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SSE job stream failed: jobId={JobId}, userId={UserId}", id, userId);
            }
        }

        /// <summary>Терминальные статусы задачи.</summary>
        private static bool IsTerminal(string status)
            => string.Equals(status, "Completed", StringComparison.Ordinal)
            || string.Equals(status, "Failed", StringComparison.Ordinal)
            || string.Equals(status, "Cancelled", StringComparison.Ordinal);

        /// <summary>
        /// Записывает одно SSE-событие и делает flush.
        /// </summary>
        private async Task WriteEventAsync(string type, object data, CancellationToken cancellationToken)
        {
            var json = JsonConvert.SerializeObject(data ?? new { }, SseJsonSettings);
            var payload = $"event: {type}\ndata: {json}\n\n";

            await Response.WriteAsync(payload, Encoding.UTF8, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        /// <summary>camelCase + UTC — как в ChatStreamController (KI-071).</summary>
        private static readonly JsonSerializerSettings SseJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        /// <summary>ID текущего пользователя из claims.</summary>
        private int GetCurrentUserId()
        {
            var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(raw, out var id))
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован");
            return id;
        }
    }
}
