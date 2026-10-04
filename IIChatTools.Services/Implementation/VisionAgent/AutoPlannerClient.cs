using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Planner LLM с fallback chain — перебирает провайдеров из
    /// <see cref="PlannerLlmOptions.FallbackChain"/>, пока один не вернёт
    /// не-fail действие.
    /// Singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф5.4). См. DESIGN § 3.2, § 5.1.
    /// </para>
    /// <para>
    /// <b>Почему Func, а не конкретные типы:</b> паттерн ADR-002. См.
    /// <see cref="AutoVisionClient"/>.
    /// </para>
    /// <para>
    /// <b>Критерий успеха:</b> <see cref="IPlannerLlmClient.PlanNextAsync"/>
    /// вернул <c>action != "fail"</c>. Fail от одной модели → следующей.
    /// Все вернули fail → возвращаем последний fail.
    /// </para>
    /// </remarks>
    public sealed class AutoPlannerClient : IPlannerLlmClient
    {
        private readonly PlannerLlmOptions _options;
        private readonly Func<IPlannerLlmClient> _lmStudioFactory;
        private readonly Func<IPlannerLlmClient> _externalFactory;
        private readonly ILogger<AutoPlannerClient> _logger;

        private IPlannerLlmClient _lmStudioInstance;
        private IPlannerLlmClient _externalInstance;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent:PlannerLlm</c>).</param>
        /// <param name="lmStudioFactory">Фабрика LmStudio-клиента (lazy).</param>
        /// <param name="externalFactory">Фабрика External-клиента (lazy).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров null.</exception>
        public AutoPlannerClient(
            IOptions<VisionAgentOptions> options,
            Func<IPlannerLlmClient> lmStudioFactory,
            Func<IPlannerLlmClient> externalFactory,
            ILogger<AutoPlannerClient> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value.PlannerLlm
                ?? throw new InvalidOperationException("Секция VisionAgent:PlannerLlm не задана.");
            _lmStudioFactory = lmStudioFactory ?? throw new ArgumentNullException(nameof(lmStudioFactory));
            _externalFactory = externalFactory ?? throw new ArgumentNullException(nameof(externalFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsReady => AnyReady();

        /// <inheritdoc />
        public async Task<VisionActionDto> PlanNextAsync(
            string task,
            IReadOnlyList<VisionStepDto> history,
            ScreenDescriptionDto screen,
            IReadOnlyList<string> plan,
            int userId = 0,
            CancellationToken cancellationToken = default)
        {
            var chain = BuildChain();
            if (chain.Count == 0)
            {
                return new VisionActionDto
                {
                    Action = "fail",
                    Reason = "AutoPlannerClient: FallbackChain пуст."
                };
            }

            VisionActionDto lastFailAction = null;
            Exception lastException = null;

            for (int i = 0; i < chain.Count; i++)
            {
                var entry = chain[i];

                if (!entry.Client.IsReady)
                {
                    _logger.LogDebug(
                        "AutoPlannerClient: [{Index}/{Total}] '{Name}' не готов — пропуск",
                        i + 1, chain.Count, entry.EntryName);
                    continue;
                }

                try
                {
                    var action = await entry.Client.PlanNextAsync(
                        task, history, screen, plan, userId, cancellationToken)
                        .ConfigureAwait(false);

                    if (action == null)
                    {
                        _logger.LogDebug(
                            "AutoPlannerClient: '{Name}' вернул null — fallback",
                            entry.EntryName);
                        continue;
                    }

                    if (!string.Equals(action.Action, "fail", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug(
                            "AutoPlannerClient: успех на '{Name}' — action={Action}",
                            entry.EntryName, action.Action);
                        return action;
                    }

                    _logger.LogDebug(
                        "AutoPlannerClient: '{Name}' вернул fail ({Reason}) — fallback",
                        entry.EntryName, action.Reason);
                    lastFailAction = action;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogWarning(ex,
                        "AutoPlannerClient: '{Name}' упал — fallback", entry.EntryName);
                    lastException = ex;
                }
            }

            if (lastFailAction != null) return lastFailAction;

            var reason = lastException != null
                ? $"Все {chain.Count} провайдеров упали. Последняя: {lastException.Message}"
                : $"Ни один из {chain.Count} провайдеров не готов.";
            return new VisionActionDto { Action = "fail", Reason = reason };
        }

        // ============================================================
        // Private
        // ============================================================

        private List<(string EntryName, IPlannerLlmClient Client)> BuildChain()
        {
            var result = new List<(string, IPlannerLlmClient)>();
            var chain = _options.FallbackChain ?? new List<string>();

            foreach (var entry in chain)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var trimmed = entry.Trim();

                if (trimmed.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((trimmed, ResolveLmStudio()));
                }
                else if (trimmed.StartsWith("external:", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((trimmed, ResolveExternal()));
                }
                else
                {
                    _logger.LogWarning(
                        "AutoPlannerClient: неизвестный элемент FallbackChain '{Entry}' — пропуск",
                        entry);
                }
            }
            return result;
        }

        private bool AnyReady()
        {
            foreach (var e in BuildChain())
            {
                if (e.Client.IsReady) return true;
            }
            return false;
        }

        private IPlannerLlmClient ResolveLmStudio()
            => _lmStudioInstance ??= _lmStudioFactory();

        private IPlannerLlmClient ResolveExternal()
            => _externalInstance ??= _externalFactory();
    }
}