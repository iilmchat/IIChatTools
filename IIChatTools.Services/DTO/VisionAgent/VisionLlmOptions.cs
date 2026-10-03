using System.Collections.Generic;

namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Конфигурация Vision LLM — модели, которая описывает UI со скриншота
    /// (возвращает <c>ScreenDescriptionDto</c>).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 3.2, § 5.1.
    /// </remarks>
    public class VisionLlmOptions
    {
        /// <summary>
        /// Провайдер: <c>lmstudio</c> | <c>external</c> | <c>auto</c>.
        /// Default: <c>lmstudio</c>.
        /// </summary>
        public string Provider { get; set; } = "lmstudio";

        /// <summary>
        /// Модель. Для LM Studio — имя в загруженном каталоге.
        /// Default: <c>ministral-3-3b-instruct-2512</c>.
        /// </summary>
        public string Model { get; set; } = "ministral-3-3b-instruct-2512";

        /// <summary>Максимальная ширина скриншота перед отправкой (default: 1024).</summary>
        public int MaxImageWidth { get; set; } = 1024;

        /// <summary>Максимальная высота скриншота перед отправкой (default: 768).</summary>
        public int MaxImageHeight { get; set; } = 768;

        /// <summary>Формат изображения: <c>png</c> | <c>webp</c> (default: png).</summary>
        public string ImageFormat { get; set; } = "png";

        /// <summary>Максимум токенов в ответе (default: 1024).</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>Temperature (default: 0.1 — детерминированное описание).</summary>
        public float Temperature { get; set; } = 0.1f;

        /// <summary>Таймаут одного запроса, сек (default: 30).</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Кэшировать описания по SHA-256 PNG. Default: <c>true</c>.
        /// Одинаковые скриншоты (например, после паузы) не будут обрабатываться заново.
        /// </summary>
        public bool CacheEnabled { get; set; } = true;

        /// <summary>
        /// Цепочка fallback'ов для <c>Provider = "auto"</c>.
        /// Пример: <c>["lmstudio", "external:groq", "external:openai"]</c>.
        /// </summary>
        public List<string> FallbackChain { get; set; } = new List<string>
        {
            "lmstudio"
        };
    }
}