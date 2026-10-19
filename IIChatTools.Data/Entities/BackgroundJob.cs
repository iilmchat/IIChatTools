using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Фоновая задача (Background Job).
    /// </summary>
    public class BackgroundJob
    {
        public int Id { get; set; }

        /// <summary>Владелец задачи.</summary>
        public int UserId { get; set; }

        /// <summary>Чат, к которому привязана задача (nullable).</summary>
        public int? ChatId { get; set; }

        /// <summary>Тип задачи: Orchestrator / Vision / Smoke.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Статус: Pending / Running / Completed / Failed / Cancelled.</summary>
        public string Status { get; set; } = "Pending";

        /// <summary>Прогресс 0-100.</summary>
        public int Progress { get; set; }

        /// <summary>Сообщение о текущем шаге (для UI).</summary>
        public string ProgressMessage { get; set; }

        /// <summary>Параметры запуска (JSON).</summary>
        public string ParametersJson { get; set; }

        /// <summary>Результат (JSON).</summary>
        public string ResultJson { get; set; }

        /// <summary>Текст ошибки (если Failed).</summary>
        public string Error { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? CancelRequestedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ApplicationUser User { get; set; }
        public Chat Chat { get; set; }
    }
}