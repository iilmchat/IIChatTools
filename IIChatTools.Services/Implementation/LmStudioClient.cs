using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация клиента LM Studio (OpenAI-совместимый API).
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
            var maxTokens = GetInt("LmStudio:MaxTokens", 4096);
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

                var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content, cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("LM Studio вернул {StatusCode}: {Body}", response.StatusCode, Truncate(responseBody, 500));
                    throw new InvalidOperationException(
                        $"LM Studio вернул ошибку {(int)response.StatusCode}: {Truncate(responseBody, 500)}");
                }

                var json = JObject.Parse(responseBody);
                var choice = json["choices"]?[0];
                if (choice == null)
                    throw new InvalidOperationException("LM Studio не вернул choices");

                var message = choice["message"];
                var finishReason = choice["finish_reason"]?.ToString();

                return new ChatCompletionResponse
                {
                    Content = message?["content"]?.ToString(),
                    ToolCalls = message?["tool_calls"] as JArray,
                    FinishReason = finishReason
                };
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