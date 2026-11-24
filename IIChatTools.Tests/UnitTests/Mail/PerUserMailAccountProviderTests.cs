using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Implementation.Mail;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Mail
{
    /// <summary>D08 / KI-108: тесты per-user почтового провайдера.</summary>
    public class PerUserMailAccountProviderTests
    {
        private sealed class StubGlobal : IMailAccountProvider
        {
            public Task<MailAccountCredentials> GetAsync(int userId, CancellationToken ct = default)
                => Task.FromResult(new MailAccountCredentials { Username = "global@example.com" });
        }

        private static PerUserMailAccountProvider CreateProvider(Dictionary<string, string> values)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            return new PerUserMailAccountProvider(config, new StubGlobal());
        }

        [Fact]
        public async Task GetAsync_UserConfigured_ReturnsUserCredentials()
        {
            var provider = CreateProvider(new Dictionary<string, string>
            {
                ["Mail:Users:42:Imap:Host"] = "imap.user.example",
                ["Mail:Users:42:Imap:Username"] = "user42@example.com",
                ["Mail:Users:42:Imap:Password"] = "secret42",
                ["Mail:Users:42:Smtp:Host"] = "smtp.user.example"
            });

            var creds = await provider.GetAsync(42);

            Assert.Equal("imap.user.example", creds.ImapHost);
            Assert.Equal("smtp.user.example", creds.SmtpHost);
            Assert.Equal("user42@example.com", creds.Username);
            Assert.Equal("secret42", creds.Password);
            Assert.Equal(993, creds.ImapPort);
            Assert.True(creds.ImapUseSsl);
        }

        [Fact]
        public async Task GetAsync_UserNotConfigured_FallsBackToGlobal()
        {
            var provider = CreateProvider(new Dictionary<string, string>());

            var creds = await provider.GetAsync(7);

            Assert.Equal("global@example.com", creds.Username);
        }
    }
}
