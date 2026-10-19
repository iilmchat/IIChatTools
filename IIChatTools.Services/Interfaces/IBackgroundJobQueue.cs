using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Очередь фоновых задач.
    /// </summary>
    public interface IBackgroundJobQueue
    {
        /// <summary>
        /// Пытается добавить задачу в очередь.
        /// </summary>
        bool TryEnqueue(int jobId);

        /// <summary>
        /// Извлекает задачу из очереди (асинхронно).
        /// </summary>
        Task<int> DequeueAsync(CancellationToken cancellationToken);
    }
}