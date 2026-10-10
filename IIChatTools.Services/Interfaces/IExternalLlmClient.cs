using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Клиент для OpenAI-совместимых внешних LLM
    /// (DeepSeek, OpenAI, Groq, Together AI, Ollama).
    /// v1.8.1 (KI-109, DESIGN_EXTERNAL_LLM § 4.1).
    ///
    /// <para>
    /// <b>Singleton.</b> Использует <c>IHttpClientFactory</c> с именованными
    /// клиентами <c>ExternalLlm:{provider}</c> (таймаут — per-provider).
    /// Stateless, кроме HttpClient factory.
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> клиент не логирует prompt и content. Только метаданные
    /// (<c>Provider</c>, токены, стоимость, длительность) передаются наверх
    /// для AuditLogs.
    /// </para>
    /// </summary>
    public interface IExternalLlmClient
    {
        /// <summary>
        /// Выполнить один запрос к внешней LLM (non-streaming).
        ///
        /// <para>
        /// Внутри клиента происходит:
        /// <list type="number">
        ///   <item>резолв провайдера (<c>request.Provider</c> или <c>DefaultProvider</c>);</item>
        ///   <item>проверка circuit breaker (при открытом — throw);</item>
        ///   <item>проверка дневного бюджета (per-user, при превышении — throw);</item>
        ///   <item>HTTP POST <c>{BaseUrl}/chat/completions</c> с Bearer-токеном;</item>
        ///   <item>парсинг ответа, расчёт стоимости (<c>ProviderCostCalculator</c>);</item>
        ///   <item>запись успеха в breaker + расход в budget tracker.</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="userId">Пользователь-инициатор (для budget tracker, per-user)</param>
        /// <param name="request">Запрос (provider, prompt, опции)</param>
        /// <param name="ct">Токен отмены (обёртка над HttpClient.Timeout + внешняя отмена)</param>
        /// <returns>
        /// Ответ с текстом, токенами, стоимостью и длительностью.
        /// </returns>
        /// <exception cref="System.InvalidOperationException">
        /// Если провайдер не найден в конфигурации, API-ключ не задан,
        /// circuit breaker открыт или бюджет пользователя исчерпан.
        /// </exception>
        /// <exception cref="System.Net.Http.HttpRequestException">
        /// Сетевая ошибка / HTTP 4xx-5xx от провайдера.
        /// </exception>
        /// <exception cref="System.TimeoutException">
        /// Таймаут HTTP-запроса (провайдер не ответил за <c>TimeoutSeconds</c>).
        /// </exception>
        /// <exception cref="System.Threading.Tasks.TaskCanceledException">
        /// Внешняя отмена (<paramref name="ct"/>).
        /// </exception>
        Task<ExternalLlmResponse> CompleteAsync(
            int userId,
            ExternalLlmRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Проверить доступность провайдера (легковесный запрос к <c>/models</c>).
        /// </summary>
        /// <param name="providerName">Имя провайдера из конфигурации</param>
        /// <param name="ct">Токен отмены</param>
        /// <returns><c>true</c>, если провайдер отвечает; иначе <c>false</c> (без исключения).</returns>
        Task<bool> TestConnectionAsync(
            string providerName,
            CancellationToken ct = default);

        /// <summary>
        /// SSE-стриминг chat completion (v1.13.10, KI-224, Фаза B).
        ///
        /// <para>
        /// <b>Поддерживается только для <see cref="ProviderFormat.OpenAI"/>.</b>
        /// Anthropic / Gemini вернут <see cref="System.NotSupportedException"/>
        /// (свой SSE-формат — v1.14+).
        /// </para>
        ///
        /// <para>
        /// <b>Circuit breaker + budget tracker:</b> работают так же, как в
        /// <see cref="CompleteAsync"/>.
        /// </para>
        /// </summary>
        /// <param name="userId">
        /// Пользователь-инициатор. <c>0</c> — системный вызов (бюджет не проверяется).
        /// </param>
        /// <param name="providerName">
        /// Имя провайдера. <c>null</c> / пусто → <c>DefaultProvider</c>.
        /// </param>
        /// <param name="messages">История сообщений (JArray, формат OpenAI).</param>
        /// <param name="tools">
        /// Список инструментов (JArray, OpenAI Function Calling) или <c>null</c>.
        /// </param>
        /// <param name="temperature">Опциональный override (иначе 0.7).</param>
        /// <param name="maxTokens">Опциональный override (иначе <c>provider.MaxTokens</c>).</param>
        /// <param name="ct">Токен отмены.</param>
        /// <returns>Поток чанков <see cref="ChatCompletionChunk"/>.</returns>
        /// <exception cref="System.ArgumentNullException">Если <paramref name="messages"/> равен null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// Провайдер не найден, circuit breaker открыт, формат ≠ OpenAI.
        /// </exception>
        /// <exception cref="System.TimeoutException">Провайдер не ответил за timeout.</exception>
        IAsyncEnumerable<ChatCompletionChunk> ChatStreamAsync(
            int userId,
            string providerName,
            JArray messages,
            JArray tools,
            double? temperature = null,
            int? maxTokens = null,
            CancellationToken ct = default);            
    }
}