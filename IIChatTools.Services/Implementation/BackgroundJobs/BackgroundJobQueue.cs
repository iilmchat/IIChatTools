using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.BackgroundJobs
{
    /// <summary>
    /// Очередь фоновых задач (in-memory).
    /// </summary>
    public class BackgroundJobQueue : IBackgroundJobQueue
    {
        private readonly Channel<int> _channel;
        private readonly ILogger<BackgroundJobQueue> _logger;

        public BackgroundJobQueue(ILogger<BackgroundJobQueue> logger)
        {
            _logger = logger;
            _channel = Channel.CreateBounded<int>(new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public bool TryEnqueue(int jobId)
        {
            if (_channel.Writer.TryWrite(jobId))
            {
                _logger.LogDebug("BackgroundJob {JobId} enqueued.", jobId);
                return true;
            }
            _logger.LogWarning("BackgroundJob queue is full. Job {JobId} rejected.", jobId);
            return false;
        }

        public async Task<int> DequeueAsync(CancellationToken cancellationToken)
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
    }
}