namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Состояние подключения Database Agent для админки
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6).
    /// Возвращается в <c>GET /api/admin/sql-agent/connections</c>
    /// и в ответе на <c>PUT</c>.
    /// <para>
    /// Содержит <b>актуальные</b> значения (override из AppSettings → baseline
    /// из appsettings.json). Флаг <see cref="IsOverridden"/> показывает,
    /// есть ли override хотя бы по одному полю.
    /// </para>
    /// </summary>
    public class SqlAgentConnectionItemDto
    {
        /// <summary>Техническое имя подключения (например, <c>internal</c>).</summary>
        public string Name { get; set; }

        /// <summary>Человекочитаемое имя (для UI).</summary>
        public string DisplayName { get; set; }

        /// <summary>Провайдер БД: <c>Sqlite</c> / <c>SqlServer</c>.</summary>
        public string Provider { get; set; }

        /// <summary>Подключение включено (не заблокировано админом).</summary>
        public bool Enabled { get; set; }

        /// <summary>Whitelist-таблицы.</summary>
        public System.Collections.Generic.IReadOnlyList<string> AllowedTables { get; set; }

        /// <summary>Blacklist-таблицы (перебивает whitelist).</summary>
        public System.Collections.Generic.IReadOnlyList<string> DeniedTables { get; set; }

        /// <summary>Лимит строк в ответе (auto-LIMIT).</summary>
        public int MaxRows { get; set; }

        /// <summary>Таймаут SQL-запроса (секунды).</summary>
        public int StatementTimeoutSeconds { get; set; }

        /// <summary>Требует ли approval для <c>execute_query</c>.</summary>
        public bool RequiresApproval { get; set; }

        /// <summary>Описание подключения.</summary>
        public string Description { get; set; }

        /// <summary>
        /// Признак, что для этого подключения есть хотя бы один override
        /// в <c>AppSettings</c> (ключ с префиксом <c>SqlAgent.{name}.</c>).
        /// UI показывает бейдж «изменено» + кнопку «Сбросить».
        /// </summary>
        public bool IsOverridden { get; set; }
    }
}