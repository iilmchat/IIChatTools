using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Interfaces;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace IIChatTools.Services.Implementation.Mail
{
    /// <summary>
    /// Реализация <see cref="IMailClient"/> на базе MailKit 4.x
    /// (v1.8.0, KI-107).
    ///
    /// <para>
    /// <b>Singleton.</b> Per-call connect → operation → disconnect.
    /// IMAP-пул с TTL — отложен (см. KI-107-more). В MVP — проще
    /// и безопаснее пересоздавать клиент на каждый вызов.
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только метаданные (uid, count, bytes).
    /// Никогда не логируются Subject / From / To / Body.
    /// </para>
    /// </summary>
    public sealed class MailKitClient : IMailClient, IDisposable
    {
        private const int DefaultTimeoutMs = 30_000;

        private readonly IMailAccountProvider _accountProvider;
        private readonly ILogger<MailKitClient> _logger;
        private bool _disposed;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="accountProvider">Провайдер creds (v1.8.0 — Global)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public MailKitClient(
            IMailAccountProvider accountProvider,
            ILogger<MailKitClient> logger)
        {
            _accountProvider = accountProvider
                ?? throw new ArgumentNullException(nameof(accountProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ============================================================
        // Public API (IMailClient)
        // ============================================================

        /// <inheritdoc />
        public async Task<IReadOnlyList<MailMessageSummaryDto>> ListAsync(
            string mailbox, int count, bool unseenOnly,
            int userId, CancellationToken ct = default)
        {
            return await WithImapAsync(userId, async (client, _) =>
            {
                var folder = ResolveFolder(client, mailbox);
                await folder.OpenAsync(FolderAccess.ReadOnly, ct);

                var query = unseenOnly ? SearchQuery.NotSeen : SearchQuery.All;
                var uids = await folder.SearchAsync(query, ct);

                // Последние N (Search возвращает в порядке возрастания UID).
                var take = Math.Max(0, Math.Min(count, uids.Count));
                var slice = take > 0
                    ? uids.Skip(uids.Count - take).ToList()
                    : new List<UniqueId>();

                if (slice.Count == 0)
                    return Array.Empty<MailMessageSummaryDto>();

                var summaries = await folder.FetchAsync(
                    slice,
                    MessageSummaryItems.Envelope
                        | MessageSummaryItems.Flags
                        | MessageSummaryItems.BodyStructure,
                    ct);

                var result = new List<MailMessageSummaryDto>(summaries.Count);
                foreach (var s in summaries)
                {
                    result.Add(MapSummary(s, mailbox));
                }

                _logger.LogInformation(
                    "Mail: list mailbox={Mailbox} count={Count} unseenOnly={Unseen}",
                    mailbox, result.Count, unseenOnly);

                return (IReadOnlyList<MailMessageSummaryDto>)result;
            }, ct);
        }

        /// <inheritdoc />
        public async Task<MailMessageDto> ReadAsync(
            uint uid, string mailbox, bool saveAttachments,
            int userId, CancellationToken ct = default)
        {
            return await WithImapAsync(userId, async (client, _) =>
            {
                var folder = ResolveFolder(client, mailbox);
                await folder.OpenAsync(FolderAccess.ReadOnly, ct);

                var message = await folder.GetMessageAsync(new UniqueId(uid), ct);

                var attachments = new List<MailAttachmentDto>();
                foreach (var entity in message.Attachments)
                {
                    if (entity is MimePart mimePart)
                    {
                        attachments.Add(new MailAttachmentDto
                        {
                            FileName = mimePart.FileName ?? "attachment",
                            ContentType = mimePart.ContentType.MimeType,
                            SizeBytes = 0,      // точный размер не читаем (Фаза 4 — IMailAttachmentService)
                            StoragePath = null  // Фаза 4 — сохранение в workspace
                        });
                    }
                }

                if (saveAttachments && attachments.Count > 0)
                {
                    _logger.LogWarning(
                        "Mail: read uid={Uid} saveAttachments=true, но IMailAttachmentService ещё не реализован (Фаза 4). " +
                        "Вложения не сохранены в workspace.",
                        uid);
                }

                _logger.LogInformation(
                    "Mail: read uid={Uid} mailbox={Mailbox} attachments={Attachments} saveAttachments={Save}",
                    uid, mailbox, attachments.Count, saveAttachments);

                return new MailMessageDto
                {
                    Uid = uid,
                    Mailbox = mailbox,
                    From = message.From?.ToString() ?? string.Empty,
                    To = message.To.Select(a => a.ToString()).ToList(),
                    Cc = message.Cc.Select(a => a.ToString()).ToList(),
                    Subject = message.Subject ?? string.Empty,
                    Date = message.Date.UtcDateTime,
                    BodyText = message.TextBody,
                    BodyHtml = message.HtmlBody,
                    Attachments = attachments
                };
            }, ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<MailMessageSummaryDto>> SearchAsync(
            SearchMailRequest request, int userId, CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return await WithImapAsync(userId, async (client, _) =>
            {
                var mailbox = string.IsNullOrWhiteSpace(request.Mailbox)
                    ? "INBOX"
                    : request.Mailbox;

                var folder = ResolveFolder(client, mailbox);
                await folder.OpenAsync(FolderAccess.ReadOnly, ct);

                var query = SearchQuery.All;

                if (!string.IsNullOrWhiteSpace(request.From))
                    query = query.And(SearchQuery.FromContains(request.From));

                if (!string.IsNullOrWhiteSpace(request.Subject))
                    query = query.And(SearchQuery.SubjectContains(request.Subject));

                if (request.Since.HasValue)
                    query = query.And(SearchQuery.DeliveredAfter(request.Since.Value));

                if (request.Before.HasValue)
                    query = query.And(SearchQuery.DeliveredBefore(request.Before.Value));

                if (request.UnseenOnly)
                    query = query.And(SearchQuery.NotSeen);

                var uids = await folder.SearchAsync(query, ct);

                var limit = request.Limit > 0 ? request.Limit : 20;
                var take = Math.Max(0, Math.Min(limit, uids.Count));
                var slice = take > 0
                    ? uids.Skip(uids.Count - take).ToList()
                    : new List<UniqueId>();

                if (slice.Count == 0)
                    return Array.Empty<MailMessageSummaryDto>();

                var summaries = await folder.FetchAsync(
                    slice,
                    MessageSummaryItems.Envelope
                        | MessageSummaryItems.Flags
                        | MessageSummaryItems.BodyStructure,
                    ct);

                var result = new List<MailMessageSummaryDto>(summaries.Count);
                foreach (var s in summaries)
                {
                    result.Add(MapSummary(s, mailbox));
                }

                _logger.LogInformation(
                    "Mail: search mailbox={Mailbox} results={Count}",
                    mailbox, result.Count);

                return (IReadOnlyList<MailMessageSummaryDto>)result;
            }, ct);
        }

        /// <inheritdoc />
        public async Task SendAsync(
            SendMailRequest request, int userId, CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.To == null || request.To.Count == 0)
                throw new ArgumentException("Не указан ни один получатель (To).", nameof(request));

            var creds = await _accountProvider.GetAsync(userId, ct);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                creds.FromDisplayName ?? "IIChatTools Agent",
                creds.FromAddress));

            foreach (var to in request.To)
                message.To.Add(MailboxAddress.Parse(to));
            foreach (var cc in request.Cc ?? Array.Empty<string>())
                message.Cc.Add(MailboxAddress.Parse(cc));
            foreach (var bcc in request.Bcc ?? Array.Empty<string>())
                message.Bcc.Add(MailboxAddress.Parse(bcc));

            message.Subject = request.Subject ?? string.Empty;

            var bodyBuilder = new BodyBuilder();
            if (request.IsHtml)
                bodyBuilder.HtmlBody = request.Body ?? string.Empty;
            else
                bodyBuilder.TextBody = request.Body ?? string.Empty;

            // Вложения — Фаза 4 (IMailAttachmentService). Сейчас не поддерживаются.
            if (request.Attachments != null && request.Attachments.Count > 0)
            {
                _logger.LogWarning(
                    "Mail: send requested with {Count} attachments, но вложения ещё не реализованы (Фаза 4). " +
                    "Письмо будет отправлено без вложений.",
                    request.Attachments.Count);
            }

            message.Body = bodyBuilder.ToMessageBody();

            using var smtp = new SmtpClient
            {
                Timeout = DefaultTimeoutMs
            };

            var smtpSecure = creds.SmtpUseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await smtp.ConnectAsync(creds.SmtpHost, creds.SmtpPort, smtpSecure, ct);
            await smtp.AuthenticateAsync(creds.Username, creds.Password, ct);

            try
            {
                await smtp.SendAsync(message, ct);

                _logger.LogInformation(
                    "Mail: sent recipients={Recipients} cc={Cc} bcc={Bcc} html={Html}",
                    request.To.Count,
                    request.Cc?.Count ?? 0,
                    request.Bcc?.Count ?? 0,
                    request.IsHtml);
            }
            finally
            {
                await smtp.DisconnectAsync(true, ct);
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(
            uint uid, string mailbox, int userId, CancellationToken ct = default)
        {
            await WithImapAsync(userId, async (client, _) =>
            {
                var folder = ResolveFolder(client, mailbox);
                await folder.OpenAsync(FolderAccess.ReadWrite, ct);

                // 1. Пометить как Deleted.
                await folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, silent: true, ct);

                // 2. Удалить (expunge).
                await folder.ExpungeAsync(new[] { new UniqueId(uid) }, ct);

                _logger.LogInformation(
                    "Mail: delete uid={Uid} mailbox={Mailbox}", uid, mailbox);

                return true;
            }, ct);
        }

        /// <inheritdoc />
        public async Task MoveAsync(
            uint uid, string fromMailbox, string toMailbox,
            int userId, CancellationToken ct = default)
        {
            await WithImapAsync(userId, async (client, _) =>
            {
                var source = ResolveFolder(client, fromMailbox);
                await source.OpenAsync(FolderAccess.ReadWrite, ct);

                var target = ResolveFolder(client, toMailbox);

                try
                {
                    await source.MoveToAsync(new UniqueId(uid), target, ct);
                }
                catch (NotSupportedException)
                {
                    // Fallback: MOVE не поддерживается — copy + delete.
                    await source.CopyToAsync(new UniqueId(uid), target, ct);
                    await source.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, silent: true, ct);
                    await source.ExpungeAsync(new[] { new UniqueId(uid) }, ct);
                }

                _logger.LogInformation(
                    "Mail: move uid={Uid} from={From} to={To}",
                    uid, fromMailbox, toMailbox);

                return true;
            }, ct);
        }

        /// <inheritdoc />
        public async Task MarkAsReadAsync(
            uint uid, string mailbox, int userId, CancellationToken ct = default)
        {
            await WithImapAsync(userId, async (client, _) =>
            {
                var folder = ResolveFolder(client, mailbox);
                await folder.OpenAsync(FolderAccess.ReadWrite, ct);

                await folder.AddFlagsAsync(
                    new UniqueId(uid),
                    MessageFlags.Seen,
                    silent: true,
                    ct);

                _logger.LogInformation(
                    "Mail: mark_as_read uid={Uid} mailbox={Mailbox}", uid, mailbox);

                return true;
            }, ct);
        }

        /// <inheritdoc />
        public async Task<bool> TestConnectionAsync(
            int userId, CancellationToken ct = default)
        {
            try
            {
                var creds = await _accountProvider.GetAsync(userId, ct);

                using var imap = new ImapClient { Timeout = 15_000 };

                var imapSecure = creds.ImapUseSsl
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTlsWhenAvailable;

                await imap.ConnectAsync(creds.ImapHost, creds.ImapPort, imapSecure, ct);
                await imap.AuthenticateAsync(creds.Username, creds.Password, ct);
                await imap.NoOpAsync(ct);
                await imap.DisconnectAsync(true, ct);

                _logger.LogInformation("Mail: test connection OK");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mail: test connection failed");
                return false;
            }
        }

        // ============================================================
        // Private helpers
        // ============================================================

        /// <summary>
        /// Открывает IMAP-соединение, выполняет операцию, корректно закрывает.
        /// </summary>
        private async Task<T> WithImapAsync<T>(
            int userId,
            Func<ImapClient, MailAccountCredentials, Task<T>> action,
            CancellationToken ct)
        {
            var creds = await _accountProvider.GetAsync(userId, ct);

            using var client = new ImapClient { Timeout = DefaultTimeoutMs };

            var secure = creds.ImapUseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(creds.ImapHost, creds.ImapPort, secure, ct);
            await client.AuthenticateAsync(creds.Username, creds.Password, ct);

            try
            {
                return await action(client, creds);
            }
            finally
            {
                try
                {
                    await client.DisconnectAsync(true, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Mail: IMAP disconnect error (ignored)");
                }
            }
        }

        /// <summary>
        /// Резолвит IMailFolder по имени (INBOX / Sent / Trash / ...).
        /// </summary>
        private static IMailFolder ResolveFolder(ImapClient client, string mailbox)
        {
            if (string.IsNullOrWhiteSpace(mailbox)
                || mailbox.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            {
                return client.Inbox;
            }

            // Special folders (imap host сам определяет, где Sent/Trash/Drafts).
            if (mailbox.Equals("Sent", StringComparison.OrdinalIgnoreCase))
                return client.GetFolder(SpecialFolder.Sent);
            if (mailbox.Equals("Trash", StringComparison.OrdinalIgnoreCase))
                return client.GetFolder(SpecialFolder.Trash);
            if (mailbox.Equals("Drafts", StringComparison.OrdinalIgnoreCase))
                return client.GetFolder(SpecialFolder.Drafts);
            if (mailbox.Equals("Archive", StringComparison.OrdinalIgnoreCase))
                return client.GetFolder(SpecialFolder.Archive);
            if (mailbox.Equals("Junk", StringComparison.OrdinalIgnoreCase))
                return client.GetFolder(SpecialFolder.Junk);

            // Fallback — по имени.
            return client.GetFolder(mailbox);
        }

        /// <summary>
        /// Маппит MessageSummary в MailMessageSummaryDto.
        /// </summary>
        private static MailMessageSummaryDto MapSummary(IMessageSummary s, string mailbox)
        {
            // v1.8.0 (KI-107, Фаза 2, fix2): IMessageSummary.Attachments возвращает
            // IEnumerable<BodyPartBasic>, у которого Count — extension-метод LINQ
            // (не свойство). Используем .Any() — универсально для IEnumerable/IList,
            // null-safe через ?. Список заполнен, если в FetchAsync запрошен
            // MessageSummaryItems.BodyStructure.
            var hasAttachments = s.Attachments?.Any() == true;

            return new MailMessageSummaryDto
            {
                Uid = (uint)s.UniqueId.Id,
                Mailbox = mailbox,
                From = s.Envelope?.From?.ToString() ?? string.Empty,
                Subject = s.Envelope?.Subject ?? string.Empty,
                Date = s.Envelope?.Date?.UtcDateTime ?? DateTime.MinValue,
                IsRead = s.Flags.HasValue && s.Flags.Value.HasFlag(MessageFlags.Seen),
                HasAttachments = hasAttachments
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            // Per-call connect → disconnect: постоянных ресурсов нет.
            // Освобождать нечего; сигнатура — для единообразия с Singleton-сервисами.
        }
    }
}