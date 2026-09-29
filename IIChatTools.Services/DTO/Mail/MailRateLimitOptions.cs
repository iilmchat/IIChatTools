namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Настройки rate limiting для Mail Agent.
    /// v1.8.0 (KI-107).
    /// </summary>
    public class MailRateLimitOptions
    {
        /// <summary>Максимум отправок в час.</summary>
        public int SendsPerHour { get; set; } = 20;

        /// <summary>Максимум отправок в минуту.</summary>
        public int SendsPerMinute { get; set; } = 2;

        /// <summary>Максимум операций чтения (list/read/search) в минуту.</summary>
        public int ReadsPerMinute { get; set; } = 30;
    }
}