using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Оркестратор Database Agent
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1 и § 7.4).
    /// <para>
    /// Реализует 4 read-only операции:
    /// <list type="bullet">
    ///   <item><description><see cref="ListConnectionsAsync"/> — метаданные подключений;</description></item>
    ///   <item><description><see cref="ListTablesAsync"/> — whitelist-таблицы + row count;</description></item>
    ///   <item><description><see cref="DescribeTableAsync"/> — колонки + типы + пример значения;</description></item>
    ///   <item><description><see cref="ExecuteQueryAsync"/> — выполнение read-only SQL
    ///   с валидацией, timeout и защитой от UNION-обхода.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> Scoped (хотя все зависимости — Singleton; следует DESIGN § 4.1).
    /// </para>
    /// </summary>
    public class SqlAgentService : ISqlAgentService
    {
        private readonly ISqlConnectionProvider _connectionProvider;
        private readonly ISqlQueryValidator _validator;
        private readonly SqlAgentOptionsProvider _optionsProvider;
        private readonly ILogger<SqlAgentService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="connectionProvider">Фабрика соединений</param>
        /// <param name="validator">Валидатор SQL-запросов</param>
        /// <param name="optionsProvider">Провайдер актуальных опций SqlAgent</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        public SqlAgentService(
            ISqlConnectionProvider connectionProvider,
            ISqlQueryValidator validator,
            SqlAgentOptionsProvider optionsProvider,
            ILogger<SqlAgentService> logger)
        {
            _connectionProvider = connectionProvider
                ?? throw new ArgumentNullException(nameof(connectionProvider));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _optionsProvider = optionsProvider
                ?? throw new ArgumentNullException(nameof(optionsProvider));
            _logger = logger;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<DatabaseConnectionInfoDto>> ListConnectionsAsync(
            CancellationToken cancellationToken = default)
        {
            var names = _optionsProvider.GetAllConnectionNames();
            var result = new List<DatabaseConnectionInfoDto>(names.Count);

            foreach (var name in names)
            {
                var opts = _optionsProvider.Get(name);
                if (opts == null) continue;

                result.Add(new DatabaseConnectionInfoDto
                {
                    Name = name,
                    DisplayName = opts.DisplayName,
                    Provider = opts.Provider,
                    Enabled = opts.Enabled
                });
            }

            // Синхронный — ничего не делает, но контракт требует async-сигнатуру.
            return Task.FromResult<IReadOnlyList<DatabaseConnectionInfoDto>>(result);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<SqlTableInfoDto>> ListTablesAsync(
            string connection,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connection))
                throw new ArgumentException("Имя подключения обязательно.", nameof(connection));

            var opts = _optionsProvider.Get(connection)
                ?? throw new ArgumentException(
                    $"Подключение '{connection}' не зарегистрировано.",
                    nameof(connection));

            if (!opts.Enabled)
                throw new InvalidOperationException(
                    $"Подключение '{connection}' отключено администратором.");

            var result = new List<SqlTableInfoDto>();

            await using var conn = await _connectionProvider
                .CreateConnectionAsync(connection, cancellationToken);

            // Whitelist минус DeniedTables (Denied перебивает).
            var comparer = GetStringComparer(opts.Provider);
            var deniedSet = new HashSet<string>(
                opts.DeniedTables ?? new List<string>(), comparer);

            foreach (var table in opts.AllowedTables ?? new List<string>())
            {
                if (deniedSet.Contains(table)) continue;

                var rowCount = -1;
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(table)}";
                    cmd.CommandTimeout = opts.StatementTimeoutSeconds;

                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (scalar != null && scalar != DBNull.Value)
                        rowCount = Convert.ToInt32(scalar);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "SqlAgent.ListTables: не удалось получить COUNT(*) для {Table} " +
                        "(connection={Connection}).", table, connection);
                }

                result.Add(new SqlTableInfoDto
                {
                    Name = table,
                    RowCount = rowCount
                });
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<SqlColumnInfoDto>> DescribeTableAsync(
            string connection,
            string table,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connection))
                throw new ArgumentException("Имя подключения обязательно.", nameof(connection));
            if (string.IsNullOrWhiteSpace(table))
                throw new ArgumentException("Имя таблицы обязательно.", nameof(table));

            var opts = _optionsProvider.Get(connection)
                ?? throw new ArgumentException(
                    $"Подключение '{connection}' не зарегистрировано.",
                    nameof(connection));

            if (!opts.Enabled)
                throw new InvalidOperationException(
                    $"Подключение '{connection}' отключено администратором.");

            // Whitelist-проверка (DESIGN § 6.3): DescribeTableAsync — тоже security-boundary.
            var comparer = GetStringComparer(opts.Provider);
            var allowedSet = new HashSet<string>(
                opts.AllowedTables ?? new List<string>(), comparer);
            var deniedSet = new HashSet<string>(
                opts.DeniedTables ?? new List<string>(), comparer);

            if (deniedSet.Contains(table))
                throw new ArgumentException(
                    $"Таблица '{table}' запрещена (DeniedTables).", nameof(table));
            if (!allowedSet.Contains(table))
                throw new ArgumentException(
                    $"Таблица '{table}' не в whitelist (AllowedTables).", nameof(table));

            await using var conn = await _connectionProvider
                .CreateConnectionAsync(connection, cancellationToken);

            var columns = await ReadColumnsAsync(conn, opts, table, cancellationToken);

            // Пример значения (первая непустая) — best-effort.
            foreach (var col in columns)
            {
                col.SampleValue = await TryReadSampleValueAsync(
                    conn, opts, table, col.Name, cancellationToken);
            }

            return columns;
        }

        /// <inheritdoc/>
        public async Task<SqlQueryResultDto> ExecuteQueryAsync(
            SqlQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var connectionName = string.IsNullOrWhiteSpace(request.Connection)
                ? _optionsProvider.DefaultConnection
                : request.Connection;

            var opts = _optionsProvider.Get(connectionName)
                ?? throw new ArgumentException(
                    $"Подключение '{connectionName}' не зарегистрировано.",
                    nameof(request));

            if (!opts.Enabled)
                throw new InvalidOperationException(
                    $"Подключение '{connectionName}' отключено администратором.");

            // ============ Валидация SQL (DESIGN § 6.2) ============
            var validation = _validator.Validate(request.Sql, opts);
            if (!validation.IsValid)
                throw new ArgumentException(
                    $"SQL отклонён валидатором: {validation.Error}", nameof(request));

            var maxRows = request.MaxRows ?? opts.MaxRows;
            if (maxRows <= 0) maxRows = opts.MaxRows;

            var sw = Stopwatch.StartNew();

            await using var conn = await _connectionProvider
                .CreateConnectionAsync(connectionName, cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = validation.SanitizedSql;
            cmd.CommandTimeout = opts.StatementTimeoutSeconds;

            var columns = new List<string>();
            var rows = new List<Dictionary<string, object>>();
            var truncated = false;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            // Читаем не более maxRows. DESIGN § 6.4: защита от UNION-обхода auto-LIMIT.
            //
            // Два сценария:
            //  (а) auto-LIMIT НЕ добавлен (в SQL явный LIMIT или он отключён).
            //      Reader вернёт все строки БД; если их больше maxRows — прерываем
            //      чтение на (maxRows + 1)-й итерации, truncated = true.
            //  (б) auto-LIMIT ДОБАВЛЕН валидатором (LimitAdded = true).
            //      SQLite/SqlServer сам обрежет результат по LIMIT — reader вернёт
            //      ровно maxRows строк, лишней итерации не будет, и `if (rows.Count >= maxRows)`
            //      НИКОГДА не сработает. Дополнительная post-loop проверка ниже
            //      выставляет truncated = true (консервативно: «могли быть ещё строки»).
            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new Dictionary<string, object>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }

            // Post-loop: сценарий (б). Auto-LIMIT добавлен и вернулось ровно maxRows —
            // консервативно помечаем как truncated (могли быть ещё строки в БД).
            // False positive возможен, если в таблице ровно maxRows строк — принимаем
            // как меньшую из зол (лучше переспросить, чем скрыть данные).
            if (!truncated && validation.LimitAdded && rows.Count >= maxRows)
            {
                truncated = true;
            }

            sw.Stop();

            _logger.LogInformation(
                "SqlAgent.ExecuteQuery: connection={Connection}, rows={Rows}, truncated={Truncated}, durationMs={Duration}",
                connectionName, rows.Count, truncated, sw.ElapsedMilliseconds);

            return new SqlQueryResultDto
            {
                Connection = connectionName,
                Columns = columns,
                Rows = rows,
                RowCount = rows.Count,
                Truncated = truncated,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        // ============ Private ============

        /// <summary>
        /// Читает список колонок таблицы (провайдер-специфично).
        /// </summary>
        private async Task<List<SqlColumnInfoDto>> ReadColumnsAsync(
            DbConnection conn,
            SqlAgentConnectionOptions opts,
            string table,
            CancellationToken ct)
        {
            var columns = new List<SqlColumnInfoDto>();

            if (IsSqlite(opts.Provider))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
                cmd.CommandTimeout = opts.StatementTimeoutSeconds;

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    // PRAGMA table_info: cid(0) name(1) type(2) notnull(3) dflt_value(4) pk(5)
                    var name = reader.GetString(1);
                    var type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    var notNull = !reader.IsDBNull(3) && reader.GetInt32(3) != 0;

                    columns.Add(new SqlColumnInfoDto
                    {
                        Name = name,
                        Type = type,
                        Nullable = !notNull
                    });
                }
            }
            else if (IsSqlServer(opts.Provider))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @tbl
                    ORDER BY ORDINAL_POSITION";
                cmd.CommandTimeout = opts.StatementTimeoutSeconds;

                var p = cmd.CreateParameter();
                p.ParameterName = "@tbl";
                p.Value = table;
                cmd.Parameters.Add(p);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    columns.Add(new SqlColumnInfoDto
                    {
                        Name = reader.GetString(0),
                        Type = reader.GetString(1),
                        Nullable = string.Equals(
                            reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"DescribeTableAsync не поддерживает провайдер '{opts.Provider}'. " +
                    "Допустимые (Фаза 1): Sqlite, SqlServer.");
            }

            return columns;
        }

        /// <summary>
        /// Читает пример значения (первая непустая строка). Best-effort: при ошибке
        /// (например, колонка BLOB) возвращает <c>null</c>.
        /// </summary>
        private async Task<string> TryReadSampleValueAsync(
            DbConnection conn,
            SqlAgentConnectionOptions opts,
            string table,
            string column,
            CancellationToken ct)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    $"SELECT {QuoteIdentifier(column)} FROM {QuoteIdentifier(table)} " +
                    $"WHERE {QuoteIdentifier(column)} IS NOT NULL LIMIT 1";
                cmd.CommandTimeout = opts.StatementTimeoutSeconds;

                var scalar = await cmd.ExecuteScalarAsync(ct);
                if (scalar == null || scalar == DBNull.Value) return null;

                var s = scalar.ToString();
                if (string.IsNullOrEmpty(s)) return null;
                if (s.Length > 200) s = s.Substring(0, 200) + "…";
                return s;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Sample value для {Table}.{Column} не получен.", table, column);
                return null;
            }
        }

        /// <summary>
        /// Возвращает компаратор строк для whitelist-проверок:
        /// Sqlite — Ordinal (case-sensitive, DESIGN § 6.3);
        /// SqlServer — OrdinalIgnoreCase.
        /// </summary>
        private static StringComparer GetStringComparer(string provider) =>
            IsSqlite(provider) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        private static bool IsSqlite(string provider) =>
            string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase);

        private static bool IsSqlServer(string provider) =>
            string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Оборачивает идентификатор в квадратные скобки (работает и в Sqlite, и в SqlServer).
        /// Экранирует внутренние <c>]</c> удвоением.
        /// </summary>
        private static string QuoteIdentifier(string name) =>
            "[" + name.Replace("]", "]]") + "]";
    }
}