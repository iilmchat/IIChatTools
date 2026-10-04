using System.Threading;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="InMemoryVisionRateLimiter"/>
    /// (v1.12.0, KI-131, Ф6.4).
    /// </summary>
    public class InMemoryVisionRateLimiterTests
    {
        private static InMemoryVisionRateLimiter CreateLimiter(int maxPerWindow = 5)
        {
            var options = new VisionAgentOptions
            {
                Limits = new VisionLimitsOptions
                {
                    MaxTasksPerUserPer5Min = maxPerWindow
                }
            };
            return new InMemoryVisionRateLimiter(
                Options.Create(options),
                NullLogger<InMemoryVisionRateLimiter>.Instance);
        }

        // ============ 1. Первый запрос — разрешён ============

        [Fact]
        public void TryAcquire_FirstAttempt_Allowed()
        {
            using var limiter = CreateLimiter(5);
            var result = limiter.TryAcquire(1);

            Assert.True(result.Allowed);
            Assert.Equal(0, result.RetryAfterSeconds);
            Assert.Equal(4, result.RemainingInWindow);
        }

        // ============ 2. N запросов в пределах лимита ============

        [Fact]
        public void TryAcquire_WithinLimit_AllAllowed()
        {
            using var limiter = CreateLimiter(5);

            for (int i = 0; i < 5; i++)
            {
                var r = limiter.TryAcquire(1);
                Assert.True(r.Allowed, $"Request {i + 1} должен быть разрешён.");
            }
        }

        // ============ 3. Сверх лимита — отказ ============

        [Fact]
        public void TryAcquire_OverLimit_Rejected()
        {
            using var limiter = CreateLimiter(5);

            for (int i = 0; i < 5; i++) limiter.TryAcquire(1);

            var r = limiter.TryAcquire(1);
            Assert.False(r.Allowed);
            Assert.True(r.RetryAfterSeconds > 0);
            Assert.Equal(0, r.RemainingInWindow);
        }

        // ============ 4. Per-user изоляция ============

        [Fact]
        public void TryAcquire_DifferentUsers_IndependentLimits()
        {
            using var limiter = CreateLimiter(2);

            // user1 — 2 раза (OK)
            Assert.True(limiter.TryAcquire(1).Allowed);
            Assert.True(limiter.TryAcquire(1).Allowed);
            // user1 — 3-й раз (отказ)
            Assert.False(limiter.TryAcquire(1).Allowed);

            // user2 — свежий, не затронут
            Assert.True(limiter.TryAcquire(2).Allowed);
            Assert.True(limiter.TryAcquire(2).Allowed);
            Assert.False(limiter.TryAcquire(2).Allowed);
        }

        // ============ 5. Некорректный userId ============

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void TryAcquire_InvalidUserId_Rejected(int userId)
        {
            using var limiter = CreateLimiter();
            var r = limiter.TryAcquire(userId);
            Assert.False(r.Allowed);
        }

        // ============ 6. RemainingInWindow убывает ============

        [Fact]
        public void TryAcquire_RemainingDecreases()
        {
            using var limiter = CreateLimiter(3);

            Assert.Equal(2, limiter.TryAcquire(1).RemainingInWindow);
            Assert.Equal(1, limiter.TryAcquire(1).RemainingInWindow);
            Assert.Equal(0, limiter.TryAcquire(1).RemainingInWindow);
        }

        // ============ 7. Clamp MaxTasksPerUserPer5Min ============

        [Fact]
        public void Constructor_ZeroMaxTasks_ClampedTo1()
        {
            using var limiter = CreateLimiter(0);

            // Clamp к 1.
            Assert.True(limiter.TryAcquire(1).Allowed);
            Assert.False(limiter.TryAcquire(1).Allowed);
        }

        [Fact]
        public void Constructor_HugeMaxTasks_ClampedTo100()
        {
            using var limiter = CreateLimiter(10000);

            // Clamp к 100. Проверяем, что хотя бы 100 запросов прошли.
            for (int i = 0; i < 100; i++)
            {
                Assert.True(limiter.TryAcquire(1).Allowed, $"Request {i + 1} должен быть разрешён.");
            }
            Assert.False(limiter.TryAcquire(1).Allowed);
        }

        // ============ 8. Dispose идемпотентен ============

        [Fact]
        public void Dispose_CalledTwice_NoThrow()
        {
            var limiter = CreateLimiter();
            limiter.Dispose();
            limiter.Dispose();   // не должен бросать.
        }
    }
}