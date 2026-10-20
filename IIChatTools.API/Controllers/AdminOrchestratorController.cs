using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Services.DTO.Orchestrator;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Административный API для управления настройками Orchestrator-Worker
    /// паттерна (v2.2.2, KI-245).
    ///
    /// <para>
    /// Позволяет в runtime (без правки <c>appsettings.json</c> и без перезапуска)
    /// менять <c>MaxSubtasks</c>, <c>MaxParallel</c>, <c>TokenBudget</c>,
    /// <c>HumanApproval</c> и whitelist агентов-воркеров
    /// (<c>AllowedWorkerAgents</c>). Override'ы сохраняются в <c>AppSettings</c>
    /// (ключи <c>Orchestrator.*</c>) и восстанавливаются при старте через
    /// <c>Program.LoadOrchestratorOverridesAsync</c>.
    /// </para>
    ///
    /// <para>
    /// Аудит действий администратора пишется внутри
    /// <see cref="IAdminOrchestratorService"/> (не дублируем в контроллере).
    /// </para>
    ///
    /// Доступен только пользователям с ролью Admin.
    /// </summary>
    [ApiController]
    [Route("api/admin/orchestrator")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminOrchestratorController : ControllerBase
    {
        private readonly IAdminOrchestratorService _orchestratorAdminService;
        private readonly ILogger<AdminOrchestratorController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="orchestratorAdminService">Административный сервис настроек Orchestrator'а</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public AdminOrchestratorController(
            IAdminOrchestratorService orchestratorAdminService,
            ILogger<AdminOrchestratorController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _orchestratorAdminService = orchestratorAdminService
                ?? throw new ArgumentNullException(nameof(orchestratorAdminService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        // ============ ЧТЕНИЕ ============

        /// <summary>
        /// Возвращает актуальные настройки Orchestrator'а (override + baseline)
        /// и флаг <c>IsOverridden</c>.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены (привязан к соединению клиента)</param>
        /// <returns>JSON { success, data: OrchestratorSettingsDto }</returns>
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettingsAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                var settings = await _orchestratorAdminService
                    .GetSettingsAsync(cancellationToken);

                return Ok(new { success = true, data = settings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения настроек Orchestrator");
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============ ОБНОВЛЕНИЕ ============

        /// <summary>
        /// Обновляет настройки Orchestrator'а и применяет их в runtime.
        /// </summary>
        /// <param name="request">Новые настройки</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: OrchestratorSettingsDto }</returns>
        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettingsAsync(
            [FromBody] OrchestratorSettingsDto request,
            CancellationToken cancellationToken)
        {
            try
            {
                if (request == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Некорректные данные запроса."].Value
                    });
                }

                var userId = GetCurrentUserId();

                var updated = await _orchestratorAdminService.UpdateSettingsAsync(
                    request, userId, cancellationToken);

                _logger.LogInformation(
                    "Orchestrator: настройки обновлены администратором {UserId}",
                    userId);

                return Ok(new { success = true, data = updated });
            }
            catch (ArgumentException ex)
            {
                // Ожидаемые ошибки валидации (диапазоны, whitelist, approval).
                _logger.LogInformation(
                    "Orchestrator: некорректный запрос обновления настроек: {Message}",
                    ex.Message);
                return Ok(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления настроек Orchestrator");
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============ СБРОС ============

        /// <summary>
        /// Сбрасывает настройки Orchestrator'а к baseline из
        /// <c>appsettings.json</c>: удаляет все ключи <c>Orchestrator.*</c>
        /// из <c>AppSettings</c>.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: OrchestratorSettingsDto }</returns>
        [HttpPost("reset")]
        public async Task<IActionResult> ResetAsync(CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();

                var reset = await _orchestratorAdminService
                    .ResetAsync(userId, cancellationToken);

                _logger.LogInformation(
                    "Orchestrator: настройки сброшены администратором {UserId}",
                    userId);

                return Ok(new { success = true, data = reset });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сброса настроек Orchestrator");
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============ ВНУТРЕННИЕ ============

        /// <summary>
        /// Извлекает идентификатор текущего пользователя из claims.
        /// </summary>
        /// <returns>Идентификатор пользователя</returns>
        /// <exception cref="UnauthorizedAccessException">
        /// Если пользователь не аутентифицирован
        /// </exception>
        private int GetCurrentUserId()
        {
            var idRaw = User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idRaw, out var id))
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован");
            return id;
        }
    }
}
