namespace IIChatTools.Services.DTO.SqlAgent
{
    /// <summary>
    /// Информация о зарегистрированном подключении к БД
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1).
    /// Возвращается инструментом <c>database_agent</c> в действии
    /// <c>list_databases</c>.
    /// <para>
    /// <b>Безопасность:</b> строка подключения (connection string)
    /// наружу НЕ передаётся — только <see cref="Name"/>, <see cref="DisplayName"/>,
    /// <see cref="Provider"/> и флаг <see cref="Enabled"/>.
    /// </para>
    /// </summary>
    public class DatabaseConnectionInfoDto
    {
        /// <summary>
        /// Техническое имя подключения (используется в параметре
        /// <c>connection</c> других действий, например <c>execute_query</c>).
        /// Например: <c>internal</c>, <c>analytics</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Человекочитаемое название подключения (отображается в UI и
        /// в ответе LLM). Например: <c>IIChatTools DB (собственная)</c>.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Имя провайдера БД: <c>Sqlite</c>, <c>SqlServer</c>
        /// (в Фазе 2 — <c>Postgres</c>, <c>MySql</c>).
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// Признак, что подключение включено (не заблокировано администратором).
        /// Если <c>false</c> — действия <c>list_tables</c> / <c>describe_table</c>
        /// / <c>execute_query</c> по этому подключению возвращают ошибку.
        /// </summary>
        public bool Enabled { get; set; }
    }
}