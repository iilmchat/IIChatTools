using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Контроллер запуска задач суб-агентом.
    /// Доступен только администраторам.
    /// </summary>
    [ApiController]
    [Route("api/subagent")]
    [Authorize(Policy = "AdminOnly")]
    public class SubAgentController : ControllerBase
    {
        private readonly ISubAgentService _subAgentService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SubAgentController> _logger;

        public SubAgentController(
            ISubAgentService subAgentService,
            IConfiguration configuration,
            ILogger<SubAgentController> logger)
        {
            _subAgentService = subAgentService ?? throw new ArgumentNullException(nameof(subAgentService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Запускает задачу суб-агентом.
        /// </summary>
        [HttpPost("run")]
        public async Task<IActionResult> RunAsync([FromBody] SubAgentRunRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Task))
                return Ok(new { success = false, message = "Параметр 'task' обязателен." });

            var userIdRaw = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? User?.FindFirst("sub")?.Value
                            ?? User?.FindFirst("nameid")?.Value;

            if (!int.TryParse(userIdRaw, out var userId))
                return Ok(new { success = false, message = "Не удалось определить пользователя из JWT." });

            var workspaceRoot = _configuration["Workspace:RootPath"];
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                return Ok(new { success = false, message = "Не задан Workspace:RootPath." });

            var context = new ToolExecutionContext
            {
                UserId = userId,
                WorkspaceRoot = workspaceRoot,
                ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CancellationToken = HttpContext.RequestAborted
            };

            var taskRequest = new SubAgentTaskRequest
            {
                Task = request.Task,
                Context = request.Context,
                MaxSteps = request.MaxSteps > 0 ? request.MaxSteps : 10,
                AutoDebug = request.AutoDebug,
                AllowedTools = request.AllowedTools
            };

            try
            {
                var result = await _subAgentService.ExecuteTaskAsync(context, taskRequest);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        sessionId = result.SessionId,
                        finalAnswer = result.FinalAnswer,
                        completed = result.Completed,
                        steps = result.Steps,
                        durationMs = result.DurationMs,
                        usedTools = result.UsedTools,
                        debugReview = result.DebugReview
                    },
                    message = result.Completed ? "Задача выполнена." : "Задача не завершена (лимит шагов)."
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Запрос суб-агента отменён клиентом (user {UserId})", userId);
                return Ok(new { success = false, message = "Запрос отменён." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения суб-агента (user {UserId})", userId);
                return Ok(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }
    }

    /// <summary>
    /// Тело запроса на запуск суб-агента.
    /// </summary>
    public class SubAgentRunRequest
    {
        public string Task { get; set; }
        public string Context { get; set; }
        public int MaxSteps { get; set; } = 10;
        public bool AutoDebug { get; set; }
        public IReadOnlyList<string> AllowedTools { get; set; }
    }
}