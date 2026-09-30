using System.Collections.Generic;

namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Настройки External-LLM Agent (bind из секции <c>ExternalLlm</c> appsettings.json).
    /// v1.8.1 (KI-109).
    ///
    /// <para>
    /// <b>Секреты</b> (API-ключи) хранятся отдельно: имя ключа — в
    /// <see cref="ExternalProviderOptions.ApiKeySecretName"/>, а сам ключ — в User Secrets / env.
    /// В appsettings.json ключей быть не должно.
    /// </para>
    /// </summary>
    public class ExternalLlmOptions
    {
        /// <summary>
        /// Глобальный переключатель. Если <c>false</c> — <c>external_llm_agent</c>
        /// и его инструменты не регистрируются в DI.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Имя провайдера по умолчанию (ключ в <see cref="Providers"/>).
        /// Используется, если в вызове не указан явный <c>provider</c>.
        /// </summary>
        public string DefaultProvider { get; set; } = "deepseek";

        /// <summary>
        /// Проверять доступность <see cref="DefaultProvider"/> при старте приложения.
        /// Если <c>true</c> и провайдер недоступен — <c>external_llm_agent</c> не регистрируется.
        /// </summary>
        public bool HealthCheckOnStartup { get; set; } = true;

        /// <summary>
        /// Дневной бюджет в USD (per-user). При превышении — <c>ask_external_llm</c> возвращает Fail.
        /// </summary>
        public decimal DailyBudgetUsd { get; set; } = 5.0m;

        /// <summary>
        /// Дневной лимит токенов (per-user). При превышении — <c>ask_external_llm</c> возвращает Fail.
        /// </summary>
        public long DailyTokensLimit { get; set; } = 500_000;

        /// <summary>
        /// Настройки circuit breaker (per-provider).
        /// </summary>
        public ExternalLlmCircuitBreakerOptions CircuitBreaker { get; set; }
            = new ExternalLlmCircuitBreakerOptions();

        /// <summary>
        /// Зарегистрированные провайдеры: name → настройки.
        /// Ключ используется в параметре <c>provider</c> инструмента <c>ask_external_llm</c>.
        /// </summary>
        public Dictionary<string, ExternalProviderOptions> Providers { get; set; }
            = new Dictionary<string, ExternalProviderOptions>();
    }
}