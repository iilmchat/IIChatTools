using System;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.API.DTO;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using IIChatTools.API.Resources;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using IIChatTools.Services.Metrics;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API-контроллер для вызова инструментов LLM.
    /// Интегрирован с системой подтверждения действий.
    /// </summary>
    [ApiController]
    [Route("api/tools")]
    [Authorize]
    public class ToolsController : ControllerBase
    {
        private readonly IToolRegistry _toolRegistry;
        private readonly IApprovalService _approvalService;
        private readonly IAuditService _auditService;
        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly ILogger<ToolsController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        public ToolsController(
            IToolRegistry toolRegistry,
            IApprovalService approvalService,
            IAuditService auditService,
            IWorkspaceResolver workspaceResolver,
            ILogger<ToolsController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _approvalService = approvalService ?? throw new ArgumentNullException(nameof(approvalService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _workspaceResolver = workspaceResolver ?? throw new ArgumentNullException(nameof(workspaceResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Возвращает список доступных инструментов с описаниями.
        /// </summary>
        /// <returns>JSON { success, data }</returns>
        [HttpGet]
        public IActionResult GetAllTools()
        {
            try
            {
                var descriptors = _toolRegistry.GetAllDescriptors();
                return Ok(new { success = true, data = descriptors });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка инструментов");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Возвращает описание конкретного инструмента.
        /// </summary>
        /// <param name="name">Имя инструмента</param>
        /// <returns>JSON { success, data }</returns>
        [HttpGet("{name}")]
        public IActionResult GetTool(string name)
        {
            try
            {
                var descriptor = _toolRegistry.GetDescriptor(name);
                if (descriptor == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                return Ok(new { success = true, data = descriptor });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения описания инструмента {Tool}", name);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Выполняет инструмент с учётом системы подтверждения.
        /// </summary>
        /// <param name="request">Запрос на выполнение</param>
        /// <returns>JSON { success, data, message }</returns>
        [HttpPost("execute")]
        public async Task<IActionResult> ExecuteAsync([FromBody] ExecuteToolRequest request)
        {
            var startTicks = DateTime.UtcNow.Ticks;

            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ToolName))
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value  });

                var descriptor = _toolRegistry.GetDescriptor(request.ToolName);
                if (descriptor == null)
                    return Ok(new { success = false, message = $"Инструмент '{request.ToolName}' не найден" });

                var userId = GetCurrentUserId();
                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

                // Проверка белого списка
                var whitelisted = await _approvalService.IsWhitelistedAsync(request.ToolName);

                // Если инструмент требует подтверждения и его нет в белом списке
                if (descriptor.RequiresApprovalByDefault && !whitelisted)
                {
                    // Если approvalId не передан — создаём запрос на подтверждение
                    if (!request.ApprovalId.HasValue)
                    {
                        var parametersJson = JsonConvert.SerializeObject(request.Arguments ?? new Newtonsoft.Json.Linq.JObject());
                        var pending = await _approvalService.CreatePendingActionAsync(userId, request.ToolName, parametersJson);

                        return Ok(new
                        {
                            success = true,
                            data = new
                            {
                                requiresApproval = true,
                                actionId = pending.Id,
                                toolName = pending.ToolName,
                                expiresAt = pending.ExpiresAt,
                                message = _localizer["Действие ожидает подтверждения пользователя."].Value
                            }
                        });
                    }

                    // Если approvalId передан — проверяем его статус
                    var action = await _approvalService.GetByIdAsync(request.ApprovalId.Value);
                    if (action == null)
                        return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                    if (action.UserId != userId && !User.IsInRole("Admin"))
                        return Ok(new { success = false, message = _localizer["Доступ запрещён."].Value  });

                    if (action.Status == "Rejected")
                    {
                        AppMetrics.ToolExecutionsTotal
                            .WithLabels(request.ToolName, "Rejected")
                            .Inc();
                        return Ok(new
                        {
                            success = false,
                            message = _localizer["Действие отклонено."].Value,
                            data = new { rejectionReason = action.RejectionReason }
                        });
                    }

                    if (action.Status == "Expired" || action.ExpiresAt <= DateTime.UtcNow)
                    {
                        AppMetrics.ToolExecutionsTotal
                            .WithLabels(request.ToolName, "Expired")
                            .Inc();
                        return Ok(new { success = false, message = _localizer["Действие истекло."].Value  });
                    }

                    if (action.Status != "Approved")
                        return Ok(new { success = false, message = _localizer["Действие ожидает подтверждения пользователя."].Value  });
                }

                // Выполнение инструмента
                var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(userId);

                var context = new ToolExecutionContext
                {
                    UserId = userId,
                    WorkspaceRoot = workspaceRoot,
                    ClientIp = clientIp,
                    CancellationToken = HttpContext.RequestAborted
                };

                var result = await _toolRegistry.ExecuteAsync(request.ToolName, context, request.Arguments);

                var durationMs = (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;

                // Метрики Prometheus: счётчик + гистограмма
                AppMetrics.ToolExecutionsTotal
                    .WithLabels(request.ToolName, result.Success ? "Success" : "Error")
                    .Inc();
                AppMetrics.ToolExecutionDurationSeconds
                    .WithLabels(request.ToolName)
                    .Observe(durationMs / 1000.0);

                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = userId,
                    ToolName = request.ToolName,
                    ParametersJson = JsonConvert.SerializeObject(request.Arguments ?? new Newtonsoft.Json.Linq.JObject()),
                    ResultJson = JsonConvert.SerializeObject(result.Data),
                    Status = result.Success ? "Success" : "Error",
                    DurationMs = durationMs,
                    ClientIp = clientIp
                });

                return Ok(new
                {
                    success = result.Success,
                    data = result.Data,
                    message = result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения инструмента {Tool}", request?.ToolName);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Возвращает статус запроса на подтверждение (для polling).
        /// </summary>
        /// <param name="actionId">Идентификатор действия</param>
        /// <returns>JSON { success, data }</returns>
        [HttpGet("execution/{actionId:int}")]
        public async Task<IActionResult> GetExecutionStatusAsync(int actionId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var action = await _approvalService.GetByIdAsync(actionId);

                if (action == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                if (action.UserId != userId && !User.IsInRole("Admin"))
                    return Ok(new { success = false, message = _localizer["Доступ запрещён."].Value  });

                // Автоматически помечаем как expired, если время истекло
                var status = action.Status;
                if (status == "Pending" && action.ExpiresAt <= DateTime.UtcNow)
                    status = "Expired";

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        actionId = action.Id,
                        toolName = action.ToolName,
                        status,
                        expiresAt = action.ExpiresAt,
                        rejectionReason = action.RejectionReason
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статуса действия {ActionId}", actionId);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Извлекает идентификатор текущего пользователя из claims.
        /// </summary>
        /// <returns>Идентификатор пользователя</returns>
        /// <exception cref="UnauthorizedAccessException">Если пользователь не аутентифицирован</exception>
        private int GetCurrentUserId()
        {
            var idRaw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idRaw, out var id))
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован");
            return id;
        }
    }
}