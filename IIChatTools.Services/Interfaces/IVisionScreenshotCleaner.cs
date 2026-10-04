using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Очистка устаревших скриншотов Vision Agent из workspace'ов всех пользователей.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.6). См. DESIGN § 6.5.
    /// </para>
    /// <para>
    /// Обходит <c>{userWorkspace}/screenshots/{taskId}/</c> всех пользователей,
    /// удаляет папки задач старше <c>Privacy.WorkspaceRetentionHours</c> (default 1 ч).
    /// </para>
    /// </remarks>
    public interface IVisionScreenshotCleaner
    {
        /// <summary>
        /// Один прогон очистки. Ошибки на уровне одной папки не прерывают остальные.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Метрики прогона.</returns>
        Task<VisionScreenshotCleanupResult> CleanupAsync(
            CancellationToken cancellationToken = default);
    }
}