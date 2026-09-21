using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Сообщение в чате.
    /// Роли: <c>user</c>, <c>assistant</c>, <c>system</c>, <c>tool</c>.
    /// </summary>
    public class ChatMessage : BaseEntity
    {
        /// <summary>
        /// Идентификатор чата.
        /// </summary>
        public int ChatId { get; set; }

        /// <summary>
        /// Навигационное свойство чата.
        /// </summary>
        public virtual Chat Chat { get; set; }

        /// <summary>
        /// Роль автора сообщения:
        /// <list type="bullet">
        ///   <item><c>user</c> — пользователь</item>
        ///   <item><c>assistant</c> — LLM</item>
        ///   <item><c>system</c> — системный промпт</item>
        ///   <item><c>tool</c> — результат вызова инструмента</item>
        /// </list>
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Текстовое содержимое сообщения.
        /// Для <c>tool</c> — JSON-сериализованный результат.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// JSON-массив вызовов инструментов (для <c>assistant</c>, если LLM вызвала tools).
        /// Формат: <c>[{ "id": "call_1", "function": { "name": "read_file", "arguments": "{...}" } }]</c>.
        /// </summary>
        public string ToolCallsJson { get; set; }

        /// <summary>
        /// Идентификатор tool call, к которому относится это сообщение (для <c>role="tool"</c>).
        /// </summary>
        public string ToolCallId { get; set; }

        /// <summary>
        /// Имя инструмента (для <c>role="tool"</c>).
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Количество токенов prompt (опционально, из LM Studio usage).
        /// </summary>
        public int? TokensIn { get; set; }

        /// <summary>
        /// Количество токенов completion (опционально).
        /// </summary>
        public int? TokensOut { get; set; }
    }
}