namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Результат проверки rate-limiter'а для Vision Agent.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф6.4). См. DESIGN § 6.3.
    /// </remarks>
    public class VisionRateLimitResult
    {
        /// <summary>Разрешена ли задача. <c>false</c> → отказ, клиент получит сообщение.</summary>
        public bool Allowed { get; set; }

        /// <summary>
        /// Через сколько секунд можно попробовать снова. <c>0</c>, если <see cref="Allowed"/> = true.
        /// </summary>
        public int RetryAfterSeconds { get; set; }

        /// <summary>
        /// Сколько слотов осталось в текущем окне (после этой задачи).
        /// <c>0</c> — больше нельзя до конца окна.
        /// </summary>
        public int RemainingInWindow { get; set; }
    }
}