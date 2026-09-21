using System;
using System.Collections.Generic;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Чат (диалог) пользователя с LLM.
    /// Один пользователь может иметь много чатов; каждый чат — много сообщений.
    /// </summary>
    public class Chat : BaseEntity
    {
        /// <summary>
        /// Владелец чата.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Навигационное свойство владельца.
        /// </summary>
        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Заголовок чата. Генерируется из первого сообщения (автоматически)
        /// или задаётся пользователем вручную.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Идентификатор модели LM Studio, использованной в этом чате.
        /// Например: "qwen/qwen3-4b-2507".
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// Системный промпт чата (опционально).
        /// Если задан — подставляется в начало истории при отправке.
        /// </summary>
        public string SystemPrompt { get; set; }

        /// <summary>
        /// Дата последнего изменения (используется для сортировки в sidebar).
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Все сообщения чата.
        /// </summary>
        public virtual ICollection<ChatMessage> Messages { get; set; }
    }
}