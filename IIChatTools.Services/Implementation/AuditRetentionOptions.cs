namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Параметры retention policy для аудита.
    /// Читаются из секции <c>Audit</c> в appsettings.json.
    /// </summary>
    public class AuditRetentionOptions
    {
        /// <summary>Чистить записи в БД (<c>AuditLogs</c>).</summary>
        public bool CleanupDatabase { get; set; } = true;

        /// <summary>Чистить JSONL-файлы в <c>LogDirectory</c>.</summary>
        public bool CleanupFiles { get; set; } = true;

        /// <summary>Сколько дней хранить записи аудита в БД.</summary>
        public int DatabaseRetentionDays { get; set; } = 90;

        /// <summary>Сколько дней хранить JSONL-файлы.</summary>
        public int FileRetentionDays { get; set; } = 30;

        /// <summary>Интервал между прогонами чистки (в часах).</summary>
        public int CleanupIntervalHours { get; set; } = 6;

        /// <summary>Каталог файлов аудита (относительный или абсолютный).</summary>
        public string LogDirectory { get; set; } = "logs/audit";
    }
}