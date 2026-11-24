using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;

namespace IIChatTools.Services.Implementation.Mail
{
    /// <summary>
    /// D08 / KI-108: провайдер почтовых учётных данных для конкретного
    /// пользователя. Читает секцию <c>Mail:Users:{userId}</c> из конфигурации
    /// (User Secrets / env), при отсутствии — delegate к глобальному провайдеру.
    /// <para>
    /// Использует индексер конфигурации (RULES § 4.56) — без GetValue/GetSection.Get.
    /// Пароли не логируются. Хранение в БД с IDataProtector — отдельный этап
    /// (требует миграции и key ring).
    /// </para>
    /// </summary>
    public sealed class PerUserMailAccountProvider : IMailAccountProvider
    {
        private readonly IConfiguration _configuration;
        private readonly IMailAccountProvider _global;

        public PerUserMailAccountProvider(
            IConfiguration configuration,
            IMailAccountProvider global)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _global = global ?? throw new ArgumentNullException(nameof(global));
        }

        /// <inheritdoc />
        public Task<MailAccountCredentials> GetAsync(
            int userId, CancellationToken ct = default)
        {
            var prefix = $"Mail:Users:{userId}:";
            var imapHost = _configuration[prefix + "Imap:Host"];

            if (string.IsNullOrWhiteSpace(imapHost))
                return _global.GetAsync(userId, ct);   // fallback на общий ящик

            var smtpHost = _configuration[prefix + "Smtp:Host"] ?? imapHost;
            var username = _configuration[prefix + "Imap:Username"];
            var password = _configuration[prefix + "Imap:Password"];
            var fromAddress = _configuration[prefix + "FromAddress"] ?? username;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    $"Mail:Users:{userId}: не заданы Imap:Username / Imap:Password.");

            return Task.FromResult(new MailAccountCredentials
            {
                ImapHost = imapHost,
                ImapPort = ReadInt(_configuration[prefix + "Imap:Port"], 993),
                ImapUseSsl = ReadBool(_configuration[prefix + "Imap:UseSsl"], true),
                SmtpHost = smtpHost,
                SmtpPort = ReadInt(_configuration[prefix + "Smtp:Port"], 465),
                SmtpUseSsl = ReadBool(_configuration[prefix + "Smtp:UseSsl"], true),
                Username = username,
                Password = password,
                FromAddress = fromAddress,
                FromDisplayName = _configuration[prefix + "FromDisplayName"] ?? "IIChatTools Agent"
            });
        }

        private static int ReadInt(string raw, int fallback)
            => int.TryParse(raw, out var v) ? v : fallback;

        private static bool ReadBool(string raw, bool fallback)
            => bool.TryParse(raw, out var v) ? v : fallback;
    }
}
