using System;

namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Изображение для multimodal-запроса к внешней LLM
    /// (v1.13.9, KI-141).
    ///
    /// <para>
    /// Передаётся в <see cref="ExternalLlmRequest.Images"/>. В
    /// OpenAI-совместимом формате собирается в <c>content[]</c> как
    /// <c>{type: "image_url", image_url: {url: "data:{MimeType};base64,{Base64Data}"}}</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Требование:</b> провайдер должен иметь
    /// <see cref="ExternalProviderOptions.SupportsVision"/> = <c>true</c>,
    /// иначе <c>ExternalLlmClient</c> бросит <see cref="InvalidOperationException"/>.
    /// </para>
    /// </summary>
    public class ExternalLlmImage
    {
        /// <summary>
        /// MIME-тип изображения: <c>image/png</c> (default), <c>image/jpeg</c>,
        /// <c>image/webp</c>, <c>image/gif</c>. Не критично для большинства
        /// провайдеров — они определяют тип из магии байтов.
        /// </summary>
        public string MimeType { get; set; } = "image/png";

        /// <summary>
        /// Содержимое изображения в base64 (без data-URL префикса).
        /// Не <c>null</c>, не пусто (валидируется в клиенте).
        /// </summary>
        public string Base64Data { get; set; }

        /// <summary>
        /// Опциональная подпись / alt-text (не используется в v1.13.9 —
        /// зарезервировано на будущее для Anthropic / Gemini).
        /// </summary>
        public string AltText { get; set; }
    }
}