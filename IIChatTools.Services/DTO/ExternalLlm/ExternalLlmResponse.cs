namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Ответ от внешней LLM (v1.8.1, KI-109).
    ///
    /// <para>
    /// В логах / AuditLogs сохраняются только метаданные (<see cref="Provider"/>,
    /// <see cref="PromptTokens"/>, <see cref="CompletionTokens"/>,
    /// <see cref="CostUsd"/>, <see cref="DurationMs"/>). <see cref="Content"/>
    /// не логируется (privacy, DESIGN § 3.5).
    /// </para>
    /// </summary>
    public class ExternalLlmResponse
    {
        /// <summary>
        /// Имя провайдера, от которого получен ответ (для aggregation / логирования).
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// Текст ответа внешней модели.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Токенов в prompt (из <c>usage.prompt_tokens</c>).
        /// </summary>
        public int PromptTokens { get; set; }

        /// <summary>
        /// Токенов в completion (из <c>usage.completion_tokens</c>).
        /// </summary>
        public int CompletionTokens { get; set; }

        /// <summary>
        /// Стоимость запроса в USD (рассчитана из тарифов провайдера и токенов).
        /// </summary>
        public decimal CostUsd { get; set; }

        /// <summary>
        /// Длительность HTTP-запроса в миллисекундах.
        /// </summary>
        public long DurationMs { get; set; }
    }
}