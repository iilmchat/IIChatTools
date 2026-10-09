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

        /// <summary>
        /// Поддерживает ли провайдер multimodal (vision) вход
        /// (v1.13.9, KI-141).
        ///
        /// <para>
        /// Если <c>false</c> (default) — попытка передать
        /// <see cref="ExternalLlmRequest.Images"/> приведёт к
        /// <see cref="System.InvalidOperationException"/> до HTTP-запроса.
        /// </para>
        ///
        /// <para>
        /// <b>true</b> для: <c>yandex-vl</c> (Qwen3.6-35B), <c>openai</c>
        /// (gpt-4o*), <c>anthropic</c> (claude-3.5+), <c>gemini</c> (1.5+).
        /// Для остальных — <c>false</c> (DeepSeek Chat, Groq, Together OSS-модели).
        /// </para>
        /// </summary>
        public bool SupportsVision { get; set; }

        /// <summary>
        /// Значение поля <c>reasoning_effort</c> в теле запроса
        /// (v1.13.9, KI-141).
        ///
        /// <para>
        /// Применяется <b>только для <see cref="ProviderFormat.OpenAI"/></b>.
        /// Если задано — добавляется как <c>payload["reasoning_effort"]</c>.
        /// </para>
        ///
        /// <para>
        /// <b>null</b> (default) — поле не отправляется (совместимо со
        /// стандартным OpenAI API).
        /// </para>
        ///
        /// <para>
        /// <b>"none"</b> — отключает reasoning-цепочку. Для Yandex
        /// Qwen3.6-35B: модель по умолчанию тратит ~500 токенов на
        /// <c>reasoning_content</c> перед <c>content</c>. С <c>"none"</c>
        /// — генерирует сразу в <c>content</c>, экономит время и токены.
        /// </para>
        ///
        /// <para>
        /// Другие значения: <c>"low"</c> / <c>"medium"</c> / <c>"high"</c>
        /// (OpenAI o1/o3, Qwen3-thinking). См. документацию провайдера.
        /// </para>
        /// </summary>
        public string ReasoningEffort { get; set; }
    }
}