namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Настройки circuit breaker для External-LLM Agent (v1.8.1, KI-109, DESIGN_EXTERNAL_LLM § 6.5).
    ///
    /// <para>
    /// Открывается per-provider после N подряд неудачных запросов.
    /// Пока открыт — <c>ask_external_llm</c> возвращает Fail без сетевого вызова.
    /// </para>
    /// </summary>
    public class ExternalLlmCircuitBreakerOptions
    {
        /// <summary>
        /// Сколько подряд неудачных запросов → открыть breaker.
        /// Clamp на [1, 10] (DESIGN § 5.6).
        /// </summary>
        public int FailureThreshold { get; set; } = 3;

        /// <summary>
        /// Длительность «открытого» состояния (секунды).
        /// Clamp на [30, 3600] (DESIGN § 5.6).
        /// </summary>
        public int BreakDurationSeconds { get; set; } = 300;
    }
}