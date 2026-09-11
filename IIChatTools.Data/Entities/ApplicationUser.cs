using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Пользователь системы с расширенными полями.
    /// Наследует IdentityUser{int} — идентификатор типа int.
    /// </summary>
    public class ApplicationUser : IdentityUser<int>
    {
        /// <summary>
        /// Полное имя пользователя.
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// Признак активности пользователя.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Дата регистрации пользователя (UTC).
        /// </summary>
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Навигационное свойство: записи аудита, связанные с пользователем.
        /// </summary>
        public virtual ICollection<AuditLog> AuditLogs { get; set; }

        /// <summary>
        /// Навигационное свойство: запросы на подтверждение, инициированные пользователем.
        /// </summary>
        public virtual ICollection<PendingAction> PendingActions { get; set; }
    }
}