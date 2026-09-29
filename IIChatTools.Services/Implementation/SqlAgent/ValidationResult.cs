using System.Collections.Generic;

namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Результат валидации SQL-запроса
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 6.2).
    /// Возвращается методом
    /// <see cref="IIChatTools.Services.Interfaces.ISqlQueryValidator.Validate"/>.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Признак успешной валидации. Если <c>false</c> — запрос НЕ должен
        /// выполняться, а <see cref="Error"/> содержит причину.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Текст ошибки (например, «Таблицы запрещены: AspNetUsers»).
        /// <c>null</c>, если <see cref="IsValid"/> = <c>true</c>.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Санитизированный SQL-запрос: тот же, что на входе,
        /// но с добавленным auto-LIMIT (если <see cref="LimitAdded"/> = <c>true</c>).
        /// <c>null</c>, если <see cref="IsValid"/> = <c>false</c>.
        /// </summary>
        public string SanitizedSql { get; set; }

        /// <summary>
        /// Список таблиц, извлечённых из запроса (<c>FROM</c> / <c>JOIN</c>).
        /// Используется для логирования и проверки whitelist.
        /// </summary>
        public IReadOnlyList<string> ReferencedTables { get; set; }

        /// <summary>
        /// Признак, что в <see cref="SanitizedSql"/> был добавлен auto-LIMIT
        /// (потому что в исходном SQL лимита не было).
        /// </summary>
        public bool LimitAdded { get; set; }
    }
}