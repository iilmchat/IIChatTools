namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Запрос к внешней LLM (v1.8.1, KI-109; v1.9.0, KI-110a — +<see cref="System"/>).
    ///
    /// <para>
    /// Заполняется инструментом <c>ask_external_llm</c> из аргументов, переданных LLM.
    /// В <see cref="IncludeContext"/> <c>false</c> по умолчанию — в внешнюю модель уходит
    /// только <see cref="Prompt"/> (privacy + cost guardrails, DESIGN § 3.4).
    /// </para>
    /// </summary>
    public class ExternalLlmRequest
    {
        /// <summary>
        /// Имя провайдера (ключ в <c>ExternalLlm:Providers</c>).
        /// Если пусто — используется <c>DefaultProvider</c>.
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// Текст запроса (prompt) — единственное обязательное поле.
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// System prompt (v1.9.0, KI-110a).
        ///
        /// <para>
        /// Используется <b>только Anthropic-веткой</b>
        /// (<see cref="Implementation.ExternalLlm.Formats.AnthropicRequestBuilder"/> —
        /// отдельное поле <c>system</c> в теле запроса). OpenAI-формат передаёт system
        /// как роль <c>system</c> в <c>messages[]</c>, но текущий
        /// <see cref="Implementation.ExternalLlm.ExternalLlmClient"/> system
        /// не поддерживает — в OpenAI-ветке поле игнорируется.
        /// </para>
        ///
        /// <para>
        /// <c>null</c> / пустая строка / whitespace — поле не добавляется в тело запроса.
        /// </para>
        /// </summary>
        public string System { get; set; }

        /// <summary>
        /// Включать ли последние N сообщений из чата в prompt.
        /// По умолчанию <c>false</c> (privacy + cost).
        /// </summary>
        public bool IncludeContext { get; set; }

        /// <summary>
        /// Опциональный второй провайдер для сценария «сравнение» (DESIGN § 3.2, сценарий D).
        /// Если задан — <c>ask_external_llm</c> делает 2 параллельных запроса
        /// и возвращает <see cref="ExternalLlmComparisonDto"/>.
        /// </summary>
        public string CompareWith { get; set; }

        /// <summary>
        /// Максимум токенов в ответе (override <c>ProviderOptions.MaxTokens</c>).
        /// <c>null</c> — использовать значение провайдера.
        /// </summary>
        public int? MaxTokens { get; set; }

        /// <summary>
        /// Temperature (override). <c>null</c> — использовать значение провайдера (0.7 дефолт).
        /// </summary>
        public double? Temperature { get; set; }
    }
}