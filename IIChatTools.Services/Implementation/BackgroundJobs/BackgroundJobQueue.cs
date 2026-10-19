using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.BackgroundJobs
{
    public class BackgroundJobQueue : IBackgroundJobQueue
    {
        private const int Capacity = 100;
        private readonly Channel<int> _channel;
        private readonly ILogger<BackgroundJobQueue> _logger;

        public BackgroundJobQueue(ILogger<BackgroundJobQueue> logger)
        {
            _logger = logger;
            _channel = Channel.CreateBounded<int>(new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public bool TryEnqueue(int jobId)
        {
            // KI-260: DropWrite возвращает true, даже если выбросил.
            // Проверяем Count явно.
            if (_channel.Reader.Count >= Capacity)
            {
                _logger.LogWarning("BackgroundJob queue is full (capacity={Cap}). Job {JobId} rejected.", Capacity, jobId);
                return false;
            }

            if (_channel.Writer.TryWrite(jobId))
            {
                _logger.LogDebug("BackgroundJob {JobId} enqueued.", jobId);
                return true;
            }
            return false;
        }

        public async Task<int> DequeueAsync(CancellationToken cancellationToken)
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
    }
}