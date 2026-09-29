namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Настройки одного endpoint (IMAP или SMTP).
    /// v1.8.0 (KI-107).
    ///
    /// <para>
    /// <b>Важно:</b> <see cref="Username"/> / <see cref="Password"/> — секреты.
    /// Хранятся в User Secrets / env (<c>Mail:Imap:Username</c>, <c>Mail:Imap:Password</c>),
    /// не в appsettings.json.
    /// </para>
    /// </summary>
    public class MailEndpointOptions
    {
        /// <summary>Хост (imap.gmail.com, smtp.yandex.ru, ...).</summary>
        public string Host { get; set; }

        /// <summary>Порт (993 для IMAP SSL, 465 для SMTP SSL).</summary>
        public int Port { get; set; }

        /// <summary>Использовать SSL/TLS.</summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>Таймаут подключения (секунды).</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>Логин (обычно email). Секрет — User Secrets / env.</summary>
        public string Username { get; set; }

        /// <summary>Пароль (App Password). Секрет — User Secrets / env.</summary>
        public string Password { get; set; }
    }
}