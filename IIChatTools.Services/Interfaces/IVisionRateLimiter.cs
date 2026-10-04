using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Rate limiter для Vision Agent — ограничивает число задач на пользователя
    /// в окне <c>MaxTasksPerUserPer5Min</c> (default 5 / 5 минут).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.4). См. DESIGN § 6.3.
    /// </para>
    /// <para>
    /// <b>Singleton.</b> Состояние — <c>ConcurrentDictionary&lt;int, UserWindow&gt;</c>.
    /// Cleanup устаревших записей — <c>Timer</c> каждые 5 минут (по образцу KI-043).
    /// </para>
    /// </remarks>
    public interface IVisionRateLimiter
    {
        /// <summary>
        /// Проверяет возможность запуска задачи и, если разрешено,
        /// инкрементирует счётчик (atomic).
        /// </summary>
        /// <param name="userId">Пользователь-инициатор (для per-user окна).</param>
        /// <returns>
        /// <see cref="VisionRateLimitResult"/>:
        /// <c>Allowed = true</c> — задача разрешена, счётчик увеличен;
        /// <c>Allowed = false</c> — превышен лимит, <c>RetryAfterSeconds</c> указывает,
        /// через сколько секунд можно попробовать снова.
        /// </returns>
        VisionRateLimitResult TryAcquire(int userId);
    }
}