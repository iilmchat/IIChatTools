using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.SqlAgent; // SqlQueryResultDto
using IIChatTools.Services.Implementation.Tools.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Интеграционные тесты Database Agent
    /// (v1.7.0, KI-097, Фаза 7C, DESIGN_DB_AGENT § 7.7).
    /// <para>
    /// Полный сквозной путь:
    /// <c>DatabaseAgentTool</c> → <c>ISqlAgentService</c> →
    /// <c>ISqlQueryValidator</c> → <c>ISqlConnectionProvider</c> →
    /// реальная Sqlite-БД (временный файл).
    /// </para>
    /// <para>
    /// Проверяет, что вся DI-цепочка правильно склеена и работает end-to-end
    /// (в отличие от unit-тестов, где компоненты изолированы).
    /// </para>
    /// </summary>
    public class SqlAgentIntegrationTests
    {
        // ============ Тесты ============

        [Fact]
        public async Task ExecuteQuery_ValidSelect_ReturnsRowCount()
        {
            var dbPath = CreateTempDbPath();

            try
            {
                SeedDatabase(dbPath, @"
                    CREATE TABLE Chats (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL);
                    INSERT INTO Chats (Title) VALUES ('Alpha'), ('Beta'), ('Gamma');
                ");

                using var sp = BuildServiceProvider($"Data Source={dbPath}");
                using var scope = sp.CreateScope();
                var tool = scope.ServiceProvider.GetRequiredService<ITool>();

                var args = new JObject
                {
                    ["action"] = "execute_query",
                    ["connection"] = "internal",
                    ["sql"] = "SELECT COUNT(*) AS Total FROM Chats"
                };
                var context = BuildContext();

                var result = await tool.ExecuteAsync(context, args);

                Assert.True(result.Success,
                    $"Ожидался успех. Message={result.Message}");

                // Проверяем не Message («Возвращено 1 строк» — это 1 строка с
                // COUNT(*)), а именно значение COUNT(*) = 3 в Data.Rows[0]["Total"].
                var dto = Assert.IsType<SqlQueryResultDto>(result.Data);
                Assert.Single(dto.Rows);
                Assert.Equal(3L, Convert.ToInt64(dto.Rows[0]["Total"]));
            }
            finally
            {
                TryDeleteFile(dbPath);
            }
        }

        [Fact]
        public async Task ExecuteQuery_DeniedTable_ViaTool_ReturnsFail()
        {
            var dbPath = CreateTempDbPath();

            try
            {
                SeedDatabase(dbPath, "CREATE TABLE Some (Id INTEGER);");

                using var sp = BuildServiceProvider($"Data Source={dbPath}");
                using var scope = sp.CreateScope();
                var tool = scope.ServiceProvider.GetRequiredService<ITool>();

                var args = new JObject
                {
                    ["action"] = "execute_query",
                    ["connection"] = "internal",
                    ["sql"] = "SELECT * FROM AspNetUsers"
                };
                var context = BuildContext();

                var result = await tool.ExecuteAsync(context, args);

                Assert.False(result.Success);
                Assert.Contains("AspNetUsers", result.Message);
            }
            finally
            {
                TryDeleteFile(dbPath);
            }
        }

        [Fact]
        public async Task ListDatabases_ViaTool_ReturnsInternalConnection()
        {
            var dbPath = CreateTempDbPath();

            try
            {
                SeedDatabase(dbPath, "CREATE TABLE Chats (Id INTEGER);");

                using var sp = BuildServiceProvider($"Data Source={dbPath}");
                using var scope = sp.CreateScope();
                var tool = scope.ServiceProvider.GetRequiredService<ITool>();

                var args = new JObject { ["action"] = "list_databases" };
                var context = BuildContext();

                var result = await tool.ExecuteAsync(context, args);

                Assert.True(result.Success);
                Assert.Contains("1", result.Message);  // "Зарегистрировано подключений: 1"
            }
            finally
            {
                TryDeleteFile(dbPath);
            }
        }

        // ============ Инфраструктура ============

        private static ServiceProvider BuildServiceProvider(string sqliteConnectionString)
        {
            var services = new ServiceCollection();

            // 1. Logging (нужен для ILogger<T>)
            services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

            // 2. Baseline SqlAgentOptions
            var baseline = new SqlAgentOptions
            {
                Enabled = true,
                DefaultConnection = "internal",
                Connections = new Dictionary<string, SqlAgentConnectionOptions>
                {
                    ["internal"] = new SqlAgentConnectionOptions
                    {
                        DisplayName = "Integration Test DB",
                        Provider = "Sqlite",
                        ConnectionStringKey = "SqlAgent:Internal:ConnectionString",
                        AllowedTables = new List<string> { "Chats", "AuditLogs" },
                        DeniedTables = new List<string> { "AspNetUsers", "AspNetRoles" },
                        MaxRows = 100,
                        StatementTimeoutSeconds = 15,
                        RequiresApproval = true,
                        Enabled = true
                    }
                },
                QueryValidation = new SqlQueryValidationOptions
                {
                    DeniedKeywords = new List<string>
                    {
                        "INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "ALTER", "CREATE",
                        "REPLACE", "MERGE", "GRANT", "REVOKE", "EXEC", "EXECUTE",
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
            services.AddSingleton(Options.Create(baseline));

            // 3. IConfiguration — резолвит connection string
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["SqlAgent:Internal:ConnectionString"] = sqliteConnectionString
                })
                .Build();
            services.AddSingleton<IConfiguration>(config);

            // 4. IAppPathProvider — для резолвинга относительных Sqlite-путей (KI-100).
            // В тесте путь абсолютный → фактически не используется.
            services.AddSingleton<IAppPathProvider>(new AppPathProvider(Path.GetTempPath()));

            // 5. SqlAgent-компоненты (порядок регистрации — как в Startup.cs)
            services.AddSingleton<SqlAgentOptionsProvider>();
            services.AddSingleton<ISqlConnectionProvider, SqlConnectionProvider>();
            services.AddSingleton<ISqlQueryValidator, SqlQueryValidator>();
            services.AddScoped<ISqlAgentService, SqlAgentService>();
            services.AddScoped<ITool, DatabaseAgentTool>();

            return services.BuildServiceProvider();
        }

        private static ToolExecutionContext BuildContext()
            => new ToolExecutionContext
            {
                UserId = 1,
                WorkspaceRoot = Path.GetTempPath(),
                ClientIp = null,
                CancellationToken = CancellationToken.None
            };

        private static string CreateTempDbPath()
            => Path.Combine(Path.GetTempPath(), $"sqlagent-int-{Guid.NewGuid():N}.db");

        private static void SeedDatabase(string dbPath, string sql)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static void TryDeleteFile(string dbPath)
        {
            try
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
            catch
            {
                // best-effort, не ломаем тест
            }
        }
    }
}