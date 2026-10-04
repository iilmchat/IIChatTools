using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Planner LLM через <see cref="IExternalLlmClient"/> (DeepSeek / OpenAI /
    /// Groq / Anthropic / Gemini — всё text-only).
    /// Singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф5.3). См. DESIGN § 3.2, § 4.5.
    /// </para>
    /// <para>
    /// <b>Резолв провайдера:</b> <see cref="PlannerLlmOptions.FallbackChain"/>
    /// сканируется на первый элемент вида <c>"external:{name}"</c>. Если не найден —
    /// используется <see cref="IExternalProviderRegistry.DefaultProvider"/>.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> в <see cref="ExternalLlmRequest.IncludeContext"/> = false,
    /// system = <see cref="VisionSystemPrompt.PlannerPlanNext"/>. Не передаём
    /// историю чата (только vision-контекст в user-prompt). См. DESIGN § 6.5.
    /// </para>
    /// </remarks>
    public sealed class ExternalPlannerClient : IPlannerLlmClient
    {
        private readonly PlannerLlmOptions _plannerOptions;
        private readonly IExternalLlmClient _externalLlmClient;
        private readonly IExternalProviderRegistry _registry;
        private readonly ILogger<ExternalPlannerClient> _logger;

        /// <summary>Имя внешнего провайдера (например, <c>deepseek</c>).</summary>
        private readonly string _externalProviderName;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent</c>).</param>
        /// <param name="externalLlmClient">Клиент внешних LLM (v1.8.1, KI-109).</param>
        /// <param name="registry">Реестр внешних провайдеров (для <c>DefaultProvider</c>).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null.</exception>
        public ExternalPlannerClient(
            IOptions<VisionAgentOptions> options,
            IExternalLlmClient externalLlmClient,
            IExternalProviderRegistry registry,
            ILogger<ExternalPlannerClient> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _plannerOptions = options.Value.PlannerLlm
                ?? throw new InvalidOperationException("Секция VisionAgent:PlannerLlm не задана.");
            _externalLlmClient = externalLlmClient ?? throw new ArgumentNullException(nameof(externalLlmClient));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Резолвим внешнего провайдера из FallbackChain:
            // первый элемент вида "external:{name}" (case-insensitive).
            _externalProviderName = ResolveExternalProviderName(_plannerOptions, _registry);

            _logger.LogDebug(
                "ExternalPlannerClient: внешний провайдер = '{Provider}'",
                _externalProviderName);
        }

        /// <inheritdoc />
        public bool IsReady =>
            !string.IsNullOrWhiteSpace(_externalProviderName) &&
            _registry.Get(_externalProviderName) != null;

        /// <inheritdoc />
        public async Task<VisionActionDto> PlanNextAsync(
            string task,
            IReadOnlyList<VisionStepDto> history,
            ScreenDescriptionDto screen,
            IReadOnlyList<string> plan,
            int userId = 0,
            CancellationToken cancellationToken = default)
        {
            if (!IsReady)
            {
                return new VisionActionDto
                {
                    Action = "fail",
                    Reason = $"External Planner не готов: провайдер '{_externalProviderName}' " +
                             "не зарегистрирован в ExternalLlm:Providers."
                };
            }

            // 1. Обрезаем history до MaxHistorySteps.
            var maxHistory = Math.Max(1, _plannerOptions.MaxHistorySteps);
            var trimmedHistory = history != null && history.Count > maxHistory
                ? history.Skip(history.Count - maxHistory).ToList()
                : (history?.ToList() ?? new List<VisionStepDto>());

            // 2. Собираем user-prompt (тот же формат, что LM Studio версия).
            var userMessage = VisionPlannerPayloadBuilder.Build(
                task, trimmedHistory, screen, plan);

            // 3. Запрос к ExternalLlmClient.
            var request = new ExternalLlmRequest
            {
                Provider = _externalProviderName,
                Prompt = userMessage,
                System = VisionSystemPrompt.PlannerPlanNext,
                IncludeContext = false,               // privacy: без истории чата
                MaxTokens = _plannerOptions.MaxTokens,
                Temperature = _plannerOptions.Temperature
            };

            _logger.LogDebug(
                "ExternalPlannerClient: PlanNextAsync — provider={Provider}, userId={UserId}, " +
                "taskLen={TaskLen}, history={HistCount}, uiElements={UiCount}",
                _externalProviderName, userId, task?.Length ?? 0,
                trimmedHistory.Count, screen?.UiElements?.Count ?? 0);

            ExternalLlmResponse response;
            try
            {
                response = await _externalLlmClient.CompleteAsync(userId, request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ExternalPlannerClient: ошибка вызова внешнего провайдера '{Provider}'",
                    _externalProviderName);
                return new VisionActionDto
                {
                    Action = "fail",
                    Reason = $"Ошибка внешнего Planner ({_externalProviderName}): {ex.Message}"
                };
            }

            if (response == null || string.IsNullOrEmpty(response.Content))
            {
                _logger.LogWarning(
                    "ExternalPlannerClient: пустой ответ от '{Provider}'",
                    _externalProviderName);
                return new VisionActionDto
                {
                    Action = "fail",
                    Reason = "Внешний Planner вернул пустой ответ."
                };
            }

            // 4. Парсим.
            var action = VisionActionParser.Parse(response.Content);

            _logger.LogDebug(
                "ExternalPlannerClient: action={Action}, target={Target}, reason={Reason}",
                action.Action, action.Target, action.Reason);

            return action;
        }

        /// <summary>
        /// Резолвит имя внешнего провайдера: первый <c>external:{name}</c>
        /// в <see cref="PlannerLlmOptions.FallbackChain"/>, иначе <c>DefaultProvider</c>.
        /// </summary>
        private static string ResolveExternalProviderName(
            PlannerLlmOptions plannerOptions,
            IExternalProviderRegistry registry)
        {
            if (plannerOptions?.FallbackChain != null)
            {
                foreach (var entry in plannerOptions.FallbackChain)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;

                    var trimmed = entry.Trim();
                    if (trimmed.StartsWith("external:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = trimmed.Substring("external:".Length).Trim();
                        if (!string.IsNullOrEmpty(name)) return name;
                    }
                }
            }

            // Fallback — DefaultProvider из ExternalLlm.
            return registry?.DefaultProvider;
        }
    }
}