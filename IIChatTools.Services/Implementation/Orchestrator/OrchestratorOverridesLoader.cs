using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IIChatTools.Services.Implementation.Orchestrator
{
    /// <summary>
    /// Загрузка persisted-override'ов Orchestrator'а из <c>AppSettings</c>
    /// (v2.2.2, KI-245).
    /// <para>
    /// Выделен из <c>Program.LoadOrchestratorOverridesAsync</c>, чтобы стартовая
    /// логика (разбор ключей <c>Orchestrator.*</c>, устойчивость к мусору в БД)
    /// была покрыта unit-тестами — это самый рискованный участок KI-245.
    /// </para>
    /// <para>
    /// Формат ключей: <c>Orchestrator.MaxSubtasks</c> (int),
    /// <c>Orchestrator.MaxParallel</c> (int), <c>Orchestrator.TokenBudget</c> (int),
    /// <c>Orchestrator.HumanApproval</c> (string),
    /// <c>Orchestrator.AllowedWorkerAgents</c> (JSON-массив).
    /// Невалидные/неизвестные значения пропускаются с warning, приложение
    /// не падает: провайдер остаётся на baseline.
    /// </para>
    /// </summary>
    public static class OrchestratorOverridesLoader
    {
        /// <summary>Префикс ключей override'ов в <c>AppSettings</c>.</summary>
        private const string SettingKeyPrefix = "Orchestrator.";

        /// <summary>
        /// Читает override'ы из <c>AppSettings</c> и применяет их к провайдеру.
        /// </summary>
        /// <param name="settingsService">Сервис настроек</param>
        /// <param name="provider">Провайдер актуальных настроек Orchestrator'а</param>
        /// <param name="logger">Логгер</param>
        /// <param name="cancellationToken">Токен отмены (в текущей реализации не используется — GetAllAsync без токена)</param>
        /// <returns>
        /// Количество применённых валидных ключей; <c>0</c> — override'ов нет.
        /// </returns>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        /// <exception cref="ArgumentException">
        /// Если итоговый снимок не проходит валидацию провайдера
        /// (например, в БД лежит MaxSubtasks=999) — вызывающий должен
        /// обработать и оставить baseline.
        /// </exception>
        public static async Task<int> ApplyAsync(
            IAppSettingsService settingsService,
            IOrchestratorOptionsProvider provider,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            if (settingsService == null)
                throw new ArgumentNullException(nameof(settingsService));
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            var settings = await settingsService.GetAllAsync();
            var orchestratorSettings = settings
                .Where(s => !string.IsNullOrEmpty(s.Key)
                            && s.Key.StartsWith(SettingKeyPrefix,
                                StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(s.Value))
                .ToList();

            if (orchestratorSettings.Count == 0)
            {
                logger.LogInformation("Orchestrator overrides: нет");
                return 0;
            }

            // Начинаем с baseline провайдера (defaults из appsettings).
            var current = provider.Get();
            var applied = 0;

            foreach (var setting in orchestratorSettings)
            {
                var field = setting.Key.Substring(SettingKeyPrefix.Length);

                switch (field.ToLowerInvariant())
                {
                    case "maxsubtasks":
                        if (int.TryParse(setting.Value, out var maxSubtasks))
                        {
                            current.MaxSubtasks = maxSubtasks;
                            applied++;
                        }
                        else
                        {
                            logger.LogWarning(
                                "Orchestrator override {Key}: '{Value}' не является числом — пропущено.",
                                setting.Key, setting.Value);
                        }
                        break;

                    case "maxparallel":
                        if (int.TryParse(setting.Value, out var maxParallel))
                        {
                            current.MaxParallel = maxParallel;
                            applied++;
                        }
                        else
                        {
                            logger.LogWarning(
                                "Orchestrator override {Key}: '{Value}' не является числом — пропущено.",
                                setting.Key, setting.Value);
                        }
                        break;

                    case "tokenbudget":
                        if (int.TryParse(setting.Value, out var tokenBudget))
                        {
                            current.TokenBudget = tokenBudget;
                            applied++;
                        }
                        else
                        {
                            logger.LogWarning(
                                "Orchestrator override {Key}: '{Value}' не является числом — пропущено.",
                                setting.Key, setting.Value);
                        }
                        break;

                    case "humanapproval":
                        current.HumanApproval = setting.Value;
                        applied++;
                        break;

                    case "allowedworkeragents":
                        try
                        {
                            current.AllowedWorkerAgents =
                                JsonConvert.DeserializeObject<List<string>>(setting.Value)
                                ?? new List<string>();
                            applied++;
                        }
                        catch (JsonException ex)
                        {
                            logger.LogWarning(ex,
                                "Orchestrator override {Key}: ошибка разбора JSON-массива.",
                                setting.Key);
                        }
                        break;

                    default:
                        logger.LogWarning(
                            "Orchestrator override {Key}: неизвестное поле '{Field}' — пропущено.",
                            setting.Key, field);
                        break;
                }
            }

            if (applied == 0)
            {
                logger.LogInformation("Orchestrator overrides: нет валидных значений");
                return 0;
            }

            // Update валидирует снимок целиком; при некорректных persisted
            // значениях бросает ArgumentException — обрабатывает вызывающий.
            provider.Update(current);

            logger.LogInformation(
                "Orchestrator overrides: применено {Applied} из {Total} настроек.",
                applied, orchestratorSettings.Count);

            return applied;
        }
    }
}
