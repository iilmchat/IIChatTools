using System.Collections.Generic;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionWhitelistValidator"/> (v1.12.0, KI-131, Ф2.3).
    /// </summary>
    public class VisionWhitelistValidatorTests
    {
        private static VisionWhitelistOptions Options(
            IEnumerable<string> domains = null,
            IEnumerable<string> denied = null,
            bool allowAny = false)
        {
            return new VisionWhitelistOptions
            {
                Domains = new List<string>(domains ?? new[] { "rzd.ru", "*.wikipedia.org", "example.com" }),
                DeniedDomains = new List<string>(denied ?? new[] { "*.sberbank.ru" }),
                AllowAnyDomain = allowAny
            };
        }

        // ============ 1. Точный матч ============

        [Theory]
        [InlineData("https://rzd.ru/tickets")]
        [InlineData("https://rzd.ru/")]
        [InlineData("http://rzd.ru")]
        [InlineData("https://rzd.ru:443/path?query=1")]
        public void IsAllowed_ExactDomain_ReturnsTrue(string url)
        {
            var (ok, _) = (VisionWhitelistValidator.IsAllowed(url, Options(), out var err), err);
            Assert.True(ok, $"URL {url} должен быть разрешён. Error: {err}");
        }

        // ============ 2. Wildcard-поддомен ============

        [Theory]
        [InlineData("https://en.wikipedia.org/wiki/Moscow")]
        [InlineData("https://ru.wikipedia.org/")]
        [InlineData("https://a.b.wikipedia.org/x")]
        public void IsAllowed_WildcardSubdomain_ReturnsTrue(string url)
        {
            Assert.True(VisionWhitelistValidator.IsAllowed(url, Options(), out _));
        }

        // ============ 3. Wildcard не матчит голый домен ============

        [Fact]
        public void IsAllowed_WildcardDoesNotMatchBareDomain_ReturnsFalse()
        {
            // *.wikipedia.org НЕ должно матчить wikipedia.org.
            Assert.False(VisionWhitelistValidator.IsAllowed(
                "https://wikipedia.org/", Options(), out _));
        }

        // ============ 4. Whitelist-поддомен для exact-домена ============

        [Fact]
        public void IsAllowed_SubdomainOfExactDomain_ReturnsFalse()
        {
            // rzd.ru в whitelist → www.rzd.ru НЕ должен матчиться (нет *.rzd.ru).
            Assert.False(VisionWhitelistValidator.IsAllowed(
                "https://www.rzd.ru/", Options(), out _));
        }

        // ============ 5. Домен не в whitelist ============

        [Fact]
        public void IsAllowed_UnknownDomain_ReturnsFalse()
        {
            Assert.False(VisionWhitelistValidator.IsAllowed(
                "https://evil.com/", Options(), out var error));
            Assert.Contains("не в whitelist", error);
        }

        // ============ 6. DeniedDomains имеет приоритет ============

        [Fact]
        public void IsAllowed_DeniedDomain_ReturnsFalse()
        {
            Assert.False(VisionWhitelistValidator.IsAllowed(
                "https://online.sberbank.ru/", Options(), out var error));
            Assert.Contains("чёрном списке", error);
        }

        [Fact]
        public void IsAllowed_DeniedOverridesAllowAny_ReturnsFalse()
        {
            // AllowAnyDomain=true, но DeniedDomains перебивает.
            var opts = Options(allowAny: true);
            Assert.False(VisionWhitelistValidator.IsAllowed(
                "https://online.sberbank.ru/", opts, out _));
            Assert.True(VisionWhitelistValidator.IsAllowed(
                "https://anything.com/", opts, out _));
        }

        // ============ 7. Схема не http/https ============

        [Theory]
        [InlineData("ftp://rzd.ru")]
        [InlineData("file:///C:/test.txt")]
        [InlineData("chrome://settings")]
        public void IsAllowed_NonHttpScheme_ReturnsFalse(string url)
        {
            Assert.False(VisionWhitelistValidator.IsAllowed(url, Options(), out var error));
            Assert.Contains("http/https", error);
        }

        // ============ 8. Невалидный / пустой URL ============

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-url")]
        public void IsAllowed_InvalidUrl_ReturnsFalse(string url)
        {
            Assert.False(VisionWhitelistValidator.IsAllowed(url, Options(), out _));
        }

        // ============ 9. Case-insensitive ============

        [Theory]
        [InlineData("https://RZD.RU/tickets")]
        [InlineData("https://rzd.RU/")]
        public void IsAllowed_CaseInsensitive_ReturnsTrue(string url)
        {
            Assert.True(VisionWhitelistValidator.IsAllowed(url, Options(), out _));
        }

        // ============ 10. Null whitelist ============

        [Fact]
        public void IsAllowed_NullWhitelist_ReturnsFalse()
        {
            Assert.False(VisionWhitelistValidator.IsAllowed(
                "https://rzd.ru/", null, out var error));
            Assert.Contains("не заданы", error);
        }
    }
}