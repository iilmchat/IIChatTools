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
        /// </summary>
        /// <param name="request">Запрос (provider, prompt, опции)</param>
        /// <param name="ct">Токен отмены (обёртка над HttpClient.Timeout + внешняя отмена)</param>
        /// <returns>
        /// Ответ с текстом, токенами, стоимостью и длительностью.
        /// </returns>
        /// <exception cref="System.InvalidOperationException">
        /// Если провайдер не найден в конфигурации или API-ключ не задан.
        /// </exception>
        /// <exception cref="System.Net.Http.HttpRequestException">
        /// Сетевая ошибка / HTTP 4xx-5xx от провайдера.
        /// </exception>
        /// <exception cref="System.Threading.Tasks.TaskCanceledException">
        /// Таймаут или внешняя отмена.
        /// </exception>
        Task<ExternalLlmResponse> CompleteAsync(
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