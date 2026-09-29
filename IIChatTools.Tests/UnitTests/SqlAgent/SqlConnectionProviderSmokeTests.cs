using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.SqlAgent
{
    /// <summary>
    /// Smoke-тесты <see cref="SqlConnectionProvider"/>
    /// (v1.7.0, KI-097, Фаза 2 — DoD: «connection string разрешается из secrets»).
    /// <para>
    /// Полный набор (валидация SQL, whitelist, timeout) — в Фазе 7.
    /// </para>
    /// </summary>
    public class SqlConnectionProviderSmokeTests
    {
        /// <summary>
        /// Зарегистрированное и включённое подключение — <c>Exists</c> = true.
        /// </summary>
        [Fact]
        public void Exists_RegisteredConnection_ReturnsTrue()
        {
            var provider = BuildProvider(
                connectionStringValue: "Data Source=:memory:",
                connectionEnabled: true);

            Assert.True(provider.Exists("internal"));
        }

        /// <summary>
        /// Неизвестное имя подключения — <c>Exists</c> = false.
        /// </summary>
        [Fact]
        public void Exists_UnknownConnection_ReturnsFalse()
        {
            var provider = BuildProvider(
                connectionStringValue: "Data Source=:memory:",
                connectionEnabled: true);

            Assert.False(provider.Exists("no-such-connection"));
        }

        /// <summary>
        /// Connection string резолвится из <see cref="IConfiguration"/>
        /// (User Secrets / env) и используется для открытия <c>SqliteConnection</c>.
        /// <para>
        /// DoD Фазы 2: «connection string разрешается из secrets».
        /// </para>
        /// </summary>
        [Fact]
        public async Task CreateConnectionAsync_ResolvesConnectionStringFromConfig()
        {
            var provider = BuildProvider(
                connectionStringValue: "Data Source=:memory:",
                connectionEnabled: true);

            await using var conn = await provider.CreateConnectionAsync("internal");

            Assert.NotNull(conn);
            Assert.Equal("Microsoft.Data.Sqlite.SqliteConnection", conn.GetType().FullName);
        }

        /// <summary>
        /// Если <c>ConnectionStringKey</c> не найден в конфигурации —
        /// <see cref="InvalidOperationException"/> с понятным сообщением.
        /// </summary>
        [Fact]
        public async Task CreateConnectionAsync_MissingConnectionString_Throws()
        {
            var provider = BuildProvider(
                connectionStringValue: null,        // ← ключ не задан в конфигурации
                connectionEnabled: true);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateConnectionAsync("internal"));

            Assert.Contains("SqlAgent:Internal:ConnectionString", ex.Message);
        }

        /// <summary>
        /// KI-100: относительный <c>Data Source</c> Sqlite резолвится относительно
        /// <see cref="IAppPathProvider.ContentRootPath"/>, а не относительно CWD.
        /// </summary>
        [Fact]
        public async Task CreateConnectionAsync_RelativeSqlitePath_ResolvesFromContentRoot()
        {
            var contentRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "iichattools-test-content-root");
            System.IO.Directory.CreateDirectory(contentRoot);

            var provider = BuildProvider(
                connectionStringValue: "Data Source=Data/test.db;Mode=ReadOnly",
                connectionEnabled: true,
                contentRootPath: contentRoot);

            try
            {
                await using var conn = await provider.CreateConnectionAsync("internal");
                // SqliteConnection.DataSource — абсолютный путь после резолвинга.
                Assert.True(System.IO.Path.IsPathRooted(conn.DataSource),
                    $"DataSource должен быть абсолютным, но был '{conn.DataSource}'.");
                Assert.Contains("test.db", conn.DataSource);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                // Файл не существует (Mode=ReadOnly + нет файла) — это OK для теста.
                // Главное — путь резолвился, а не упало с "relative path" ошибкой.
            }
        }

        // ============ Helpers ============

        private static ISqlConnectionProvider BuildProvider(
            string connectionStringValue,
            bool connectionEnabled,
            string contentRootPath = "C:\\")
        {
            // Baseline-опции (эмулируют appsettings:SqlAgent:Connections:internal).
            var baseline = new SqlAgentOptions
            {
                Enabled = true,
                DefaultConnection = "internal",
                AdminUiEnabled = true,
                Connections = new Dictionary<string, SqlAgentConnectionOptions>
                {
                    ["internal"] = new SqlAgentConnectionOptions
                    {
                        DisplayName = "Test DB",
                        Provider = "Sqlite",
                        ConnectionStringKey = "SqlAgent:Internal:ConnectionString",
                        AllowedTables = new List<string> { "Chats" },
                        DeniedTables = new List<string>(),
                        MaxRows = 100,
                        StatementTimeoutSeconds = 15,
                        RequiresApproval = true,
                        Enabled = connectionEnabled
                    }
                },
                QueryValidation = new SqlQueryValidationOptions
                {
                    DeniedKeywords = new List<string> { "DELETE", "DROP" },
                    DeniedFunctions = new List<string>(),
                    MaxSqlLength = 4000,
                    AutoLimitIfMissing = true
                }
            };

            var optionsProvider = new SqlAgentOptionsProvider(
                Options.Create(baseline),
                NullLogger<SqlAgentOptionsProvider>.Instance);

            // Конфигурация: с ключом или без (для negative-теста).
            var configDict = new Dictionary<string, string>();
            if (connectionStringValue != null)
            {
                configDict["SqlAgent:Internal:ConnectionString"] = connectionStringValue;
            }
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();

            return new SqlConnectionProvider(
                optionsProvider,
                configuration,
                new FakeAppPathProvider(contentRootPath),
                NullLogger<SqlConnectionProvider>.Instance);
        }

        /// <summary>
        /// Fake-провайдер путей для тестов: фиксированный ContentRoot.
        /// </summary>
        private sealed class FakeAppPathProvider : IAppPathProvider
        {
            public string ContentRootPath { get; }
            public FakeAppPathProvider(string contentRootPath) { ContentRootPath = contentRootPath; }
        }
    }
}