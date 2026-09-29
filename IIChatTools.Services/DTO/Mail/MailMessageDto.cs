using System;
using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Полное письмо (для read_email).
    /// v1.8.0 (KI-107).
    /// </summary>
    public class MailMessageDto
    {
        /// <summary>IMAP UID.</summary>
        public uint Uid { get; set; }

        /// <summary>Папка.</summary>
        public string Mailbox { get; set; }

        /// <summary>Отправитель.</summary>
        public string From { get; set; }

        /// <summary>Получатели (To).</summary>
        public IReadOnlyList<string> To { get; set; } = Array.Empty<string>();

        /// <summary>Копия (Cc).</summary>
        public IReadOnlyList<string> Cc { get; set; } = Array.Empty<string>();

        /// <summary>Тема.</summary>
        public string Subject { get; set; }

        /// <summary>Дата (UTC).</summary>
        public DateTime Date { get; set; }

        /// <summary>Тело в plain text.</summary>
        public string BodyText { get; set; }

        /// <summary>Тело в HTML (если есть).</summary>
        public string BodyHtml { get; set; }

        /// <summary>Вложения.</summary>
        public IReadOnlyList<MailAttachmentDto> Attachments { get; set; }
            = Array.Empty<MailAttachmentDto>();
    }
}