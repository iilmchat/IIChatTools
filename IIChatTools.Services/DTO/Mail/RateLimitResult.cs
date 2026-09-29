namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Результат проверки rate limit.
    /// v1.8.0 (KI-107).
    /// </summary>
    public class RateLimitResult
    {
        /// <summary>Разрешена ли операция.</summary>
        public bool Allowed { get; set; }

        /// <summary>Через сколько секунд можно повторить (если не разрешено).</summary>
        public int RetryAfterSeconds { get; set; }

        /// <summary>Причина отказа (например, "SendsPerHour exceeded").</summary>
        public string Reason { get; set; }
    }
}