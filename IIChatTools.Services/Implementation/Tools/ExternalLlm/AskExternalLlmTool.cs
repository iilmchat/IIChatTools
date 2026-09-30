using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.ExternalLlm
{
    /// <summary>
    /// Инструмент LLM: обращение к внешней LLM
    /// (v1.8.1, KI-109, Фаза 3, DESIGN_EXTERNAL_LLM § 4.3).
    ///
    /// <para>
    /// Поддерживает два режима:
    /// <list type="bullet">
    ///   <item><b>Одиночный</b> — один запрос к указанному провайдеру.</item>
    ///   <item><b>Сравнение</b> — при <c>compare_with</c> делаются два
    ///   параллельных запроса (<c>Task.WhenAll</c>), возвращается
    ///   <c>{ primary, secondary, totalCostUsd }</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Approval не требуется</b> (<c>RequiresApprovalByDefault = false</c>):
    /// защита от «$1000 за ночь» — через <see cref="IExternalLlmBudgetTracker"/>
    /// (per-user, daily). См. DESIGN § 6.1.
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только метаданные (длина prompt, провайдер,
    /// токены). Никогда не логируется содержимое prompt или ответа.
    /// </para>
    /// </summary>
    public sealed class AskExternalLlmTool : ITool
    {
        private const double DefaultTemperature = 0.7;
        private const double MinTemperature = 0.0;
        private const double MaxTemperature = 2.0;

        private readonly IExternalLlmClient _client;
        private readonly IExternalProviderRegistry _registry;
        private readonly ILogger<AskExternalLlmTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="client">Клиент внешних LLM</param>
        /// <param name="registry">Реестр провайдеров (для резолва DefaultProvider / GetNames)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public AskExternalLlmTool(
            IExternalLlmClient client,
            IExternalProviderRegistry registry,
            ILogger<AskExternalLlmTool> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "ask_external_llm";

        /// <inheritdoc />
        public string Description =>
            "Отправляет запрос внешней LLM (DeepSeek, OpenAI, Groq, Together AI, Ollama). " +
            "Используй для: (а) сложных задач, где локальная модель не справляется " +
            "(свежие данные, специализированные знания); (б) сравнения ответов двух " +
            "провайдеров (параметр compare_with). По умолчанию в внешнюю модель " +
            "уходит ТОЛЬКО prompt — без истории чата (privacy + cost).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "prompt",
                Type = "string",
                Description = "Текст запроса к внешней модели. Не включай PII (персональные данные).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "provider",
                Type = "string",
                Description = "Имя провайдера (deepseek / openai / groq / together / ollama). " +
                              "Если не указано — используется DefaultProvider.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "compare_with",
                Type = "string",
                Description = "Имя второго провайдера для сравнения ответов. " +
                              "Если задан — выполняются 2 параллельных запроса.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "include_context",
                Type = "boolean",
                Description = "Включить последние сообщения чата в prompt (по умолчанию false — " +
                              "privacy + cost). Внимание: может раскрыть PII.",
                Required = false,
                Default = false
            },
            new ToolParameterDescriptor
            {
                Name = "max_tokens",
                Type = "integer",
                Description = "Максимум токенов в ответе (override провайдера).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "temperature",
                Type = "number",
                Description = "Температура (0.0–2.0, по умолчанию 0.7).",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                if (context == null || context.UserId <= 0)
                    return ToolResult.Fail("UserId не задан в контексте.");

                var prompt = arguments?["prompt"]?.ToString();
                if (string.IsNullOrWhiteSpace(prompt))
                    return ToolResult.Fail("Не указан параметр 'prompt'.");

                var providerName = ResolveProviderName(arguments);
                if (_registry.Get(providerName) == null)
                {
                    return ToolResult.Fail(
                        $"Провайдер '{providerName}' не зарегистрирован. " +
                        $"Доступные: {string.Join(", ", _registry.GetNames())}.");
                }

                var compareWith = arguments?["compare_with"]?.ToString();
                if (!string.IsNullOrWhiteSpace(compareWith))
                {
                    if (string.Equals(compareWith, providerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ToolResult.Fail(
                            "compare_with совпадает с provider — сравнение не имеет смысла.");
                    }

                    if (_registry.Get(compareWith) == null)
                    {
                        return ToolResult.Fail(
                            $"Провайдер '{compareWith}' (compare_with) не зарегистрирован. " +
                            $"Доступные: {string.Join(", ", _registry.GetNames())}.");
                    }
                }

                var includeContext = arguments?["include_context"]?.Value<bool>() ?? false;
                var maxTokens = ParseNullableInt(arguments?["max_tokens"]);
                var temperature = ParseNullableDouble(arguments?["temperature"]);

                if (temperature.HasValue)
                {
                    temperature = Math.Clamp(temperature.Value, MinTemperature, MaxTemperature);
                }

                if (includeContext)
                {
                    // v1.8.1 Фаза 3: реальная инъекция контекста — Фаза 4
                    // (ChatStreamService прокидывает историю). Пока параметр
                    // принимается и логируется, но не влияет на запрос.
                    _logger.LogDebug(
                        "ask_external_llm: include_context=true (в Фазе 3 не инжектится, будет в Фазе 4).");
                }

                var baseRequest = new ExternalLlmRequest
                {
                    Prompt = prompt,
                    IncludeContext = includeContext,
                    MaxTokens = maxTokens,
                    Temperature = temperature
                };

                // ============ Режим «сравнение» ============
                if (!string.IsNullOrWhiteSpace(compareWith))
                {
                    return await ExecuteComparisonAsync(
                        context.UserId, providerName, compareWith, baseRequest);
                }

                // ============ Режим «одиночный» ============
                baseRequest.Provider = providerName;

                var response = await _client.CompleteAsync(
                    context.UserId, baseRequest, context.CancellationToken);

                var data = new
                {
                    provider = response.Provider,
                    content = response.Content,
                    promptTokens = response.PromptTokens,
                    completionTokens = response.CompletionTokens,
                    costUsd = response.CostUsd,
                    durationMs = response.DurationMs
                };

                return ToolResult.Ok(data, $"Ответ от {response.Provider} получен.");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Запрос отменён.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AskExternalLlmTool: ошибка (provider={Provider})",
                    arguments?["provider"]?.ToString());

                return ToolResult.Fail($"Ошибка запроса к внешней LLM: {ex.Message}");
            }
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Режим «сравнение»: два параллельных запроса.
        /// </summary>
        private async Task<ToolResult> ExecuteComparisonAsync(
            int userId,
            string primaryProvider,
            string secondaryProvider,
            ExternalLlmRequest baseRequest)
        {
            var primaryRequest = new ExternalLlmRequest
            {
                Provider = primaryProvider,
                Prompt = baseRequest.Prompt,
                IncludeContext = baseRequest.IncludeContext,
                MaxTokens = baseRequest.MaxTokens,
                Temperature = baseRequest.Temperature
            };

            var secondaryRequest = new ExternalLlmRequest
            {
                Provider = secondaryProvider,
                Prompt = baseRequest.Prompt,
                IncludeContext = baseRequest.IncludeContext,
                MaxTokens = baseRequest.MaxTokens,
                Temperature = baseRequest.Temperature
            };

            ExternalLlmResponse primary;
            ExternalLlmResponse secondary;
            try
            {
                var tasks = new[]
                {
                    _client.CompleteAsync(userId, primaryRequest, default),
                    _client.CompleteAsync(userId, secondaryRequest, default)
                };
                var results = await Task.WhenAll(tasks);
                primary = results[0];
                secondary = results[1];
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(
                    $"Один из запросов сравнения упал: {ex.Message}");
            }

            var dto = new ExternalLlmComparisonDto
            {
                Primary = primary,
                Secondary = secondary,
                TotalCostUsd = primary.CostUsd + secondary.CostUsd
            };

            return ToolResult.Ok(
                dto,
                $"Сравнение {primary.Provider} и {secondary.Provider} получено.");
        }

        /// <summary>
        /// Возвращает имя провайдера: явный <c>provider</c> или <c>DefaultProvider</c>.
        /// </summary>
        private string ResolveProviderName(JObject arguments)
        {
            var explicitProvider = arguments?["provider"]?.ToString();
            if (!string.IsNullOrWhiteSpace(explicitProvider))
                return explicitProvider;

            return _registry.DefaultProvider;
        }

        /// <summary>
        /// Парсит nullable int из JToken.
        /// </summary>
        private static int? ParseNullableInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            return int.TryParse(token.ToString(), out var v) ? v : (int?)null;
        }

        /// <summary>
        /// Парсит nullable double из JToken.
        /// </summary>
        private static double? ParseNullableDouble(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<double>();

            return double.TryParse(token.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var v) ? v : (double?)null;
        }
    }
}