using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.Implementation.Orchestrator;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Orchestrator
{
    /// <summary>
    /// Тесты <see cref="OrchestratorOverridesLoader"/>
    /// (v2.2.2, KI-245, verification-fix).
    /// <para>
    /// Покрывают стартовое восстановление override'ов из <c>AppSettings</c>:
    /// валидные ключи, частичный набор, мусор в БД (нечисловые значения,
    /// битый JSON, неизвестные поля, значения вне диапазона) и аргументы.
    /// </para>
    /// </summary>
    public class OrchestratorOverridesLoaderTests
    {
        // ============ Пустой / частичный набор ============

        [Fact]
        public async Task ApplyAsync_NoSettings_ReturnsZero()
        {
            var ctx = TestContext.Create();

            var applied = await ctx.Loader.ApplyAsync();

            Assert.Equal(0, applied);
            Assert.False(ctx.Provider.IsOverridden);
            Assert.Equal(5, ctx.Provider.Get().MaxSubtasks);
        }

        [Fact]
        public async Task ApplyAsync_NoOrchestratorKeys_ReturnsZero()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("SubAgents.web_agent", "{}");

            var applied = await ctx.Loader.ApplyAsync();

            Assert.Equal(0, applied);
            Assert.False(ctx.Provider.IsOverridden);
        }

        [Fact]
        public async Task ApplyAsync_AllFiveKeys_AppliesOverride()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("Orchestrator.MaxSubtasks", "8");
            ctx.Settings.Add("Orchestrator.MaxParallel", "2");
            ctx.Settings.Add("Orchestrator.TokenBudget", "123456");
            ctx.Settings.Add("Orchestrator.HumanApproval", "atend");
            ctx.Settings.Add("Orchestrator.AllowedWorkerAgents",
                "[\"web_agent\",\"code_agent\"]");

            var applied = await ctx.Loader.ApplyAsync();

            Assert.Equal(5, applied);
            var settings = ctx.Provider.Get();
            Assert.True(settings.IsOverridden);
            Assert.Equal(8, settings.MaxSubtasks);
            Assert.Equal(2, settings.MaxParallel);
            Assert.Equal(123_456, settings.TokenBudget);
            Assert.Equal("AtEnd", settings.HumanApproval);
            Assert.Equal(new[] { "web_agent", "code_agent" },
                settings.AllowedWorkerAgents);
        }

        [Fact]
        public async Task ApplyAsync_PartialKeys_MergesWithBaseline()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("Orchestrator.MaxSubtasks", "7");

            var applied = await ctx.Loader.ApplyAsync();

            Assert.Equal(1, applied);
            var settings = ctx.Provider.Get();
            Assert.Equal(7, settings.MaxSubtasks);
            Assert.Equal(3, settings.MaxParallel);      // baseline
            Assert.Equal(50_000, settings.TokenBudget); // baseline
            Assert.Equal("Never", settings.HumanApproval);
            Assert.Equal(6, settings.AllowedWorkerAgents.Count);
            Assert.True(settings.IsOverridden);
        }

        // ============ Мусор в БД ============

        [Fact]
        public async Task ApplyAsync_UnparsableNumber_IsSkipped()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("Orchestrator.MaxSubtasks", "not-a-number");

            var applied = await ctx.Loader.ApplyAsync();

            Assert.Equal(0, applied);
            Assert.False(ctx.Provider.IsOverridden);
            Assert.Equal(5, ctx.Provider.Get().MaxSubtasks);
        }

        [Fact]
        public async Task ApplyAsync_OneValidOneCorrupt_AppliesOnlyValid()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("Orchestrator.MaxSubtasks", "9");
            ctx.Settings.Add("Orchestrator.MaxParallel", "oops");

            var applied = await ctx.Loader.ApplyAsync();

            Assert.Equal(1, applied);
            var settings = ctx.Provider.Get();
            Assert.Equal(9, settings.MaxSubtasks);
            Assert.Equal(3, settings.MaxParallel);
        }

        [Fact]
        public async Task ApplyAsync_CorruptWorkersJson_IsSkipped()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("Orchestrator.AllowedWorkerAgents", "{not-json");
            ctx.Settings.Add("Orchestrator.TokenBudget", "60000");

            var applied = await ctx.Loader.ApplyAsync();

            Assert.Equal(1, applied);
            var settings = ctx.Provider.Get();
            Assert.Equal(60_000, settings.TokenBudget);
            Assert.Equal(6, settings.AllowedWorkerAgents.Count);
        }

        [Fact]
        public async Task ApplyAsync_UnknownField_IsSkipped()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("Orchestrator.Unknown", "42");
            ctx.Settings.Add("Orchestrator.MaxSubtasks", "6");

            var applied = await ctx.Loader.ApplyAsync();

            Assert.Equal(1, applied);
            Assert.Equal(6, ctx.Provider.Get().MaxSubtasks);
        }

        [Fact]
        public async Task ApplyAsync_OutOfRangeValue_ThrowsAndKeepsBaseline()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("Orchestrator.MaxSubtasks", "999");

            await Assert.ThrowsAsync<ArgumentException>(() => ctx.Loader.ApplyAsync());

            Assert.False(ctx.Provider.IsOverridden);
            Assert.Equal(5, ctx.Provider.Get().MaxSubtasks);
        }

        [Fact]
        public async Task ApplyAsync_InvalidHumanApproval_ThrowsAndKeepsBaseline()
        {
            var ctx = TestContext.Create();
            ctx.Settings.Add("Orchestrator.HumanApproval", "Sometimes");

            await Assert.ThrowsAsync<ArgumentException>(() => ctx.Loader.ApplyAsync());

            Assert.False(ctx.Provider.IsOverridden);
        }

        // ============ Аргументы ============

        [Fact]
        public async Task ApplyAsync_NullService_Throws()
        {
            var provider = CreateProvider();
            var loader = new LoaderFacade(
                null, provider,
                NullLogger.Instance);

            await Assert.ThrowsAsync<ArgumentNullException>(() => loader.ApplyAsync());
        }

        // ============ Infrastructure ============

        private static OrchestratorOptionsProvider CreateProvider()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>())
                .Build();

            return new OrchestratorOptionsProvider(
                configuration,
                NullLogger<OrchestratorOptionsProvider>.Instance);
        }

        /// <summary>
        /// Обёртка над статическим <see cref="OrchestratorOverridesLoader"/>,
        /// чтобы тесты единообразно работали с контекстом.
        /// </summary>
        private sealed class LoaderFacade
        {
            private readonly IAppSettingsService _settings;
            private readonly IOrchestratorOptionsProvider _provider;
            private readonly Microsoft.Extensions.Logging.ILogger _logger;

            public LoaderFacade(
                IAppSettingsService settings,
                IOrchestratorOptionsProvider provider,
                Microsoft.Extensions.Logging.ILogger logger)
            {
                _settings = settings;
                _provider = provider;
                _logger = logger;
            }

            public Task<int> ApplyAsync()
                => OrchestratorOverridesLoader.ApplyAsync(
                    _settings, _provider, _logger);
        }

        private sealed class TestContext
        {
            public FakeAppSettingsService Settings { get; }
            public OrchestratorOptionsProvider Provider { get; }
            public LoaderFacade Loader { get; }

            private TestContext(
                FakeAppSettingsService settings,
                OrchestratorOptionsProvider provider)
            {
                Settings = settings;
                Provider = provider;
                Loader = new LoaderFacade(
                    settings, provider,
                    NullLogger.Instance);
            }

            public static TestContext Create()
            {
                return new TestContext(
                    new FakeAppSettingsService(),
                    CreateProvider());
            }
        }

        private sealed class FakeAppSettingsService : IAppSettingsService
        {
            public List<SettingDto> Items { get; } = new List<SettingDto>();
            private int _nextId = 1;

            public void Add(string key, string value)
            {
                Items.Add(new SettingDto
                {
                    Id = _nextId++,
                    Key = key,
                    Value = value,
                    Type = "string"
                });
            }

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
                => throw new NotImplementedException();

            public Task<bool> DeleteAsync(int id)
                => throw new NotImplementedException();

            public Task<SettingDto> ResetToDefaultAsync(int id)
                => throw new NotImplementedException();

            public Task<int> SyncDefaultsFromConfigurationAsync()
                => throw new NotImplementedException();
        }
    }
}
