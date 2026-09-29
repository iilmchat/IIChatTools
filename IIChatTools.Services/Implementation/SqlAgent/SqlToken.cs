namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Виды токенов SQL-запроса
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 6.2).
    /// Используется внутренним токенизатором
    /// <see cref="SqlQueryValidator"/>.
    /// </summary>
    internal enum SqlTokenKind
    {
        /// <summary>Идентификатор или ключевое слово (SELECT, FROM, Chats, ...).</summary>
        Identifier,

        /// <summary>Строковый литерал (<c>'text'</c>). Кавычки включены в <see cref="SqlToken.Text"/>.</summary>
        StringLiteral,

        /// <summary>Идентификатор в кавычках (<c>"Chats"</c>, <c>[Chats]</c>, <c>`Chats`</c>).</summary>
        QuotedIdentifier,

        /// <summary>Комментарий (<c>-- ...</c> до конца строки или <c>/* ... */</c>).</summary>
        Comment,

        /// <summary>Числовой литерал (42, 3.14, 1e10).</summary>
        Number,

        /// <summary>Знаки пунктуации: <c>( ) , ; = &lt; &gt; ! + - * / % . | &amp; ^ ~</c>.</summary>
        Punctuation
    }

    /// <summary>
    /// Один токен SQL-запроса (v1.7.0, KI-097).
    /// </summary>
    internal sealed class SqlToken
    {
        /// <summary>Вид токена.</summary>
        public SqlTokenKind Kind { get; init; }

        /// <summary>
        /// Текстовое представление. Для <see cref="SqlTokenKind.StringLiteral"/>
        /// и <see cref="SqlTokenKind.QuotedIdentifier"/> — содержимое **без**
        /// обрамляющих кавычек (с раскрытыми escape-последовательностями).
        /// Для остальных — как в исходном SQL.
        /// </summary>
        public string Text { get; init; }

        /// <summary>Позиция начала токена в исходной строке (0-based).</summary>
        public int Start { get; init; }

        /// <summary>Длина токена в исходной строке.</summary>
        public int Length { get; init; }
    }
}