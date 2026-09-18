using System;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using IIChatTools.API.Resources;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API управления запросами на подтверждение действий.
    /// </summary>
    [ApiController]
    [Route("api/approvals")]
    [Authorize]
    public class ApprovalsController : ControllerBase
    {
        private readonly IApprovalService _approvalService;
        private readonly IAuditService _auditService;
        private readonly ILogger<ApprovalsController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        public ApprovalsController(
            IApprovalService approvalService,
            IAuditService auditService,
            ILogger<ApprovalsController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _approvalService = approvalService ?? throw new ArgumentNullException(nameof(approvalService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Возвращает список ожидающих действий текущего пользователя.
        /// </summary>
        /// <returns>JSON { success, data, message }</returns>
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingAsync()
        {
            try
            {
                var userId = GetCurrentUserId();
                var list = await _approvalService.GetPendingForUserAsync(userId);
                var result = list.Select(a => new
                {
                    a.Id,
                    a.ToolName,
                    a.ParametersJson,
                    a.Status,
                    a.CreatedAt,
                    a.ExpiresAt
                });
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка ожидающих действий");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Подтверждает действие.
        /// </summary>
        /// <param name="id">Идентификатор действия</param>
        /// <returns>JSON { success, message }</returns>
        [HttpPost("{id:int}/approve")]
        public async Task<IActionResult> ApproveAsync(int id)
        {
            try
            {
                var userId = GetCurrentUserId();
                var action = await _approvalService.GetByIdAsync(id);

                if (action == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value });

                // Разрешаем только владельцу или администратору
                if (action.UserId != userId && !User.IsInRole("Admin"))
                    return Ok(new { success = false, message = _localizer["Доступ запрещён."].Value  });

                var ok = await _approvalService.ApproveAsync(id, userId);
                if (!ok)
                    return Ok(new { success = false, message = _localizer["Действие истекло."].Value  });

                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = userId,
                    ToolName = "approval.approve",
                    ParametersJson = $"{{\"actionId\":{id}}}",
                    Status = "Success",
                    DurationMs = 0
                });

                return Ok(new { success = true, message = _localizer["Действие подтверждено."].Value  });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка подтверждения действия {ActionId}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Отклоняет действие.
        /// </summary>
        /// <param name="id">Идентификатор действия</param>
        /// <param name="request">Тело запроса с причиной</param>
        /// <returns>JSON { success, message }</returns>
        [HttpPost("{id:int}/reject")]
        public async Task<IActionResult> RejectAsync(int id, [FromBody] RejectRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var action = await _approvalService.GetByIdAsync(id);

                if (action == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                if (action.UserId != userId && !User.IsInRole("Admin"))
                    return Ok(new { success = false, message = _localizer["Доступ запрещён."].Value  });

                var ok = await _approvalService.RejectAsync(id, userId, request?.Reason);
                if (!ok)
                    return Ok(new { success = false, message = _localizer["Действие истекло."].Value  });

                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = userId,
                    ToolName = "approval.reject",
                    ParametersJson = $"{{\"actionId\":{id}}}",
                    Status = "Success",
                    DurationMs = 0
                });

                return Ok(new { success = true, message = _localizer["Действие отклонено."].Value  });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отклонения действия {ActionId}", id);
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

    /// <summary>
    /// Тело запроса отклонения действия.
    /// </summary>
    public class RejectRequest
    {
        /// <summary>
        /// Причина отклонения.
        /// </summary>
        public string Reason { get; set; }
    }
}