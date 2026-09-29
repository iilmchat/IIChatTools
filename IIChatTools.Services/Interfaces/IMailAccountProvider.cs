using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Провайдер учётных данных почтового ящика.
    /// v1.8.0 (KI-107) — Global (один ящик).
    /// v1.8.x (KI-108) — PerUser (свой ящик у каждого пользователя).
    /// </summary>
    public interface IMailAccountProvider
    {
        /// <summary>Вернуть creds для пользователя.</summary>
        Task<MailAccountCredentials> GetAsync(
            int userId, CancellationToken ct = default);
    }
}