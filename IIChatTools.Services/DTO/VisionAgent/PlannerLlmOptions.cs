using System.Collections.Generic;

namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Конфигурация Planner LLM — модели, которая решает следующее действие
    /// на основе задачи + истории + описания экрана.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 3.2, § 4.5, § 5.1.
    /// </remarks>
    public class PlannerLlmOptions
    {
        /// <summary>
        /// Провайдер: <c>lmstudio</c> | <c>external</c> | <c>auto</c>.
        /// Default: <c>lmstudio</c>.
        /// </summary>
        public string Provider { get; set; } = "lmstudio";

        /// <summary>
        /// Модель. Default: <c>qwen3-coder-30b-a3b-instruct</c> (MoE, 3.3B активных,
        /// Agentic Browser-Use SOTA).
        /// </summary>
        public string Model { get; set; } = "qwen3-coder-30b-a3b-instruct";

        /// <summary>Максимум токенов в ответе (default: 2048).</summary>
        public int MaxTokens { get; set; } = 2048;

        /// <summary>Temperature (default: 0.1 — детерминированные решения).</summary>
        public float Temperature { get; set; } = 0.1f;

        /// <summary>Таймаут одного запроса, сек (default: 60).</summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Сколько последних шагов хранить в истории (default: 20).
        /// Защита от переполнения контекста на длинных задачах.
        /// </summary>
        public int MaxHistorySteps { get; set; } = 20;

        /// <summary>
        /// Цепочка fallback'ов для <c>Provider = "auto"</c>.
        /// Пример: <c>["lmstudio", "external:deepseek", "external:openai"]</c>.
        /// </summary>
        public List<string> FallbackChain { get; set; } = new List<string>
        {
            "lmstudio"
        };
    }
}