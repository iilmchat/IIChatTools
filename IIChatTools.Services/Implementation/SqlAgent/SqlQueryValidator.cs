using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Валидатор SQL-запросов Database Agent
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 6.2).
    /// <para>
    /// 7 шагов валидации:
    /// <list type="number">
    ///   <item><description>Базовые проверки (пустой / длина / первый токен / multi-statement);</description></item>
    ///   <item><description>Токенизация (литералы и комментарии исключаются из дальнейших проверок);</description></item>
    ///   <item><description>Запрет ключевых слов (INSERT, DELETE, DROP, ...);</description></item>
    ///   <item><description>Запрет функций (load_extension, readfile, ...);</description></item>
    ///   <item><description>Извлечение таблиц (FROM / JOIN);</description></item>
    ///   <item><description>Whitelist / blacklist таблиц;</description></item>
    ///   <item><description>Auto-LIMIT (если в SQL нет LIMIT).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> Singleton (stateless).
    /// </para>
    /// </summary>
    public class SqlQueryValidator : ISqlQueryValidator
    {
        /// <summary>Ключевые слова, разрешённые в начале SQL (read-only конструкции).</summary>
        private static readonly HashSet<string> AllowedFirstKeywords =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SELECT", "WITH" };

        private readonly SqlAgentOptionsProvider _optionsProvider;
        private readonly ILogger<SqlQueryValidator> _logger;

        /// <summary>
        /// Создаёт валидатор.
        /// </summary>
        /// <param name="optionsProvider">Провайдер актуальных опций SqlAgent (для правил валидации)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        public SqlQueryValidator(
            SqlAgentOptionsProvider optionsProvider,
            ILogger<SqlQueryValidator> logger)
        {
            _optionsProvider = optionsProvider
                ?? throw new ArgumentNullException(nameof(optionsProvider));
            _logger = logger;
        }

        /// <inheritdoc/>
        public ValidationResult Validate(string sql, SqlAgentConnectionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var rules = _optionsProvider.QueryValidation;

            // ============ Шаг 1: базовые проверки ============
            if (string.IsNullOrWhiteSpace(sql))
                return Fail("SQL-запрос пуст.");

            if (rules.MaxSqlLength > 0 && sql.Length > rules.MaxSqlLength)
                return Fail($"SQL превышает лимит {rules.MaxSqlLength} символов.");

            var tokens = Tokenize(sql);
            var significant = tokens
                .Where(t => t.Kind != SqlTokenKind.Comment)
                .ToList();

            if (significant.Count == 0)
                return Fail("SQL-запрос не содержит значимых токенов.");

            // Первый значимый токен — SELECT или WITH.
            var first = significant[0];
            if (first.Kind != SqlTokenKind.Identifier
                || !AllowedFirstKeywords.Contains(first.Text))
            {
                return Fail("Разрешены только SELECT и WITH (CTE).");
            }

            // Multi-statement: если есть `;`, после которого идут значимые токены → fail.
            var semicolonIndexes = significant
                .Select((t, idx) => new { Token = t, Index = idx })
                .Where(x => x.Token.Kind == SqlTokenKind.Punctuation && x.Token.Text == ";")
                .Select(x => x.Index)
                .ToList();

            if (semicolonIndexes.Count > 0)
            {
                var lastSemicolonIdx = semicolonIndexes[semicolonIndexes.Count - 1];
                if (lastSemicolonIdx < significant.Count - 1)
                {
                    return Fail("Множественные операторы не поддерживаются.");
                }
                // Если `;` — последний значимый токен, это допустимо.
                // Но если `;` встречается более одного раза в середине — тоже fail.
                if (semicolonIndexes.Count > 1)
                {
                    return Fail("Множественные операторы не поддерживаются.");
                }
            }

            // ============ Шаг 3: запрет ключевых слов ============
            var deniedKeywordSet = new HashSet<string>(
                rules.DeniedKeywords ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var t in significant)
            {
                if (t.Kind != SqlTokenKind.Identifier) continue;
                if (deniedKeywordSet.Contains(t.Text))
                    return Fail($"Запрещённое ключевое слово: {t.Text.ToUpperInvariant()}");
            }

            // ============ Шаг 4: запрет функций ============
            var deniedFunctionSet = new HashSet<string>(
                rules.DeniedFunctions ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var t in significant)
            {
                if (t.Kind != SqlTokenKind.Identifier) continue;
                if (deniedFunctionSet.Contains(t.Text))
                    return Fail($"Запрещённая функция: {t.Text}");
            }

            // ============ Шаг 5: извлечение таблиц ============
            var cteNames = ExtractCteNames(significant);
            var tableNames = ExtractTableNames(significant, cteNames);

            // ============ Шаг 6: whitelist / blacklist ============
            // Sqlite — case-sensitive (RULES § 4.26), SqlServer — case-insensitive.
            // ВАЖНО: для HashSet<string> нужен StringComparer (IEqualityComparer<string>),
            // а НЕ StringComparison (enum для string.Equals / Contains).
            var comparer = IsSqlite(options.Provider)
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase;

            var deniedTablesSet = new HashSet<string>(
                options.DeniedTables ?? new List<string>(), comparer);
            var allowedTablesSet = new HashSet<string>(
                options.AllowedTables ?? new List<string>(), comparer);

            var deniedHit = tableNames.FirstOrDefault(t => deniedTablesSet.Contains(t));
            if (deniedHit != null)
                return Fail($"Таблица запрещена: {deniedHit}");

            if (allowedTablesSet.Count > 0)
            {
                var notAllowed = tableNames
                    .Where(t => !allowedTablesSet.Contains(t))
                    .ToList();
                if (notAllowed.Count > 0)
                    return Fail($"Таблицы не в whitelist: {string.Join(", ", notAllowed)}");
            }

            // ============ Шаг 7: auto-LIMIT ============
            var sanitizedSql = sql;
            var limitAdded = false;
            if (rules.AutoLimitIfMissing && !HasLimitClause(significant))
            {
                sanitizedSql = AddLimit(sql, options.MaxRows);
                limitAdded = true;
            }

            return new ValidationResult
            {
                IsValid = true,
                Error = null,
                SanitizedSql = sanitizedSql,
                ReferencedTables = tableNames,
                LimitAdded = limitAdded
            };
        }

        // ============ Токенизация ============

        /// <summary>
        /// Разбивает SQL на токены. Whitespace пропускается.
        /// </summary>
        internal static List<SqlToken> Tokenize(string sql)
        {
            var tokens = new List<SqlToken>();
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }

                // Комментарий -- ... до конца строки
                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    int start = i;
                    while (i < sql.Length && sql[i] != '\n') i++;
                    tokens.Add(new SqlToken
                    {
                        Kind = SqlTokenKind.Comment,
                        Text = sql.Substring(start, i - start),
                        Start = start,
                        Length = i - start
                    });
                    continue;
                }

                // Комментарий /* ... */
                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    int start = i;
                    i += 2;
                    while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                    i = Math.Min(i + 2, sql.Length);
                    tokens.Add(new SqlToken
                    {
                        Kind = SqlTokenKind.Comment,
                        Text = sql.Substring(start, i - start),
                        Start = start,
                        Length = i - start
                    });
                    continue;
                }

                // Строковый литерал '...'
                if (c == '\'')
                {
                    int start = i;
                    i++;
                    var sb = new StringBuilder();
                    while (i < sql.Length)
                    {
                        if (sql[i] == '\'')
                        {
                            if (i + 1 < sql.Length && sql[i + 1] == '\'')
                            {
                                sb.Append('\'');
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        sb.Append(sql[i]);
                        i++;
                    }
                    tokens.Add(new SqlToken
                    {
                        Kind = SqlTokenKind.StringLiteral,
                        Text = sb.ToString(),
                        Start = start,
                        Length = i - start
                    });
                    continue;
                }

                // Quoted identifier "..." / [...] / `...`
                if (c == '"' || c == '[' || c == '`')
                {
                    char close = c == '[' ? ']' : c;
                    int start = i;
                    i++;
                    var sb = new StringBuilder();
                    while (i < sql.Length)
                    {
                        if (sql[i] == close)
                        {
                            if (c != '[' && i + 1 < sql.Length && sql[i + 1] == close)
                            {
                                sb.Append(close);
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        sb.Append(sql[i]);
                        i++;
                    }
                    tokens.Add(new SqlToken
                    {
                        Kind = SqlTokenKind.QuotedIdentifier,
                        Text = sb.ToString(),
                        Start = start,
                        Length = i - start
                    });
                    continue;
                }

                // Identifier / keyword
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < sql.Length &&
                           (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '$'))
                        i++;
                    tokens.Add(new SqlToken
                    {
                        Kind = SqlTokenKind.Identifier,
                        Text = sql.Substring(start, i - start),
                        Start = start,
                        Length = i - start
                    });
                    continue;
                }

                // Number
                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < sql.Length &&
                           (char.IsDigit(sql[i]) || sql[i] == '.' ||
                            sql[i] == 'e' || sql[i] == 'E'))
                        i++;
                    tokens.Add(new SqlToken
                    {
                        Kind = SqlTokenKind.Number,
                        Text = sql.Substring(start, i - start),
                        Start = start,
                        Length = i - start
                    });
                    continue;
                }

                // Punctuation
                tokens.Add(new SqlToken
                {
                    Kind = SqlTokenKind.Punctuation,
                    Text = c.ToString(),
                    Start = i,
                    Length = 1
                });
                i++;
            }
            return tokens;
        }

        // ============ Извлечение CTE ============

        /// <summary>
        /// Извлекает имена CTE из конструкции <c>WITH cte1 AS (...), cte2 AS (...)</c>.
        /// Возвращает пустой список, если WITH нет.
        /// </summary>
        private static List<string> ExtractCteNames(List<SqlToken> significant)
        {
            var names = new List<string>();
            if (significant.Count == 0) return names;

            // Первый токен — WITH (мы уже проверяли в шаге 1).
            if (!string.Equals(significant[0].Text, "WITH", StringComparison.OrdinalIgnoreCase))
                return names;

            int i = 1;
            while (i < significant.Count)
            {
                // Следующий токен — имя CTE (Identifier).
                if (significant[i].Kind == SqlTokenKind.Identifier)
                {
                    names.Add(significant[i].Text);
                    i++;
                }
                else
                {
                    break;
                }

                // Пропускаем до AS.
                while (i < significant.Count &&
                       !string.Equals(significant[i].Text, "AS", StringComparison.OrdinalIgnoreCase))
                    i++;
                if (i >= significant.Count) break;
                i++; // AS

                // Пропускаем `(` ... `)` со вложенными скобками.
                if (i >= significant.Count
                    || significant[i].Kind != SqlTokenKind.Punctuation
                    || significant[i].Text != "(")
                    break;

                int depth = 1;
                i++;
                while (i < significant.Count && depth > 0)
                {
                    if (significant[i].Kind == SqlTokenKind.Punctuation)
                    {
                        if (significant[i].Text == "(") depth++;
                        else if (significant[i].Text == ")") depth--;
                    }
                    i++;
                }

                // После `)` — либо `,` (следующий CTE), либо конец WITH.
                if (i >= significant.Count) break;
                if (significant[i].Kind == SqlTokenKind.Punctuation && significant[i].Text == ",")
                {
                    i++;
                    continue;
                }
                break;
            }

            return names;
        }

        // ============ Извлечение таблиц ============

        /// <summary>
        /// Извлекает имена таблиц после <c>FROM</c> / <c>JOIN</c>.
        /// Исключает имена CTE.
        /// </summary>
        private static List<string> ExtractTableNames(
            List<SqlToken> significant,
            List<string> cteNames)
        {
            var names = new List<string>();
            var cteSet = new HashSet<string>(cteNames, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < significant.Count; i++)
            {
                var t = significant[i];
                if (t.Kind != SqlTokenKind.Identifier) continue;

                bool isFrom = string.Equals(t.Text, "FROM", StringComparison.OrdinalIgnoreCase);
                bool isJoin = t.Text.EndsWith("JOIN", StringComparison.OrdinalIgnoreCase)
                              && (t.Text.Length == 4 || t.Text.Length == 5 || t.Text.Length == 6);
                // LEFT/RIGHT/INNER/OUTER/CROSS JOIN — токенизатор разбивает их на 2 токена
                // (LEFT + JOIN). Значит isJoin == true только для одиночного JOIN (4).

                if (!isFrom && !isJoin) continue;

                // Следующий значимый токен.
                int j = i + 1;
                if (j >= significant.Count) continue;
                var next = significant[j];

                // Если после FROM/JOIN идёт `(` — это подзапрос. Пропускаем
                // (внутри него будет свой FROM, который мы подхватим отдельно).
                if (next.Kind == SqlTokenKind.Punctuation && next.Text == "(") continue;

                // Identifier / QuotedIdentifier — это имя таблицы (возможно schema.table).
                if (next.Kind == SqlTokenKind.Identifier
                    || next.Kind == SqlTokenKind.QuotedIdentifier)
                {
                    var name = next.Text;

                    // schema.table — берём последнюю часть (dbo.Chats → Chats).
                    if (j + 2 < significant.Count
                        && significant[j + 1].Kind == SqlTokenKind.Punctuation
                        && significant[j + 1].Text == "."
                        && (significant[j + 2].Kind == SqlTokenKind.Identifier
                            || significant[j + 2].Kind == SqlTokenKind.QuotedIdentifier))
                    {
                        name = significant[j + 2].Text;
                    }

                    // Пропускаем CTE-имена.
                    if (!cteSet.Contains(name))
                        names.Add(name);
                }
            }

            return names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ============ LIMIT ============

        /// <summary>
        /// Проверяет, есть ли в запросе <c>LIMIT</c> (или <c>TOP</c> для SqlServer).
        /// </summary>
        private static bool HasLimitClause(List<SqlToken> significant)
        {
            foreach (var t in significant)
            {
                if (t.Kind != SqlTokenKind.Identifier) continue;
                if (string.Equals(t.Text, "LIMIT", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(t.Text, "TOP", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Добавляет <c>LIMIT N</c> в конец SQL (перед финальной <c>;</c>, если она есть).
        /// </summary>
        private static string AddLimit(string sql, int maxRows)
        {
            var trimmed = sql.TrimEnd();
            bool hasTrailingSemi = trimmed.EndsWith(";");
            if (hasTrailingSemi)
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();

            return hasTrailingSemi
                ? $"{trimmed} LIMIT {maxRows};"
                : $"{trimmed} LIMIT {maxRows}";
        }

        // ============ Helpers ============

        private static bool IsSqlite(string provider)
        {
            return string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase);
        }

        private static ValidationResult Fail(string error)
        {
            return new ValidationResult
            {
                IsValid = false,
                Error = error,
                SanitizedSql = null,
                ReferencedTables = new List<string>(),
                LimitAdded = false
            };
        }
    }
}