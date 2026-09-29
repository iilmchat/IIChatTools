using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Административный API для управления подключениями Database Agent
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6).
    ///
    /// <para>
    /// Позволяет редактировать whitelist-таблицы, MaxRows, Timeout и флаг
    /// <c>Enabled</c> в runtime — без правки <c>appsettings.json</c> и
    /// без перезапуска приложения. Override'ы сохраняются в <c>AppSettings</c>
    /// (ключи <c>SqlAgent.{name}.{field}</c>) и восстанавливаются при старте
    /// через <c>Program.LoadSqlAgentOverrides</c>.
    /// </para>
    ///
    /// <para>
    /// Аудит действий администратора пишется внутри
    /// <see cref="IAdminSqlAgentService"/> (не дублируем в контроллере).
    /// </para>
    ///
    /// Доступен только пользователям с ролью Admin.
    /// </summary>
    [ApiController]
    [Route("api/admin/sql-agent")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminSqlAgentController : ControllerBase
    {
        private readonly IAdminSqlAgentService _sqlAgentAdminService;
        private readonly ILogger<AdminSqlAgentController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="sqlAgentAdminService">Административный сервис управления подключениями</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public AdminSqlAgentController(
            IAdminSqlAgentService sqlAgentAdminService,
            ILogger<AdminSqlAgentController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _sqlAgentAdminService = sqlAgentAdminService
                ?? throw new ArgumentNullException(nameof(sqlAgentAdminService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        // ============ СПИСОК ============

        /// <summary>
        /// Возвращает список всех зарегистрированных подключений Database Agent
        /// с актуальными настройками (override + baseline) и флагом <c>IsOverridden</c>.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены (привязан к соединению клиента)</param>
        /// <returns>JSON { success, data: SqlAgentConnectionItemDto[] }</returns>
        [HttpGet("connections")]
        public async Task<IActionResult> GetConnectionsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var connections = await _sqlAgentAdminService
                    .GetAllConnectionsAsync(cancellationToken);

                return Ok(new { success = true, data = connections });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка подключений SqlAgent");
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============ ОБНОВЛЕНИЕ ============

        /// <summary>
        /// Обновляет override-настройки указанного подключения.
        /// Поля с <c>null</c> не изменяются. Изменения применяются в runtime.
        /// </summary>
        /// <param name="name">Техническое имя подключения (например, <c>internal</c>)</param>
        /// <param name="request">Поля для изменения</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: SqlAgentConnectionItemDto }</returns>
        [HttpPut("connections/{name}")]
        public async Task<IActionResult> UpdateConnectionAsync(
            string name,
            [FromBody] UpdateSqlAgentConnectionRequest request,
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

                var updated = await _sqlAgentAdminService.UpdateConnectionAsync(
                    name, request, userId, cancellationToken);

                _logger.LogInformation(
                    "SqlAgent: подключение '{Name}' обновлено администратором {UserId}",
                    name, userId);

                return Ok(new { success = true, data = updated });
            }
            catch (ArgumentException ex)
            {
                // Ожидаемые ошибки: подключение не найдено, MaxRows / Timeout вне диапазона.
                _logger.LogInformation(
                    "SqlAgent: некорректный запрос обновления '{Name}': {Message}",
                    name, ex.Message);
                return Ok(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления подключения SqlAgent '{Name}'", name);
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============ ТЕСТ ============

        /// <summary>
        /// Проверяет подключение: открывает соединение и выполняет <c>SELECT 1</c>.
        /// Работает и для отключённых подключений (<c>Enabled = false</c>) —
        /// удобно проверить «до включения».
        /// </summary>
        /// <param name="name">Техническое имя подключения</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: SqlAgentTestResultDto }</returns>
        [HttpPost("connections/{name}/test")]
        public async Task<IActionResult> TestConnectionAsync(
            string name,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await _sqlAgentAdminService
                    .TestConnectionAsync(name, cancellationToken);

                // Внимание: DTO возвращается как data всегда, даже при Success=false.
                // Различие между «endpoint упал» (success=false в outer) и
                // «тест провалился» (success=false в data) — намеренно, чтобы UI
                // мог показать сообщение об ошибке подключения.
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка теста подключения SqlAgent '{Name}'", name);
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============ СБРОС ============

        /// <summary>
        /// Сбрасывает override-настройки подключения к значениям из
        /// <c>appsettings.json</c>: удаляет все ключи
        /// <c>SqlAgent.{name}.*</c> из <c>AppSettings</c>.
        /// </summary>
        /// <param name="name">Техническое имя подключения</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: SqlAgentConnectionItemDto }</returns>
        [HttpPost("connections/{name}/reset")]
        public async Task<IActionResult> ResetConnectionAsync(
            string name,
            CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();

                var reset = await _sqlAgentAdminService
                    .ResetConnectionAsync(name, userId, cancellationToken);

                _logger.LogInformation(
                    "SqlAgent: подключение '{Name}' сброшено администратором {UserId}",
                    name, userId);

                return Ok(new { success = true, data = reset });
            }
            catch (ArgumentException ex)
            {
                _logger.LogInformation(
                    "SqlAgent: некорректный запрос сброса '{Name}': {Message}",
                    name, ex.Message);
                return Ok(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сброса подключения SqlAgent '{Name}'", name);
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