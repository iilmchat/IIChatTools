using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;

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
    }
}