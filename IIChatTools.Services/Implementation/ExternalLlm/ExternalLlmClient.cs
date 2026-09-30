using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ExternalLlm
{
    /// <summary>
    /// Клиент внешних LLM (OpenAI-совместимый API)
    /// (v1.8.1, KI-109, Фаза 2.5, DESIGN_EXTERNAL_LLM § 4.1).
    ///
    /// <para>
    /// <b>Singleton.</b> Stateless, использует <see cref="IHttpClientFactory"/>.
    /// Провайдеры: DeepSeek, OpenAI, Groq, Together AI, Ollama.
    /// </para>
    ///
    /// <para>
    /// <b>Внутри CompleteAsync:</b>
    /// <list type="number">
    ///   <item>валидация запроса (prompt не пуст);</item>
    ///   <item>резолв провайдера (request.Provider или DefaultProvider);</item>
    ///   <item>проверка circuit breaker;</item>
    ///   <item>проверка дневного бюджета (per-user);</item>
    ///   <item>резолв API-ключа из конфигурации;</item>
    ///   <item>HTTP POST с Bearer-токеном и retry 1× при 5xx/429;</item>
    ///   <item>парсинг ответа, расчёт стоимости;</item>
    ///   <item>запись успеха в breaker + расход в budget tracker.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только метаданные (длина prompt, токены,
    /// стоимость, длительность). Никогда не логируются prompt и content.
    /// </para>
    /// </summary>
    public sealed class ExternalLlmClient : IExternalLlmClient
    {
        private const int MaxRetryAttempts = 2;              // 1 попытка + 1 retry
        private const int RetryDelayMs = 1000;
        private static readonly TimeSpan TestConnectionTimeout = TimeSpan.FromSeconds(10);

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
                    $"Превышен дневной бюджет External-LLM. Повторите завтра (UTC).");
            }

            // 4. API-ключ.
            var apiKey = ResolveApiKey(providerName, provider);

            // 5. Формирование OpenAI-совместимого запроса.
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

            // 6. HTTP с retry.
            var sw = Stopwatch.StartNew();
            JObject responseJson;
            try
            {
                responseJson = await SendWithRetryAsync(url, payload, apiKey, timeoutSec, ct);
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

            // 7. Парсинг ответа.
            var choice = responseJson["choices"]?[0];
            if (choice == null)
                throw new InvalidOperationException(
                    $"Провайдер '{providerName}' вернул ответ без choices[].");

            var content = choice["message"]?["content"]?.ToString() ?? string.Empty;
            var promptTokens = responseJson["usage"]?["prompt_tokens"]?.Value<int>() ?? 0;
            var completionTokens = responseJson["usage"]?["completion_tokens"]?.Value<int>() ?? 0;
            var cost = ProviderCostCalculator.Calculate(promptTokens, completionTokens, provider);

            // 8. Учёты.
            _circuitBreaker.RecordSuccess(providerName);
            _budgetTracker.RecordUsage(userId, promptTokens, completionTokens, cost);

            _logger.LogInformation(
                "External-LLM: {Provider} promptLen={PromptLen} tokens={PromptTokens}+{CompletionTokens} " +
                "cost=${Cost} durationMs={Duration}",
                providerName, request.Prompt.Length,
                promptTokens, completionTokens, cost, sw.ElapsedMilliseconds);

            return new ExternalLlmResponse
            {
                Provider = providerName,
                Content = content,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
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
        // Private
        // ============================================================

        /// <summary>
        /// HTTP POST с retry 1 раз при 5xx или 429.
        /// </summary>
        private async Task<JObject> SendWithRetryAsync(
            string url,
            JObject payload,
            string apiKey,
            int timeoutSec,
            CancellationToken ct)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    return await SendOnceAsync(url, payload, apiKey, timeoutSec, ct);
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
        private async Task<JObject> SendOnceAsync(
            string url,
            JObject payload,
            string apiKey,
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

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
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
        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }
    }
}