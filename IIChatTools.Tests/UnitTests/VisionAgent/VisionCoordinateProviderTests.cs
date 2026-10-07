using System;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Тесты <see cref="VisionCoordinateProvider"/> (KI-161).
    /// Fallback bounds-center (KI-190).
    /// </summary>
    public class VisionCoordinateProviderTests
    {
        [Fact]
        public void Name_IsVision()
        {
            var provider = new VisionCoordinateProvider();
            Assert.Equal("vision", provider.Name);
        }

        [Fact]
        public async Task ResolveAsync_ValidBounds_ReturnsCenter()
        {
            var provider = new VisionCoordinateProvider();
            var request = new CoordinateRequest
            {
                TargetId = "search_button",
                TargetBounds = new UiElementBoundsDto
                {
                    X = 100, Y = 200, W = 80, H = 40
                }
            };

            var result = await provider.ResolveAsync(request);

            Assert.True(result.Found);
            Assert.False(result.FromDom);
            Assert.Equal(140, result.X);   // 100 + 80/2
            Assert.Equal(220, result.Y);   // 200 + 40/2
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task ResolveAsync_NullBounds_ReturnsNotFound()
        {
            var provider = new VisionCoordinateProvider();
            var request = new CoordinateRequest
            {
                TargetId = "x",
                TargetBounds = null
            };

            var result = await provider.ResolveAsync(request);

            Assert.False(result.Found);
            Assert.False(result.FromDom);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task ResolveAsync_ZeroWidthBounds_ReturnsNotFound()
        {
            var provider = new VisionCoordinateProvider();
            var request = new CoordinateRequest
            {
                TargetId = "x",
                TargetBounds = new UiElementBoundsDto { X = 10, Y = 10, W = 0, H = 0 }
            };

            var result = await provider.ResolveAsync(request);

            Assert.False(result.Found);
        }

        [Fact]
        public async Task ResolveAsync_NullRequest_Throws()
        {
            var provider = new VisionCoordinateProvider();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => provider.ResolveAsync(null));
        }
    }
}