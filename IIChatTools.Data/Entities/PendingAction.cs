using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Действие, ожидающее подтверждения пользователя
    /// </summary>
    public class PendingAction : BaseEntity
    {
        /// <summary>
        /// Идентификатор пользователя, инициировавшего действие (или агента)
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Навигационное свойство пользователя
        /// </summary>
        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Название инструмента
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Параметры в JSON
        /// </summary>
        public string ParametersJson { get; set; }

        /// <summary>
        /// Статус: Pending, Approved, Rejected, Expired
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Время создания
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Время истечения (например, через 5 минут)
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Идентификатор администратора, утвердившего/отклонившего
        /// </summary>
        public int? ApprovedByUserId { get; set; }

        /// <summary>
        /// Время утверждения/отклонения
        /// </summary>
        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// Комментарий при отклонении
        /// </summary>
        public string RejectionReason { get; set; }

        /// <summary>
        /// Результат выполнения после подтверждения (заполняется после выполнения)
        /// </summary>
        public string ResultJson { get; set; }
    }
}