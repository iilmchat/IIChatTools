using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Действие, ожидающее подтверждения пользователя.
    /// </summary>
    public class PendingAction : BaseEntity
    {
        /// <summary>
        /// Идентификатор пользователя, инициировавшего действие.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Навигационное свойство пользователя.
        /// </summary>
        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Название инструмента.
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Параметры вызова (JSON).
        /// </summary>
        public string ParametersJson { get; set; }

        /// <summary>
        /// Статус: Pending, Approved, Rejected, Expired.
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Время истечения запроса (UTC).
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Идентификатор администратора, утвердившего/отклонившего действие.
        /// </summary>
        public int? ApprovedByUserId { get; set; }

        /// <summary>
        /// Время утверждения/отклонения (UTC).
        /// </summary>
        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// Комментарий при отклонении.
        /// </summary>
        public string RejectionReason { get; set; }

        /// <summary>
        /// Результат выполнения после подтверждения (JSON).
        /// </summary>
        public string ResultJson { get; set; }

        // Примечание: свойства CreatedAt и UpdatedAt наследуются от BaseEntity.
    }
}