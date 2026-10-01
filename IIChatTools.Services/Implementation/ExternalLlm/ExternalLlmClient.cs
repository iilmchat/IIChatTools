using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm.Formats;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ExternalLlm
{
    /// <summary>
    /// Клиент внешних LLM (v1.8.1, KI-109; v1.9.0, KI-110a — Anthropic).
    ///
    /// <para>
    /// <b>Singleton.</b> Stateless, использует <see cref="IHttpClientFactory"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Форматы:</b>
    /// <list type="bullet">
    ///   <item><description><see cref="ProviderFormat.OpenAI"/> — <c>POST {BaseUrl}/chat/completions</c>,
    ///   <c>Authorization: Bearer</c>. Провайдеры: DeepSeek, OpenAI, Groq, Together AI, Ollama.</description></item>
    ///   <item><description><see cref="ProviderFormat.Anthropic"/> — <c>POST {BaseUrl}/messages</c>,
    ///   <c>x-api-key</c> + <c>anthropic-version: 2023-06-01</c>.</description></item>
    ///   <item><description><see cref="ProviderFormat.Gemini"/> — <c>POST {BaseUrl}/models/{model}:generateContent</c>,
    ///   <c>x-goog-api-key</c> (v1.10.0, KI-110b).</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Внутри CompleteAsync (общая обвязка для всех форматов):</b>
    /// <list type="number">
    ///   <item>валидация запроса (prompt не пуст);</item>
    ///   <item>резолв провайдера (request.Provider или DefaultProvider);</item>
    ///   <item>проверка circuit breaker;</item>
    ///   <item>проверка дневного бюджета (per-user);</item>
    ///   <item>резолв API-ключа из конфигурации;</item>
    ///   <item>switch по <see cref="ProviderFormat"/> → приватный метод
    ///   (<c>CompleteOpenAiAsync</c> / <c>CompleteAnthropicAsync</c>);</item>
    ///   <item>расчёт стоимости (<c>ProviderCostCalculator</c>), запись
    ///   в breaker + budget tracker;</item>
    ///   <item>return <see cref="ExternalLlmResponse"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только метаданные (длина prompt, токены,
    /// стоимость, длительность). Никогда не логируются prompt, content,
    /// API-ключ. Значение заголовка <c>x-api-key</c> / <c>Authorization</c>
    /// не выводится в лог даже при ошибке.
    /// </para>
    /// </summary>
    public sealed class ExternalLlmClient : IExternalLlmClient
    {
        private const int MaxRetryAttempts = 2;              // 1 попытка + 1 retry
        private const int RetryDelayMs = 1000;
        private static readonly TimeSpan TestConnectionTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Значение заголовка <c>anthropic-version</c> — обязателен для
        /// Anthropic Messages API (DESIGN § 3.3, KI-110a).
        /// </summary>
        internal const string AnthropicApiVersion = "2023-06-01";

        private readonly IExternalProviderRegistry _registry;
        private readonly IExternalLlmCircuitBreaker _circuitBreaker;
        private readonly IExternalLlmBudgetTracker _budgetTracker;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ExternalLlmClient> _logger;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="registry">Реестр провайдеров</param>
        /// <param name="circuitBreaker">Circuit breaker (per-provider)</param>
        /// <param name="budgetTracker">Дневной бюджет (per-user)</param>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов</param>
        /// <param name="configuration">Конфигурация (для резолва API-ключей)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public ExternalLlmClient(
            IExternalProviderRegistry registry,
            IExternalLlmCircuitBreaker circuitBreaker,
            IExternalLlmBudgetTracker budgetTracker,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<ExternalLlmClient> logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
            _budgetTracker = budgetTracker ?? throw new ArgumentNullException(nameof(budgetTracker));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<ExternalLlmResponse> CompleteAsync(
            int userId,
            ExternalLlmRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new InvalidOperationException("ExternalLlmRequest.Prompt не задан.");

            // 1. Резолв провайдера.
            var providerName = !string.IsNullOrWhiteSpace(request.Provider)
                ? request.Provider
                : _registry.DefaultProvider;

            var provider = _registry.Get(providerName);
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"Провайдер '{providerName}' не зарегистрирован. " +
                    $"Доступные: {string.Join(", ", _registry.GetNames())}.");
            }

            // 2. Circuit breaker.
            if (_circuitBreaker.IsOpen(providerName))
            {
                var status = _circuitBreaker.GetStatus(providerName);
                throw new InvalidOperationException(
                    $"Провайдер '{providerName}' временно недоступен: " +
                    $"{status?.LastError ?? "circuit breaker open"}.");
            }

            // 3. Дневной бюджет (per-user).
            if (!_budgetTracker.CanSpend(userId))
            {
                throw new InvalidOperationException(
                    "Превышен дневной бюджет External-LLM. Повторите завтра (UTC).");
            }

            // 4. API-ключ (для Ollama может быть null — ApiKeySecretName не задан).
            var apiKey = ResolveApiKey(providerName, provider);

            // 5. HTTP + парсинг. Switch по Format — единственная точка ветвления.
            var sw = Stopwatch.StartNew();
            (string Content, int PromptTokens, int CompletionTokens) parsed;
            try
            {
                parsed = provider.Format switch
                {
                    ProviderFormat.OpenAI =>
                        await CompleteOpenAiAsync(provider, apiKey, request, providerName, ct),

                    ProviderFormat.Anthropic =>
                        await CompleteAnthropicAsync(provider, apiKey, request, providerName, ct),

                    ProviderFormat.Gemini =>
                        await CompleteGeminiAsync(provider, apiKey, request, providerName, ct),

                    _ => throw new InvalidOperationException(
                        $"Неизвестный ProviderFormat: {provider.Format} " +
                        $"(провайдер '{providerName}').")
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _circuitBreaker.RecordFailure(providerName, ex.Message);
                _logger.LogWarning(
                    "External-LLM: {Provider} failed после {Attempts} попыток: {Error}",
                    providerName, MaxRetryAttempts, ex.Message);
                throw;
            }
            sw.Stop();

            // 6. Расчёт стоимости (одинаков для всех форматов).
            var cost = ProviderCostCalculator.Calculate(
                parsed.PromptTokens, parsed.CompletionTokens, provider);

            // 7. Учёты.
            _circuitBreaker.RecordSuccess(providerName);
            _budgetTracker.RecordUsage(
                userId, parsed.PromptTokens, parsed.CompletionTokens, cost);

            _logger.LogInformation(
                "External-LLM: {Provider} ({Format}) promptLen={PromptLen} " +
                "tokens={PromptTokens}+{CompletionTokens} cost=${Cost} durationMs={Duration}",
                providerName, provider.Format, request.Prompt.Length,
                parsed.PromptTokens, parsed.CompletionTokens, cost, sw.ElapsedMilliseconds);

            // 8. Ответ.
            return new ExternalLlmResponse
            {
                Provider = providerName,
                Content = parsed.Content,
                PromptTokens = parsed.PromptTokens,
                CompletionTokens = parsed.CompletionTokens,
                CostUsd = cost,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        /// <inheritdoc />
        public async Task<bool> TestConnectionAsync(
            string providerName,
            CancellationToken ct = default)
        {
            var provider = _registry.Get(providerName);
            if (provider == null)
                return false;

            try
            {
                var client = _httpClientFactory.CreateClient();

                var url = provider.BaseUrl.TrimEnd('/') + "/models";

                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

                if (!string.IsNullOrWhiteSpace(provider.ApiKeySecretName))
                {
                    var apiKey = _configuration[provider.ApiKeySecretName];
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        httpRequest.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", apiKey);
                    }
                }

                // Тот же приём, что в SendOnceAsync: не трогаем HttpClient.Timeout,
                // используем CancellationTokenSource.CancelAfter (RULES § 4.48).
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TestConnectionTimeout);

                using var response = await client.SendAsync(httpRequest, timeoutCts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "External-LLM: TestConnection({Provider}) — недоступен.",
                    providerName);
                return false;
            }
        }

        // ============================================================
        // Private — форматы
        // ============================================================

        /// <summary>
        /// OpenAI-совместимый формат: <c>POST {BaseUrl}/chat/completions</c>,
        /// <c>Authorization: Bearer &lt;key&gt;</c>.
        ///
        /// <para>
        /// Провайдеры: DeepSeek, OpenAI, Groq, Together AI, Ollama.
        /// Возвращает распарсенный content + токены; сборка DTO
        /// и расчёт стоимости — в <see cref="CompleteAsync"/>.
        /// </para>
        /// </summary>
        /// <param name="provider">Настройки провайдера</param>
        /// <param name="apiKey">API-ключ (может быть <c>null</c> для Ollama)</param>
        /// <param name="request">Запрос</param>
        /// <param name="providerName">Имя провайдера (для сообщений об ошибках)</param>
        /// <param name="ct">Токен отмены</param>
        /// <returns>Кортеж (content, promptTokens, completionTokens)</returns>
        private async Task<(string Content, int PromptTokens, int CompletionTokens)>
            CompleteOpenAiAsync(
                ExternalProviderOptions provider,
                string apiKey,
                ExternalLlmRequest request,
                string providerName,
                CancellationToken ct)
        {
            var payload = new JObject
            {
                ["model"] = provider.Model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = request.Prompt
                    }
                },
                ["temperature"] = request.Temperature ?? 0.7,
                ["max_tokens"] = request.MaxTokens ?? provider.MaxTokens,
                ["stream"] = false
            };

            var url = provider.BaseUrl.TrimEnd('/') + "/chat/completions";
            var timeoutSec = Math.Clamp(provider.TimeoutSeconds, 1, 600);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                headers["Authorization"] = $"Bearer {apiKey}";
            }

            var responseJson = await SendWithRetryAsync(url, payload, headers, timeoutSec, ct);

            var choice = responseJson["choices"]?[0];
            if (choice == null)
            {
                throw new InvalidOperationException(
                    $"Провайдер '{providerName}' вернул ответ без choices[].");
            }

            var content = choice["message"]?["content"]?.ToString() ?? string.Empty;
            var promptTokens = responseJson["usage"]?["prompt_tokens"]?.Value<int>() ?? 0;
            var completionTokens = responseJson["usage"]?["completion_tokens"]?.Value<int>() ?? 0;

            return (content, promptTokens, completionTokens);
        }

        /// <summary>
        /// Anthropic Messages API: <c>POST {BaseUrl}/messages</c>,
        /// <c>x-api-key</c> + <c>anthropic-version: 2023-06-01</c>
        /// (DESIGN § 3.3, KI-110a).
        ///
        /// <para>
        /// Тело собирает <see cref="AnthropicRequestBuilder"/>
        /// (<c>system</c> — отдельное поле, <c>max_tokens</c> обязателен,
        /// <c>temperature</c> clamp [0, 1]), ответ парсит
        /// <see cref="AnthropicResponseParser"/> (склейка блоков
        /// <c>content[type=text]</c> через <c>\n</c>, <c>usage.input_tokens</c>
        /// / <c>usage.output_tokens</c>).
        /// </para>
        ///
        /// <para>
        /// Блоки <c>type=="tool_use"</c> игнорируются (v1.9.0 — без function
        /// calling). <c>stop_reason</c> не извлекается (не критично для
        /// v1.9.0).
        /// </para>
        /// </summary>
        /// <param name="provider">Настройки провайдера</param>
        /// <param name="apiKey">API-ключ (Anthropic всегда требует)</param>
        /// <param name="request">Запрос (prompt, system, temperature, maxTokens)</param>
        /// <param name="providerName">Имя провайдера (для сообщений об ошибках)</param>
        /// <param name="ct">Токен отмены</param>
        /// <returns>Кортеж (content, promptTokens, completionTokens)</returns>
        private async Task<(string Content, int PromptTokens, int CompletionTokens)>
            CompleteAnthropicAsync(
                ExternalProviderOptions provider,
                string apiKey,
                ExternalLlmRequest request,
                string providerName,
                CancellationToken ct)
        {
            // Тело собирает helper из Фазы 2.
            var payload = AnthropicRequestBuilder.Build(provider, request);

            var url = provider.BaseUrl.TrimEnd('/') + "/messages";
            var timeoutSec = Math.Clamp(provider.TimeoutSeconds, 1, 600);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["anthropic-version"] = AnthropicApiVersion
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                headers["x-api-key"] = apiKey;
            }

            var responseJson = await SendWithRetryAsync(url, payload, headers, timeoutSec, ct);

            // Парсер не падает при отсутствии полей (DESIGN § 3.5).
            // Если Anthropic вернул error-ответ при HTTP 200 (маловероятно) —
            // парсер вернёт пустую строку + 0 токенов, cost = 0.
            return AnthropicResponseParser.Parse(responseJson);
        }

        /// <summary>
        /// Google Gemini API: <c>POST {BaseUrl}/models/{Model}:generateContent</c>,
        /// <c>x-goog-api-key</c> (v1.10.0, KI-110b, DESIGN_GEMINI § 3.3).
        ///
        /// <para>
        /// Тело собирает <see cref="GeminiRequestBuilder"/>
        /// (<c>contents[]</c>, <c>systemInstruction</c> — отдельный Content-объект,
        /// <c>generationConfig.maxOutputTokens</c> обязателен), ответ парсит
        /// <see cref="GeminiResponseParser"/> (склейка
        /// <c>candidates[0].content.parts[].text</c> через <c>\n</c>,
        /// <c>usageMetadata.promptTokenCount</c> / <c>candidatesTokenCount</c>).
        /// </para>
        ///
        /// <para>
        /// <b>Модель в URL, не в body</b> — в отличие от OpenAI/Anthropic,
        /// Gemini требует <c>/models/{model}:generateContent</c>.
        /// </para>
        ///
        /// <para>
        /// <b>Auth</b> — заголовок <c>x-goog-api-key</c> (не Bearer, не query).
        /// Ключ не логируется.
        /// </para>
        ///
        /// <para>
        /// Блоки <c>thought: true</c>, <c>functionCall</c>, <c>functionResponse</c>,
        /// <c>inlineData</c>, <c>codeExecutionResult</c> игнорируются
        /// (v1.10.0 — только prompt → text).
        /// </para>
        /// </summary>
        /// <param name="provider">Настройки провайдера</param>
        /// <param name="apiKey">API-ключ (Gemini всегда требует)</param>
        /// <param name="request">Запрос (prompt, system, temperature, maxTokens)</param>
        /// <param name="providerName">Имя провайдера (для сообщений об ошибках)</param>
        /// <param name="ct">Токен отмены</param>
        /// <returns>Кортеж (content, promptTokens, completionTokens)</returns>
        private async Task<(string Content, int PromptTokens, int CompletionTokens)>
            CompleteGeminiAsync(
                ExternalProviderOptions provider,
                string apiKey,
                ExternalLlmRequest request,
                string providerName,
                CancellationToken ct)
        {
            // Тело собирает helper из Фазы 1 (DESIGN_GEMINI § 3.1).
            var payload = GeminiRequestBuilder.Build(provider, request);

            // URL с моделью в пути (специфика Gemini — /models/{model}:generateContent).
            var url = $"{provider.BaseUrl.TrimEnd('/')}/models/{provider.Model}:generateContent";
            var timeoutSec = Math.Clamp(provider.TimeoutSeconds, 1, 600);

            // Заголовки: x-goog-api-key (DESIGN_GEMINI § 6.1).
            // Не Authorization: Bearer, не query ?key=.
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                headers["x-goog-api-key"] = apiKey;
            }

            var responseJson = await SendWithRetryAsync(url, payload, headers, timeoutSec, ct);

            // Парсер не падает при отсутствии полей (DESIGN_GEMINI § 3.2 / § 3.4).
            // SAFETY / пустой candidates[] → пустая строка + токены.
            return GeminiResponseParser.Parse(responseJson);
        }

        // ============================================================
        // Private — HTTP
        // ============================================================

        /// <summary>
        /// HTTP POST с retry 1 раз при 5xx или 429 (не при 4xx и timeout).
        /// </summary>
        /// <param name="url">Полный URL</param>
        /// <param name="payload">Тело запроса</param>
        /// <param name="headers">Дополнительные заголовки (Authorization / x-api-key / anthropic-version)</param>
        /// <param name="timeoutSec">Таймаут запроса (секунды)</param>
        /// <param name="ct">Внешний токен отмены</param>
        /// <returns>Распарсенный JSON-ответ</returns>
        private async Task<JObject> SendWithRetryAsync(
            string url,
            JObject payload,
            IReadOnlyDictionary<string, string> headers,
            int timeoutSec,
            CancellationToken ct)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    return await SendOnceAsync(url, payload, headers, timeoutSec, ct);
                }
                catch (Exception ex) when (IsTransient(ex) && attempt < MaxRetryAttempts)
                {
                    lastException = ex;

                    _logger.LogDebug(
                        "External-LLM: transient error на попытке {Attempt}: {Error}. Retry через {Delay} мс.",
                        attempt, ex.Message, RetryDelayMs);

                    await Task.Delay(RetryDelayMs, ct);
                }
            }

            throw lastException
                ?? new InvalidOperationException("External-LLM: все попытки исчерпаны.");
        }

        /// <summary>
        /// Одна попытка HTTP POST.
        /// </summary>
        /// <param name="url">Полный URL</param>
        /// <param name="payload">Тело запроса</param>
        /// <param name="headers">Дополнительные заголовки</param>
        /// <param name="timeoutSec">Таймаут запроса (секунды)</param>
        /// <param name="ct">Внешний токен отмены</param>
        /// <returns>Распарсенный JSON-ответ</returns>
        private async Task<JObject> SendOnceAsync(
            string url,
            JObject payload,
            IReadOnlyDictionary<string, string> headers,
            int timeoutSec,
            CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient();

            // ВАЖНО: HttpClient.Timeout нельзя менять после первого SendAsync
            // (InvalidOperationException: "This instance has already started...").
            // В retry-цикле с переиспользованием HttpClient (mock, кэш, DI-контейнер)
            // это ломается. Используем CancellationTokenSource.CancelAfter —
            // таймаут привязан к конкретному запросу, а не к клиенту.
            // Handler ОБЯЗАН уважать ct (RULES § 4.35).

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    payload.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };

            // Заголовки. Authorization — типизировано (Headers.Authorization),
            // остальные (x-api-key, anthropic-version) — через TryAddWithoutValidation.
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    if (string.IsNullOrEmpty(kv.Value))
                        continue;

                    if (string.Equals(kv.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        // Формат "Scheme value" (Bearer xxx / Basic yyy).
                        var spaceIndex = kv.Value.IndexOf(' ');
                        if (spaceIndex > 0)
                        {
                            httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
                                kv.Value.Substring(0, spaceIndex),
                                kv.Value.Substring(spaceIndex + 1));
                        }
                        else
                        {
                            httpRequest.Headers.TryAddWithoutValidation("Authorization", kv.Value);
                        }
                    }
                    else
                    {
                        httpRequest.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Внешняя отмена (Stop в чате).
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout (timeoutCts сработал).
                throw new TimeoutException(
                    $"Провайдер не ответил за {timeoutSec} секунд.");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var truncated = Truncate(body, 400);

                    // 5xx и 429 — transient, обрабатывается на уровне retry.
                    if (statusCode >= 500 || statusCode == 429)
                    {
                        throw new HttpRequestException(
                            $"HTTP {statusCode}: {truncated}");
                    }

                    // 4xx (кроме 429) — постоянная ошибка (неверный ключ, модель).
                    throw new InvalidOperationException(
                        $"Провайдер вернул HTTP {statusCode}: {truncated}");
                }

                try
                {
                    return JObject.Parse(body);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Не удалось распарсить ответ провайдера: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Transient ли ошибка (можно retry): 5xx / 429 / сеть.
        /// </summary>
        /// <param name="ex">Исключение</param>
        /// <returns>true, если ошибку имеет смысл повторить</returns>
        private static bool IsTransient(Exception ex)
        {
            if (ex is HttpRequestException)
                return true;

            if (ex is InvalidOperationException)
                return false;

            // Timeout не ретраим — если провайдер не ответил за 60 с, второй
            // раз ждать бессмысленно (DESIGN § 4.1 — retry только для 5xx/429).
            return false;
        }

        /// <summary>
        /// Резолвит API-ключ из конфигурации. Возвращает <c>null</c>,
        /// если <c>ApiKeySecretName</c> не задан (Ollama).
        /// </summary>
        /// <param name="providerName">Имя провайдера</param>
        /// <param name="provider">Настройки провайдера</param>
        /// <returns>API-ключ или <c>null</c></returns>
        /// <exception cref="InvalidOperationException">
        /// Если <c>ApiKeySecretName</c> задан, но значение не найдено.
        /// </exception>
        private string ResolveApiKey(string providerName, ExternalProviderOptions provider)
        {
            if (string.IsNullOrWhiteSpace(provider.ApiKeySecretName))
                return null;

            var apiKey = _configuration[provider.ApiKeySecretName];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    $"API-ключ для '{providerName}' не найден в конфигурации. " +
                    $"Задайте через User Secrets: " +
                    $"dotnet user-secrets set \"{provider.ApiKeySecretName}\" \"<key>\".");
            }

            return apiKey;
        }

        /// <summary>
        /// Обрезает строку до указанной длины.
        /// </summary>
        /// <param name="value">Строка</param>
        /// <param name="max">Максимум символов</param>
        /// <returns>Обрезанная строка с «…»</returns>
        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }
    }
}