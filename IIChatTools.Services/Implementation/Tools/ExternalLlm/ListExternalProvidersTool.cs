using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.ExternalLlm
{
    /// <summary>
    /// Инструмент LLM: список провайдеров внешних LLM + статус каждого
    /// (v1.8.1, KI-109, Фаза 3, DESIGN_EXTERNAL_LLM § 4.3).
    ///
    /// <para>
    /// Возвращает: <c>default</c> (имя DefaultProvider) + <c>providers[]</c>
    /// с именем, отображаемым именем, моделью, доступностью (circuit breaker),
    /// тарифами и последней ошибкой.
    /// </para>
    ///
    /// <para>
    /// Read-only, <c>RequiresApprovalByDefault = false</c>.
    /// </para>
    /// </summary>
    public sealed class ListExternalProvidersTool : ITool
    {
        private readonly IExternalProviderRegistry _registry;
        private readonly IExternalLlmCircuitBreaker _circuitBreaker;
        private readonly ILogger<ListExternalProvidersTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="registry">Реестр провайдеров</param>
        /// <param name="circuitBreaker">Circuit breaker (для статуса)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public ListExternalProvidersTool(
            IExternalProviderRegistry registry,
            IExternalLlmCircuitBreaker circuitBreaker,
            ILogger<ListExternalProvidersTool> logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "list_external_providers";

        /// <inheritdoc />
        public string Description =>
            "Возвращает список доступных провайдеров внешних LLM с их статусом " +
            "(доступность, тарифы, последняя ошибка). Используй перед ask_external_llm, " +
            "чтобы выбрать провайдера.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => Array.Empty<ToolParameterDescriptor>();

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                var names = _registry.GetNames();
                var providers = new List<object>(names.Count);

                foreach (var name in names)
                {
                    var opts = _registry.Get(name);
                    if (opts == null) continue;

                    var status = _circuitBreaker.GetStatus(name);

                    providers.Add(new
                    {
                        name = name,
                        displayName = opts.DisplayName,
                        model = opts.Model,
                        available = status?.Available ?? true,
                        costPer1kInputUsd = opts.CostPer1kInputUsd,
                        costPer1kOutputUsd = opts.CostPer1kOutputUsd,
                        lastError = status?.LastError
                    });
                }

                var data = new
                {
                    @default = _registry.DefaultProvider,
                    providers = providers
                };

                return Task.FromResult(ToolResult.Ok(
                    data,
                    $"Найдено {providers.Count} провайдеров."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListExternalProvidersTool: ошибка");
                return Task.FromResult(ToolResult.Fail(
                    $"Ошибка получения списка провайдеров: {ex.Message}"));
            }
        }
    }
}