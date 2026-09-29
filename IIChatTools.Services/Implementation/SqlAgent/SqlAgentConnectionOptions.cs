using System.Collections.Generic;

namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Опции одного подключения БД
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 5.1, подсекция
    /// <c>SqlAgent:Connections[*]</c>).
    /// <para>
    /// Используется:
    /// <list type="bullet">
    ///   <item><description><see cref="IIChatTools.Services.Interfaces.ISqlConnectionProvider"/>
    ///   — для создания соединения (Provider, ConnectionStringKey);</description></item>
    ///   <item><description><see cref="IIChatTools.Services.Interfaces.ISqlQueryValidator"/>
    ///   — для проверки whitelist (AllowedTables, DeniedTables, MaxRows);</description></item>
    ///   <item><description><c>SqlAgentService</c> — для управления
    ///   таймаутом (StatementTimeoutSeconds) и approval (RequiresApproval).</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public class SqlAgentConnectionOptions
    {
        /// <summary>
        /// Человекочитаемое название подключения (для UI и ответа LLM).
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Провайдер БД: <c>Sqlite</c>, <c>SqlServer</c>
        /// (в Фазе 2 — <c>Postgres</c>, <c>MySql</c>).
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// Ключ конфигурации, по которому лежит строка подключения
        /// (например, <c>SqlAgent:Internal:ConnectionString</c>).
        /// <b>Не сама строка</b> — она хранится в User Secrets / env.
        /// </summary>
        public string ConnectionStringKey { get; set; }

        /// <summary>
        /// Whitelist таблиц. Запросы к таблицам, которых здесь нет,
        /// отклоняются валидатором.
        /// <para>
        /// Сравнение — case-sensitive для Sqlite (там таблицы
        /// case-sensitive по умолчанию), для SqlServer — case-insensitive,
        /// но рекомендуется писать точно как в БД.
        /// </para>
        /// </summary>
        public List<string> AllowedTables { get; set; } = new List<string>();

        /// <summary>
        /// Blacklist таблиц. Перебивает whitelist — таблица из обоих
        /// списков считается запрещённой. Защита от случайного добавления PII.
        /// </summary>
        public List<string> DeniedTables { get; set; } = new List<string>();

        /// <summary>
        /// Максимальное количество строк в результате.
        /// Используется для auto-LIMIT и для дополнительной защиты
        /// при чтении результата (обход через <c>UNION</c>).
        /// По умолчанию — 100.
        /// </summary>
        public int MaxRows { get; set; } = 100;

        /// <summary>
        /// Таймаут выполнения SQL-запроса в секундах
        /// (<c>DbCommand.CommandTimeout</c>). По умолчанию — 15 секунд.
        /// </summary>
        public int StatementTimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// Требовать ли approval для <c>execute_query</c>.
        /// Метаданные (<c>list_*</c>, <c>describe_*</c>) — всегда без approval.
        /// </summary>
        public bool RequiresApproval { get; set; } = true;

        /// <summary>
        /// Опциональное описание подключения (для UI и ответа LLM).
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Признак, что подключение включено (runtime-флаг).
        /// Управляется через <c>/admin → SQL Agent</c> и хранится
        /// в <c>AppSettings</c> (ключ <c>SqlAgent.{name}.enabled</c>).
        /// <b>Не читается из <c>appsettings.json</c></b> — там его нет.
        /// По умолчанию — <c>true</c>.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}