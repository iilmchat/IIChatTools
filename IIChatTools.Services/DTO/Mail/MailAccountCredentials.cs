namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Учётные данные почтового ящика (v1.8.0, KI-107).
    ///
    /// <para>
    /// v1.8.0: возвращается <c>GlobalMailAccountProvider</c> из <see cref="MailOptions"/>.
    /// v1.8.x: возвращается <c>PerUserMailAccountProvider</c> из таблицы UserMailAccount.
    /// </para>
    /// </summary>
    public class MailAccountCredentials
    {
        /// <summary>IMAP-хост.</summary>
        public string ImapHost { get; set; }

        /// <summary>IMAP-порт.</summary>
        public int ImapPort { get; set; }

        /// <summary>IMAP SSL.</summary>
        public bool ImapUseSsl { get; set; }

        /// <summary>SMTP-хост.</summary>
        public string SmtpHost { get; set; }

        /// <summary>SMTP-порт.</summary>
        public int SmtpPort { get; set; }

        /// <summary>SMTP SSL.</summary>
        public bool SmtpUseSsl { get; set; }

        /// <summary>Логин (email).</summary>
        public string Username { get; set; }

        /// <summary>Пароль (App Password).</summary>
        public string Password { get; set; }

        /// <summary>Адрес отправителя.</summary>
        public string FromAddress { get; set; }

        /// <summary>Отображаемое имя отправителя.</summary>
        public string FromDisplayName { get; set; }
    }
}