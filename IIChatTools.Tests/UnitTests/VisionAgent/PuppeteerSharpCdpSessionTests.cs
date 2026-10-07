using System;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Тесты <see cref="PuppeteerSharpCdpSession"/> (KI-161).
    /// <para>
    /// Большинство тестов требует реального Chrome с
    /// <c>--remote-debugging-port=9222</c> → помечены
    /// <c>[Fact(Skip=...)]</c>. Non-Skip тесты проверяют только
    /// граничные случаи (пустой URL, not-connected).
    /// </para>
    /// </summary>
    public class PuppeteerSharpCdpSessionTests
    {
        // ============================================================
        // Non-Skip (без Chrome)
        // ============================================================

        [Fact]
        public async Task ConnectAsync_EmptyUrl_ReturnsFalse()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);

            var connected = await session.ConnectAsync(string.Empty);

            Assert.False(connected);
            Assert.False(session.IsConnected);
        }

        [Fact]
        public async Task ConnectAsync_WhitespaceUrl_ReturnsFalse()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);

            var connected = await session.ConnectAsync("   ");

            Assert.False(connected);
        }

        [Fact]
        public async Task FindElementAsync_NotConnected_ReturnsNotFound()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);

            var result = await session.FindElementAsync(new CdpElementQuery
            {
                Label = "Найти",
                Type = "button"
            });

            Assert.False(result.Found);
            Assert.Contains("не подключён", result.Error);
        }

        [Fact]
        public async Task GetViewportInfoAsync_NotConnected_ReturnsDefaults()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);

            var info = await session.GetViewportInfoAsync();

            Assert.NotNull(info);
            Assert.Equal(1.0, info.DevicePixelRatio);
            Assert.Equal(0, info.WindowScreenX);
            Assert.Equal(0, info.WindowScreenY);
        }

        [Fact]
        public async Task DisposeAsync_Idempotent_NoThrow()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);

            await session.DisposeAsync();
            await session.DisposeAsync();   // второй раз — OK
        }

        [Fact]
        public async Task FindElementAsync_NullQuery_Throws()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => session.FindElementAsync(null));
        }

        // ============================================================
        // Skip (требуют реального Chrome)
        // ============================================================

        [Fact(Skip = "Требует Chrome с --remote-debugging-port=9222 и открытой страницей.")]
        public async Task ConnectAsync_RealChrome_Connects()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);

            var connected = await session.ConnectAsync("http://127.0.0.1:9222");

            Assert.True(connected);
            Assert.True(session.IsConnected);
        }

        [Fact(Skip = "Требует Chrome с открытой страницей, содержащей <button>.")]
        public async Task FindElementAsync_RealChrome_FindsButtonByLabel()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);
            await session.ConnectAsync("http://127.0.0.1:9222");

            var result = await session.FindElementAsync(new CdpElementQuery
            {
                Label = "Найти",
                Type = "button"
            });

            Assert.True(result.Found);
            Assert.True(result.ViewportX > 0);
            Assert.True(result.ViewportY > 0);
        }

        [Fact(Skip = "Требует Chrome с открытой страницей.")]
        public async Task GetViewportInfoAsync_RealChrome_ReturnsDpr()
        {
            var session = new PuppeteerSharpCdpSession(
                NullLogger<PuppeteerSharpCdpSession>.Instance);
            await session.ConnectAsync("http://127.0.0.1:9222");

            var info = await session.GetViewportInfoAsync();

            Assert.True(info.DevicePixelRatio >= 1.0);
        }
    }
}