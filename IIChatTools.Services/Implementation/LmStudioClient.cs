using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;                    // StreamReader (можно писать полный путь, как в коде)
using System.Runtime.CompilerServices;   // EnumeratorCancellation

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация клиента LM Studio (OpenAI-совместимый API).
    /// Поддерживает reasoning-модели (поле reasoning_content) и обычные модели.
    /// </summary>
    public class LmStudioClient : ILmStudioClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LmStudioClient> _logger;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public LmStudioClient(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<LmStudioClient> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<ChatCompletionResponse> CompleteAsync(
            JArray messages,
            JArray tools,
            CancellationToken cancellationToken)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));

            var baseUrl = _configuration["LmStudio:BaseUrl"] ?? "http://localhost:8034";
            var model = _configuration["LmStudio:Model"] ?? "local-model";
            var temperature = GetDouble("LmStudio:Temperature", 0.7);
            var maxTokens = GetInt("LmStudio:MaxTokens", 8192);
            var timeoutSeconds = GetInt("LmStudio:RequestTimeoutSeconds", 300);

            var payload = new JObject
            {
                ["model"] = model,
                ["messages"] = messages,
                ["temperature"] = temperature,
                ["max_tokens"] = maxTokens,
                ["stream"] = false
            };

            if (tools != null && tools.Count > 0)
            {
                payload["tools"] = tools;
                payload["tool_choice"] = "auto";
            }

            var url = baseUrl.TrimEnd('/') + "/v1/chat/completions";

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                var content = new StringContent(
                    payload.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json");

                var response = await client.PostAsync(url, content, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "LM Studio вернул {StatusCode}: {Body}",
                        response.StatusCode, Truncate(responseBody, 500));

                    throw new InvalidOperationException(
                        $"LM Studio вернул ошибку {(int)response.StatusCode}: {Truncate(responseBody, 500)}");
                }

                var json = JObject.Parse(responseBody);
                var choice = json["choices"]?[0];
                if (choice == null)
                    throw new InvalidOperationException("LM Studio не вернул choices");

                var message = choice["message"];
                var finishReason = choice["finish_reason"]?.ToString();

                // Нормализуем tool_calls: пустой массив → null
                JArray toolCalls = null;
                if (message?["tool_calls"] is JArray rawToolCalls && rawToolCalls.Count > 0)
                    toolCalls = rawToolCalls;

                var result = new ChatCompletionResponse
                {
                    Content = message?["content"]?.ToString(),
                    ReasoningContent = message?["reasoning_content"]?.ToString(),
                    ToolCalls = toolCalls,
                    FinishReason = finishReason,
                    Usage = ParseUsage(json["usage"] as JObject)
                };

                _logger.LogInformation(
                    "LM Studio: model={Model}, finish={FinishReason}, " +
                    "prompt={PromptTokens}, completion={CompletionTokens}, reasoning={ReasoningTokens}, " +
                    "contentLen={ContentLen}, reasoningLen={ReasoningLen}, toolCalls={ToolCalls}",
                    model,
                    finishReason,
                    result.Usage?.PromptTokens ?? 0,
                    result.Usage?.CompletionTokens ?? 0,
                    result.Usage?.ReasoningTokens ?? 0,
                    result.Content?.Length ?? 0,
                    result.ReasoningContent?.Length ?? 0,
                    result.ToolCalls?.Count ?? 0);

                return result;
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException($"LM Studio не ответил за {timeoutSeconds} секунд");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обращении к LM Studio: {Url}", url);
                throw;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ChatCompletionChunk> ChatStreamAsync(
            JArray messages,
            JArray tools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));

            var baseUrl = _configuration["LmStudio:BaseUrl"] ?? "http://localhost:8034";
            var model = _configuration["LmStudio:Model"] ?? "local-model";
            var temperature = GetDouble("LmStudio:Temperature", 0.7);
            var maxTokens = GetInt("LmStudio:MaxTokens", 8192);
            var timeoutSeconds = GetInt("LmStudio:RequestTimeoutSeconds", 300);

            var payload = new JObject
            {
                ["model"] = model,
                ["messages"] = messages,
                ["temperature"] = temperature,
                ["max_tokens"] = maxTokens,
                ["stream"] = true
            };

            if (tools != null && tools.Count > 0)
            {
                payload["tools"] = tools;
                payload["tool_choice"] = "auto";
            }

            var url = baseUrl.TrimEnd('/') + "/v1/chat/completions";

            _logger.LogInformation(
                "LM Studio SSE-стрим: url={Url}, model={Model}, messages={MsgCount}, tools={ToolCount}",
                url, model, messages.Count, tools?.Count ?? 0);

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    payload.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException($"LM Studio не ответил за {timeoutSeconds} секунд");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "LM Studio SSE вернул {StatusCode}: {Body}",
                    response.StatusCode, Truncate(errBody, 500));
                throw new InvalidOperationException(
                    $"LM Studio вернул ошибку {(int)response.StatusCode}: {Truncate(errBody, 500)}");
            }

            // Читаем SSE-поток построчно
            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
            {
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    string line;
                    try
                    {
                        line = await reader.ReadLineAsync();
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        _logger.LogWarning(ex, "Ошибка чтения SSE-строки");
                        break;
                    }

                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // SSE-формат: "data: {...}" или "data: [DONE]"
                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                        continue; // пропускаем "event:", "id:", "retry:" и пустые

                    var jsonPart = line.Substring(5).Trim();
                    if (jsonPart == "[DONE]")
                    {
                        yield return new ChatCompletionChunk { IsDone = true };
                        yield break;
                    }

                    JObject chunk;
                    try
                    {
                        chunk = JObject.Parse(jsonPart);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Не удалось распарсить SSE-чанк: {Line}", Truncate(jsonPart, 200));
                        continue;
                    }

                    var choice = chunk["choices"]?[0];
                    if (choice == null) continue;

                    var delta = choice["delta"];
                    var finishReason = choice["finish_reason"]?.ToString();

                    var result = new ChatCompletionChunk
                    {
                        DeltaContent = delta?["content"]?.ToString(),
                        DeltaReasoning = delta?["reasoning_content"]?.ToString(),
                        DeltaToolCall = delta?["tool_calls"]?[0] as JObject,
                        FinishReason = finishReason,
                        Usage = ParseUsage(chunk["usage"] as JObject),
                        IsDone = !string.IsNullOrEmpty(finishReason)
                    };

                    yield return result;

                    if (result.IsDone) yield break;
                }
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetModelIdsAsync(CancellationToken cancellationToken)
        {
            var baseUrl = _configuration["LmStudio:BaseUrl"] ?? "http://localhost:8034";
            var timeoutSeconds = GetInt("LmStudio:RequestTimeoutSeconds", 300);
            var url = baseUrl.TrimEnd('/') + "/v1/models";

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 30));

                var responseBody = await client.GetStringAsync(url);
                var json = JObject.Parse(responseBody);
                var data = json["data"] as JArray;

                if (data == null)
                    return Array.Empty<string>();

                return data
                    .Select(x => x["id"]?.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка моделей LM Studio: {Url}", url);
                throw;
            }
        }

        /// <summary>
        /// Разбирает блок usage из ответа LM Studio.
        /// </summary>
        /// <param name="usage">JObject usage или null</param>
        /// <returns>Объект с расходом токенов или null</returns>
        private static ChatCompletionUsage ParseUsage(JObject usage)
        {
            if (usage == null) return null;

            return new ChatCompletionUsage
            {
                PromptTokens = usage["prompt_tokens"]?.Value<int>() ?? 0,
                CompletionTokens = usage["completion_tokens"]?.Value<int>() ?? 0,
                TotalTokens = usage["total_tokens"]?.Value<int>() ?? 0,
                ReasoningTokens = usage["completion_tokens_details"]?["reasoning_tokens"]?.Value<int>() ?? 0
            };
        }

        /// <summary>
        /// Извлекает значение double из конфигурации.
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <returns>Значение</returns>
        private double GetDouble(string key, double defaultValue)
        {
            var raw = _configuration[key];
            return double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
        }

        /// <summary>
        /// Извлекает значение int из конфигурации.
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <returns>Значение</returns>
        private int GetInt(string key, int defaultValue)
        {
            var raw = _configuration[key];
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        /// <summary>
        /// Обрезает строку до указанной длины.
        /// </summary>
        /// <param name="value">Строка</param>
        /// <param name="max">Максимум символов</param>
        /// <returns>Обрезанная строка</returns>
        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }
    }
}