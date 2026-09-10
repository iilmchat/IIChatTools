using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Состояние сессии суб-агента
    /// </summary>
    public class AgentState : BaseEntity
    {
        /// <summary>
        /// Идентификатор пользователя, владеющего сессией
        /// </summary>
        public int UserId { get; set; }

        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Уникальный идентификатор сессии (GUID)
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Описание задачи
        /// </summary>
        public string TaskDescription { get; set; }

        /// <summary>
        /// Текущий шаг выполнения
        /// </summary>
        public int CurrentStep { get; set; }

        /// <summary>
        /// Снимок кода/состояния (JSON)
        /// </summary>
        public string CodeSnapshotJson { get; set; }

        /// <summary>
        /// Дата последнего обновления
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Завершена ли сессия
        /// </summary>
        public bool IsCompleted { get; set; }
    }
}