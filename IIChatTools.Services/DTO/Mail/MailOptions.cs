using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Настройки Mail Agent (bind из секции <c>Mail</c> appsettings.json).
    /// v1.8.0 (KI-107).
    /// </summary>
    public class MailOptions
    {
        /// <summary>Глобальный переключатель. Если false — mail_agent не регистрируется.</summary>
        public bool Enabled { get; set; }

        /// <summary>Папка по умолчанию для операций (обычно INBOX).</summary>
        public string DefaultMailbox { get; set; } = "INBOX";

        /// <summary>Настройки IMAP-подключения.</summary>
        public MailEndpointOptions Imap { get; set; } = new MailEndpointOptions();

        /// <summary>Настройки SMTP-подключения.</summary>
        public MailEndpointOptions Smtp { get; set; } = new MailEndpointOptions();

        /// <summary>Адрес отправителя (From).</summary>
        public string FromAddress { get; set; }

        /// <summary>Отображаемое имя отправителя.</summary>
        public string FromDisplayName { get; set; } = "IIChatTools Agent";

        /// <summary>Настройки вложений.</summary>
        public MailAttachmentsOptions Attachments { get; set; } = new MailAttachmentsOptions();

        /// <summary>Настройки поиска.</summary>
        public MailSearchOptions Search { get; set; } = new MailSearchOptions();

        /// <summary>Разрешённые папки (whitelist). Пустой — fallback на ["INBOX"].</summary>
        public List<string> AllowedMailboxes { get; set; } = new List<string>();
    }
}