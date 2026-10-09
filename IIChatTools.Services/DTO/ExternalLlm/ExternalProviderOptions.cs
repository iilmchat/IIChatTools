namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Настройки одного провайдера внешних LLM (OpenAI-совместимый API).
    /// v1.8.1 (KI-109).
    ///
    /// <para>
    /// Все провайдеры в v1.8.1 используют единый формат
    /// <c>POST {BaseUrl}/chat/completions</c> с Bearer-токеном.
    /// Anthropic / Gemini (свои форматы) — v1.9+ (KI-110).
    /// </para>
    ///
    /// <para>
    /// <b>API-ключ</b> хранится не здесь, а в User Secrets / env. Имя ключа
    /// секрета задаётся в <see cref="ApiKeySecretName"/> — по нему значение
    /// резолвится из <c>IConfiguration</c>.
    /// </para>
    /// </summary>
    public class ExternalProviderOptions
    {
        /// <summary>
        /// Отображаемое имя (для UI и логов): <c>DeepSeek</c>, <c>OpenAI</c>, <c>Groq</c>, ...
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Формат API провайдера (v1.9.0, KI-110a).
        ///
        /// <para>
        /// По умолчанию <see cref="ProviderFormat.OpenAI"/> — 5 существующих
        /// провайдеров (DeepSeek, OpenAI, Groq, Together AI, Ollama) не задают
        /// это поле в <c>appsettings.json</c> и продолжают работать без изменений
        /// (DESIGN v1.9 § 3.2).
        /// </para>
        ///
        /// <para>
        /// <see cref="ProviderFormat.Anthropic"/> включает Anthropic-ветку
        /// (<c>POST /messages</c>, <c>x-api-key</c>, system отдельно).
        /// <see cref="ProviderFormat.Gemini"/> — Google Gemini (v1.10.0, KI-110b).
        /// </para>
        /// </summary>
        public ProviderFormat Format { get; set; } = ProviderFormat.OpenAI;

        /// <summary>
        /// Префикс схемы в заголовке <c>Authorization</c> (v1.13.8, KI-221).
        ///
        /// <para>
        /// Применяется <b>только для <see cref="ProviderFormat.OpenAI"/></b>
        /// (Anthropic / Gemini используют собственные заголовки —
        /// <c>x-api-key</c> / <c>x-goog-api-key</c>).
        /// </para>
        ///
        /// <para>
        /// <b>Default:</b> <c>"Bearer"</c> — для DeepSeek, OpenAI, Groq,
        /// Together AI, Ollama. <b>Yandex AI Studio</b> требует
        /// <c>"Api-Key"</c> (документация Yandex Cloud).
        /// </para>
        ///
        /// <para>
        /// Собирается в <c>ExternalLlmClient</c> как
        /// <c>{AuthScheme} {apiKey}</c> и передаётся в заголовок
        /// <c>Authorization</c>. Значение никогда не логируется.
        /// </para>
        /// </summary>
        public string AuthScheme { get; set; } = "Bearer";

        /// <summary>
        /// Base URL провайдера без trailing slash,
        /// например <c>https://api.deepseek.com/v1</c>.
        /// Должен быть <c>https://</c> (DESIGN § 6.6).
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>
        /// Идентификатор модели по умолчанию
        /// (<c>deepseek-chat</c>, <c>gpt-4o-mini</c>, <c>llama-3.3-70b-versatile</c>, ...).
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// Ключ User Secrets / env, содержащий API-ключ.
        /// Например, <c>ExternalLlm:DeepSeek:ApiKey</c>.
        /// </summary>
        public string ApiKeySecretName { get; set; }

        /// <summary>
        /// Стоимость 1k input-токенов в USD.
        /// </summary>
        public decimal CostPer1kInputUsd { get; set; }

        /// <summary>
        /// Стоимость 1k output-токенов в USD.
        /// </summary>
        public decimal CostPer1kOutputUsd { get; set; }

        /// <summary>
        /// Максимум токенов в ответе (per-request hard limit).
        /// </summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>
        /// Таймаут HTTP-запроса (секунды). По умолчанию 60.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;
    }
}