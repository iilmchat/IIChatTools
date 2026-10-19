using System;
using System.Collections.Concurrent;
using System.Threading;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.BackgroundJobs
{
    /// <summary>
    /// In-memory реестр CTS фоновых задач (Singleton).
    /// </summary>
    public class JobCancellationRegistry : IJobCancellationRegistry
    {
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _registry = new();

        /// <inheritdoc />
        public CancellationTokenSource GetOrCreate(int jobId)
        {
            return _registry.GetOrAdd(jobId, _ => new CancellationTokenSource());
        }

        /// <inheritdoc />
        public void Cancel(int jobId)
        {
            if (_registry.TryGetValue(jobId, out var cts))
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Runner уже завершил job и удалил CTS — отменять нечего.
                }
            }
        }

        /// <inheritdoc />
        public void Remove(int jobId)
        {
            if (_registry.TryRemove(jobId, out var cts))
            {
                cts.Dispose();
            }
        }
    }
}
