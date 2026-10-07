using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Клиент Planner LLM к LM Studio (text-only).
    /// Принимает task + history + screen + plan, возвращает <see cref="VisionActionDto"/>.
    /// Singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф5.2). См. DESIGN § 3.2, § 4.5.
    /// </para>
    /// <para>
    /// <b>Формат запроса:</b> OpenAI-совместимый <c>/v1/chat/completions</c>.
    /// <c>system</c> = <see cref="VisionSystemPrompt.PlannerPlanNext"/>,
    /// <c>user</c> = JSON с полями <c>task</c>, <c>history</c>, <c>screen</c>,
    /// <c>plan</c> (camelCase).
    /// </para>
    /// <para>
    /// <b>Timeout:</b> через <c>CancellationTokenSource.CancelAfter</c> (RULES § 4.48).
    /// </para>
    /// </remarks>
    public sealed class LmStudioPlannerClient : IPlannerLlmClient
    {
        private readonly PlannerLlmOptions _plannerOptions;
        private readonly string _lmStudioBaseUrl;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LmStudioPlannerClient> _logger;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent</c>).</param>
        /// <param name="configuration">Конфигурация приложения (для <c>LmStudio:BaseUrl</c>).</param>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов.</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null.</exception>
        public LmStudioPlannerClient(
            IOptions<VisionAgentOptions> options,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<LmStudioPlannerClient> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _plannerOptions = options.Value.PlannerLlm
                ?? throw new InvalidOperationException("Секция VisionAgent:PlannerLlm не задана.");
            _lmStudioBaseUrl = configuration?["LmStudio:BaseUrl"]
                ?? throw new ArgumentNullException(nameof(configuration), "LmStudio:BaseUrl не задан.");
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsReady =>
            !string.IsNullOrWhiteSpace(_plannerOptions.Model) &&
            !string.IsNullOrWhiteSpace(_lmStudioBaseUrl);

        /// <inheritdoc />
        public async Task<VisionActionDto> PlanNextAsync(
            string task,
            IReadOnlyList<VisionStepDto> history,
            ScreenDescriptionDto screen,
            IReadOnlyList<string> plan,
            int userId = 0,                                        // v1.12.0 (KI-131, Ф5.3): не используется локально.
            CancellationToken cancellationToken = default)
        {
            if (!IsReady)
            {
                throw new InvalidOperationException(
                    "LmStudioPlannerClient не готов: не задан Model или LmStudio:BaseUrl.");
            }

            // 1. Обрезаем history до MaxHistorySteps.
            var maxHistory = Math.Max(1, _plannerOptions.MaxHistorySteps);
            var trimmedHistory = history != null && history.Count > maxHistory
                ? history.Skip(history.Count - maxHistory).ToList()
                : (history?.ToList() ?? new List<VisionStepDto>());

            // 2. Формируем user-message (task + history + screen + plan).
            var userMessage = VisionPlannerPayloadBuilder.Build(
                task, trimmedHistory, screen, plan);

            // 3. Тело запроса.
            var body = new JObject
            {
                ["model"] = _plannerOptions.Model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = VisionSystemPrompt.PlannerPlanNext
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = userMessage
                    }
                },
                ["max_tokens"] = _plannerOptions.MaxTokens,
                ["temperature"] = _plannerOptions.Temperature,
                ["stream"] = false
            };

            var json = body.ToString(Formatting.None);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 4. Timeout через CancelAfter (RULES § 4.48).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_plannerOptions.TimeoutSeconds));

            // RULES § 4.48 + KI-200: HttpClient.Timeout по умолчанию 100 сек.
            // Наш таймаут — через cts.CancelAfter(TimeoutSeconds). Без этой
            // строки два таймаута конкурируют.
            var client = _httpClientFactory.CreateClient();
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

            var url = $"{_lmStudioBaseUrl.TrimEnd('/')}/v1/chat/completions";

            _logger.LogDebug(
                "VisionAgent: PlanNextAsync — model={Model}, task={TaskLen}, history={HistCount}, screen={UiCount}",
                _plannerOptions.Model, task?.Length ?? 0, trimmedHistory.Count,
                screen?.UiElements?.Count ?? 0);

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "VisionAgent: PlanNextAsync timeout ({Seconds} сек)",
                    _plannerOptions.TimeoutSeconds);
                throw new TimeoutException(
                    $"Planner LLM не ответила за {_plannerOptions.TimeoutSeconds} секунд.");
            }

            using (response)
            {
                var responseText = await response.Content
                    .ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "VisionAgent: PlanNextAsync HTTP {Status}: {Body}",
                        (int)response.StatusCode,
                        Truncate(responseText, 500));
                    throw new HttpRequestException(
                        $"Planner LLM вернула HTTP {(int)response.StatusCode}.");
                }

                var assistantContent = ExtractAssistantContent(responseText);
                if (string.IsNullOrEmpty(assistantContent))
                {
                    _logger.LogWarning(
                        "VisionAgent: PlanNextAsync — пустой content в ответе (len={Len})",
                        responseText.Length);
                    return new VisionActionDto
                    {
                        Action = "fail",
                        Reason = "Planner LLM вернула пустой ответ."
                    };
                }

                var action = VisionActionParser.Parse(assistantContent);

                // KI-197 (Fix 2): LogWarning при fail от парсера — видно
                // в логе сразу, а не только по возвращаемому action.
                // Полезно при отладке галлюцинаций Planner LLM
                // (navigate / goto / open / search / scroll_to).
                if (string.Equals(action.Action, "fail", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "VisionAgent: PlanNextAsync — Planner LLM вернула fail " +
                        "или невалидный action. reason={Reason}, rawContent={RawContent}",
                        action.Reason,
                        Truncate(assistantContent, 300));
                }
                else
                {
                    _logger.LogDebug(
                        "VisionAgent: PlanNextAsync — action={Action}, target={Target}, reason={Reason}",
                        action.Action, action.Target, action.Reason);
                }

                return action;
            }
        }

        /// <summary>
        /// Извлекает <c>choices[0].message.content</c> из ответа LM Studio.
        /// </summary>
        private static string ExtractAssistantContent(string responseText)
        {
            try
            {
                var root = JObject.Parse(responseText);
                var content = root["choices"]?[0]?["message"]?["content"];

                if (content == null || content.Type == JTokenType.Null) return null;

                if (content.Type == JTokenType.String)
                    return content.Value<string>();

                if (content is JArray arr)
                {
                    var sb = new StringBuilder();
                    foreach (var item in arr)
                    {
                        var t = item["text"]?.Value<string>();
                        if (!string.IsNullOrEmpty(t)) sb.Append(t);
                    }
                    return sb.ToString();
                }

                return content.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) + "…";
        }
    }
}