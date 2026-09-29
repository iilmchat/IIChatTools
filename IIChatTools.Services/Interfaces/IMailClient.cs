using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Клиент для работы с IMAP/SMTP через MailKit.
    /// v1.8.0 (KI-107).
    ///
    /// <para>
    /// Не выставляет MailKit-типы наружу — только DTO. Это позволяет
    /// в будущем заменить MailKit на другой клиент без переписывания tools.
    /// </para>
    ///
    /// <para>
    /// <b>Singleton.</b> Не потокобезопасен на уровне IMAP-соединения —
    /// MailKitClient внутри держит пул per-user с TTL и lock.
    /// </para>
    /// </summary>
    public interface IMailClient
    {
        /// <summary>Список писем из папки (последние N).</summary>
        Task<IReadOnlyList<MailMessageSummaryDto>> ListAsync(
            string mailbox, int count, bool unseenOnly,
            int userId, CancellationToken ct = default);

        /// <summary>Прочитать письмо по UID.</summary>
        Task<MailMessageDto> ReadAsync(
            uint uid, string mailbox, bool saveAttachments,
            int userId, CancellationToken ct = default);

        /// <summary>Поиск писем по фильтрам.</summary>
        Task<IReadOnlyList<MailMessageSummaryDto>> SearchAsync(
            SearchMailRequest request,
            int userId, CancellationToken ct = default);

        /// <summary>Отправить письмо через SMTP.</summary>
        Task SendAsync(
            SendMailRequest request,
            int userId, CancellationToken ct = default);

        /// <summary>Удалить письмо (переместить в Trash).</summary>
        Task DeleteAsync(
            uint uid, string mailbox,
            int userId, CancellationToken ct = default);

        /// <summary>Переместить письмо между папками.</summary>
        Task MoveAsync(
            uint uid, string fromMailbox, string toMailbox,
            int userId, CancellationToken ct = default);

        /// <summary>Пометить прочитанным.</summary>
        Task MarkAsReadAsync(
            uint uid, string mailbox,
            int userId, CancellationToken ct = default);

        /// <summary>Проверить подключение (IMAP NOOP + SMTP connection test).</summary>
        Task<bool> TestConnectionAsync(
            int userId, CancellationToken ct = default);
    }
}