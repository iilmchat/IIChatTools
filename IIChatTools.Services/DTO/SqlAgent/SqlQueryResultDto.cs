using System.Collections.Generic;

namespace IIChatTools.Services.DTO.SqlAgent
{
    /// <summary>
    /// Результат выполнения read-only SQL-запроса
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1 и Приложение B).
    /// </summary>
    public class SqlQueryResultDto
    {
        /// <summary>
        /// Имя подключения, на котором выполнен запрос.
        /// </summary>
        public string Connection { get; set; }

        /// <summary>
        /// Список имён колонок результата (порядок соответствует <c>SELECT</c>).
        /// </summary>
        public List<string> Columns { get; set; } = new List<string>();

        /// <summary>
        /// Строки результата. Каждая строка — словарь
        /// «имя колонки → значение» (<c>object</c>, приводится к типу на клиенте).
        /// </summary>
        public List<Dictionary<string, object>> Rows { get; set; } =
            new List<Dictionary<string, object>>();

        /// <summary>
        /// Фактическое количество прочитанных строк.
        /// Может быть меньше запрошенного, если БД вернула меньше данных.
        /// </summary>
        public int RowCount { get; set; }

        /// <summary>
        /// Признак, что результат был обрезан по лимиту
        /// (<c>MaxRows</c> или auto-LIMIT).
        /// </summary>
        public bool Truncated { get; set; }

        /// <summary>
        /// Длительность выполнения запроса в миллисекундах
        /// (Stopwatch, включает сетевой round-trip к БД).
        /// </summary>
        public long DurationMs { get; set; }
    }
}