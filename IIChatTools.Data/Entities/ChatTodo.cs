using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Задача (Todo) внутри чата.
    /// </summary>
    public class ChatTodo
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; }

        /// <summary>Статус: Pending / InProgress / Completed.</summary>
        public string Status { get; set; } = "Pending";

        /// <summary>Порядок сортировки.</summary>
        public int SortOrder { get; set; }

        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Chat Chat { get; set; }
    }
}