using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Состояние сессии суб-агента.
    /// </summary>
    public class AgentState : BaseEntity
    {
        /// <summary>
        /// Идентификатор пользователя, владеющего сессией.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Навигационное свойство пользователя.
        /// </summary>
        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Уникальный идентификатор сессии (GUID).
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Описание задачи.
        /// </summary>
        public string TaskDescription { get; set; }

        /// <summary>
        /// Текущий шаг выполнения.
        /// </summary>
        public int CurrentStep { get; set; }

        /// <summary>
        /// Снимок кода/состояния (JSON).
        /// </summary>
        public string CodeSnapshotJson { get; set; }

        /// <summary>
        /// Признак завершённости сессии.
        /// </summary>
        public bool IsCompleted { get; set; }

        // Примечание: свойства CreatedAt и UpdatedAt наследуются от BaseEntity.
    }
}