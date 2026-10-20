using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.DTO.Orchestrator;
using IIChatTools.Services.Implementation.Orchestrator;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Orchestrator
{
    /// <summary>
    /// Тесты <see cref="AdminOrchestratorService"/>
    /// (v2.2.2, KI-245).
    /// <para>
    /// Покрывают: чтение baseline, валидацию (диапазоны, whitelist, approval),
    /// persist в <c>AppSettings</c> (ключи <c>Orchestrator.*</c>), применение
    /// к провайдеру, audit, сброс к baseline.
    /// </para>
    /// </summary>
    public class AdminOrchestratorServiceTests
    {
        // ============ Чтение ============

        [Fact]
        public async Task GetSettingsAsync_ReturnsBaseline()
        {
            using var ctx = TestContext.Create();

            var settings = await ctx.Service.GetSettingsAsync();

            Assert.Equal(5, settings.MaxSubtasks);
            Assert.Equal(3, settings.MaxParallel);
            Assert.Equal(50_000, settings.TokenBudget);
            Assert.Equal("Never", settings.HumanApproval);
            Assert.Equal(6, settings.AllowedWorkerAgents.Count);
            Assert.False(settings.IsOverridden);
        }

        // ============ Обновление: persist ============

        [Fact]
        public async Task UpdateSettingsAsync_PersistsAllFiveKeys()
        {
            using var ctx = TestContext.Create();

            await ctx.Service.UpdateSettingsAsync(BuildContext(), userId: 1);

            Assert.Equal(5, ctx.AppSettings.Items.Count);

            var keyed = ctx.AppSettings.Items.ToDictionary(s => s.Key, StringComparer.Ordinal);
            Assert.Equal("8", keyed["Orchestrator.MaxSubtasks"].Value);
            Assert.Equal("4", keyed["Orchestrator.MaxParallel"].Value);
            Assert.Equal("100000", keyed["Orchestrator.TokenBudget"].Value);
            Assert.Equal("BetweenSubtasks", keyed["Orchestrator.HumanApproval"].Value);

            foreach (var setting in ctx.AppSettings.Items)
            {
                Assert.Equal("Orchestrator", setting.Category);
            }

            Assert.Equal("int", keyed["Orchestrator.MaxSubtasks"].Type);
            Assert.Equal("string", keyed["Orchestrator.HumanApproval"].Type);
            Assert.Equal("json", keyed["Orchestrator.AllowedWorkerAgents"].Type);
        }

        [Fact]
        public async Task UpdateSettingsAsync_PersistsWorkersAsJsonArray()
        {
            using var ctx = TestContext.Create();

            await ctx.Service.UpdateSettingsAsync(BuildContext(), userId: 1);

            var raw = ctx.AppSettings.Items
                .Single(s => s.Key == "Orchestrator.AllowedWorkerAgents").Value;
            var agents = JsonConvert.DeserializeObject<List<string>>(raw);

            Assert.Equal(new[] { "web_agent", "code_agent" }, agents);
        }

        [Fact]
        public async Task UpdateSettingsAsync_UpdatesExistingKeysWithoutDuplicates()
        {
            using var ctx = TestContext.Create();

            await ctx.Service.UpdateSettingsAsync(BuildContext(maxSubtasks: 6), userId: 1);
            await ctx.Service.UpdateSettingsAsync(BuildContext(maxSubtasks: 7), userId: 1);

            Assert.Equal(5, ctx.AppSettings.Items.Count);
            Assert.Equal("7", ctx.AppSettings.Items
                .Single(s => s.Key == "Orchestrator.MaxSubtasks").Value);
        }

        [Fact]
        public async Task UpdateSettingsAsync_NormalizesInput()
        {
            using var ctx = TestContext.Create();

            var request = new OrchestratorSettingsDto
            {
                MaxSubtasks = 5,
                MaxParallel = 3,
                TokenBudget = 50_000,
                HumanApproval = "betweensubtasks",
                AllowedWorkerAgents = new List<string>
                {
                    " web_agent ", "WEB_AGENT", "", "code_agent"
                }
            };

            await ctx.Service.UpdateSettingsAsync(request, userId: 1);

            var keyed = ctx.AppSettings.Items.ToDictionary(s => s.Key, StringComparer.Ordinal);
            Assert.Equal("BetweenSubtasks", keyed["Orchestrator.HumanApproval"].Value);

            var agents = JsonConvert.DeserializeObject<List<string>>(
                keyed["Orchestrator.AllowedWorkerAgents"].Value);
            Assert.Equal(new[] { "web_agent", "code_agent" }, agents);
        }

        [Fact]
        public async Task UpdateSettingsAsync_AppliesToProviderImmediately()
        {
            using var ctx = TestContext.Create();

            var result = await ctx.Service.UpdateSettingsAsync(
                BuildContext(), userId: 42);

            Assert.True(result.IsOverridden);
            Assert.True(ctx.Provider.IsOverridden);

            var current = ctx.Provider.Get();
            Assert.Equal(8, current.MaxSubtasks);
            Assert.Equal(new[] { "web_agent", "code_agent" },
                current.AllowedWorkerAgents);
        }

        [Fact]
        public async Task UpdateSettingsAsync_WritesAudit()
        {
            using var ctx = TestContext.Create();

            await ctx.Service.UpdateSettingsAsync(BuildContext(), userId: 42);

            ctx.AuditMock.Verify(a => a.LogActionAsync(
                It.Is<AuditLog>(l =>
                    l.UserId == 42
                    && l.ToolName == "admin.orchestrator.settings.update"
                    && l.Status == "Success")), Times.Once);
        }

        // ============ Обновление: валидация ============

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(11)]
        public async Task UpdateSettingsAsync_MaxSubtasksOutOfRange_Throws(int value)
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                ctx.Service.UpdateSettingsAsync(BuildContext(maxSubtasks: value), userId: 1));

            await AssertNoSideEffectsAsync(ctx);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(11)]
        public async Task UpdateSettingsAsync_MaxParallelOutOfRange_Throws(int value)
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                ctx.Service.UpdateSettingsAsync(BuildContext(maxParallel: value), userId: 1));

            await AssertNoSideEffectsAsync(ctx);
        }

        [Theory]
        [InlineData(999)]
        [InlineData(1_000_001)]
        public async Task UpdateSettingsAsync_TokenBudgetOutOfRange_Throws(int value)
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                ctx.Service.UpdateSettingsAsync(BuildContext(tokenBudget: value), userId: 1));

            await AssertNoSideEffectsAsync(ctx);
        }

        [Fact]
        public async Task UpdateSettingsAsync_EmptyWorkers_Throws()
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                ctx.Service.UpdateSettingsAsync(
                    BuildContext(workers: new List<string>()), userId: 1));

            await AssertNoSideEffectsAsync(ctx);
        }

        [Fact]
        public async Task UpdateSettingsAsync_WhitespaceWorkers_Throws()
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                ctx.Service.UpdateSettingsAsync(
                    BuildContext(workers: new List<string> { "  ", "\t" }), userId: 1));

            await AssertNoSideEffectsAsync(ctx);
        }

        [Theory]
        [InlineData("Always")]
        [InlineData("")]
        public async Task UpdateSettingsAsync_InvalidHumanApproval_Throws(string value)
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                ctx.Service.UpdateSettingsAsync(
                    BuildContext(humanApproval: value), userId: 1));

            await AssertNoSideEffectsAsync(ctx);
        }

        [Fact]
        public async Task UpdateSettingsAsync_NullHumanApproval_Throws()
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                ctx.Service.UpdateSettingsAsync(
                    BuildContext(humanApproval: null), userId: 1));

            await AssertNoSideEffectsAsync(ctx);
        }

        [Fact]
        public async Task UpdateSettingsAsync_NullSettings_Throws()
        {
            using var ctx = TestContext.Create();

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                ctx.Service.UpdateSettingsAsync(null, userId: 1));
        }

        // ============ Сброс ============

        [Fact]
        public async Task ResetAsync_DeletesAllOrchestratorKeys()
        {
            using var ctx = TestContext.Create();
            await ctx.Service.UpdateSettingsAsync(BuildContext(), userId: 1);

            // Посторонний ключ не должен пострадать.
            ctx.AppSettings.Items.Add(new SettingDto
            {
                Key = "SubAgents.web_agent",
                Value = "{}",
                Category = "Agents"
            });

            var result = await ctx.Service.ResetAsync(userId: 1);

            Assert.False(result.IsOverridden);
            Assert.DoesNotContain(ctx.AppSettings.Items,
                s => s.Key.StartsWith("Orchestrator.", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(ctx.AppSettings.Items,
                s => s.Key == "SubAgents.web_agent");
        }

        [Fact]
        public async Task ResetAsync_RestoresProviderBaseline()
        {
            using var ctx = TestContext.Create();
            await ctx.Service.UpdateSettingsAsync(BuildContext(), userId: 1);
            Assert.True(ctx.Provider.IsOverridden);

            var result = await ctx.Service.ResetAsync(userId: 1);

            Assert.False(ctx.Provider.IsOverridden);
            Assert.False(result.IsOverridden);
            Assert.Equal(5, result.MaxSubtasks);
            Assert.Equal(3, result.MaxParallel);
            Assert.Equal(50_000, result.TokenBudget);
            Assert.Equal("Never", result.HumanApproval);
            Assert.Equal(6, result.AllowedWorkerAgents.Count);
        }

        [Fact]
        public async Task ResetAsync_WritesAudit()
        {
            using var ctx = TestContext.Create();

            await ctx.Service.ResetAsync(userId: 9);

            ctx.AuditMock.Verify(a => a.LogActionAsync(
                It.Is<AuditLog>(l =>
                    l.UserId == 9
                    && l.ToolName == "admin.orchestrator.settings.reset")), Times.Once);
        }

        [Fact]
        public async Task ResetAsync_WithoutOverrides_DoesNotThrow()
        {
            using var ctx = TestContext.Create();

            var result = await ctx.Service.ResetAsync(userId: 1);

            Assert.False(result.IsOverridden);
            Assert.Empty(ctx.AppSettings.Items);
        }

        // ============ Helpers ============

        private static OrchestratorSettingsDto BuildContext(
            int maxSubtasks = 8,
            int maxParallel = 4,
            int tokenBudget = 100_000,
            string humanApproval = "BetweenSubtasks",
            List<string> workers = null)
        {
            return new OrchestratorSettingsDto
            {
                MaxSubtasks = maxSubtasks,
                MaxParallel = maxParallel,
                TokenBudget = tokenBudget,
                HumanApproval = humanApproval,
                AllowedWorkerAgents = workers
                    ?? new List<string> { "web_agent", "code_agent" }
            };
        }

        private static async Task AssertNoSideEffectsAsync(TestContext ctx)
        {
            Assert.Empty(ctx.AppSettings.Items);
            Assert.False(ctx.Provider.IsOverridden);
            Assert.Equal(5, ctx.Provider.Get().MaxSubtasks);

            ctx.AuditMock.Verify(a => a.LogActionAsync(It.IsAny<AuditLog>()),
                Times.Never);

            await Task.CompletedTask;
        }

        // ============ Test infrastructure ============

        private sealed class TestContext : IDisposable
        {
            public FakeAppSettingsService AppSettings { get; }
            public OrchestratorOptionsProvider Provider { get; }
            public Mock<IAuditService> AuditMock { get; }
            public AdminOrchestratorService Service { get; }

            private TestContext(
                FakeAppSettingsService settings,
                OrchestratorOptionsProvider provider,
                Mock<IAuditService> auditMock,
                AdminOrchestratorService service)
            {
                AppSettings = settings;
                Provider = provider;
                AuditMock = auditMock;
                Service = service;
            }

            public static TestContext Create()
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string>())
                    .Build();

                var provider = new OrchestratorOptionsProvider(
                    configuration,
                    NullLogger<OrchestratorOptionsProvider>.Instance);

                var settings = new FakeAppSettingsService();

                var auditMock = new Mock<IAuditService>();
                auditMock
                    .Setup(a => a.LogActionAsync(It.IsAny<AuditLog>()))
                    .Returns(Task.CompletedTask);

                var service = new AdminOrchestratorService(
                    provider,
                    settings,
                    auditMock.Object,
                    NullLogger<AdminOrchestratorService>.Instance);

                return new TestContext(settings, provider, auditMock, service);
            }

            public void Dispose()
            {
                // Ресурсов нет — контекст нужен только для единообразия.
            }
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

                existing.Key = dto.Key;
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
    }
}
