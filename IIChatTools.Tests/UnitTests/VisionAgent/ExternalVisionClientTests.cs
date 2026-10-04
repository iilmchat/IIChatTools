using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="ExternalVisionClient"/>
    /// (v1.12.0, KI-131, Ф5.3). Клиент — скелет, до KI-141.
    /// </summary>
    public class ExternalVisionClientTests
    {
        [Fact]
        public void IsReady_AlwaysFalse()
        {
            var client = new ExternalVisionClient();
            Assert.False(client.IsReady);
        }

        [Fact]
        public async Task DescribeAsync_ThrowsNotSupported()
        {
            var client = new ExternalVisionClient();
            var png = new byte[] { 1, 2, 3 };

            var ex = await Assert.ThrowsAsync<NotSupportedException>(
                () => client.DescribeAsync(png, CancellationToken.None));

            Assert.Contains("KI-141", ex.Message);
            Assert.Contains("multimodal", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}