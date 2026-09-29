using IIChatTools.Services.DTO.Mail;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Rate limiter для Mail Agent (v1.8.0, KI-107).
    ///
    /// <para>
    /// Singleton. Проверяет лимиты на отправку и чтение.
    /// Cleanup — Timer каждые 5 минут (по образцу KI-043).
    /// </para>
    /// </summary>
    public interface IMailRateLimiter
    {
        /// <summary>Проверить и зафиксировать отправку.</summary>
        RateLimitResult CheckSend(int userId);

        /// <summary>Проверить и зафиксировать чтение (list/read/search).</summary>
        RateLimitResult CheckRead(int userId);

        /// <summary>Записать байты отправленных вложений (hourly limit).</summary>
        void RecordBytesSent(int userId, long bytes);
    }
}