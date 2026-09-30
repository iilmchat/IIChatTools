using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.ExternalLlm
{
    /// <summary>
    /// Инструмент LLM: проверка доступности внешнего провайдера
    /// (v1.8.1, KI-109, Фаза 3, DESIGN_EXTERNAL_LLM § 3.6).
    ///
    /// <para>
    /// Лёгкий GET на <c>/models</c> выбранного провайдера. Не тратит бюджет
    /// (не через <c>ask_external_llm</c>). Позволяет LLM самой решить,
    /// есть ли интернет, до вызова тяжёлого запроса.
    /// </para>
    ///
    /// <para>
    /// Read-only, <c>RequiresApprovalByDefault = false</c>.
    /// </para>
    /// </summary>
    public sealed class CheckInternetConnectionTool : ITool
    {
        private readonly IExternalLlmClient _client;
        private readonly IExternalProviderRegistry _registry;
        private readonly ILogger<CheckInternetConnectionTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="client">Клиент внешних LLM (TestConnectionAsync)</param>
        /// <param name="registry">Реестр провайдеров</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public CheckInternetConnectionTool(
            IExternalLlmClient client,
            IExternalProviderRegistry registry,
            ILogger<CheckInternetConnectionTool> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "check_internet_connection";

        /// <inheritdoc />
        public string Description =>
            "Проверяет доступность внешнего провайдера LLM (лёгкий запрос). " +
            "Используй перед ask_external_llm, если не уверена, что есть интернет.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "provider",
                Type = "string",
                Description = "Имя провайдера для проверки. Если не указано — DefaultProvider.",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                var providerName = arguments?["provider"]?.ToString();
                if (string.IsNullOrWhiteSpace(providerName))
                    providerName = _registry.DefaultProvider;

                if (_registry.Get(providerName) == null)
                {
                    return ToolResult.Fail(
                        $"Провайдер '{providerName}' не зарегистрирован.");
                }

                var available = await _client.TestConnectionAsync(
                    providerName, context?.CancellationToken ?? default);

                var data = new
                {
                    provider = providerName,
                    available = available
                };

                var message = available
                    ? $"Провайдер '{providerName}' доступен."
                    : $"Провайдер '{providerName}' недоступен (проверьте интернет/API key).";

                return ToolResult.Ok(data, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckInternetConnectionTool: ошибка");
                return ToolResult.Fail($"Ошибка проверки: {ex.Message}");
            }
        }
    }
}