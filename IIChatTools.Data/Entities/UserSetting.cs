namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Настройка пользователя (ключ-значение), хранимая в БД.
    /// v1.4.x (KI-067): per-user override глобальных настроек.
    ///
    /// Ключи (snake_case с точками):
    /// - <c>Chat.RetentionDays</c> — int, срок хранения чатов в днях (0 = глобальный).
    /// - <c>Chat.DoNotDelete</c>  — bool, «не удалять чаты вообще» (перебивает RetentionDays).
    /// </summary>
    public class UserSetting : BaseEntity
    {
        /// <summary>
        /// Идентификатор пользователя-владельца настройки.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Навигационное свойство пользователя.
        /// </summary>
        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Уникальный ключ настройки в рамках пользователя
        /// (например, <c>Chat.RetentionDays</c>).
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Значение настройки (в виде строки).
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Тип данных для парсинга: <c>string</c>, <c>int</c>, <c>bool</c>, <c>json</c>.
        /// </summary>
        public string Type { get; set; }
    }
}