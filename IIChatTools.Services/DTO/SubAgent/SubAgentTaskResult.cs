using System;
using System.Collections.Generic;
using IIChatTools.Services.DTO.Chat;   // v1.6.1 (KI-086-post): ChatSourceDto

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

        /// <summary>
        /// v1.6.1 (KI-086-post): источники, использованные inner-инструментами
        /// агента (например, <c>wikipedia_search</c>, <c>search_knowledge_base</c>).
        ///
        /// <para>
        /// Собираются в <see cref="Implementation.SubAgentService"/> из
        /// <c>ToolResult.Sources</c> каждого вызова внутри цикла агента.
        /// Дедуплицируются по ключу <c>(Type|DocumentPath|Url|ChunkIndex)</c>.
        /// </para>
        ///
        /// <para>
        /// Пробрасываются наружу через <c>AgentToolBase</c> → <c>ToolResult.Sources</c>
        /// → <c>ChatStreamService</c> accumulator → UI-блок «📚 Источники».
        /// </para>
        ///
        /// <para><c>null</c>, если inner-инструменты не вернули sources.</para>
        /// </summary>
        public IReadOnlyList<ChatSourceDto> Sources { get; set; }
    }
}