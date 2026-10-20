using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.DTO.Orchestrator;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IIChatTools.Services.Implementation.Orchestrator
{
    /// <summary>
    /// Административный сервис управления настройками Orchestrator'а
    /// (v2.2.2, KI-245).
    /// <para>
    /// <b>Lifecycle:</b> Scoped (работает с <c>AppSettings</c> через
    /// <see cref="IAppSettingsService"/> и с <see cref="IAuditService"/>).
    /// </para>
    /// <para>
    /// <b>Persist:</b> override'ы хранятся в <c>AppSettings</c> отдельными
    /// ключами (по образцу <c>AdminSqlAgentService</c>, v1.7.0, KI-097):
    /// <list type="bullet">
    ///   <item><c>Orchestrator.MaxSubtasks</c> — <c>int</c>;</item>
    ///   <item><c>Orchestrator.MaxParallel</c> — <c>int</c>;</item>
    ///   <item><c>Orchestrator.TokenBudget</c> — <c>int</c>;</item>
    ///   <item><c>Orchestrator.HumanApproval</c> — <c>string</c>;</item>
    ///   <item><c>Orchestrator.AllowedWorkerAgents</c> — JSON-массив строк.</item>
    /// </list>
    /// При старте значения восстанавливаются
    /// в <c>Program.LoadOrchestratorOverridesAsync</c>.
    /// </para>
    /// </summary>
    public class AdminOrchestratorService : IAdminOrchestratorService
    {
        /// <summary>Префикс ключей в AppSettings для override'ов Orchestrator'а.</summary>
        private const string SettingKeyPrefix = "Orchestrator.";

        /// <summary>Категория в AppSettings для override'ов Orchestrator'а.</summary>
        private const string SettingCategory = "Orchestrator";

        /// <summary>Поле: maxSubtasks.</summary>
        private const string FieldMaxSubtasks = "MaxSubtasks";

        /// <summary>Поле: maxParallel.</summary>
        private const string FieldMaxParallel = "MaxParallel";

        /// <summary>Поле: tokenBudget.</summary>
        private const string FieldTokenBudget = "TokenBudget";

        /// <summary>Поле: humanApproval.</summary>
        private const string FieldHumanApproval = "HumanApproval";

        /// <summary>Поле: allowedWorkerAgents.</summary>
        private const string FieldAllowedWorkerAgents = "AllowedWorkerAgents";

        /// <summary>Допустимые режимы Human-in-the-loop (канонический вид).</summary>
        private static readonly string[] AllowedHumanApprovalValues =
        {
            "Never",
            "BetweenSubtasks",
            "AtEnd"
        };

        private readonly IOrchestratorOptionsProvider _optionsProvider;
        private readonly IAppSettingsService _settingsService;
        private readonly IAuditService _auditService;
        private readonly ILogger<AdminOrchestratorService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="optionsProvider">Провайдер актуальных настроек Orchestrator'а</param>
        /// <param name="settingsService">Сервис настроек (persist override'ов)</param>
        /// <param name="auditService">Сервис аудита</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        public AdminOrchestratorService(
            IOrchestratorOptionsProvider optionsProvider,
            IAppSettingsService settingsService,
            IAuditService auditService,
            ILogger<AdminOrchestratorService> logger)
        {
            _optionsProvider = optionsProvider
                ?? throw new ArgumentNullException(nameof(optionsProvider));
            _settingsService = settingsService
                ?? throw new ArgumentNullException(nameof(settingsService));
            _auditService = auditService
                ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task<OrchestratorSettingsDto> GetSettingsAsync(
            CancellationToken cancellationToken = default)
        {
            // Провайдер уже заполняет IsOverridden по наличию runtime-снимка.
            return Task.FromResult(_optionsProvider.Get());
        }

        /// <inheritdoc />
        public async Task<OrchestratorSettingsDto> UpdateSettingsAsync(
            OrchestratorSettingsDto settings,
            int userId,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            // Валидация ДО persist: некорректные значения не должны попасть в БД.
            var normalized = ValidateAndNormalize(settings);

            var allSettings = await _settingsService.GetAllAsync();

            await UpsertSettingAsync(allSettings, FieldMaxSubtasks,
                normalized.MaxSubtasks.ToString(), "int");
            await UpsertSettingAsync(allSettings, FieldMaxParallel,
                normalized.MaxParallel.ToString(), "int");
            await UpsertSettingAsync(allSettings, FieldTokenBudget,
                normalized.TokenBudget.ToString(), "int");
            await UpsertSettingAsync(allSettings, FieldHumanApproval,
                normalized.HumanApproval, "string");
            await UpsertSettingAsync(allSettings, FieldAllowedWorkerAgents,
                JsonConvert.SerializeObject(normalized.AllowedWorkerAgents), "json");

            // Применяем in-memory (без рестарта приложения).
            _optionsProvider.Update(normalized);

            await LogAdminActionAsync(userId, "admin.orchestrator.settings.update",
                normalized);

            _logger.LogInformation(
                "Orchestrator: настройки обновлены администратором {UserId}: " +
                "maxSubtasks={MaxSubtasks}, maxParallel={MaxParallel}, " +
                "tokenBudget={TokenBudget}, humanApproval={HumanApproval}, workers={Workers}",
                userId, normalized.MaxSubtasks, normalized.MaxParallel,
                normalized.TokenBudget, normalized.HumanApproval,
                normalized.AllowedWorkerAgents.Count);

            return _optionsProvider.Get();
        }

        /// <inheritdoc />
        public async Task<OrchestratorSettingsDto> ResetAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            // --- Удаляем все override-ключи Orchestrator.* ---
            var allSettings = await _settingsService.GetAllAsync();
            var toDelete = allSettings
                .Where(s => !string.IsNullOrEmpty(s.Key)
                            && s.Key.StartsWith(SettingKeyPrefix,
                                StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var setting in toDelete)
            {
                await _settingsService.DeleteAsync(setting.Id);
            }

            // --- Сбрасываем runtime-override (возврат к baseline) ---
            _optionsProvider.Reset();

            await LogAdminActionAsync(userId, "admin.orchestrator.settings.reset", null);

            _logger.LogInformation(
                "Orchestrator: настройки сброшены к baseline администратором {UserId} " +
                "(удалено override-ключей: {Count})",
                userId, toDelete.Count);

            return _optionsProvider.Get();
        }

        // ============ Private ============

        /// <summary>
        /// Валидирует входные настройки и нормализует их:
        /// диапазоны чисел, канонический режим approval, trim + дедупликация
        /// whitelist. Некорректные значения → <see cref="ArgumentException"/>.
        /// </summary>
        private static OrchestratorSettingsDto ValidateAndNormalize(
            OrchestratorSettingsDto source)
        {
            if (source.MaxSubtasks < OrchestratorOptionsProvider.MinMaxSubtasks
                || source.MaxSubtasks > OrchestratorOptionsProvider.MaxMaxSubtasks)
            {
                throw new ArgumentException(
                    $"MaxSubtasks должен быть в диапазоне " +
                    $"{OrchestratorOptionsProvider.MinMaxSubtasks}–" +
                    $"{OrchestratorOptionsProvider.MaxMaxSubtasks}.",
                    nameof(source));
            }

            if (source.MaxParallel < OrchestratorOptionsProvider.MinMaxParallel
                || source.MaxParallel > OrchestratorOptionsProvider.MaxMaxParallel)
            {
                throw new ArgumentException(
                    $"MaxParallel должен быть в диапазоне " +
                    $"{OrchestratorOptionsProvider.MinMaxParallel}–" +
                    $"{OrchestratorOptionsProvider.MaxMaxParallel}.",
                    nameof(source));
            }

            if (source.TokenBudget < OrchestratorOptionsProvider.MinTokenBudget
                || source.TokenBudget > OrchestratorOptionsProvider.MaxTokenBudget)
            {
                throw new ArgumentException(
                    $"TokenBudget должен быть в диапазоне " +
                    $"{OrchestratorOptionsProvider.MinTokenBudget}–" +
                    $"{OrchestratorOptionsProvider.MaxTokenBudget}.",
                    nameof(source));
            }

            var humanApproval = NormalizeHumanApproval(source.HumanApproval);

            var agents = NormalizeAgentList(source.AllowedWorkerAgents);
            if (agents.Count == 0)
            {
                throw new ArgumentException(
                    "AllowedWorkerAgents не может быть пустым: укажите хотя бы одного воркера.",
                    nameof(source));
            }

            if (agents.Count > OrchestratorOptionsProvider.MaxWorkerAgents)
            {
                throw new ArgumentException(
                    $"AllowedWorkerAgents: не более " +
                    $"{OrchestratorOptionsProvider.MaxWorkerAgents} агентов.",
                    nameof(source));
            }

            return new OrchestratorSettingsDto
            {
                MaxSubtasks = source.MaxSubtasks,
                MaxParallel = source.MaxParallel,
                TokenBudget = source.TokenBudget,
                HumanApproval = humanApproval,
                AllowedWorkerAgents = agents,
                IsOverridden = false
            };
        }

        /// <summary>
        /// Приводит режим Human-in-the-loop к каноническому виду
        /// (регистронезависимо); неизвестное значение → <see cref="ArgumentException"/>.
        /// </summary>
        private static string NormalizeHumanApproval(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new ArgumentException(
                    $"HumanApproval обязателен. Допустимые значения: " +
                    $"{string.Join(", ", AllowedHumanApprovalValues)}.",
                    nameof(raw));
            }

            var match = AllowedHumanApprovalValues.FirstOrDefault(
                value => string.Equals(value, raw.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                throw new ArgumentException(
                    $"HumanApproval='{raw}' недопустим. Допустимые значения: " +
                    $"{string.Join(", ", AllowedHumanApprovalValues)}.",
                    nameof(raw));
            }

            return match;
        }

        /// <summary>
        /// Нормализует список агентов: trim, удаление пустых, дедупликация
        /// без учёта регистра с сохранением исходного порядка.
        /// </summary>
        private static List<string> NormalizeAgentList(IEnumerable<string> source)
        {
            var result = new List<string>();
            if (source == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in source)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var name = raw.Trim();
                if (!seen.Add(name)) continue;
                result.Add(name);
            }

            return result;
        }

        /// <summary>
        /// Upsert-ит значение override'а в <c>AppSettings</c>.
        /// </summary>
        private async Task UpsertSettingAsync(
            IReadOnlyList<SettingDto> allSettings,
            string field,
            string value,
            string type)
        {
            var key = $"{SettingKeyPrefix}{field}";
            var existing = allSettings.FirstOrDefault(s =>
                string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                await _settingsService.UpdateAsync(existing.Id, new SettingDto
                {
                    Id = existing.Id,
                    Key = key,
                    Value = value,
                    Type = type,
                    Category = SettingCategory
                });
            }
            else
            {
                await _settingsService.CreateAsync(new SettingDto
                {
                    Key = key,
                    Value = value,
                    Type = type,
                    Category = SettingCategory
                });
            }
        }

        /// <summary>
        /// Пишет действие администратора в журнал аудита.
        /// </summary>
        private async Task LogAdminActionAsync(
            int userId, string action, OrchestratorSettingsDto settings)
        {
            try
            {
                var parametersJson = settings == null
                    ? "{}"
                    : JsonConvert.SerializeObject(new
                    {
                        settings.MaxSubtasks,
                        settings.MaxParallel,
                        settings.TokenBudget,
                        settings.HumanApproval,
                        workers = settings.AllowedWorkerAgents?.Count ?? 0
                    });

                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = userId,
                    ToolName = action,
                    ParametersJson = parametersJson,
                    Status = "Success",
                    DurationMs = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось записать аудит Orchestrator действия {Action}", action);
            }
        }
    }
}
