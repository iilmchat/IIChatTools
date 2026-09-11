using System;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API статуса и статистики. Используется для polling со страницы статуса.
    /// </summary>
    [ApiController]
    [Route("api/status")]
    [Authorize]
    public class StatusController : ControllerBase
    {
        private readonly IStatusService _statusService;
        private readonly ILogger<StatusController> _logger;
        private readonly IStringLocalizer<StatusController> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="statusService">Сервис статистики</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        public StatusController(
            IStatusService statusService,
            ILogger<StatusController> logger,
            IStringLocalizer<StatusController> localizer)
        {
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Возвращает снимок состояния системы.
        /// </summary>
        /// <returns>JSON { success, data, message }</returns>
        [HttpGet("snapshot")]
        public async Task<IActionResult> GetSnapshotAsync()
        {
            try
            {
                var snapshot = await _statusService.GetSnapshotAsync();
                return Ok(new { success = true, data = snapshot });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения снимка состояния");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."] });
            }
        }
    }
}