using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Лог аудита действий пользователей и агентов
    /// </summary>
    public class AuditLog : BaseEntity
    {
        /// <summary>
        /// Идентификатор пользователя, выполнившего действие
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Навигационное свойство пользователя
        /// </summary>
        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Название вызванного инструмента
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Параметры вызова (JSON)
        /// </summary>
        public string ParametersJson { get; set; }

        /// <summary>
        /// Результат выполнения (JSON или текст)
        /// </summary>
        public string ResultJson { get; set; }

        /// <summary>
        /// Длительность выполнения в миллисекундах
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Статус выполнения (Success, Error, Pending, Cancelled)
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// IP-адрес клиента (опционально)
        /// </summary>
        public string ClientIp { get; set; }
    }
}