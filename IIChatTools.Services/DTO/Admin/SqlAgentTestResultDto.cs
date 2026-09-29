namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Результат проверки подключения БД
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6).
    /// Возвращается <c>POST /api/admin/sql-agent/connections/{name}/test</c>.
    /// </summary>
    public class SqlAgentTestResultDto
    {
        /// <summary>true, если соединение открылось и <c>SELECT 1</c> выполнился.</summary>
        public bool Success { get; set; }

        /// <summary>Сообщение (успех или текст ошибки).</summary>
        public string Message { get; set; }

        /// <summary>Длительность проверки (мс).</summary>
        public long DurationMs { get; set; }
    }
}