using System.Collections.Generic;

namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Опции Database Agent, загружаемые из секции <c>SqlAgent</c>
    /// в <c>appsettings.json</c> (v1.7.0, KI-097, DESIGN_DB_AGENT § 5.1).
    /// </summary>
    public class SqlAgentOptions
    {
        /// <summary>
        /// Глобальный флаг включения Database Agent.
        /// Если <c>false</c> — <c>DatabaseAgentTool</c> не регистрируется в DI,
        /// LLM его не видит.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Имя подключения по умолчанию. Используется, если LLM не указала
        /// параметр <c>connection</c> в вызове инструмента.
        /// </summary>
        public string DefaultConnection { get; set; } = "internal";

        /// <summary>
        /// Показывать ли вкладку «SQL Agent» в админке (<c>/admin</c>).
        /// </summary>
        public bool AdminUiEnabled { get; set; } = true;

        /// <summary>
        /// Словарь зарегистрированных подключений
        /// «имя подключения → опции». В Фазе 1 — только <c>internal</c>.
        /// </summary>
        public Dictionary<string, SqlAgentConnectionOptions> Connections { get; set; } =
            new Dictionary<string, SqlAgentConnectionOptions>();

        /// <summary>
        /// Правила валидации SQL-запросов (общие для всех подключений).
        /// </summary>
        public SqlQueryValidationOptions QueryValidation { get; set; } =
            new SqlQueryValidationOptions();
    }

    /// <summary>
    /// Правила валидации SQL-запросов
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 5.1, подсекция
    /// <c>SqlAgent:QueryValidation</c>).
    /// </summary>
    public class SqlQueryValidationOptions
    {
        /// <summary>
        /// Запрещённые ключевые слова (case-insensitive, по границам слов).
        /// По умолчанию: <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>, <c>DROP</c>,
        /// <c>TRUNCATE</c>, <c>ALTER</c>, <c>CREATE</c>, ...
        /// </summary>
        public List<string> DeniedKeywords { get; set; } = new List<string>();

        /// <summary>
        /// Запрещённые функции (например, Sqlite <c>load_extension</c> — RCE).
        /// </summary>
        public List<string> DeniedFunctions { get; set; } = new List<string>();

        /// <summary>
        /// Максимальная длина SQL-запроса в символах (защита от гигантских
        /// запросов). По умолчанию — 4000.
        /// </summary>
        public int MaxSqlLength { get; set; } = 4000;

        /// <summary>
        /// Добавлять ли <c>LIMIT N</c> в конец SQL, если его там нет.
        /// <c>N</c> берётся из <c>Connections[*].MaxRows</c>.
        /// </summary>
        public bool AutoLimitIfMissing { get; set; } = true;
    }
}