using System;
using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionMouseCoordinates"/> (v1.12.0, KI-131, Ф2.5).
    /// </summary>
    public class VisionMouseCoordinatesTests
    {
        [Fact]
        public void NormalizeToAbsolute_TopLeft_Returns00()
        {
            var (nx, ny) = VisionMouseCoordinates.NormalizeToAbsolute(0, 0, 1920, 1080);
            Assert.Equal(0, nx);
            Assert.Equal(0, ny);
        }

        [Fact]
        public void NormalizeToAbsolute_BottomRight_ReturnsMax()
        {
            var (nx, ny) = VisionMouseCoordinates.NormalizeToAbsolute(1919, 1079, 1920, 1080);
            Assert.Equal(65535, nx);
            Assert.Equal(65535, ny);
        }

        [Fact]
        public void NormalizeToAbsolute_Center_ReturnsMidpoint()
        {
            var (nx, ny) = VisionMouseCoordinates.NormalizeToAbsolute(960, 540, 1920, 1080);
            // Приблизительно середина (не точно 32767 из-за (screen-1)).
            Assert.InRange(nx, 32700, 32800);
            Assert.InRange(ny, 32700, 32800);
        }

        [Theory]
        [InlineData(-1, 0, 1920, 1080)]
        [InlineData(0, -1, 1920, 1080)]
        public void NormalizeToAbsolute_Negative_Throws(int x, int y, int w, int h)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => VisionMouseCoordinates.NormalizeToAbsolute(x, y, w, h));
        }

        [Theory]
        [InlineData(1920, 0, 1920, 1080)]
        [InlineData(0, 1080, 1920, 1080)]
        public void NormalizeToAbsolute_OutsideScreen_Throws(int x, int y, int w, int h)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => VisionMouseCoordinates.NormalizeToAbsolute(x, y, w, h));
        }

        [Theory]
        [InlineData(100, 100, 1, 1080)]
        [InlineData(100, 100, 1920, 1)]
        [InlineData(100, 100, 0, 0)]
        public void NormalizeToAbsolute_InvalidScreen_Throws(int x, int y, int w, int h)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => VisionMouseCoordinates.NormalizeToAbsolute(x, y, w, h));
        }

        [Theory]
        [InlineData(0, 0, 2560, 1440, 0, 0)]
        [InlineData(2559, 1439, 2560, 1440, 65535, 65535)]
        public void NormalizeToAbsolute_4K_ReturnsExpected(
            int x, int y, int w, int h, int expectedX, int expectedY)
        {
            var (nx, ny) = VisionMouseCoordinates.NormalizeToAbsolute(x, y, w, h);
            Assert.Equal(expectedX, nx);
            Assert.Equal(expectedY, ny);
        }

        [Fact]
        public void NormalizeToAbsolute_QuarterScreen_ReturnsQuarter()
        {
            var (nx, _) = VisionMouseCoordinates.NormalizeToAbsolute(480, 0, 1920, 1080);
            // 480 / 1919 * 65535 ≈ 16388.
            Assert.InRange(nx, 16300, 16450);
        }

        [Fact]
        public void NormalizeToAbsolute_SinglePixelScreen_Throws()
        {
            // screenWidth = 2 → (2-1) = 1 → x=0 OK, x=1 = 65535.
            var (nx, ny) = VisionMouseCoordinates.NormalizeToAbsolute(0, 0, 2, 2);
            Assert.Equal(0, nx);
            Assert.Equal(0, ny);

            var (nx2, ny2) = VisionMouseCoordinates.NormalizeToAbsolute(1, 1, 2, 2);
            Assert.Equal(65535, nx2);
            Assert.Equal(65535, ny2);
        }
    }
}