using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Запрос на отправку письма.
    /// v1.8.0 (KI-107).
    /// </summary>
    public class SendMailRequest
    {
        /// <summary>Получатели (обязательно, ≥ 1).</summary>
        public IReadOnlyList<string> To { get; set; } = new List<string>();

        /// <summary>Копия.</summary>
        public IReadOnlyList<string> Cc { get; set; } = new List<string>();

        /// <summary>Скрытая копия.</summary>
        public IReadOnlyList<string> Bcc { get; set; } = new List<string>();

        /// <summary>Тема.</summary>
        public string Subject { get; set; }

        /// <summary>Тело письма (Markdown → HTML или plain text).</summary>
        public string Body { get; set; }

        /// <summary>Отправлять как HTML (по умолчанию false — plain text).</summary>
        public bool IsHtml { get; set; }

        /// <summary>Относительные пути вложений в workspace.</summary>
        public IReadOnlyList<string> Attachments { get; set; } = new List<string>();

        /// <summary>UID письма для "Re:" (опционально).</summary>
        public uint? ReplyToUid { get; set; }

        /// <summary>Папка письма для Reply-to (если ReplyToUid задан).</summary>
        public string ReplyToMailbox { get; set; }
    }
}