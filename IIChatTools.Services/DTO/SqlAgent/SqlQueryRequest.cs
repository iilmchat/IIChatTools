namespace IIChatTools.Services.DTO.SqlAgent
{
    /// <summary>
    /// Запрос на выполнение read-only SQL-запроса
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1).
    /// Принимается методом
    /// <see cref="IIChatTools.Services.Interfaces.ISqlAgentService.ExecuteQueryAsync"/>.
    /// </summary>
    public class SqlQueryRequest
    {
        /// <summary>
        /// Имя подключения (например, <c>internal</c>).
        /// Если <c>null</c> / пусто — используется
        /// <c>SqlAgent:DefaultConnection</c> из конфигурации.
        /// </summary>
        public string Connection { get; set; }

        /// <summary>
        /// SQL-запрос (только <c>SELECT</c> / <c>WITH</c>).
        /// Валидируется <c>ISqlQueryValidator</c> перед выполнением.
        /// </summary>
        public string Sql { get; set; }

        /// <summary>
        /// Опциональный лимит строк (переопределяет <c>Connections[*].MaxRows</c>).
        /// Если не задан — используется значение из конфигурации подключения.
        /// </summary>
        public int? MaxRows { get; set; }
    }
}