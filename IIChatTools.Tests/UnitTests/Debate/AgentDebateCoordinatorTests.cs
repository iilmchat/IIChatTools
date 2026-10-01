using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Implementation.Debate;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Debate
{
    /// <summary>
    /// Unit-тесты <see cref="AgentDebateCoordinator"/>
    /// (v1.11.0, KI-126, Шаг 1E).
    /// </summary>
    public class AgentDebateCoordinatorTests
    {
        private static AgentDebateCoordinator CreateCoordinator()
            => new AgentDebateCoordinator(NullLogger<AgentDebateCoordinator>.Instance);

        [Fact]
        public async Task WaitForFeedbackAsync_ProvideBeforeTimeout_ReturnsFeedback()
        {
            var coordinator = CreateCoordinator();

            var waitTask = coordinator.WaitForFeedbackAsync(
                sessionId: 1, timeout: TimeSpan.FromSeconds(5));

            // Даём TCS зарегистрироваться.
            await Task.Delay(50);

            var ok = await coordinator.ProvideFeedbackAsync(1, "исправь edge case");
            var feedback = await waitTask;

            Assert.True(ok);
            Assert.Equal("исправь edge case", feedback);
        }

        [Fact]
        public async Task WaitForFeedbackAsync_NoProvide_ReturnsNullOnTimeout()
        {
            var coordinator = CreateCoordinator();

            // Таймаут 100 мс — быстрый тест.
            var feedback = await coordinator.WaitForFeedbackAsync(
                sessionId: 42, timeout: TimeSpan.FromMilliseconds(100));

            Assert.Null(feedback);
        }

        [Fact]
        public async Task WaitForFeedbackAsync_Cancelled_ReturnsNull()
        {
            var coordinator = CreateCoordinator();

            using var cts = new CancellationTokenSource();
            var waitTask = coordinator.WaitForFeedbackAsync(
                sessionId: 7, timeout: TimeSpan.FromSeconds(5), cts.Token);

            await Task.Delay(50);
            cts.Cancel();

            var feedback = await waitTask;
            Assert.Null(feedback);
        }

        [Fact]
        public async Task WaitForFeedbackAsync_DuplicateSession_ReturnsNull()
        {
            var coordinator = CreateCoordinator();

            // Первый ожидающий.
            var first = coordinator.WaitForFeedbackAsync(
                sessionId: 100, timeout: TimeSpan.FromSeconds(5));

            await Task.Delay(50);

            // Дубликат — должен получить null сразу.
            var second = await coordinator.WaitForFeedbackAsync(
                sessionId: 100, timeout: TimeSpan.FromMilliseconds(200));
            Assert.Null(second);

            // Разбудим первого, чтобы не висел.
            await coordinator.ProvideFeedbackAsync(100, "ok");
            Assert.Equal("ok", await first);
        }

        [Fact]
        public async Task ProvideFeedbackAsync_NoWaiting_ReturnsFalse()
        {
            var coordinator = CreateCoordinator();

            var ok = await coordinator.ProvideFeedbackAsync(999, "некому");
            Assert.False(ok);
        }

        [Fact]
        public async Task ProvideFeedbackAsync_EmptyFeedback_ReturnsFalse()
        {
            var coordinator = CreateCoordinator();

            var ok = await coordinator.ProvideFeedbackAsync(1, "   ");
            Assert.False(ok);
        }

        [Fact]
        public async Task ProvideFeedbackAsync_AfterTimeout_ReturnsFalse()
        {
            var coordinator = CreateCoordinator();

            // Таймаут истечёт раньше, чем придёт feedback.
            await coordinator.WaitForFeedbackAsync(5, TimeSpan.FromMilliseconds(80));

            var ok = await coordinator.ProvideFeedbackAsync(5, "поздно");
            Assert.False(ok);
        }
    }
}