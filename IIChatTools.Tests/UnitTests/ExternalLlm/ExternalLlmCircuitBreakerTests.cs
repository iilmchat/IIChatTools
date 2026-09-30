using System.Threading;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm
{
    /// <summary>
    /// Тесты <see cref="ExternalLlmCircuitBreaker"/> (v1.8.1, KI-109, Фаза 2.2).
    ///
    /// <para>
    /// <b>Важно:</b> тесты используют короткий <c>BreakDurationSeconds = 30</c>
    /// (clamp минимум). Для проверки «break истёк» потребовался бы
    /// <c>Thread.Sleep</c> ≤ 31 с — это дорого, поэтому такой тест
    /// отсутствует. Вместо него — проверка логики threshold и reset.
    /// </para>
    /// </summary>
    public class ExternalLlmCircuitBreakerTests
    {
        private static ExternalLlmCircuitBreaker Create(
            int threshold = 3,
            int breakSeconds = 30)
        {
            var opts = new ExternalLlmOptions
            {
                CircuitBreaker = new ExternalLlmCircuitBreakerOptions
                {
                    FailureThreshold = threshold,
                    BreakDurationSeconds = breakSeconds
                }
            };

            return new ExternalLlmCircuitBreaker(
                Options.Create(opts),
                NullLogger<ExternalLlmCircuitBreaker>.Instance);
        }

        [Fact]
        public void IsOpen_InitiallyFalse()
        {
            using var cb = Create();
            Assert.False(cb.IsOpen("deepseek"));
        }

        [Fact]
        public void IsOpen_EmptyProviderName_False()
        {
            using var cb = Create();
            Assert.False(cb.IsOpen(null));
            Assert.False(cb.IsOpen(""));
            Assert.False(cb.IsOpen("   "));
        }

        [Fact]
        public void RecordFailure_DoesNotOpenBeforeThreshold()
        {
            using var cb = Create(threshold: 3);

            cb.RecordFailure("deepseek", "timeout");
            cb.RecordFailure("deepseek", "timeout");

            Assert.False(cb.IsOpen("deepseek"));
        }

        [Fact]
        public void RecordFailure_OpensAtThreshold()
        {
            using var cb = Create(threshold: 3);

            cb.RecordFailure("deepseek", "err1");
            cb.RecordFailure("deepseek", "err2");
            cb.RecordFailure("deepseek", "err3");

            Assert.True(cb.IsOpen("deepseek"));
        }

        [Fact]
        public void RecordSuccess_ResetsBreaker()
        {
            using var cb = Create(threshold: 3);

            cb.RecordFailure("deepseek", "err1");
            cb.RecordFailure("deepseek", "err2");
            cb.RecordFailure("deepseek", "err3");
            Assert.True(cb.IsOpen("deepseek"));

            cb.RecordSuccess("deepseek");

            Assert.False(cb.IsOpen("deepseek"));

            // После сброса нужно снова накопить threshold.
            cb.RecordFailure("deepseek", "err1");
            cb.RecordFailure("deepseek", "err2");
            Assert.False(cb.IsOpen("deepseek"));
        }

        [Fact]
        public void GetStatus_UnknownProvider_AvailableTrue()
        {
            using var cb = Create();
            var status = cb.GetStatus("unknown");

            Assert.Equal("unknown", status.Provider);
            Assert.True(status.Available);
            Assert.Null(status.LastError);
            Assert.Null(status.LastCheckAt);
        }

        [Fact]
        public void GetStatus_OpenProvider_UnavailableWithError()
        {
            using var cb = Create(threshold: 2);

            cb.RecordFailure("deepseek", "connection refused");
            cb.RecordFailure("deepseek", "connection refused");

            var status = cb.GetStatus("deepseek");

            Assert.False(status.Available);
            Assert.NotNull(status.LastError);
            Assert.Contains("Circuit breaker open", status.LastError);
            Assert.Contains("connection refused", status.LastError);
            Assert.NotNull(status.LastCheckAt);
        }

        [Fact]
        public void GetStatus_ClosedWithFailures_AvailableTrue()
        {
            using var cb = Create(threshold: 3);

            cb.RecordFailure("deepseek", "single fail");

            var status = cb.GetStatus("deepseek");
            Assert.True(status.Available);
            Assert.Equal("single fail", status.LastError);
        }

        [Fact]
        public void Constructor_ClampsThresholdBelowMin()
        {
            // FailureThreshold=0 → clamp к 1.
            using var cb = Create(threshold: 0);

            cb.RecordFailure("deepseek", "err");
            Assert.True(cb.IsOpen("deepseek"));
        }

        [Fact]
        public void Constructor_ClampsThresholdAboveMax()
        {
            // FailureThreshold=100 → clamp к 10.
            using var cb = Create(threshold: 100);

            for (int i = 0; i < 9; i++)
                cb.RecordFailure("deepseek", $"err{i}");
            Assert.False(cb.IsOpen("deepseek"));

            cb.RecordFailure("deepseek", "err10");
            Assert.True(cb.IsOpen("deepseek"));
        }

        [Fact]
        public void RecordSuccess_UnknownProvider_NoOp()
        {
            using var cb = Create();

            // Не должно падать.
            cb.RecordSuccess("unknown");
            cb.RecordSuccess("");
            cb.RecordSuccess(null);

            Assert.False(cb.IsOpen("unknown"));
        }
    }
}