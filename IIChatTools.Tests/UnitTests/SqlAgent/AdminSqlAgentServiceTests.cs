using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Implementation.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.SqlAgent
{
    /// <summary>
    /// Тесты <see cref="AdminSqlAgentService"/>
    /// (v1.7.0, KI-097, Фаза 7A).
    /// <para>
    /// Проверяют admin-слой управления подключениями Database Agent:
    /// Get / Update (валидация + persist + runtime) / Test / Reset.
    /// </para>
    /// </summary>
    public class AdminSqlAgentServiceTests
    {
        private const string ConnectionName = "internal";
        private const string SettingKeyPrefix = "SqlAgent.";

        // ============ GetAllConnectionsAsync ============

        [Fact]
        public async Task GetAllConnectionsAsync_ReturnsBaseline()
        {
            using var ctx = TestContext.Create();

            var result = await ctx.Service.GetAllConnectionsAsync();

            Assert.Single(result);
            Assert.Equal("internal", result[0].Name);
            Assert.Equal("Test DB", result[0].DisplayName);
            Assert.Equal("Sqlite", result[0].Provider);
            Assert.True(result[0].Enabled);
            // baseline = нет override'ов.
            Assert.False(result[0].IsOverridden);
        }

        [Fact]
        public async Task GetAllConnectionsAsync_WithOverride_SetsIsOverridden()
        {
            using var ctx = TestContext.Create();

            // Сохраняем override
            await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest { MaxRows = 42 },
                userId: 1);

            var result = await ctx.Service.GetAllConnectionsAsync();

            Assert.Single(result);
            Assert.True(result[0].IsOverridden);
            Assert.Equal(42, result[0].MaxRows);
        }

        // ============ UpdateConnectionAsync: валидация ============

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(10001)]
        public async Task UpdateConnectionAsync_InvalidMaxRows_Throws(int maxRows)
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(
                () => ctx.Service.UpdateConnectionAsync(
                    ConnectionName,
                    new UpdateSqlAgentConnectionRequest { MaxRows = maxRows },
                    userId: 1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(301)]
        public async Task UpdateConnectionAsync_InvalidTimeout_Throws(int timeout)
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(
                () => ctx.Service.UpdateConnectionAsync(
                    ConnectionName,
                    new UpdateSqlAgentConnectionRequest { StatementTimeoutSeconds = timeout },
                    userId: 1));
        }

        [Fact]
        public async Task UpdateConnectionAsync_UnknownConnection_Throws()
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(
                () => ctx.Service.UpdateConnectionAsync(
                    "no-such",
                    new UpdateSqlAgentConnectionRequest { MaxRows = 50 },
                    userId: 1));
        }

        // ============ UpdateConnectionAsync: persist ============

        [Fact]
        public async Task UpdateConnectionAsync_PersistsEnabledToAppSettings()
        {
            using var ctx = TestContext.Create();

            await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest { Enabled = false },
                userId: 1);

            var key = $"{SettingKeyPrefix}{ConnectionName}.enabled";
            var setting = ctx.AppSettings.Items.FirstOrDefault(s => s.Key == key);
            Assert.NotNull(setting);
            Assert.Equal("false", setting.Value);
            Assert.Equal("bool", setting.Type);
        }

        [Fact]
        public async Task UpdateConnectionAsync_PersistsAllowedTablesAsJson()
        {
            using var ctx = TestContext.Create();
            var tables = new[] { "Chats", "AuditLogs" };

            await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest { AllowedTables = tables },
                userId: 1);

            var key = $"{SettingKeyPrefix}{ConnectionName}.allowedTables";
            var setting = ctx.AppSettings.Items.FirstOrDefault(s => s.Key == key);
            Assert.NotNull(setting);
            Assert.Contains("Chats", setting.Value);
            Assert.Contains("AuditLogs", setting.Value);
            Assert.Equal("json", setting.Type);
        }

        [Fact]
        public async Task UpdateConnectionAsync_AppliesRuntimeToOptionsProvider()
        {
            using var ctx = TestContext.Create();

            await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest
                {
                    MaxRows = 77,
                    StatementTimeoutSeconds = 99,
                    Enabled = false
                },
                userId: 1);

            var opts = ctx.OptionsProvider.Get(ConnectionName);
            Assert.Equal(77, opts.MaxRows);
            Assert.Equal(99, opts.StatementTimeoutSeconds);
            Assert.False(opts.Enabled);
        }

        [Fact]
        public async Task UpdateConnectionAsync_WritesAuditLog()
        {
            using var ctx = TestContext.Create();

            await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest { MaxRows = 50 },
                userId: 42);

            ctx.AuditMock.Verify(
                a => a.LogActionAsync(It.Is<AuditLog>(log =>
                    log.UserId == 42
                    && log.ToolName == "admin.sqlagent.connection.update"
                    && log.Status == "Success")),
                Times.Once);
        }

        // ============ TestConnectionAsync ============

        [Fact]
        public async Task TestConnectionAsync_ValidConnection_ReturnsSuccess()
        {
            using var ctx = TestContext.Create();

            var result = await ctx.Service.TestConnectionAsync(ConnectionName);

            Assert.True(result.Success);
            Assert.NotNull(result.Message);
            Assert.True(result.DurationMs >= 0);
        }

        [Fact]
        public async Task TestConnectionAsync_UnknownConnection_ReturnsFailed()
        {
            using var ctx = TestContext.Create();

            var result = await ctx.Service.TestConnectionAsync("no-such");

            // Не бросает исключение — возвращает DTO с Success=false.
            Assert.False(result.Success);
            Assert.Contains("no-such", result.Message);
        }

        [Fact]
        public async Task TestConnectionAsync_DisabledConnection_StillWorks()
        {
            using var ctx = TestContext.Create();

            // Отключаем — тест всё равно должен пройти (ignoreEnabled: true).
            await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest { Enabled = false },
                userId: 1);

            var result = await ctx.Service.TestConnectionAsync(ConnectionName);

            Assert.True(result.Success);
        }

        // ============ ResetConnectionAsync ============

        [Fact]
        public async Task ResetConnectionAsync_RemovesAllOverrideKeys()
        {
            using var ctx = TestContext.Create();

            // Создаём 3 override-ключа
            await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest
                {
                    Enabled = false,
                    MaxRows = 10,
                    StatementTimeoutSeconds = 5
                },
                userId: 1);

            Assert.True(ctx.AppSettings.Items.Count >= 3);

            // Сбрасываем
            await ctx.Service.ResetConnectionAsync(ConnectionName, userId: 1);

            // Все ключи с префиксом SqlAgent.internal. удалены
            var remaining = ctx.AppSettings.Items
                .Where(s => s.Key.StartsWith($"{SettingKeyPrefix}{ConnectionName}.",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Empty(remaining);
        }

        [Fact]
        public async Task ResetConnectionAsync_ReturnsBaseline()
        {
            using var ctx = TestContext.Create();

            // Меняем, потом сбрасываем
            await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest { MaxRows = 42 },
                userId: 1);
            var updated = await ctx.Service.UpdateConnectionAsync(
                ConnectionName,
                new UpdateSqlAgentConnectionRequest { MaxRows = 42 },
                userId: 1);
            Assert.True(updated.IsOverridden);

            var reset = await ctx.Service.ResetConnectionAsync(ConnectionName, userId: 1);

            Assert.False(reset.IsOverridden);
            Assert.Equal(100, reset.MaxRows);  // baseline
        }

        [Fact]
        public async Task ResetConnectionAsync_UnknownConnection_Throws()
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(
                () => ctx.Service.ResetConnectionAsync("no-such", userId: 1));
        }

        // ============ Test infrastructure ============

        private sealed class TestContext : IDisposable
        {
            private readonly SqliteConnection _kernel;

            public FakeAppSettingsService AppSettings { get; }
            public SqlAgentOptionsProvider OptionsProvider { get; }
            public Mock<IAuditService> AuditMock { get; }
            public AdminSqlAgentService Service { get; }

            private TestContext(
                SqliteConnection kernel,
                FakeAppSettingsService settings,
                SqlAgentOptionsProvider optionsProvider,
                Mock<IAuditService> auditMock,
                AdminSqlAgentService service)
            {
                _kernel = kernel;
                AppSettings = settings;
                OptionsProvider = optionsProvider;
                AuditMock = auditMock;
                Service = service;
            }

            public static TestContext Create()
            {
                var kernel = new SqliteConnection("Data Source=:memory:");
                kernel.Open();

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
                    QueryValidation = new SqlQueryValidationOptions()
                };

                var optionsProvider = new SqlAgentOptionsProvider(
                    Options.Create(baseline),
                    NullLogger<SqlAgentOptionsProvider>.Instance);

                var settings = new FakeAppSettingsService();

                var connProvider = new FakeConnectionProvider(
                    new NonClosingSqliteConnection(kernel), "internal", "Sqlite");

                var auditMock = new Mock<IAuditService>();
                auditMock
                    .Setup(a => a.LogActionAsync(It.IsAny<AuditLog>()))
                    .Returns(Task.CompletedTask);

                var service = new AdminSqlAgentService(
                    optionsProvider,
                    connProvider,
                    settings,
                    auditMock.Object,
                    NullLogger<AdminSqlAgentService>.Instance);

                return new TestContext(kernel, settings, optionsProvider, auditMock, service);
            }

            public void Dispose() => _kernel.Dispose();
        }

        /// <summary>
        /// In-memory fake <see cref="IAppSettingsService"/> — минимальный CRUD.
        /// </summary>
        private sealed class FakeAppSettingsService : IAppSettingsService
        {
            public List<SettingDto> Items { get; } = new List<SettingDto>();
            private int _nextId = 1;

            public Task<IReadOnlyList<SettingDto>> GetAllAsync()
                => Task.FromResult<IReadOnlyList<SettingDto>>(Items.ToList());

            public Task<SettingDto> GetByIdAsync(int id)
                => Task.FromResult(Items.FirstOrDefault(s => s.Id == id));

            public Task<SettingDto> CreateAsync(SettingDto dto)
            {
                dto.Id = _nextId++;
                Items.Add(dto);
                return Task.FromResult(dto);
            }

            public Task<SettingDto> UpdateAsync(int id, SettingDto dto)
            {
                var existing = Items.FirstOrDefault(s => s.Id == id);
                if (existing == null) return Task.FromResult<SettingDto>(null);

                existing.Value = dto.Value;
                existing.Type = dto.Type;
                existing.Category = dto.Category;
                return Task.FromResult(existing);
            }

            public Task<bool> DeleteAsync(int id)
            {
                var existing = Items.FirstOrDefault(s => s.Id == id);
                if (existing == null) return Task.FromResult(false);
                Items.Remove(existing);
                return Task.FromResult(true);
            }

            public Task<SettingDto> ResetToDefaultAsync(int id)
                => throw new NotImplementedException();

            public Task<int> SyncDefaultsFromConfigurationAsync()
                => throw new NotImplementedException();
        }

        /// <summary>
        /// Fake-провайдер соединений: всегда один и тот же DbConnection.
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
                string connectionName,
                bool ignoreEnabled = false,
                CancellationToken ct = default)
            {
                if (!Exists(connectionName))
                    throw new ArgumentException($"Подключение '{connectionName}' не зарегистрировано.");
                return Task.FromResult(_connection);
            }
        }

        /// <summary>
        /// Обёртка над <see cref="SqliteConnection"/>, игнорирующая Dispose —
        /// чтобы сервис не закрывал kernel-connection in-memory БД.
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

            protected override void Dispose(bool disposing) { /* no-op */ }
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}