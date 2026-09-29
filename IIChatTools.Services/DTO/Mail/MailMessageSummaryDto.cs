using System;

namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Краткая информация о письме (для list_emails / search_emails).
    /// v1.8.0 (KI-107).
    /// </summary>
    public class MailMessageSummaryDto
    {
        /// <summary>IMAP UID (уникален в папке).</summary>
        public uint Uid { get; set; }

        /// <summary>Папка (INBOX, Sent, ...).</summary>
        public string Mailbox { get; set; }

        /// <summary>Отправитель (email или "Имя &lt;email&gt;").</summary>
        public string From { get; set; }

        /// <summary>Тема.</summary>
        public string Subject { get; set; }

        /// <summary>Дата (UTC).</summary>
        public DateTime Date { get; set; }

        /// <summary>Прочитано?</summary>
        public bool IsRead { get; set; }

        /// <summary>Есть вложения?</summary>
        public bool HasAttachments { get; set; }
    }
}