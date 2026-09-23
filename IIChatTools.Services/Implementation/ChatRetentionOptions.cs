namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Параметры retention policy для чатов.
    /// Читаются из секции <c>Chat:Retention</c> в appsettings.json.
    /// </summary>
    public class ChatRetentionOptions
    {
        /// <summary>
        /// Включён ли retention (удаление старых чатов).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Срок хранения чатов (в днях). Чаты с <c>UpdatedAt</c> старше этого
        /// порога будут удалены. По умолчанию — 30.
        /// </summary>
        public int DefaultDays { get; set; } = 30;

        /// <summary>
        /// Верхняя граница для <see cref="DefaultDays"/> (защита от опечаток
        /// в конфиге). По умолчанию — 365.
        /// </summary>
        public int MaxDays { get; set; } = 365;

        /// <summary>
        /// Интервал между прогонами чистки (в часах). По умолчанию — 24.
        /// </summary>
        public int CleanupIntervalHours { get; set; } = 24;
    }
}