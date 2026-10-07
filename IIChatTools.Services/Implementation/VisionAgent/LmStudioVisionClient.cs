using System;
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

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Клиент Vision LLM к LM Studio (multimodal: PNG + prompt).
    /// Singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф5.1). См. DESIGN § 3.2, § 4.4.
    /// </para>
    /// <para>
    /// <b>Multimodal POST:</b> <c>/v1/chat/completions</c> с <c>content</c> в
    /// виде массива (text + image_url с data-URL PNG base64).
    /// </para>
    /// <para>
    /// <b>Timeout:</b> через <c>CancellationTokenSource.CancelAfter</c>
    /// (RULES § 4.48), не через <c>HttpClient.Timeout</c>.
    /// </para>
    /// </remarks>
    public sealed class LmStudioVisionClient : IVisionLlmClient
    {
        private readonly VisionLlmOptions _visionOptions;
        private readonly string _lmStudioBaseUrl;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LmStudioVisionClient> _logger;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent</c>).</param>
        /// <param name="configuration">Конфигурация приложения (для <c>LmStudio:BaseUrl</c>).</param>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов.</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null.</exception>
        public LmStudioVisionClient(
            IOptions<VisionAgentOptions> options,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<LmStudioVisionClient> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _visionOptions = options.Value.VisionLlm
                ?? throw new InvalidOperationException("Секция VisionAgent:VisionLlm не задана.");
            _lmStudioBaseUrl = configuration?["LmStudio:BaseUrl"]
                ?? throw new ArgumentNullException(nameof(configuration), "LmStudio:BaseUrl не задан.");
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsReady =>
            !string.IsNullOrWhiteSpace(_visionOptions.Model) &&
            !string.IsNullOrWhiteSpace(_lmStudioBaseUrl);

        /// <inheritdoc />
        public async Task<ScreenDescriptionDto> DescribeAsync(
            byte[] screenshotPng,
            CancellationToken cancellationToken = default)
        {
            if (screenshotPng == null || screenshotPng.Length == 0)
            {
                throw new ArgumentException("PNG-скриншот пуст.", nameof(screenshotPng));
            }

            if (!IsReady)
            {
                throw new InvalidOperationException(
                    "LmStudioVisionClient не готов: не задан Model или LmStudio:BaseUrl.");
            }

            // 1. Кодируем PNG в base64 data-URL.
            var base64 = Convert.ToBase64String(screenshotPng);
            var dataUrl = $"data:image/png;base64,{base64}";

            // 2. Формируем тело запроса (OpenAI-совместимый multimodal формат).
            var body = new JObject
            {
                ["model"] = _visionOptions.Model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = VisionSystemPrompt.VisionUiDescribe
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "text",
                                ["text"] = "Опиши UI на скриншоте. Ответ — строго JSON."
                            },
                            new JObject
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new JObject { ["url"] = dataUrl }
                            }
                        }
                    }
                },
                ["max_tokens"] = _visionOptions.MaxTokens,
                ["temperature"] = _visionOptions.Temperature,
                ["stream"] = false
            };

            var json = body.ToString(Formatting.None);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 3. Timeout через CancelAfter (RULES § 4.48).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_visionOptions.TimeoutSeconds));

            var client = _httpClientFactory.CreateClient();
            var url = $"{_lmStudioBaseUrl.TrimEnd('/')}/v1/chat/completions";

            _logger.LogDebug(
                "VisionAgent: DescribeAsync — model={Model}, pngBytes={Bytes}",
                _visionOptions.Model, screenshotPng.Length);

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "VisionAgent: DescribeAsync timeout ({Seconds} сек)",
                    _visionOptions.TimeoutSeconds);
                throw new TimeoutException(
                    $"Vision LLM не ответила за {_visionOptions.TimeoutSeconds} секунд.");
            }

            using (response)
            {
                var responseText = await response.Content
                    .ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "VisionAgent: DescribeAsync HTTP {Status}: {Body}",
                        (int)response.StatusCode,
                        Truncate(responseText, 500));
                    throw new HttpRequestException(
                        $"Vision LLM вернула HTTP {(int)response.StatusCode}.");
                }

                // 4. Извлечь content из ответа.
                var assistantContent = ExtractAssistantContent(responseText);
                if (string.IsNullOrEmpty(assistantContent))
                {
                    _logger.LogWarning(
                        "VisionAgent: DescribeAsync — пустой content в ответе (len={Len})",
                        responseText.Length);
                    return new ScreenDescriptionDto
                    {
                        Description = string.Empty,
                        UiElements = new System.Collections.Generic.List<UiElementDto>()
                    };
                }

                // 5. Парсим.
                var result = ScreenDescriptionParser.Parse(assistantContent);

                _logger.LogDebug(
                    "VisionAgent: DescribeAsync — description={DescLen} символов, ui_elements={Count}",
                    result.Description?.Length ?? 0, result.UiElements?.Count ?? 0);

                return result;
            }
        }

        /// <inheritdoc />
        public async Task<VerifyTargetResultDto> VerifyTargetAsync(
            byte[] croppedPng,
            string targetDescription,
            UiElementBoundsDto originalBounds,
            CancellationToken cancellationToken = default)
        {
            if (croppedPng == null || croppedPng.Length == 0)
            {
                return new VerifyTargetResultDto
                {
                    Found = false,
                    Error = "PNG-кроп пуст."
                };
            }

            if (!IsReady)
            {
                return new VerifyTargetResultDto
                {
                    Found = false,
                    Error = "LmStudioVisionClient не готов."
                };
            }

            // 1. Кодируем кроп в base64 data-URL.
            var base64 = Convert.ToBase64String(croppedPng);
            var dataUrl = $"data:image/png;base64,{base64}";

            // 2. System prompt с подставленным targetDescription.
            var systemPrompt = VisionSystemPrompt.GetVerifyTargetPrompt(targetDescription);

            // 3. Тело запроса (OpenAI-совместимый multimodal).
            var body = new JObject
            {
                ["model"] = _visionOptions.Model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = systemPrompt
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "text",
                                ["text"] = "Верни координаты центра элемента в JSON."
                            },
                            new JObject
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new JObject { ["url"] = dataUrl }
                            }
                        }
                    }
                },
                ["max_tokens"] = 256,   // мало нужно: 4 поля JSON.
                ["temperature"] = 0.0f, // детерминированно.
                ["stream"] = false
            };

            var json = body.ToString(Formatting.None);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_visionOptions.TimeoutSeconds));

            var client = _httpClientFactory.CreateClient();
            var url = $"{_lmStudioBaseUrl.TrimEnd('/')}/v1/chat/completions";

            _logger.LogDebug(
                "VisionAgent: VerifyTargetAsync — model={Model}, cropBytes={Bytes}, target='{Target}', origBounds=({X},{Y},{W},{H})",
                _visionOptions.Model, croppedPng.Length, targetDescription,
                originalBounds?.X ?? 0, originalBounds?.Y ?? 0,
                originalBounds?.W ?? 0, originalBounds?.H ?? 0);

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "VisionAgent: VerifyTargetAsync timeout ({Seconds} сек)",
                    _visionOptions.TimeoutSeconds);
                return new VerifyTargetResultDto
                {
                    Found = false,
                    Error = $"Vision LLM не ответила за {_visionOptions.TimeoutSeconds} секунд."
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VisionAgent: VerifyTargetAsync HTTP-ошибка");
                return new VerifyTargetResultDto
                {
                    Found = false,
                    Error = $"HTTP-ошибка: {ex.Message}"
                };
            }

            using (response)
            {
                var responseText = await response.Content
                    .ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "VisionAgent: VerifyTargetAsync HTTP {Status}: {Body}",
                        (int)response.StatusCode, Truncate(responseText, 300));
                    return new VerifyTargetResultDto
                    {
                        Found = false,
                        Error = $"Vision LLM вернула HTTP {(int)response.StatusCode}."
                    };
                }

                var assistantContent = ExtractAssistantContent(responseText);
                if (string.IsNullOrEmpty(assistantContent))
                {
                    return new VerifyTargetResultDto
                    {
                        Found = false,
                        Error = "VL вернула пустой content."
                    };
                }

                // 4. Парсим — VerifyResponseParser никогда не бросает.
                var result = VerifyResponseParser.Parse(assistantContent);

                _logger.LogDebug(
                    "VisionAgent: VerifyTargetAsync — Found={Found}, ({X},{Y}), conf={Conf:F2}, error={Err}",
                    result.Found, result.X, result.Y, result.Confidence,
                    result.Error ?? "(нет)");

                return result;
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

                // Content может быть строкой или массивом (некоторые модели).
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

        /// <summary>
        /// Усечение длинной строки для логов.
        /// </summary>
        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) + "…";
        }
    }
}