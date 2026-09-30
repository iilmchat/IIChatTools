using System;

namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Статус провайдера External-LLM (v1.8.1, KI-109).
    ///
    /// <para>
    /// Возвращается инструментом <c>list_external_providers</c> и
    /// <see cref="Interfaces.IExternalLlmCircuitBreaker.GetStatus"/>.
    /// </para>
    /// </summary>
    public class ProviderHealthStatus
    {
        /// <summary>
        /// Имя провайдера (ключ в <c>ExternalLlm:Providers</c>).
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// Доступен ли провайдер на момент последней проверки
        /// (<c>false</c>, если circuit breaker открыт или health-check упал).
        /// </summary>
        public bool Available { get; set; }

        /// <summary>
        /// Текст последней ошибки (для UI и диагностики). <c>null</c>, если ошибок не было.
        /// Не содержит PII: сообщение должно быть коротким техническим описанием
        /// (например, «Circuit breaker open (retry in 3m)»).
        /// </summary>
        public string LastError { get; set; }

        /// <summary>
        /// Время последней проверки (UTC). <c>null</c>, если провайдер ещё не проверялся.
        /// </summary>
        public DateTime? LastCheckAt { get; set; }
    }
}