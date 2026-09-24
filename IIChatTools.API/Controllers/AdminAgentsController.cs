using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Административный API для управления специализированными суб-агентами
    /// (v1.4.0 Фаза 6, KI-052).
    ///
    /// Persist: изменения сохраняются в <c>AppSettings</c> как JSON
    /// по ключу <c>SubAgents.{name}</c>. При старте приложения —
    /// восстанавливаются в <see cref="ISubAgentRegistry"/> (см. Program.cs).
    ///
    /// Доступен только пользователям с ролью Admin.
    /// </summary>
    [ApiController]
    [Route("api/admin/agents")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminAgentsController : ControllerBase
    {
        /// <summary>Префикс ключа в AppSettings для override'ов агентов.</summary>
        private const string SettingKeyPrefix = "SubAgents.";

        /// <summary>Категория в AppSettings для override'ов агентов.</summary>
        private const string SettingCategory = "Агенты";

        /// <summary>Тип значения в AppSettings для override'ов агентов.</summary>
        private const string SettingType = "json";

        /// <summary>Минимальное количество шагов.</summary>
        private const int MinSteps = 1;

        /// <summary>Максимальное количество шагов.</summary>
        private const int MaxStepsLimit = 30;

        private readonly ISubAgentRegistry _registry;
        private readonly IAppSettingsService _settingsService;
        private readonly IAuditService _auditService;
        private readonly ILogger<AdminAgentsController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        public AdminAgentsController(
            ISubAgentRegistry registry,
            IAppSettingsService settingsService,
            IAuditService auditService,
            ILogger<AdminAgentsController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        // ============ СПИСОК ============

        /// <summary>
        /// Возвращает список всех зарегистрированных суб-агентов (включая отключённых).
        /// </summary>
        /// <returns>JSON { success, data: AgentListItemDto[] }</returns>
        [HttpGet]
        public IActionResult GetAgents()
        {
            try
            {
                var list = _registry.GetAll().Select(ToDto).ToList();
                return Ok(new { success = true, data = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка суб-агентов");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        // ============ ОБНОВЛЕНИЕ ============

        /// <summary>
        /// Обновляет дескриптор суб-агента.
        /// Сохраняет override в <c>AppSettings</c> и применяет in-memory
        /// (немедленно отражается на Chat, без перезапуска).
        /// </summary>
        /// <param name="name">Техническое имя агента (snake_case)</param>
        /// <param name="request">Поля для изменения (null = не менять)</param>
        /// <returns>JSON { success, data: AgentListItemDto }</returns>
        [HttpPut("{name}")]
        public async Task<IActionResult> UpdateAgentAsync(
            string name,
            [FromBody] UpdateAgentRequest request)
        {
            try
            {
                if (request == null)
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value });

                var existing = _registry.Get(name);
                if (existing == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value });

                // --- Валидация ---
                if (string.IsNullOrWhiteSpace(request.DisplayName))
                    return Ok(new { success = false, message = "DisplayName обязателен." });

                if (request.MaxSteps.HasValue &&
                    (request.MaxSteps.Value < MinSteps || request.MaxSteps.Value > MaxStepsLimit))
                {
                    return Ok(new
                    {
                        success = false,
                        message = $"MaxSteps должен быть в диапазоне {MinSteps}–{MaxStepsLimit}."
                    });
                }

                // --- Собираем обновлённый дескриптор ---
                var updated = new SubAgentDescriptor
                {
                    Name = existing.Name,
                    DisplayName = request.DisplayName.Trim(),
                    Description = request.Description?.Trim() ?? existing.Description,
                    Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
                    MaxSteps = request.MaxSteps ?? existing.MaxSteps,
                    RequiresApprovalByDefault = request.RequiresApprovalByDefault ?? existing.RequiresApprovalByDefault,
                    Disabled = request.Disabled ?? existing.Disabled,
                    SystemPrompt = request.SystemPrompt ?? existing.SystemPrompt,
                    AllowedTools = request.AllowedTools ?? existing.AllowedTools ?? Array.Empty<string>()
                };

                // --- Применяем in-memory (немедленно) ---
                _registry.Update(updated);

                // --- Persist в AppSettings ---
                await SaveAgentOverrideAsync(updated);

                // --- Аудит ---
                await LogAdminActionAsync("admin.agent.update", updated.Name);

                _logger.LogInformation("Обновлён агент {Name}", updated.Name);

                return Ok(new { success = true, data = ToDto(updated) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления агента {Name}", name);
                return Ok(new { success = false, message = ex.Message });
            }
        }

        // ============ СБРОС ============

        /// <summary>
        /// Сбрасывает дескриптор суб-агента к значениям из <c>appsettings.json</c>.
        /// Удаляет override из <c>AppSettings</c> и применяет in-memory.
        /// </summary>
        /// <param name="name">Техническое имя агента (snake_case)</param>
        /// <returns>JSON { success, data: AgentListItemDto }</returns>
        [HttpPost("{name}/reset")]
        public async Task<IActionResult> ResetAgentAsync(string name)
        {
            try
            {
                var existing = _registry.Get(name);
                if (existing == null)
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value });

                // --- In-memory reset ---
                _registry.Reset(name);

                // --- Удаляем override из БД ---
                await DeleteAgentOverrideAsync(name);

                // --- Аудит ---
                await LogAdminActionAsync("admin.agent.reset", name);

                _logger.LogInformation("Агент {Name} сброшен к значениям по умолчанию", name);

                var reset = _registry.Get(name);
                return Ok(new { success = true, data = ToDto(reset) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сброса агента {Name}", name);
                return Ok(new { success = false, message = ex.Message });
            }
        }

        // ============ ВНУТРЕННИЕ ============

        /// <summary>
        /// Сохраняет override дескриптора в AppSettings (upsert по ключу).
        /// </summary>
        /// <param name="descriptor">Обновлённый дескриптор</param>
        private async Task SaveAgentOverrideAsync(SubAgentDescriptor descriptor)
        {
            var key = SettingKeyPrefix + descriptor.Name;
            var json = JsonConvert.SerializeObject(descriptor, Formatting.Indented);

            var settings = await _settingsService.GetAllAsync();
            var existing = settings.FirstOrDefault(s =>
                string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                await _settingsService.UpdateAsync(existing.Id, new SettingDto
                {
                    Id = existing.Id,
                    Key = key,
                    Value = json,
                    Type = SettingType,
                    Category = SettingCategory
                });
            }
            else
            {
                await _settingsService.CreateAsync(new SettingDto
                {
                    Key = key,
                    Value = json,
                    Type = SettingType,
                    Category = SettingCategory
                });
            }
        }

        /// <summary>
        /// Удаляет override дескриптора из AppSettings.
        /// </summary>
        /// <param name="name">Техническое имя агента</param>
        private async Task DeleteAgentOverrideAsync(string name)
        {
            var key = SettingKeyPrefix + name;
            var settings = await _settingsService.GetAllAsync();
            var existing = settings.FirstOrDefault(s =>
                string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                await _settingsService.DeleteAsync(existing.Id);
            }
        }

        /// <summary>
        /// Мапит дескриптор в DTO для админки.
        /// </summary>
        /// <param name="d">Дескриптор</param>
        /// <returns>DTO</returns>
        private static AgentListItemDto ToDto(SubAgentDescriptor d)
        {
            return new AgentListItemDto
            {
                Name = d.Name,
                DisplayName = d.DisplayName,
                Description = d.Description,
                Model = d.Model,
                MaxSteps = d.MaxSteps,
                RequiresApprovalByDefault = d.RequiresApprovalByDefault,
                Disabled = d.Disabled,
                SystemPrompt = d.SystemPrompt,
                AllowedTools = d.AllowedTools ?? Array.Empty<string>(),
                ToolCount = d.AllowedTools?.Count ?? 0
            };
        }

        /// <summary>
        /// Записывает действие администратора в журнал аудита.
        /// </summary>
        /// <param name="action">Имя действия</param>
        /// <param name="agentName">Имя агента</param>
        private async Task LogAdminActionAsync(string action, string agentName)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = userId,
                    ToolName = action,
                    ParametersJson = $"{{\"agent\":\"{agentName.Replace("\"", "\\\"")}\"}}",
                    Status = "Success",
                    DurationMs = 0,
                    ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось записать аудит действия {Action}", action);
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