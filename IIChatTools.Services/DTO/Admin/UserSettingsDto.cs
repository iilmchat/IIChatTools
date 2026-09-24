namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Настройки per-user retention чатов (v1.4.x, KI-067-3).
    ///
    /// <para>
    /// Используется в:
    /// <list type="bullet">
    ///   <item><c>GET/PUT /api/admin/users/{id}/settings</c> — админ управляет чужими;</item>
    ///   <item><c>GET/PUT /api/profile/settings</c> — пользователь управляет своими.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class UserSettingsDto
    {
        /// <summary>
        /// Свой срок хранения чатов (в днях).
        /// <c>null</c> — использовать глобальный (<c>Chat:Retention:DefaultDays</c>).
        /// Значение 0 не допускается: для «не удалять вообще» — <see cref="DoNotDelete"/>.
        /// </summary>
        public int? RetentionDays { get; set; }

        /// <summary>
        /// «Не удалять чаты вообще». Перебивает <see cref="RetentionDays"/>.
        /// </summary>
        public bool DoNotDelete { get; set; }

        /// <summary>
        /// Глобальный срок хранения из <c>Chat:Retention:DefaultDays</c> (read-only).
        /// Заполняется сервером в ответе GET; при PUT игнорируется.
        /// </summary>
        public int GlobalRetentionDays { get; set; }

        /// <summary>
        /// Максимальный срок из <c>Chat:Retention:MaxDays</c> (read-only).
        /// Для UI-валидации.
        /// </summary>
        public int MaxRetentionDays { get; set; }
    }
}