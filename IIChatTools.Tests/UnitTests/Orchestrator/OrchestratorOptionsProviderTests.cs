using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Orchestrator;
using IIChatTools.Services.Implementation.Orchestrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Orchestrator
{
    /// <summary>
    /// Тесты <see cref="OrchestratorOptionsProvider"/>
    /// (v2.2.2, KI-245).
    /// <para>
    /// Покрывают: baseline из конфигурации (indexer + TryParse, RULES § 4.56),
    /// defaults при отсутствии секции, clamp/нормализацию, runtime-update,
    /// reset, изоляцию копий и потокобезопасность.
    /// </para>
    /// </summary>
    public class OrchestratorOptionsProviderTests
    {
        // ============ Baseline ============

        [Fact]
        public void Get_WithoutSection_ReturnsDefaults()
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            var settings = provider.Get();

            Assert.Equal(5, settings.MaxSubtasks);
            Assert.Equal(3, settings.MaxParallel);
            Assert.Equal(50_000, settings.TokenBudget);
            Assert.Equal("Never", settings.HumanApproval);
            Assert.False(settings.IsOverridden);
            Assert.Equal(6, settings.AllowedWorkerAgents.Count);
            Assert.Contains("code_agent", settings.AllowedWorkerAgents);
            Assert.Contains("web_agent", settings.AllowedWorkerAgents);
            Assert.Contains("file_system_agent", settings.AllowedWorkerAgents);
            Assert.Contains("database_agent", settings.AllowedWorkerAgents);
            Assert.Contains("browser_agent", settings.AllowedWorkerAgents);
            Assert.Contains("external_llm_agent", settings.AllowedWorkerAgents);
        }

        [Fact]
        public void Get_ReadsBaselineFromConfiguration()
        {
            var provider = CreateProvider(new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:MaxSubtasks"] = "7",
                ["SubAgents:orchestrate_task:MaxParallel"] = "2",
                ["SubAgents:orchestrate_task:TokenBudget"] = "12000",
                ["SubAgents:orchestrate_task:HumanApproval"] = "AtEnd",
                ["SubAgents:orchestrate_task:AllowedWorkerAgents:0"] = "web_agent",
                ["SubAgents:orchestrate_task:AllowedWorkerAgents:1"] = "code_agent"
            });

            var settings = provider.Get();

            Assert.Equal(7, settings.MaxSubtasks);
            Assert.Equal(2, settings.MaxParallel);
            Assert.Equal(12_000, settings.TokenBudget);
            Assert.Equal("AtEnd", settings.HumanApproval);
            Assert.Equal(new[] { "web_agent", "code_agent" },
                settings.AllowedWorkerAgents);
            Assert.False(settings.IsOverridden);
        }

        [Fact]
        public void Get_ClampsOutOfRangeBaselineValues()
        {
            var provider = CreateProvider(new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:MaxSubtasks"] = "99",
                ["SubAgents:orchestrate_task:MaxParallel"] = "0",
                ["SubAgents:orchestrate_task:TokenBudget"] = "5"
            });

            var settings = provider.Get();

            Assert.Equal(OrchestratorOptionsProvider.MaxMaxSubtasks, settings.MaxSubtasks);
            Assert.Equal(OrchestratorOptionsProvider.MinMaxParallel, settings.MaxParallel);
            Assert.Equal(OrchestratorOptionsProvider.MinTokenBudget, settings.TokenBudget);
        }

        [Fact]
        public void Get_InvalidNumericBaseline_FallsBackToDefaults()
        {
            var provider = CreateProvider(new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:MaxSubtasks"] = "not-a-number",
                ["SubAgents:orchestrate_task:MaxParallel"] = "",
                ["SubAgents:orchestrate_task:TokenBudget"] = "abc"
            });

            var settings = provider.Get();

            Assert.Equal(5, settings.MaxSubtasks);
            Assert.Equal(3, settings.MaxParallel);
            Assert.Equal(50_000, settings.TokenBudget);
        }

        [Fact]
        public void Get_UnknownHumanApproval_FallsBackToNever()
        {
            var provider = CreateProvider(new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:HumanApproval"] = "Sometimes"
            });

            Assert.Equal("Never", provider.Get().HumanApproval);
        }

        [Fact]
        public void Get_HumanApproval_NormalizedCaseInsensitive()
        {
            var provider = CreateProvider(new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:HumanApproval"] = "betweensubtasks"
            });

            Assert.Equal("BetweenSubtasks", provider.Get().HumanApproval);
        }

        [Fact]
        public void Get_EmptyWorkers_FallsBackToDefaults()
        {
            var dict = new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:AllowedWorkerAgents:0"] = "   "
            };
            var provider = CreateProvider(dict);

            var settings = provider.Get();

            Assert.Equal(6, settings.AllowedWorkerAgents.Count);
            Assert.Contains("code_agent", settings.AllowedWorkerAgents);
        }

        [Fact]
        public void Get_TrimsAndDeduplicatesWorkers_CaseInsensitive()
        {
            var provider = CreateProvider(new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:AllowedWorkerAgents:0"] = " code_agent ",
                ["SubAgents:orchestrate_task:AllowedWorkerAgents:1"] = "CODE_AGENT",
                ["SubAgents:orchestrate_task:AllowedWorkerAgents:2"] = "",
                ["SubAgents:orchestrate_task:AllowedWorkerAgents:3"] = "web_agent"
            });

            var settings = provider.Get();

            Assert.Equal(new[] { "code_agent", "web_agent" },
                settings.AllowedWorkerAgents);
        }

        // ============ Update ============

        [Fact]
        public void Update_AppliesRuntimeOverride()
        {
            var provider = CreateProvider(new Dictionary<string, string>());
            provider.Update(new OrchestratorSettingsDto
            {
                MaxSubtasks = 8,
                MaxParallel = 4,
                TokenBudget = 100_000,
                HumanApproval = "betweenSubtasks",
                AllowedWorkerAgents = new List<string> { "web_agent", "web_agent", " code_agent " }
            });

            var settings = provider.Get();

            Assert.Equal(8, settings.MaxSubtasks);
            Assert.Equal(4, settings.MaxParallel);
            Assert.Equal(100_000, settings.TokenBudget);
            Assert.Equal("BetweenSubtasks", settings.HumanApproval);
            Assert.Equal(new[] { "web_agent", "code_agent" },
                settings.AllowedWorkerAgents);
            Assert.True(settings.IsOverridden);
        }

        [Fact]
        public void Update_Null_Throws()
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            Assert.Throws<ArgumentNullException>(() => provider.Update(null));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(11)]
        public void Update_MaxSubtasksOutOfRange_Throws(int maxSubtasks)
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            Assert.Throws<ArgumentException>(() => provider.Update(
                ValidSettings(s => s.MaxSubtasks = maxSubtasks)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(11)]
        public void Update_MaxParallelOutOfRange_Throws(int maxParallel)
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            Assert.Throws<ArgumentException>(() => provider.Update(
                ValidSettings(s => s.MaxParallel = maxParallel)));
        }

        [Theory]
        [InlineData(999)]
        [InlineData(1_000_001)]
        public void Update_TokenBudgetOutOfRange_Throws(int tokenBudget)
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            Assert.Throws<ArgumentException>(() => provider.Update(
                ValidSettings(s => s.TokenBudget = tokenBudget)));
        }

        [Fact]
        public void Update_EmptyWorkers_Throws()
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            Assert.Throws<ArgumentException>(() => provider.Update(
                ValidSettings(s => s.AllowedWorkerAgents = new List<string>())));
        }

        [Fact]
        public void Update_WhitespaceWorkers_Throws()
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            Assert.Throws<ArgumentException>(() => provider.Update(
                ValidSettings(s => s.AllowedWorkerAgents =
                    new List<string> { "  ", "\t" })));
        }

        [Fact]
        public void Update_InvalidHumanApproval_Throws()
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            Assert.Throws<ArgumentException>(() => provider.Update(
                ValidSettings(s => s.HumanApproval = "Always")));
        }

        [Fact]
        public void Update_FailedValidation_DoesNotOverride()
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            Assert.Throws<ArgumentException>(() => provider.Update(
                ValidSettings(s => s.MaxSubtasks = 0)));

            Assert.False(provider.IsOverridden);
            Assert.Equal(5, provider.Get().MaxSubtasks);
        }

        [Fact]
        public void Update_InputIsOverriddenFlagIsIgnored()
        {
            var provider = CreateProvider(new Dictionary<string, string>());
            var dto = ValidSettings(_ => { });
            dto.IsOverridden = false;

            provider.Update(dto);

            Assert.True(provider.Get().IsOverridden);
        }

        [Fact]
        public void Update_EqualToBaseline_IsNotReportedAsOverridden()
        {
            // v2.2.2-fix (verification): «Сохранить» без изменений не должно
            // зажигать бейдж «изменено» и менять IsOverridden.
            var provider = CreateProvider(new Dictionary<string, string>());
            var baseline = provider.Get();

            provider.Update(new OrchestratorSettingsDto
            {
                MaxSubtasks = baseline.MaxSubtasks,
                MaxParallel = baseline.MaxParallel,
                TokenBudget = baseline.TokenBudget,
                HumanApproval = baseline.HumanApproval,
                AllowedWorkerAgents = baseline.AllowedWorkerAgents.ToList()
            });

            Assert.False(provider.IsOverridden);
            Assert.False(provider.Get().IsOverridden);
        }

        [Fact]
        public void Update_WithFiftyWorkers_IsAccepted()
        {
            var provider = CreateProvider(new Dictionary<string, string>());
            var workers = Enumerable.Range(1, OrchestratorOptionsProvider.MaxWorkerAgents)
                .Select(i => $"agent_{i}")
                .ToList();

            provider.Update(ValidSettings(s => s.AllowedWorkerAgents = workers));

            Assert.Equal(OrchestratorOptionsProvider.MaxWorkerAgents,
                provider.Get().AllowedWorkerAgents.Count);
        }

        [Fact]
        public void Update_WithFiftyOneWorkers_Throws()
        {
            var provider = CreateProvider(new Dictionary<string, string>());
            var workers = Enumerable.Range(1, OrchestratorOptionsProvider.MaxWorkerAgents + 1)
                .Select(i => $"agent_{i}")
                .ToList();

            Assert.Throws<ArgumentException>(() => provider.Update(
                ValidSettings(s => s.AllowedWorkerAgents = workers)));
        }

        // ============ Reset ============

        [Fact]
        public void Reset_RestoresBaseline()
        {
            var provider = CreateProvider(new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:MaxSubtasks"] = "6"
            });
            provider.Update(ValidSettings(s => s.MaxSubtasks = 9));
            Assert.Equal(9, provider.Get().MaxSubtasks);

            provider.Reset();

            var settings = provider.Get();
            Assert.Equal(6, settings.MaxSubtasks);
            Assert.False(settings.IsOverridden);
            Assert.False(provider.IsOverridden);
        }

        // ============ Изоляция / потокобезопасность ============

        [Fact]
        public void Get_ReturnsCopy_MutationDoesNotAffectProvider()
        {
            var provider = CreateProvider(new Dictionary<string, string>());
            var first = provider.Get();
            first.MaxSubtasks = 10;
            first.AllowedWorkerAgents.Clear();

            var second = provider.Get();

            Assert.Equal(5, second.MaxSubtasks);
            Assert.Equal(6, second.AllowedWorkerAgents.Count);
        }

        [Fact]
        public void Update_ConcurrentGetUpdateReset_IsThreadSafe()
        {
            var provider = CreateProvider(new Dictionary<string, string>());
            var errors = new List<Exception>();

            Parallel.For(0, 1000, i =>
            {
                try
                {
                    switch (i % 3)
                    {
                        case 0:
                            provider.Get();
                            break;
                        case 1:
                            provider.Update(ValidSettings(s => s.MaxSubtasks = i % 10 + 1));
                            break;
                        default:
                            provider.Reset();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    lock (errors) { errors.Add(ex); }
                }
            });

            Assert.Empty(errors);

            provider.Update(ValidSettings(s => s.MaxSubtasks = 7));
            var final = provider.Get();
            Assert.Equal(7, final.MaxSubtasks);
            Assert.True(final.IsOverridden);
        }

        // ============ Аргументы конструктора ============

        [Fact]
        public void Ctor_NullConfiguration_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new OrchestratorOptionsProvider(
                    null, NullLogger<OrchestratorOptionsProvider>.Instance));
        }

        [Fact]
        public void Ctor_NullLogger_Throws()
        {
            var config = new ConfigurationBuilder().Build();

            Assert.Throws<ArgumentNullException>(() =>
                new OrchestratorOptionsProvider(
                    config,
                    (ILogger<OrchestratorOptionsProvider>)null));
        }

        // ============ Helpers ============

        private static OrchestratorOptionsProvider CreateProvider(
            IDictionary<string, string> values)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();

            return new OrchestratorOptionsProvider(
                configuration,
                NullLogger<OrchestratorOptionsProvider>.Instance);
        }

        private static OrchestratorSettingsDto ValidSettings(
            Action<OrchestratorSettingsDto> mutate)
        {
            var dto = new OrchestratorSettingsDto
            {
                MaxSubtasks = 5,
                MaxParallel = 3,
                TokenBudget = 50_000,
                HumanApproval = "Never",
                AllowedWorkerAgents = new List<string> { "web_agent" }
            };

            mutate(dto);
            return dto;
        }
    }
}
