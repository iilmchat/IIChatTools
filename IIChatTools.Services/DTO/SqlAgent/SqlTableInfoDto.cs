namespace IIChatTools.Services.DTO.SqlAgent
{
    /// <summary>
    /// Информация о whitelist-таблице подключения
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1).
    /// Возвращается инструментом <c>database_agent</c> в действии
    /// <c>list_tables</c>.
    /// </summary>
    public class SqlTableInfoDto
    {
        /// <summary>
        /// Имя таблицы (точно как в БД, регистрозависимо для Sqlite).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Приблизительное количество строк (<c>SELECT COUNT(*)</c>).
        /// Может быть <c>-1</c>, если подсчёт не удалось выполнить.
        /// </summary>
        public int RowCount { get; set; }

        /// <summary>
        /// Опциональное описание таблицы (из конфига, если задано).
        /// </summary>
        public string Description { get; set; }
    }
}