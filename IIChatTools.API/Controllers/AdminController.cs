﻿using System;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using IIChatTools.API.Resources;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Административный API: CRUD пользователей, настроек, белый список, аудит.
    /// Доступен только пользователям с ролью Admin.
    /// </summary>
    [ApiController]
    [Route("api/admin")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminController : ControllerBase
    {
        private readonly IUserAdminService _userAdminService;
        private readonly IAppSettingsService _settingsService;
        private readonly IAuditQueryService _auditQueryService;
        private readonly IToolRegistry _toolRegistry;
        private readonly IApprovalService _approvalService;
        private readonly IAuditService _auditService;
        private readonly IUserSettingsService _userSettingsService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AdminController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        public AdminController(
            IUserAdminService userAdminService,
            IAppSettingsService settingsService,
            IAuditQueryService auditQueryService,
            IToolRegistry toolRegistry,
            IApprovalService approvalService,
            IAuditService auditService,
            IUserSettingsService userSettingsService,
            IConfiguration configuration,
            ILogger<AdminController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _userAdminService = userAdminService ?? throw new ArgumentNullException(nameof(userAdminService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _auditQueryService = auditQueryService ?? throw new ArgumentNullException(nameof(auditQueryService));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _approvalService = approvalService ?? throw new ArgumentNullException(nameof(approvalService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }
        // ============ ПОЛЬЗОВАТЕЛИ ============

        /// <summary>
        /// Возвращает список пользователей.
        /// </summary>
        /// <returns>JSON { success, data }</returns>
        [HttpGet("users")]
        public async Task<IActionResult> GetUsersAsync()
        {
            try
            {
                var users = await _userAdminService.GetAllAsync();
                return Ok(new { success = true, data = users });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка пользователей");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Возвращает пользователя по идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>JSON { success, data }</returns>
        [HttpGet("users/{id:int}")]
        public async Task<IActionResult> GetUserAsync(int id)
        {
            try
            {
                var user = await _userAdminService.GetByIdAsync(id);
                if (user == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });
                return Ok(new { success = true, data = user });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения пользователя {Id}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Создаёт нового пользователя.
        /// </summary>
        /// <param name="dto">Данные пользователя</param>
        /// <returns>JSON { success, data, message }</returns>
        [HttpPost("users")]
        public async Task<IActionResult> CreateUserAsync([FromBody] UserEditDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value  });

                var created = await _userAdminService.CreateAsync(dto);
                await LogAdminActionAsync("admin.user.create", created.Id);
                return Ok(new { success = true, data = created });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка создания пользователя");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Обновляет пользователя.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <param name="dto">Новые данные</param>
        /// <returns>JSON { success, data, message }</returns>
        [HttpPut("users/{id:int}")]
        public async Task<IActionResult> UpdateUserAsync(int id, [FromBody] UserEditDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value  });

                var updated = await _userAdminService.UpdateAsync(id, dto);
                if (updated == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                await LogAdminActionAsync("admin.user.update", id);
                return Ok(new { success = true, data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления пользователя {Id}", id);
                return Ok(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Удаляет пользователя.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>JSON { success, message }</returns>
        [HttpDelete("users/{id:int}")]
        public async Task<IActionResult> DeleteUserAsync(int id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == id)
                    return Ok(new { success = false, message = "Нельзя удалить самого себя" });

                var ok = await _userAdminService.DeleteAsync(id);
                if (!ok)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                await LogAdminActionAsync("admin.user.delete", id);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка удаления пользователя {Id}", id);
                return Ok(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Сбрасывает пароль пользователя.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <param name="request">Тело запроса с новым паролем</param>
        /// <returns>JSON { success, message }</returns>
        [HttpPost("users/{id:int}/reset-password")]
        public async Task<IActionResult> ResetPasswordAsync(int id, [FromBody] ResetPasswordRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.NewPassword))
                    return Ok(new { success = false, message = "Новый пароль обязателен" });

                var ok = await _userAdminService.ResetPasswordAsync(id, request.NewPassword);
                if (!ok)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                await LogAdminActionAsync("admin.user.reset_password", id);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сброса пароля пользователя {Id}", id);
                return Ok(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Возвращает список доступных ролей.
        /// </summary>
        /// <returns>JSON { success, data }</returns>
        [HttpGet("roles")]
        public async Task<IActionResult> GetRolesAsync()
        {
            try
            {
                var roles = await _userAdminService.GetRolesAsync();
                return Ok(new { success = true, data = roles });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка ролей");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        // ============ PER-USER НАСТРОЙКИ (KI-067-3) ============

        /// <summary>
        /// Возвращает per-user настройки retention для указанного пользователя.
        /// </summary>
        /// <param name="id">Идентификатор пользователя</param>
        /// <returns>JSON { success, data: UserSettingsDto }</returns>
        [HttpGet("users/{id:int}/settings")]
        public async Task<IActionResult> GetUserSettingsAsync(int id)
        {
            try
            {
                var user = await _userAdminService.GetByIdAsync(id);
                if (user == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value });

                var dto = await BuildUserSettingsDtoAsync(id);
                return Ok(new { success = true, data = dto });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения настроек пользователя {Id}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Обновляет per-user настройки retention для указанного пользователя.
        /// <para>
        /// Семантика:
        /// <list type="bullet">
        ///   <item><c>RetentionDays = null</c> → удалить override (использовать глобальный);</item>
        ///   <item><c>RetentionDays = N</c> (1..MaxDays) → установить override;</item>
        ///   <item><c>DoNotDelete = true</c> → пользователь исключается из retention;</item>
        ///   <item><c>DoNotDelete = false</c> → удалить override.</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="id">Идентификатор пользователя</param>
        /// <param name="dto">Новые значения</param>
        /// <returns>JSON { success, data: UserSettingsDto }</returns>
        [HttpPut("users/{id:int}/settings")]
        public async Task<IActionResult> UpdateUserSettingsAsync(int id, [FromBody] UserSettingsDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value });

                var user = await _userAdminService.GetByIdAsync(id);
                if (user == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value });

                var maxDays = _configuration.GetValue<int>("Chat:Retention:MaxDays", 365);

                // --- RetentionDays ---
                if (dto.RetentionDays.HasValue)
                {
                    if (dto.RetentionDays.Value <= 0 || dto.RetentionDays.Value > maxDays)
                    {
                        return Ok(new
                        {
                            success = false,
                            message = $"Срок хранения должен быть в диапазоне 1–{maxDays}."
                        });
                    }

                    await _userSettingsService.SetAsync(
                        id, "Chat.RetentionDays", dto.RetentionDays.Value.ToString(), "int");
                }
                else
                {
                    await _userSettingsService.DeleteAsync(id, "Chat.RetentionDays");
                }

                // --- DoNotDelete ---
                if (dto.DoNotDelete)
                {
                    await _userSettingsService.SetAsync(
                        id, "Chat.DoNotDelete", "true", "bool");
                }
                else
                {
                    await _userSettingsService.DeleteAsync(id, "Chat.DoNotDelete");
                }

                await LogAdminActionAsync("admin.user.settings.update", id);

                var updated = await BuildUserSettingsDtoAsync(id);
                return Ok(new { success = true, data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления настроек пользователя {Id}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Собирает <see cref="UserSettingsDto"/> из per-user настроек пользователя.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>DTO с текущими значениями</returns>
        private async Task<UserSettingsDto> BuildUserSettingsDtoAsync(int userId)
        {
            var settings = await _userSettingsService.GetAllForUserAsync(userId);

            int? retentionDays = null;
            var doNotDelete = false;

            foreach (var s in settings)
            {
                if (string.Equals(s.Key, "Chat.RetentionDays", StringComparison.Ordinal)
                    && int.TryParse(s.Value, out var days) && days > 0)
                {
                    retentionDays = days;
                }
                else if (string.Equals(s.Key, "Chat.DoNotDelete", StringComparison.Ordinal)
                         && bool.TryParse(s.Value, out var dn) && dn)
                {
                    doNotDelete = true;
                }
            }

            return new UserSettingsDto
            {
                RetentionDays = retentionDays,
                DoNotDelete = doNotDelete,
                GlobalRetentionDays = _configuration.GetValue<int>("Chat:Retention:DefaultDays", 30),
                MaxRetentionDays = _configuration.GetValue<int>("Chat:Retention:MaxDays", 365)
            };
        }

        // ============ НАСТРОЙКИ ============

        /// <summary>
        /// Возвращает все настройки.
        /// </summary>
        /// <returns>JSON { success, data }</returns>
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettingsAsync()
        {
            try
            {
                var list = await _settingsService.GetAllAsync();
                return Ok(new { success = true, data = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения настроек");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Создаёт настройку.
        /// </summary>
        /// <param name="dto">Данные</param>
        /// <returns>JSON { success, data, message }</returns>
        [HttpPost("settings")]
        public async Task<IActionResult> CreateSettingAsync([FromBody] SettingDto dto)
        {
            try
            {
                var created = await _settingsService.CreateAsync(dto);
                await LogAdminActionAsync("admin.setting.create", created.Id);
                return Ok(new { success = true, data = created });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка создания настройки");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Обновляет настройку.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <param name="dto">Новые данные</param>
        /// <returns>JSON { success, data, message }</returns>
        [HttpPut("settings/{id:int}")]
        public async Task<IActionResult> UpdateSettingAsync(int id, [FromBody] SettingDto dto)
        {
            try
            {
                var updated = await _settingsService.UpdateAsync(id, dto);
                if (updated == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                await LogAdminActionAsync("admin.setting.update", id);
                return Ok(new { success = true, data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления настройки {Id}", id);
                return Ok(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Удаляет настройку.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>JSON { success, message }</returns>
        [HttpDelete("settings/{id:int}")]
        public async Task<IActionResult> DeleteSettingAsync(int id)
        {
            try
            {
                var ok = await _settingsService.DeleteAsync(id);
                if (!ok)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                await LogAdminActionAsync("admin.setting.delete", id);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка удаления настройки {Id}", id);
                return Ok(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Сбрасывает настройку к значению по умолчанию.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>JSON { success, data, message }</returns>
        [HttpPost("settings/{id:int}/reset")]
        public async Task<IActionResult> ResetSettingAsync(int id)
        {
            try
            {
                var result = await _settingsService.ResetToDefaultAsync(id);
                if (result == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                await LogAdminActionAsync("admin.setting.reset", id);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сброса настройки {Id}", id);
                return Ok(new { success = false, message = ex.Message });
            }
        }

        // ============ БЕЛЫЙ СПИСОК ============

        /// <summary>
        /// Возвращает белый список инструментов.
        /// </summary>
        /// <returns>JSON { success, data }</returns>
        [HttpGet("whitelist")]
        public async Task<IActionResult> GetWhitelistAsync()
        {
            try
            {
                var allTools = _toolRegistry.GetAllDescriptors();
                var result = new System.Collections.Generic.List<WhitelistEntryDto>();

                foreach (var tool in allTools)
                {
                    var whitelisted = await _approvalService.IsWhitelistedAsync(tool.Name);
                    if (whitelisted)
                    {
                        result.Add(new WhitelistEntryDto
                        {
                            ToolName = tool.Name,
                            Description = tool.Description
                        });
                    }
                }

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения белого списка");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        /// <summary>
        /// Добавляет инструмент в белый список.
        /// </summary>
        /// <param name="request">Запрос с именем инструмента</param>
        /// <returns>JSON { success, message }</returns>
        [HttpPost("whitelist")]
        public async Task<IActionResult> AddToWhitelistAsync([FromBody] WhitelistRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ToolName))
                    return Ok(new { success = false, message = "Имя инструмента обязательно" });

                var descriptor = _toolRegistry.GetDescriptor(request.ToolName);
                if (descriptor == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value  });

                await UpdateWhitelistAsync(request.ToolName, add: true);
                await LogAdminActionAsync("admin.whitelist.add", 0, request.ToolName);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка добавления в белый список");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Убирает инструмент из белого списка.
        /// </summary>
        /// <param name="toolName">Имя инструмента</param>
        /// <returns>JSON { success, message }</returns>
        [HttpDelete("whitelist/{toolName}")]
        public async Task<IActionResult> RemoveFromWhitelistAsync(string toolName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(toolName))
                    return Ok(new { success = false, message = "Имя инструмента обязательно" });

                await UpdateWhitelistAsync(toolName, add: false);
                await LogAdminActionAsync("admin.whitelist.remove", 0, toolName);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка удаления из белого списка");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        // ============ АУДИТ ============

        /// <summary>
        /// Возвращает постраничный журнал аудита.
        /// </summary>
        /// <param name="page">Номер страницы</param>
        /// <param name="pageSize">Размер страницы</param>
        /// <param name="userId">Фильтр по пользователю</param>
        /// <param name="toolName">Фильтр по инструменту</param>
        /// <returns>JSON { success, data }</returns>
        [HttpGet("audit")]
        public async Task<IActionResult> GetAuditAsync(int page = 1, int pageSize = 20, int? userId = null, string toolName = null)
        {
            try
            {
                var result = await _auditQueryService.GetPagedAsync(page, pageSize, userId, toolName);

                var dto = new
                {
                    result.Page,
                    result.PageSize,
                    result.TotalCount,
                    result.TotalPages,
                    Items = result.Items.Select(a => new
                    {
                        a.Id,
                        a.UserId,
                        UserName = a.User != null ? (a.User.FullName ?? a.User.Email) : "(аноним)",
                        a.ToolName,
                        a.Status,
                        a.DurationMs,
                        a.CreatedAt,
                        a.ClientIp
                    })
                };

                return Ok(new { success = true, data = dto });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения аудита");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }

        // ============ СЛУЖЕБНЫЕ ============

        /// <summary>
        /// Обновляет белый список, добавляя или удаляя имя инструмента.
        /// </summary>
        /// <param name="toolName">Имя инструмента</param>
        /// <param name="add">true — добавить, false — удалить</param>
        /// <returns>Асинхронная задача</returns>
        private async Task UpdateWhitelistAsync(string toolName, bool add)
        {
            var settings = await _settingsService.GetAllAsync();
            var entry = settings.FirstOrDefault(s => s.Key == "Tools:Whitelist");

            string currentValue;
            if (entry != null)
            {
                currentValue = entry.Value ?? string.Empty;
                var items = currentValue
                    .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();

                if (add)
                {
                    if (!items.Contains(toolName, StringComparer.OrdinalIgnoreCase))
                        items.Add(toolName);
                }
                else
                {
                    items.RemoveAll(x => string.Equals(x, toolName, StringComparison.OrdinalIgnoreCase));
                }

                var newValue = string.Join(",", items);
                await _settingsService.UpdateAsync(entry.Id, new SettingDto
                {
                    Id = entry.Id,
                    Key = entry.Key,
                    Value = newValue,
                    Type = entry.Type,
                    Category = entry.Category
                });
            }
            else if (add)
            {
                await _settingsService.CreateAsync(new SettingDto
                {
                    Key = "Tools:Whitelist",
                    Value = toolName,
                    Type = "string",
                    Category = "Инструменты"
                });
            }
        }

        /// <summary>
        /// Записывает административное действие в журнал аудита.
        /// </summary>
        /// <param name="action">Имя действия</param>
        /// <param name="entityId">Идентификатор сущности</param>
        /// <param name="extra">Дополнительная информация</param>
        /// <returns>Асинхронная задача</returns>
        private async Task LogAdminActionAsync(string action, int entityId, string extra = null)
        {
            try
            {
                var userId = GetCurrentUserId();
                var parameters = $"{{\"entityId\":{entityId}";
                if (!string.IsNullOrWhiteSpace(extra))
                    parameters += $",\"extra\":\"{extra.Replace("\"", "\\\"")}\"";
                parameters += "}";

                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = userId,
                    ToolName = action,
                    ParametersJson = parameters,
                    Status = "Success",
                    DurationMs = 0,
                    ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось записать аудит административного действия {Action}", action);
            }
        }

        /// <summary>
        /// Возвращает идентификатор текущего пользователя из claims.
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
    /// Тело запроса сброса пароля.
    /// </summary>
    public class ResetPasswordRequest
    {
        /// <summary>
        /// Новый пароль.
        /// </summary>
        public string NewPassword { get; set; }
    }

    /// <summary>
    /// Тело запроса на добавление инструмента в белый список.
    /// </summary>
    public class WhitelistRequest
    {
        /// <summary>
        /// Имя инструмента.
        /// </summary>
        public string ToolName { get; set; }
    }
}