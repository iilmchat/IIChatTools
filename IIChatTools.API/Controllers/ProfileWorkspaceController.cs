using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API per-user Workspace-индекса
    /// (v1.5.0, KI-083, Шаг 7C.1).
    ///
    /// <para>
    /// Позволяет текущему пользователю включить/выключить семантический
    /// поиск по своим файлам workspace, запустить переиндексацию и
    /// отследить прогресс через polling.
    /// </para>
    ///
    /// <para>
    /// Доступ — только аутентифицированные (не Admin).
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/profile/workspace-index")]
    [Authorize]
    public class ProfileWorkspaceController : ControllerBase
    {
        private readonly IWorkspaceIndexService _service;
        private readonly ILogger<ProfileWorkspaceController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт контроллер.
        /// </summary>
        /// <param name="service">Сервис Workspace-индекса (Singleton)</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ProfileWorkspaceController(
            IWorkspaceIndexService service,
            ILogger<ProfileWorkspaceController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        // ============================================================
        // GET /api/profile/workspace-index
        // ============================================================

        /// <summary>
        /// Возвращает текущий статус Workspace-индекса (первичная загрузка UI).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: WorkspaceIndexStatusDto }</returns>
        [HttpGet]
        public Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken)
            => GetStatusInternalAsync(cancellationToken);

        // ============================================================
        // GET /api/profile/workspace-index/status
        // ============================================================

        /// <summary>
        /// Возвращает статус Workspace-индекса для polling (каждые 2 с при индексации).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: WorkspaceIndexStatusDto }</returns>
        [HttpGet("status")]
        public Task<IActionResult> GetStatusPollingAsync(CancellationToken cancellationToken)
            => GetStatusInternalAsync(cancellationToken);

        // ============================================================
        // POST /api/profile/workspace-index/enable
        // ============================================================

        /// <summary>
        /// Включает Workspace-индекс и запускает фоновую индексацию.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: WorkspaceIndexStatusDto }</returns>
        [HttpPost("enable")]
        public async Task<IActionResult> EnableAsync(CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();
                var status = await _service.EnableAsync(userId, cancellationToken);
                return Ok(new { success = true, data = status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка включения Workspace index");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        // ============================================================
        // POST /api/profile/workspace-index/disable
        // ============================================================

        /// <summary>
        /// Отключает Workspace-индекс и очищает все чанки пользователя.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: WorkspaceIndexStatusDto }</returns>
        [HttpPost("disable")]
        public async Task<IActionResult> DisableAsync(CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();
                var status = await _service.DisableAsync(userId, cancellationToken);
                return Ok(new { success = true, data = status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отключения Workspace index");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        // ============================================================
        // POST /api/profile/workspace-index/reindex
        // ============================================================

        /// <summary>
        /// Переиндексирует Workspace (только если индекс включён).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: WorkspaceIndexStatusDto }</returns>
        [HttpPost("reindex")]
        public async Task<IActionResult> ReindexAsync(CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();
                var status = await _service.ReindexAsync(userId, cancellationToken);
                return Ok(new { success = true, data = status });
            }
            catch (InvalidOperationException ex)
            {
                // Ожидаемая бизнес-ошибка (индекс выключен) — отдаём текст клиенту.
                return Ok(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка переиндексации Workspace");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        // ============================================================
        // Внутренние
        // ============================================================

        /// <summary>
        /// Общая реализация GET-статуса (используется в обоих endpoint'ах).
        /// </summary>
        private async Task<IActionResult> GetStatusInternalAsync(CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();
                var status = await _service.GetStatusAsync(userId, cancellationToken);
                return Ok(new { success = true, data = status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статуса Workspace index");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Извлекает ID текущего пользователя из claims.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">Если пользователь не аутентифицирован</exception>
        private int GetCurrentUserId()
        {
            var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(raw, out var id))
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован");
            return id;
        }
    }
}