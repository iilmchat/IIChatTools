using IIChatTools.Services.DTO.SqlAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Валидатор SQL-запросов Database Agent
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 6.2).
    /// <para>
    /// Отвечает за:
    /// <list type="bullet">
    ///   <item><description>базовые проверки (<c>SELECT</c> / <c>WITH</c>,
    ///   длина, одиночный statement);</description></item>
    ///   <item><description>запрет ключевых слов (<c>INSERT</c>, <c>DELETE</c>, <c>DROP</c>, ...);</description></item>
    ///   <item><description>запрет опасных функций (<c>load_extension</c>, <c>readfile</c>, ...);</description></item>
    ///   <item><description>извлечение таблиц + проверка whitelist;</description></item>
    ///   <item><description>добавление auto-LIMIT, если в запросе нет <c>LIMIT</c>.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public interface ISqlQueryValidator
    {
        /// <summary>
        /// Проверяет SQL-запрос на соответствие правилам безопасности
        /// указанного подключения.
        /// </summary>
        /// <param name="sql">Исходный SQL-запрос от LLM</param>
        /// <param name="options">Опции подключения (whitelist / blacklist таблиц, лимиты)</param>
        /// <returns>
        /// Результат валидации: флаг <c>IsValid</c>, санитизированный SQL
        /// (с auto-LIMIT), список использованных таблиц или текст ошибки.
        /// </returns>
        ValidationResult Validate(string sql, SqlAgentConnectionOptions options);
    }
}