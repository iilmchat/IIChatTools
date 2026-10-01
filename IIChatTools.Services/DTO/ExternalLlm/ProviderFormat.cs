using System;

namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Формат API внешнего провайдера LLM (v1.9.0, KI-110a).
    ///
    /// <para>
    /// <b>OpenAI</b> — <c>POST {BaseUrl}/chat/completions</c>,
    /// <c>Authorization: Bearer</c>, тело <c>{model, messages[]}</c>.
    /// Провайдеры: DeepSeek, OpenAI, Groq, Together AI, Ollama.
    /// </para>
    ///
    /// <para>
    /// <b>Anthropic</b> — <c>POST {BaseUrl}/messages</c>,
    /// <c>x-api-key</c> + <c>anthropic-version</c>, system отдельно,
    /// <c>max_tokens</c> обязателен. Ответ — <c>content[]</c> (блоки).
    /// </para>
    ///
    /// <para>
    /// <b>Gemini</b> — зарезервировано на v1.9.x (KI-110b).
    /// При вызове — <see cref="System.NotSupportedException"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Значение по умолчанию</b> — <see cref="OpenAI"/> (= 0).
    /// Все 5 существующих провайдеров в <c>appsettings.json</c> не задают
    /// <c>Format</c> — продолжают работать как раньше (DESIGN v1.9 § 3.2).
    /// </para>
    /// </summary>
    public enum ProviderFormat
    {
        /// <summary>
        /// OpenAI-совместимый формат (по умолчанию). Используется
        /// DeepSeek, OpenAI, Groq, Together AI, Ollama.
        /// </summary>
        OpenAI = 0,

        /// <summary>
        /// Anthropic Messages API (<c>POST {BaseUrl}/messages</c>,
        /// <c>x-api-key</c> + <c>anthropic-version: 2023-06-01</c>).
        /// </summary>
        Anthropic = 1,

        /// <summary>
        /// Google Gemini (<c>/v1beta/models/{model}:generateContent</c>).
        /// v1.9.x (KI-110b). Пока не реализовано — при вызове
        /// <see cref="System.NotSupportedException"/>.
        /// </summary>
        Gemini = 2
    }
}