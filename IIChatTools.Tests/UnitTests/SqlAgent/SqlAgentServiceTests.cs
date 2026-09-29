using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;              // FirstOrDefault / Single для IReadOnlyList
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Implementation.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.SqlAgent
{
    /// <summary>
    /// Тесты <see cref="SqlAgentService"/>
    /// (v1.7.0, KI-097, Фаза 4).
    /// <para>
    /// Используют реальный in-memory Sqlite + fake <see cref="ISqlConnectionProvider"/>,
    /// который отдаёт общий connection (без закрытия при Dispose).
    /// </para>
    /// </summary>
    public class SqlAgentServiceTests
    {
        // ============ ListConnectionsAsync ============

        [Fact]
        public async Task ListConnectionsAsync_ReturnsRegisteredConnections()
        {
            using var ctx = TestContext.Create();
            var result = await ctx.Service.ListConnectionsAsync();

            Assert.Single(result);
            Assert.Equal("internal", result[0].Name);
            Assert.Equal("Test DB", result[0].DisplayName);
            Assert.Equal("Sqlite", result[0].Provider);
            Assert.True(result[0].Enabled);
        }

        [Fact]
        public async Task ListConnectionsAsync_DoesNotExposeConnectionString()
        {
            using var ctx = TestContext.Create();
            var result = await ctx.Service.ListConnectionsAsync();

            // DTO не имеет поля ConnectionString — проверяем через рефлексию,
            // что такой property не завёлся случайно.
            var props = typeof(DatabaseConnectionInfoDto).GetProperties();
            foreach (var p in props)
            {
                Assert.DoesNotContain("ConnectionString", p.Name);
            }
        }

        // ============ ListTablesAsync ============

        [Fact]
        public async Task ListTablesAsync_ReturnsWhitelistTablesWithCounts()
        {
            using var ctx = TestContext.Create();
            await ctx.SeedTableAsync("Chats", 3);
            await ctx.SeedTableAsync("AuditLogs", 0);

            var result = await ctx.Service.ListTablesAsync("internal");

            Assert.Equal(2, result.Count);
            Assert.Contains(result, t => t.Name == "Chats" && t.RowCount == 3);
            Assert.Contains(result, t => t.Name == "AuditLogs" && t.RowCount == 0);
        }

        [Fact]
        public async Task ListTablesAsync_SkipsDeniedTables()
        {
            using var ctx = TestContext.Create();
            await ctx.SeedTableAsync("Chats", 1);
            await ctx.SeedTableAsync("Secrets", 0);  // Denied
            ctx.SetAllowedTables("Chats", "Secrets");
            ctx.SetDeniedTables("Secrets");

            var result = await ctx.Service.ListTablesAsync("internal");

            Assert.Single(result);
            Assert.Equal("Chats", result[0].Name);
        }

        [Fact]
        public async Task ListTablesAsync_UnknownConnection_Throws()
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(
                () => ctx.Service.ListTablesAsync("no-such"));
        }

        // ============ DescribeTableAsync ============

        [Fact]
        public async Task DescribeTableAsync_ReturnsColumnsWithTypes()
        {
            using var ctx = TestContext.Create();
            await ctx.ExecAsync(
                "CREATE TABLE Chats (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, Note TEXT)");

            var result = await ctx.Service.DescribeTableAsync("internal", "Chats");

            Assert.Equal(3, result.Count);
            // IReadOnlyList<T> не имеет .Find (это instance-метод List<T>) —
            // используем LINQ FirstOrDefault.
            var id = result.FirstOrDefault(c => c.Name == "Id");
            Assert.NotNull(id);
            Assert.Equal("INTEGER", id.Type);

            var title = result.FirstOrDefault(c => c.Name == "Title");
            Assert.NotNull(title);
            Assert.False(title.Nullable);

            var note = result.FirstOrDefault(c => c.Name == "Note");
            Assert.NotNull(note);
            Assert.True(note.Nullable);
        }

        [Fact]
        public async Task DescribeTableAsync_NotInWhitelist_Throws()
        {
            using var ctx = TestContext.Create();
            await ctx.ExecAsync("CREATE TABLE Other (Id INTEGER)");

            await Assert.ThrowsAsync<ArgumentException>(
                () => ctx.Service.DescribeTableAsync("internal", "Other"));
        }

        // ============ ExecuteQueryAsync ============

        [Fact]
        public async Task ExecuteQueryAsync_ValidSelect_ReturnsRows()
        {
            using var ctx = TestContext.Create();
            await ctx.SeedTableAsync("Chats", 5);

            var result = await ctx.Service.ExecuteQueryAsync(new SqlQueryRequest
            {
                Connection = "internal",
                Sql = "SELECT COUNT(*) AS Total FROM Chats"
            });

            Assert.Single(result.Rows);
            Assert.Single(result.Columns);            // xUnit2013: для Count == 1 использовать Single
            Assert.Equal("Total", result.Columns[0]);
            // COUNT(*) возвращает Int64.
            Assert.Equal(5L, Convert.ToInt64(result.Rows[0]["Total"]));
            Assert.False(result.Truncated);
        }

        [Fact]
        public async Task ExecuteQueryAsync_AutoLimitApplied()
        {
            using var ctx = TestContext.Create();
            await ctx.SeedTableAsync("Chats", 200);

            // Без LIMIT → auto-LIMIT = MaxRows (100 в тестовом baseline).
            var result = await ctx.Service.ExecuteQueryAsync(new SqlQueryRequest
            {
                Connection = "internal",
                Sql = "SELECT Id FROM Chats"
            });

            Assert.True(result.Truncated);
            Assert.Equal(100, result.Rows.Count);
        }

        [Fact]
        public async Task ExecuteQueryAsync_DeniedKeyword_Throws()
        {
            using var ctx = TestContext.Create();
            await ctx.SeedTableAsync("Chats", 1);

            var ex = await Assert.ThrowsAsync<ArgumentException>(
                () => ctx.Service.ExecuteQueryAsync(new SqlQueryRequest
                {
                    Connection = "internal",
                    Sql = "DELETE FROM Chats"
                }));

            Assert.Contains("валидатор", ex.Message);
        }

        [Fact]
        public async Task ExecuteQueryAsync_Truncated_WhenRowCountExceedsMaxRows()
        {
            using var ctx = TestContext.Create();
            ctx.SetMaxRows(10);
            await ctx.SeedTableAsync("Chats", 25);

            var result = await ctx.Service.ExecuteQueryAsync(new SqlQueryRequest
            {
                Connection = "internal",
                Sql = "SELECT Id FROM Chats"
            });

            Assert.True(result.Truncated);
            Assert.Equal(10, result.Rows.Count);
        }

        // ============ Test infrastructure ============

        /// <summary>
        /// Изолированный test-context: реальный in-memory Sqlite + fake-провайдер.
        /// </summary>
        private sealed class TestContext : IDisposable
        {
            private readonly SqliteConnection _kernel;
            private readonly SqlAgentOptionsProvider _optionsProvider;
            private readonly SqlAgentConnectionOptions _connectionOptions;

            public SqlAgentService Service { get; }

            private TestContext(SqliteConnection kernel,
                SqlAgentOptionsProvider optionsProvider,
                SqlAgentConnectionOptions connectionOptions)
            {
                _kernel = kernel;
                _optionsProvider = optionsProvider;
                _connectionOptions = connectionOptions;

                var validator = new SqlQueryValidator(
                    optionsProvider, NullLogger<SqlQueryValidator>.Instance);

                var connProvider = new FakeConnectionProvider(
                    new NonClosingSqliteConnection(kernel), "internal", "Sqlite");

                Service = new SqlAgentService(
                    connProvider, validator, optionsProvider,
                    NullLogger<SqlAgentService>.Instance);
            }

            public static TestContext Create()
            {
                var kernel = new SqliteConnection("Data Source=:memory:");
                kernel.Open();

                _ = kernel;  // подавляем warning про неиспользуемую переменную

                var conn = new SqlAgentConnectionOptions
                {
                    DisplayName = "Test DB",
                    Provider = "Sqlite",
                    ConnectionStringKey = "SqlAgent:Internal:ConnectionString",
                    AllowedTables = new List<string> { "Chats", "AuditLogs" },
                    DeniedTables = new List<string>(),
                    MaxRows = 100,
                    StatementTimeoutSeconds = 15,
                    RequiresApproval = true,
                    Enabled = true
                };

                var baseline = new SqlAgentOptions
                {
                    Enabled = true,
                    DefaultConnection = "internal",
                    Connections = new Dictionary<string, SqlAgentConnectionOptions>
                    {
                        ["internal"] = conn
                    },
                    QueryValidation = new SqlQueryValidationOptions
                    {
                        DeniedKeywords = new List<string>
                        {
                            "INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "ALTER",
                            "CREATE", "REPLACE", "MERGE", "GRANT", "REVOKE", "EXEC", "EXECUTE",
                            "ATTACH", "DETACH", "PRAGMA", "VACUUM", "ANALYZE", "REINDEX",
                            "COMMIT", "ROLLBACK", "SAVEPOINT", "BEGIN", "END"
                        },
                        DeniedFunctions = new List<string>
                        {
                            "load_extension", "readfile", "writefile", "edit", "fts3_tokenizer"
                        },
                        MaxSqlLength = 4000,
                        AutoLimitIfMissing = true
                    }
                };

                var opts = new SqlAgentOptionsProvider(
                    Options.Create(baseline),
                    NullLogger<SqlAgentOptionsProvider>.Instance);

                return new TestContext(kernel, opts, conn);
            }

            public Task SeedTableAsync(string name, int rowCount)
            {
                var sql = $"CREATE TABLE [{name}] (Id INTEGER PRIMARY KEY, Value TEXT)";
                var cmd = _kernel.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
                cmd.Dispose();

                for (int i = 0; i < rowCount; i++)
                {
                    var ins = _kernel.CreateCommand();
                    ins.CommandText = $"INSERT INTO [{name}] (Value) VALUES ('v{i}')";
                    ins.ExecuteNonQuery();
                    ins.Dispose();
                }
                return Task.CompletedTask;
            }

            public Task ExecAsync(string sql)
            {
                var cmd = _kernel.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
                cmd.Dispose();
                return Task.CompletedTask;
            }

            public void SetAllowedTables(params string[] tables)
            {
                _connectionOptions.AllowedTables = new List<string>(tables);
            }

            public void SetDeniedTables(params string[] tables)
            {
                _connectionOptions.DeniedTables = new List<string>(tables);
            }

            public void SetMaxRows(int max) { _connectionOptions.MaxRows = max; }

            public void Dispose()
            {
                _kernel.Dispose();
            }
        }

        /// <summary>
        /// Fake-провайдер соединений: всегда возвращает один и тот же DbConnection.
        /// </summary>
        private sealed class FakeConnectionProvider : ISqlConnectionProvider
        {
            private readonly DbConnection _connection;
            private readonly string _name;
            private readonly string _provider;

            public FakeConnectionProvider(DbConnection conn, string name, string provider)
            {
                _connection = conn; _name = name; _provider = provider;
            }

            public bool Exists(string connectionName) =>
                string.Equals(connectionName, _name, StringComparison.OrdinalIgnoreCase);

            public string GetProvider(string connectionName)
            {
                if (!Exists(connectionName))
                    throw new ArgumentException($"Подключение '{connectionName}' не зарегистрировано.");
                return _provider;
            }

            public Task<DbConnection> CreateConnectionAsync(
                string connectionName, CancellationToken ct = default)
            {
                if (!Exists(connectionName))
                    throw new ArgumentException($"Подключение '{connectionName}' не зарегистрировано.");
                return Task.FromResult(_connection);
            }
        }

        /// <summary>
        /// Обёртка над <see cref="SqliteConnection"/>, которая игнорирует
        /// <c>Dispose</c> — чтобы сервис не закрывал kernel-connection
        /// (иначе in-memory БД теряется).
        /// </summary>
        private sealed class NonClosingSqliteConnection : DbConnection
        {
            private readonly SqliteConnection _inner;

            public NonClosingSqliteConnection(SqliteConnection inner) { _inner = inner; }

            public override string ConnectionString
            {
                get => _inner.ConnectionString;
                set => _inner.ConnectionString = value;
            }
            public override string Database => _inner.Database;
            public override string DataSource => _inner.DataSource;
            public override string ServerVersion => _inner.ServerVersion;
            public override ConnectionState State => _inner.State;

            public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
            public override void Close() { /* no-op */ }
            public override void Open() { if (_inner.State != ConnectionState.Open) _inner.Open(); }
            public override Task OpenAsync(CancellationToken ct) =>
                _inner.State == ConnectionState.Open ? Task.CompletedTask : _inner.OpenAsync(ct);

            protected override DbTransaction BeginDbTransaction(IsolationLevel level) =>
                _inner.BeginTransaction(level);
            protected override DbCommand CreateDbCommand() => _inner.CreateCommand();

            protected override void Dispose(bool disposing) { /* no-op — kernel закрывается TestContext'ом */ }
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}