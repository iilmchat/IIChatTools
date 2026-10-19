using IIChatTools.Services.Implementation.BackgroundJobs;
using Xunit;

namespace IIChatTools.Tests.UnitTests.BackgroundJobs
{
    /// <summary>
    /// Unit-тесты <see cref="JobCancellationRegistry"/> (KI-259).
    /// </summary>
    public class JobCancellationRegistryTests
    {
        /// <summary>
        /// Повторный GetOrCreate для одного jobId возвращает тот же CTS.
        /// </summary>
        [Fact]
        public void GetOrCreate_SameJob_ReturnsSameInstance()
        {
            var registry = new JobCancellationRegistry();

            var first = registry.GetOrCreate(1);
            var second = registry.GetOrCreate(1);

            Assert.Same(first, second);
            registry.Remove(1);
        }

        /// <summary>
        /// Cancel отменяет токен задачи.
        /// </summary>
        [Fact]
        public void Cancel_CancelsJobToken()
        {
            var registry = new JobCancellationRegistry();
            var cts = registry.GetOrCreate(42);

            registry.Cancel(42);

            Assert.True(cts.IsCancellationRequested);
            registry.Remove(42);
        }

        /// <summary>
        /// Remove удаляет CTS; следующий GetOrCreate создаёт новый, не отменённый.
        /// </summary>
        [Fact]
        public void Remove_AllowsNewTokenAfterwards()
        {
            var registry = new JobCancellationRegistry();
            var oldCts = registry.GetOrCreate(7);

            registry.Remove(7);
            var newCts = registry.GetOrCreate(7);

            Assert.NotSame(oldCts, newCts);
            Assert.False(newCts.IsCancellationRequested);
            registry.Remove(7);
        }

        /// <summary>
        /// Cancel/Remove для отсутствующего jobId — no-op, без исключений.
        /// </summary>
        [Fact]
        public void CancelAndRemove_UnknownJob_DoNotThrow()
        {
            var registry = new JobCancellationRegistry();

            registry.Cancel(999);
            registry.Remove(999);
        }
    }
}
