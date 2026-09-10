using Microsoft.AspNetCore.Identity;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Пользователь системы с расширенными полями
    /// </summary>
    public class ApplicationUser : IdentityUser<int>
    {
        /// <summary>
        /// Полное имя пользователя
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// Активен ли пользователь
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Дата регистрации
        /// </summary>
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Навигационное свойство для аудита
        /// </summary>
        public virtual ICollection<AuditLog> AuditLogs { get; set; }

        /// <summary>
        /// Навигационное свойство для действий на подтверждении
        /// </summary>
        public virtual ICollection<PendingAction> PendingActions { get; set; }
    }
}