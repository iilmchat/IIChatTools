using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm
{
    /// <summary>
    /// Тесты <see cref="ExternalLlmBudgetTracker"/> (v1.8.1, KI-109, Фаза 2.3).
    ///
    /// <para>
    /// Lazy-reset при смене дня UTC протестирован через явное вмешательство
    /// в состояние невозможно (state — private). Поэтому покрываются
    /// только базовые сценарии: лимит USD, лимит токенов, аккумуляция.
    /// </para>
    /// </summary>
    public class ExternalLlmBudgetTrackerTests
    {
        private const int UserId = 1;

        private static ExternalLlmBudgetTracker Create(
            decimal dailyBudgetUsd = 5m,
            long dailyTokensLimit = 500_000)
        {
            var opts = new ExternalLlmOptions
            {
                DailyBudgetUsd = dailyBudgetUsd,
                DailyTokensLimit = dailyTokensLimit
            };

            return new ExternalLlmBudgetTracker(
                Options.Create(opts),
                NullLogger<ExternalLlmBudgetTracker>.Instance);
        }

        [Fact]
        public void CanSpend_InitiallyTrue()
        {
            using var tracker = Create();
            Assert.True(tracker.CanSpend(UserId));
        }

        [Fact]
        public void CanSpend_InvalidUserId_False()
        {
            using var tracker = Create();
            Assert.False(tracker.CanSpend(0));
            Assert.False(tracker.CanSpend(-1));
        }

        [Fact]
        public void CanSpend_AfterBudgetExceeded_False()
        {
            using var tracker = Create(dailyBudgetUsd: 1m);

            tracker.RecordUsage(UserId, promptTokens: 1000, completionTokens: 500, costUsd: 0.5m);
            Assert.True(tracker.CanSpend(UserId));

            tracker.RecordUsage(UserId, promptTokens: 1000, completionTokens: 500, costUsd: 0.6m);
            Assert.False(tracker.CanSpend(UserId));
        }

        [Fact]
        public void CanSpend_AfterTokensExceeded_False()
        {
            using var tracker = Create(dailyBudgetUsd: 100m, dailyTokensLimit: 1000);

            tracker.RecordUsage(UserId, promptTokens: 400, completionTokens: 100, costUsd: 0m);
            Assert.True(tracker.CanSpend(UserId));

            tracker.RecordUsage(UserId, promptTokens: 400, completionTokens: 200, costUsd: 0m);
            Assert.False(tracker.CanSpend(UserId));
        }

        [Fact]
        public void RecordUsage_AccumulatesCost()
        {
            using var tracker = Create();

            tracker.RecordUsage(UserId, 100, 50, 0.001m);
            tracker.RecordUsage(UserId, 100, 50, 0.002m);
            tracker.RecordUsage(UserId, 100, 50, 0.003m);

            Assert.Equal(0.006m, tracker.GetTodayCostUsd(UserId));
        }

        [Fact]
        public void RecordUsage_AccumulatesTokens()
        {
            using var tracker = Create();

            tracker.RecordUsage(UserId, 100, 50, 0m);
            tracker.RecordUsage(UserId, 200, 30, 0m);

            Assert.Equal(380L, tracker.GetTodayTokens(UserId));    // 150 + 230
        }

        [Fact]
        public void RecordUsage_IsolatedPerUser()
        {
            using var tracker = Create();

            tracker.RecordUsage(userId: 1, 100, 50, 0.001m);
            tracker.RecordUsage(userId: 2, 200, 100, 0.002m);

            Assert.Equal(0.001m, tracker.GetTodayCostUsd(1));
            Assert.Equal(0.002m, tracker.GetTodayCostUsd(2));
            Assert.Equal(150L, tracker.GetTodayTokens(1));
            Assert.Equal(300L, tracker.GetTodayTokens(2));
        }

        [Fact]
        public void GetTodayCostUsd_UnknownUser_ReturnsZero()
        {
            using var tracker = Create();
            Assert.Equal(0m, tracker.GetTodayCostUsd(999));
            Assert.Equal(0L, tracker.GetTodayTokens(999));
        }

        [Fact]
        public void RecordUsage_NegativeInput_ClampedToZero()
        {
            using var tracker = Create();

            tracker.RecordUsage(UserId, promptTokens: -100, completionTokens: 100, costUsd: -0.5m);

            Assert.Equal(100L, tracker.GetTodayTokens(UserId));
            Assert.Equal(0m, tracker.GetTodayCostUsd(UserId));
        }

        [Fact]
        public void Constructor_ClampsBudget()
        {
            // DailyBudgetUsd = -5 → clamp к 0 (= безлимит по бюджету).
            using var tracker = Create(dailyBudgetUsd: -5m);

            tracker.RecordUsage(UserId, 100, 50, 100m);
            Assert.True(tracker.CanSpend(UserId));
        }

        [Fact]
        public void Constructor_ClampsTokensLimit()
        {
            // DailyTokensLimit = -1000 → clamp к 0 (= безлимит по токенам).
            using var tracker = Create(dailyBudgetUsd: 100m, dailyTokensLimit: -1000);

            tracker.RecordUsage(UserId, 100_000, 50_000, 0m);
            Assert.True(tracker.CanSpend(UserId));
        }
    }
}