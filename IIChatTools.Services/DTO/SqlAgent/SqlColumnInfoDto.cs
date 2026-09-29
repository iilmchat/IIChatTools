namespace IIChatTools.Services.DTO.SqlAgent
{
    /// <summary>
    /// Информация о колонке таблицы
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1).
    /// Возвращается инструментом <c>database_agent</c> в действии
    /// <c>describe_table</c>.
    /// </summary>
    public class SqlColumnInfoDto
    {
        /// <summary>
        /// Имя колонки (точно как в БД).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Тип данных (провайдер-специфичный: <c>TEXT</c>, <c>INTEGER</c>,
        /// <c>nvarchar(255)</c> и т. п.).
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Признак, что колонка допускает <c>NULL</c>.
        /// </summary>
        public bool Nullable { get; set; }

        /// <summary>
        /// Пример значения из таблицы (первая непустая строка).
        /// Может быть <c>null</c>, если таблица пуста или значение не получено.
        /// Обрезается до 200 символов.
        /// </summary>
        public string SampleValue { get; set; }
    }
}