using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Запрос на обновление подключения Database Agent
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6).
    /// <para>
    /// Все поля опциональны: <c>null</c> = «не менять».
    /// Позволяет UI отправлять только изменённые поля.
    /// </para>
    /// </summary>
    public class UpdateSqlAgentConnectionRequest
    {
        /// <summary>Включить / отключить подключение.</summary>
        public bool? Enabled { get; set; }

        /// <summary>
        /// Whitelist-таблицы. <c>null</c> = не менять.
        /// Пустой список = очистить.
        /// </summary>
        public IReadOnlyList<string> AllowedTables { get; set; }

        /// <summary>
        /// Blacklist-таблицы. <c>null</c> = не менять.
        /// Пустой список = очистить.
        /// </summary>
        public IReadOnlyList<string> DeniedTables { get; set; }

        /// <summary>
        /// Лимит строк (1–10000). <c>null</c> = не менять.
        /// Значение вне диапазона → <c>ArgumentException</c>.
        /// </summary>
        public int? MaxRows { get; set; }

        /// <summary>
        /// Таймаут SQL-запроса (1–300 сек). <c>null</c> = не менять.
        /// </summary>
        public int? StatementTimeoutSeconds { get; set; }
    }
}