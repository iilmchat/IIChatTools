using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис очистки устаревших записей аудита
    /// (по retention policy из конфигурации).
    /// </summary>
    public interface IAuditRetentionService
    {
        /// <summary>
        /// Выполняет один прогон чистки: БД + файлы.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Асинхронная задача</returns>
        Task RunCleanupAsync(CancellationToken cancellationToken = default);
    }
}