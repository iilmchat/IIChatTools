using IIChatTools.Services.DTO.ExternalLlm;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Circuit breaker для внешних LLM (v1.8.1, KI-109, DESIGN_EXTERNAL_LLM § 6.5).
    ///
    /// <para>
    /// Per-provider: N подряд неудачных запросов → «открыт» на <c>BreakDurationSeconds</c>.
    /// Пока открыт — <c>IExternalLlmClient.CompleteAsync</c> вызываться не должен;
    /// tool <c>ask_external_llm</c> возвращает Fail.
    /// </para>
    ///
    /// <para>
    /// <b>Singleton.</b> Состояние — <c>ConcurrentDictionary&lt;string, CircuitState&gt;</c>.
    /// Cleanup устаревших состояний — <c>Timer</c> каждые 5 минут (по образцу KI-043).
    /// </para>
    /// </summary>
    public interface IExternalLlmCircuitBreaker
    {
        /// <summary>
        /// Открыт ли breaker для провайдера (т.е. сейчас запросы запрещены).
        /// </summary>
        /// <param name="providerName">Имя провайдера</param>
        /// <returns><c>true</c> — breaker открыт, запрос надо отклонить без HTTP.</returns>
        bool IsOpen(string providerName);

        /// <summary>
        /// Записать успешный запрос (сбрасывает счётчик неудач).
        /// </summary>
        /// <param name="providerName">Имя провайдера</param>
        void RecordSuccess(string providerName);

        /// <summary>
        /// Записать неудачный запрос (инкрементирует счётчик; при достижении
        /// порога — открывает breaker).
        /// </summary>
        /// <param name="providerName">Имя провайдера</param>
        /// <param name="errorMessage">Краткое техническое описание ошибки (без PII)</param>
        void RecordFailure(string providerName, string errorMessage);

        /// <summary>
        /// Текущее состояние провайдера (для <c>list_external_providers</c> и `/admin`).
        /// </summary>
        /// <param name="providerName">Имя провайдера</param>
        /// <returns>DTO со статусом; <c>Available = true</c>, если breaker закрыт.</returns>
        ProviderHealthStatus GetStatus(string providerName);
    }
}