using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Implementation.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Mail
{
    /// <summary>
    /// Smoke-тесты для Mail Agent, Фаза 2 (v1.8.0, KI-107).
    ///
    /// <para>
    /// Реальные IMAP/SMTP-соединения не делаются — тесты проверяют
    /// конфигурацию и поведение провайдера creds.
    /// </para>
    /// </summary>
    public class MailKitClientSmokeTests
    {
        /// <summary>
        /// Создаёт MailOptions с заполненными полями.
        /// </summary>
        private static MailOptions CreateValidOptions()
        {
            return new MailOptions
            {
                Enabled = true,
                FromAddress = "test@yandex.ru",
                FromDisplayName = "Test Agent",
                Imap = new MailEndpointOptions
                {
                    Host = "imap.yandex.ru",
                    Port = 993,
                    UseSsl = true,
                    Username = "test@yandex.ru",
                    Password = "app-password"
                },
                Smtp = new MailEndpointOptions
                {
                    Host = "smtp.yandex.ru",
                    Port = 465,
                    UseSsl = true
                }
            };
        }

        [Fact]
        public async Task GlobalMailAccountProvider_ValidOptions_ReturnsCredentials()
        {
            // Arrange
            var options = CreateValidOptions();
            var provider = new GlobalMailAccountProvider(
                Options.Create(options));

            // Act
            var creds = await provider.GetAsync(userId: 1);

            // Assert
            Assert.NotNull(creds);
            Assert.Equal("imap.yandex.ru", creds.ImapHost);
            Assert.Equal(993, creds.ImapPort);
            Assert.True(creds.ImapUseSsl);
            Assert.Equal("smtp.yandex.ru", creds.SmtpHost);
            Assert.Equal(465, creds.SmtpPort);
            Assert.Equal("test@yandex.ru", creds.Username);
            Assert.Equal("app-password", creds.Password);
            Assert.Equal("test@yandex.ru", creds.FromAddress);
            Assert.Equal("Test Agent", creds.FromDisplayName);
        }

        [Fact]
        public async Task GlobalMailAccountProvider_Disabled_Throws()
        {
            // Arrange
            var options = CreateValidOptions();
            options.Enabled = false;
            var provider = new GlobalMailAccountProvider(Options.Create(options));

            // Act + Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetAsync(userId: 1));

            Assert.Contains("Mail Agent отключён", ex.Message);
            Assert.Contains("Mail:Enabled", ex.Message);
        }

        [Fact]
        public async Task MailKitClient_TestConnection_InvalidHost_ReturnsFalse()
        {
            // Arrange: невалидный хост → MailKit бросит SocketException внутри,
            // метод должен вернуть false (не бросать наружу).
            var options = new MailOptions
            {
                Enabled = true,
                FromAddress = "test@example.com",
                Imap = new MailEndpointOptions
                {
                    Host = "invalid-host-that-does-not-exist-xyz123.local",
                    Port = 993,
                    UseSsl = true,
                    Username = "x",
                    Password = "y"
                },
                Smtp = new MailEndpointOptions
                {
                    Host = "smtp.example.com",
                    Port = 465,
                    UseSsl = true
                }
            };

            var provider = new GlobalMailAccountProvider(Options.Create(options));
            var client = new MailKitClient(provider, NullLogger<MailKitClient>.Instance);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Act
            var result = await client.TestConnectionAsync(userId: 1, cts.Token);

            // Assert
            Assert.False(result);
        }
    }
}