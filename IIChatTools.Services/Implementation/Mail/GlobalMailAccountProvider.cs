using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.Mail
{
    /// <summary>
    /// Глобальный провайдер учётных данных почтового ящика (v1.8.0, KI-107).
    ///
    /// <para>
    /// Все пользователи работают с <b>одним ящиком</b> — creds берутся
    /// из <see cref="MailOptions"/> (секция <c>Mail</c> appsettings + User Secrets).
    /// </para>
    ///
    /// <para>
    /// <b>Singleton.</b> В v1.8.x (KI-108) будет заменён на
    /// <c>PerUserMailAccountProvider</c> — 1 строка в DI.
    /// </para>
    /// </summary>
    public sealed class GlobalMailAccountProvider : IMailAccountProvider
    {
        private readonly MailOptions _options;

        /// <summary>
        /// Создаёт провайдер.
        /// </summary>
        /// <param name="options">Опции Mail Agent (bind из appsettings:Mail)</param>
        /// <exception cref="ArgumentNullException">Если options равен null</exception>
        public GlobalMailAccountProvider(IOptions<MailOptions> options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _options = options.Value;
        }

        /// <inheritdoc />
        public Task<MailAccountCredentials> GetAsync(
            int userId, CancellationToken ct = default)
        {
            // Fail-fast, если агент выключен (не должно происходить —
            // mail_agent регистрируется только при Mail:Enabled = true).
            if (!_options.Enabled)
            {
                throw new InvalidOperationException(
                    "Mail Agent отключён (Mail:Enabled = false). " +
                    "Включите в User Secrets: dotnet user-secrets set \"Mail:Enabled\" \"true\".");
            }

            // Критичные проверки конфигурации (DESIGN § 5.6).
            if (_options.Imap == null || string.IsNullOrWhiteSpace(_options.Imap.Host))
            {
                throw new InvalidOperationException(
                    "Mail:Imap:Host не задан. " +
                    "Задайте в User Secrets: dotnet user-secrets set \"Mail:Imap:Host\" \"imap.yandex.ru\".");
            }

            if (_options.Smtp == null || string.IsNullOrWhiteSpace(_options.Smtp.Host))
            {
                throw new InvalidOperationException(
                    "Mail:Smtp:Host не задан. " +
                    "Задайте в User Secrets: dotnet user-secrets set \"Mail:Smtp:Host\" \"smtp.yandex.ru\".");
            }

            if (string.IsNullOrWhiteSpace(_options.FromAddress))
            {
                throw new InvalidOperationException(
                    "Mail:FromAddress не задан. " +
                    "Задайте в User Secrets: dotnet user-secrets set \"Mail:FromAddress\" \"you@yandex.ru\".");
            }

            // Username/Password — общие для IMAP и SMTP (App Password).
            // Если заданы раздельно — приоритет у IMAP:Username / IMAP:Password.
            var username = !string.IsNullOrWhiteSpace(_options.Imap.Username)
                ? _options.Imap.Username
                : _options.Smtp.Username;

            var password = !string.IsNullOrWhiteSpace(_options.Imap.Password)
                ? _options.Imap.Password
                : _options.Smtp.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "Mail:Imap:Username / Mail:Imap:Password не заданы. " +
                    "Задайте в User Secrets (App Password): " +
                    "dotnet user-secrets set \"Mail:Imap:Password\" \"<app-password>\".");
            }

            var creds = new MailAccountCredentials
            {
                ImapHost = _options.Imap.Host,
                ImapPort = _options.Imap.Port,
                ImapUseSsl = _options.Imap.UseSsl,
                SmtpHost = _options.Smtp.Host,
                SmtpPort = _options.Smtp.Port,
                SmtpUseSsl = _options.Smtp.UseSsl,
                Username = username,
                Password = password,
                FromAddress = _options.FromAddress,
                FromDisplayName = string.IsNullOrWhiteSpace(_options.FromDisplayName)
                    ? "IIChatTools Agent"
                    : _options.FromDisplayName
            };

            return Task.FromResult(creds);
        }
    }
}