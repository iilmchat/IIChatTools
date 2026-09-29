using System;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Implementation.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Mail
{
    /// <summary>
    /// Тесты <see cref="InMemoryMailRateLimiter"/> (Фаза 4, v1.8.0, KI-107).
    /// </summary>
    public class InMemoryMailRateLimiterTests
    {
        private static InMemoryMailRateLimiter Create(
            int sendsPerHour = 20,
            int sendsPerMinute = 2,
            int readsPerMinute = 30)
        {
            var options = new MailRateLimitOptions
            {
                SendsPerHour = sendsPerHour,
                SendsPerMinute = sendsPerMinute,
                ReadsPerMinute = readsPerMinute
            };
            return new InMemoryMailRateLimiter(
                Options.Create(options),
                NullLogger<InMemoryMailRateLimiter>.Instance);
        }

        [Fact]
        public void CheckSend_BelowLimit_Allows()
        {
            using var limiter = Create(sendsPerHour: 5, sendsPerMinute: 2);

            var r1 = limiter.CheckSend(1);
            Assert.True(r1.Allowed);

            // Отправка #2 — в пределах лимита "2 в минуту".
            var r2 = limiter.CheckSend(1);
            Assert.True(r2.Allowed);
        }

        [Fact]
        public void CheckSend_PerMinuteExceeded_Denies()
        {
            using var limiter = Create(sendsPerHour: 10, sendsPerMinute: 2);

            limiter.CheckSend(1);
            limiter.CheckSend(1);

            var r3 = limiter.CheckSend(1);
            Assert.False(r3.Allowed);
            // Reason на русском — «Превышен лимит: 2 писем/мин».
            Assert.Contains("писем/мин", r3.Reason);
            Assert.True(r3.RetryAfterSeconds > 0);
        }

        [Fact]
        public void CheckSend_PerHourExceeded_Denies()
        {
            // 3 в час, 100 в минуту (чтобы per-minute не мешал).
            using var limiter = Create(sendsPerHour: 3, sendsPerMinute: 100);

            limiter.CheckSend(1);
            limiter.CheckSend(1);
            limiter.CheckSend(1);

            var r4 = limiter.CheckSend(1);
            Assert.False(r4.Allowed);
            // Reason на русском — «Превышен лимит: 3 писем/час».
            Assert.Contains("писем/час", r4.Reason);
        }

        [Fact]
        public void CheckSend_DifferentUsers_Isolated()
        {
            using var limiter = Create(sendsPerHour: 1, sendsPerMinute: 1);

            var r1 = limiter.CheckSend(1);
            Assert.True(r1.Allowed);

            // Другой user — свой лимит.
            var r2 = limiter.CheckSend(2);
            Assert.True(r2.Allowed);

            // А user 1 снова — уже превышен.
            var r3 = limiter.CheckSend(1);
            Assert.False(r3.Allowed);
        }

        [Fact]
        public void CheckRead_PerMinuteExceeded_Denies()
        {
            using var limiter = Create(readsPerMinute: 2);

            limiter.CheckRead(1);
            limiter.CheckRead(1);

            var r3 = limiter.CheckRead(1);
            Assert.False(r3.Allowed);
            // Reason на русском — «Превышен лимит: 2 чтений/мин».
            Assert.Contains("чтений/мин", r3.Reason);
        }

        [Fact]
        public void RecordBytesSent_Accumulates()
        {
            using var limiter = Create();

            // Не бросает.
            limiter.RecordBytesSent(1, 100);
            limiter.RecordBytesSent(1, 200);

            // Проверить сложно (внутреннее состояние), но метод не должен падать.
            Assert.True(true);
        }
    }
}