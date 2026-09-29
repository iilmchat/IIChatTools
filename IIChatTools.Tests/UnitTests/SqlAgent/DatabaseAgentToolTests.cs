using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Implementation.Tools.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.SqlAgent
{
    /// <summary>
    /// Тесты <see cref="DatabaseAgentTool"/>
    /// (v1.7.0, KI-097, Фаза 5, DESIGN_DB_AGENT § 7.5).
    /// <para>
    /// Используют fake <see cref="ISqlAgentService"/> — не поднимают реальный SQLite.
    /// Реальный smoke — через <c>/api/tools/execute</c> (см. CHANGELOG).
    /// </para>
    /// </summary>
    public class DatabaseAgentToolTests
    {
        // ============ Метаданные tool ============

        [Fact]
        public void Name_IsDatabaseAgent()
        {
            var tool = BuildTool(new FakeSqlAgentService());
            Assert.Equal("database_agent", tool.Name);
        }

        [Fact]
        public void Description_IsNotEmpty_AndMentionsActions()
        {
            var tool = BuildTool(new FakeSqlAgentService());

            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            // Описание должно упоминать ключевые действия (для LLM).
            Assert.Contains("list_databases", tool.Description);
            Assert.Contains("execute_query", tool.Description);
        }

        [Fact]
        public void RequiresApprovalByDefault_IsTrue()
        {
            var tool = BuildTool(new FakeSqlAgentService());
            // v1.7.0: все 4 действия требуют approval (per-action — KI-101).
            Assert.True(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public void Parameters_HasRequiredAction()
        {
            var tool = BuildTool(new FakeSqlAgentService());

            var action = tool.Parameters.FirstOrDefault(p => p.Name == "action");
            Assert.NotNull(action);
            Assert.True(action.Required);
            Assert.Equal("string", action.Type);

            // action упомянуто в описании как одно из 4 значений.
            Assert.Contains("list_databases", action.Description);
            Assert.Contains("execute_query", action.Description);
        }

        [Fact]
        public void Parameters_HasOptionalConnectionAndSqlAndTable()
        {
            var tool = BuildTool(new FakeSqlAgentService());

            var names = tool.Parameters.Select(p => p.Name).ToList();
            Assert.Contains("connection", names);
            Assert.Contains("sql", names);
            Assert.Contains("table", names);
            Assert.Contains("maxRows", names);

            // Все опциональные.
            Assert.False(tool.Parameters.First(p => p.Name == "sql").Required);
            Assert.False(tool.Parameters.First(p => p.Name == "table").Required);
        }

        // ============ Обработка ошибок ============

        [Fact]
        public async Task ExecuteAsync_MissingAction_ReturnsFail()
        {
            var tool = BuildTool(new FakeSqlAgentService());
            var result = await tool.ExecuteAsync(BuildContext(), new JObject());

            Assert.False(result.Success);
            Assert.Contains("action", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownAction_ReturnsFail()
        {
            var tool = BuildTool(new FakeSqlAgentService());
            var args = new JObject { ["action"] = "no-such-action" };

            var result = await tool.ExecuteAsync(BuildContext(), args);

            Assert.False(result.Success);
            Assert.Contains("no-such-action", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_DescribeTable_MissingTable_ReturnsFail()
        {
            var tool = BuildTool(new FakeSqlAgentService());

            var result = await tool.ExecuteAsync(
                BuildContext(),
                new JObject { ["action"] = "describe_table" });

            Assert.False(result.Success);
            Assert.Contains("table", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_ExecuteQuery_MissingSql_ReturnsFail()
        {
            var tool = BuildTool(new FakeSqlAgentService());

            var result = await tool.ExecuteAsync(
                BuildContext(),
                new JObject { ["action"] = "execute_query" });

            Assert.False(result.Success);
            Assert.Contains("sql", result.Message);
        }

        // ============ Успешные вызовы ============

        [Fact]
        public async Task ExecuteAsync_ListDatabases_CallsService()
        {
            var fake = new FakeSqlAgentService
            {
                ConnectionsResult = new List<DatabaseConnectionInfoDto>
                {
                    new DatabaseConnectionInfoDto
                    {
                        Name = "internal",
                        DisplayName = "Test DB",
                        Provider = "Sqlite",
                        Enabled = true
                    }
                }
            };
            var tool = BuildTool(fake);

            var result = await tool.ExecuteAsync(
                BuildContext(),
                new JObject { ["action"] = "list_databases" });

            Assert.True(result.Success);
            Assert.True(fake.ListConnectionsCalled);
        }

        [Fact]
        public async Task ExecuteAsync_ListTables_PassesConnection()
        {
            var fake = new FakeSqlAgentService
            {
                TablesResult = new List<SqlTableInfoDto>
                {
                    new SqlTableInfoDto { Name = "Chats", RowCount = 5 }
                }
            };
            var tool = BuildTool(fake);

            var result = await tool.ExecuteAsync(
                BuildContext(),
                new JObject { ["action"] = "list_tables", ["connection"] = "internal" });

            Assert.True(result.Success);
            Assert.True(fake.ListTablesCalled);
            Assert.Equal("internal", fake.LastConnection);
        }

        [Fact]
        public async Task ExecuteAsync_DescribeTable_PassesTable()
        {
            var fake = new FakeSqlAgentService
            {
                ColumnsResult = new List<SqlColumnInfoDto>
                {
                    new SqlColumnInfoDto { Name = "Id", Type = "INTEGER", Nullable = false }
                }
            };
            var tool = BuildTool(fake);

            var result = await tool.ExecuteAsync(
                BuildContext(),
                new JObject
                {
                    ["action"] = "describe_table",
                    ["connection"] = "internal",
                    ["table"] = "Chats"
                });

            Assert.True(result.Success);
            Assert.True(fake.DescribeTableCalled);
            Assert.Equal("Chats", fake.LastTable);
        }

        [Fact]
        public async Task ExecuteAsync_ExecuteQuery_PassesSql()
        {
            var fake = new FakeSqlAgentService
            {
                QueryResult = new SqlQueryResultDto
                {
                    Connection = "internal",
                    Columns = new List<string> { "Total" },
                    Rows = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object> { ["Total"] = 42L }
                    },
                    RowCount = 1,
                    Truncated = false,
                    DurationMs = 5
                }
            };
            var tool = BuildTool(fake);

            var result = await tool.ExecuteAsync(
                BuildContext(),
                new JObject
                {
                    ["action"] = "execute_query",
                    ["connection"] = "internal",
                    ["sql"] = "SELECT COUNT(*) AS Total FROM Chats"
                });

            Assert.True(result.Success);
            Assert.True(fake.ExecuteQueryCalled);
            Assert.Equal("SELECT COUNT(*) AS Total FROM Chats", fake.LastSql);
            Assert.Equal("internal", fake.LastConnection);
        }

        [Fact]
        public async Task ExecuteAsync_ExecuteQuery_ServiceThrowsArgumentException_ReturnsFail()
        {
            // Симулируем ошибку валидатора (DELETE-запрос).
            var fake = new FakeSqlAgentService
            {
                ThrowOnExecute = new ArgumentException("SQL отклонён валидатором: Запрещённое ключевое слово: DELETE")
            };
            var tool = BuildTool(fake);

            var result = await tool.ExecuteAsync(
                BuildContext(),
                new JObject { ["action"] = "execute_query", ["sql"] = "DELETE FROM Chats" });

            Assert.False(result.Success);
            Assert.Contains("DELETE", result.Message);
        }

        // ============ Helpers ============

        private static DatabaseAgentTool BuildTool(ISqlAgentService svc)
            => new DatabaseAgentTool(svc, NullLogger<DatabaseAgentTool>.Instance);

        private static ToolExecutionContext BuildContext()
            => new ToolExecutionContext
            {
                UserId = 1,
                WorkspaceRoot = null,
                ClientIp = null,
                CancellationToken = CancellationToken.None
            };

        /// <summary>
        /// Fake <see cref="ISqlAgentService"/> — записывает вызовы, возвращает
        /// настраиваемые результаты. Не держит БД.
        /// </summary>
        private sealed class FakeSqlAgentService : ISqlAgentService
        {
            public bool ListConnectionsCalled { get; private set; }
            public bool ListTablesCalled { get; private set; }
            public bool DescribeTableCalled { get; private set; }
            public bool ExecuteQueryCalled { get; private set; }
            public string LastConnection { get; private set; }
            public string LastTable { get; private set; }
            public string LastSql { get; private set; }

            public IReadOnlyList<DatabaseConnectionInfoDto> ConnectionsResult { get; set; }
                = new List<DatabaseConnectionInfoDto>();
            public IReadOnlyList<SqlTableInfoDto> TablesResult { get; set; }
                = new List<SqlTableInfoDto>();
            public IReadOnlyList<SqlColumnInfoDto> ColumnsResult { get; set; }
                = new List<SqlColumnInfoDto>();
            public SqlQueryResultDto QueryResult { get; set; }
                = new SqlQueryResultDto();

            /// <summary>Если задан — сервис бросает его вместо возврата результата.</summary>
            public Exception ThrowOnExecute { get; set; }

            public Task<IReadOnlyList<DatabaseConnectionInfoDto>> ListConnectionsAsync(
                CancellationToken ct = default)
            {
                ListConnectionsCalled = true;
                return Task.FromResult(ConnectionsResult);
            }

            public Task<IReadOnlyList<SqlTableInfoDto>> ListTablesAsync(
                string connection, CancellationToken ct = default)
            {
                ListTablesCalled = true;
                LastConnection = connection;
                return Task.FromResult(TablesResult);
            }

            public Task<IReadOnlyList<SqlColumnInfoDto>> DescribeTableAsync(
                string connection, string table, CancellationToken ct = default)
            {
                DescribeTableCalled = true;
                LastConnection = connection;
                LastTable = table;
                return Task.FromResult(ColumnsResult);
            }

            public Task<SqlQueryResultDto> ExecuteQueryAsync(
                SqlQueryRequest request, CancellationToken ct = default)
            {
                if (ThrowOnExecute != null) throw ThrowOnExecute;
                ExecuteQueryCalled = true;
                LastSql = request?.Sql;
                LastConnection = request?.Connection;
                return Task.FromResult(QueryResult);
            }
        }
    }
}