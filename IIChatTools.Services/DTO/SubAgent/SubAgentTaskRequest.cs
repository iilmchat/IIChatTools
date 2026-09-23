using System.Collections.Generic;

namespace IIChatTools.Services.DTO.SubAgent
{
    /// <summary>
    /// Запрос на выполнение задачи суб-агентом.
    /// </summary>
    public class SubAgentTaskRequest
    {
        /// <summary>
        /// Текстовое описание задачи.
        /// </summary>
        public string Task { get; set; }

        /// <summary>
        /// Дополнительный контекст (например, содержимое файлов).
        /// </summary>
        public string Context { get; set; }

        /// <summary>
        /// Максимальное число шагов цикла (по умолчанию 10, максимум 30).
        /// </summary>
        public int MaxSteps { get; set; } = 10;

        /// <summary>
        /// Включить авто-отладку (вызов агента-рецензента после основного цикла).
        /// </summary>
        public bool AutoDebug { get; set; }

        /// <summary>
        /// Список имён инструментов, доступных суб-агенту.
        /// Если пуст — доступны все, кроме consult_secondary_agent.
        /// </summary>
        public IReadOnlyList<string> AllowedTools { get; set; }

        /// <summary>
        /// Системный промпт. Если <c>null</c> — используется
        /// <c>SubAgent:SystemPrompt</c> из appsettings.json.
        /// Заполняется <c>AgentToolBase</c> из <see cref="SubAgentDescriptor.SystemPrompt"/>.
        /// </summary>
        public string SystemPromptOverride { get; set; }

        /// <summary>
        /// Модель LM Studio. Если <c>null</c> — <c>LmStudio:Model</c>
        /// (для consult_secondary_agent) или <c>SubAgents:X.Model</c>
        /// (для специализированных, через <see cref="SubAgentDescriptor.Model"/>).
        /// </summary>
        public string ModelOverride { get; set; }
    }
}