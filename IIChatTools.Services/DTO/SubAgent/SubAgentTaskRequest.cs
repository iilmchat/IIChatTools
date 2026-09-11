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
    }
}