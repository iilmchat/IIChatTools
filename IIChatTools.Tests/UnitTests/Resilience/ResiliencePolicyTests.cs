using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Implementation.Resilience;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Resilience
{
    /// <summary>
    /// D03 / KI-265: тесты единой политики timeout/retry.
    /// </summary>
    public class ResiliencePolicyTests
    {
        private static ResiliencePolicy.Options FastOptions(int attempts = 2) => new()
        {
            MaxAttempts = attempts,
            Timeout = TimeSpan.FromSeconds(2),
            BaseDelay = TimeSpan.FromMilliseconds(1)
        };

        [Fact]
        public async Task ExecuteWithRetryAsync_TransientFailure_RetriesAndSucceeds()
        {
            var calls = 0;
            var result = await ResiliencePolicy.ExecuteWithRetryAsync<int>(
                _ =>
                {
                    calls++;
                    if (calls == 1)
                        throw new HttpRequestException("transient");
                    return Task.FromResult(42);
                },
                FastOptions(),
                CancellationToken.None);

            Assert.Equal(42, result);
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task ExecuteWithRetryAsync_NonTransientFailure_NoRetry()
        {
            var calls = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ResiliencePolicy.ExecuteWithRetryAsync<int>(
                    _ =>
                    {
                        calls++;
                        throw new InvalidOperationException("permanent");
                    },
                    FastOptions(),
                    CancellationToken.None));

            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task ExecuteWithRetryAsync_ExternalCancellation_ThrowsImmediately()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var calls = 0;

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                ResiliencePolicy.ExecuteWithRetryAsync<int>(
                    _ =>
                    {
                        calls++;
                        return Task.FromResult(1);
                    },
                    FastOptions(),
                    cts.Token));

            Assert.Equal(0, calls);
        }
    }
}
