using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис управления фоновыми задачами.
    /// </summary>
    public interface IBackgroundJobService
    {
        /// <summary>Создаёт задачу в статусе Pending.</summary>
        Task<BackgroundJob> CreateAsync(int userId, int? chatId, string type, string parametersJson);

        /// <summary>Возвращает задачу по ID (без проверки владельца — проверяет вызывающий).</summary>
        Task<BackgroundJob> GetAsync(int jobId);

        /// <summary>
        /// Список задач пользователя (до 50, новые сверху).
        /// <paramref name="chatId"/> = null — все чаты пользователя.
        /// </summary>
        Task<IReadOnlyList<BackgroundJob>> ListAsync(int userId, int? chatId);

        /// <summary>Обновляет статус/прогресс/результат.</summary>
        Task UpdateStatusAsync(
            int jobId, string status, int progress, string message,
            string resultJson = null, string error = null);

        Task<int> CountActiveByUserAsync(int userId);
    }
}