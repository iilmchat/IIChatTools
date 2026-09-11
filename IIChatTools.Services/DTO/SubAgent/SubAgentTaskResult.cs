using System;
using System.Collections.Generic;

namespace IIChatTools.Services.DTO.SubAgent
{
    /// <summary>
    /// Результат выполнения задачи суб-агентом.
    /// </summary>
    public class SubAgentTaskResult
    {
        /// <summary>
        /// Уникальный идентификатор сессии суб-агента.
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Финальный текст, сгенерированный суб-агентом.
        /// </summary>
        public string FinalAnswer { get; set; }

        /// <summary>
        /// Флаг завершения задачи (false — если исчерпан лимит шагов).
        /// </summary>
        public bool Completed { get; set; }

        /// <summary>
        /// Количество выполненных шагов.
        /// </summary>
        public int Steps { get; set; }

        /// <summary>
        /// Длительность выполнения в миллисекундах.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Список имён инструментов, использованных в процессе.
        /// </summary>
        public IReadOnlyList<string> UsedTools { get; set; }

        /// <summary>
        /// Результат авто-отладки (если была включена).
        /// </summary>
        public string DebugReview { get; set; }
    }
}