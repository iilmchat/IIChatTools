using System;
using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionImageResizer"/> (v1.12.0, KI-131, Ф2.8).
    /// Тесты <see cref="VisionImageResizer.CalculateTargetSize"/> — кроссплатформенные.
    /// Тесты <see cref="VisionImageResizer.Resize"/> — runtime-skip на не-Windows
    /// (CI ubuntu-latest).
    /// </summary>
    public class VisionImageResizerTests
    {
        // ============ CalculateTargetSize — кроссплатформенные ============

        [Fact]
        public void CalculateTargetSize_SmallerThanMax_ReturnsOriginal()
        {
            var (w, h) = VisionImageResizer.CalculateTargetSize(800, 600, 1024, 768);
            Assert.Equal(800, w);
            Assert.Equal(600, h);
        }

        [Fact]
        public void CalculateTargetSize_ExactMax_ReturnsOriginal()
        {
            var (w, h) = VisionImageResizer.CalculateTargetSize(1024, 768, 1024, 768);
            Assert.Equal(1024, w);
            Assert.Equal(768, h);
        }

        [Fact]
        public void CalculateTargetSize_WideImage_ScalesByWidth()
        {
            // 1920×1080 → max 1024×768: scaleW = 1024/1920 ≈ 0.533 → 1024×576.
            var (w, h) = VisionImageResizer.CalculateTargetSize(1920, 1080, 1024, 768);
            Assert.Equal(1024, w);
            Assert.Equal(576, h);
        }

        [Fact]
        public void CalculateTargetSize_TallImage_ScalesByHeight()
        {
            // 1080×1920 → max 1024×768: scaleH = 768/1920 = 0.4 → 432×768.
            var (w, h) = VisionImageResizer.CalculateTargetSize(1080, 1920, 1024, 768);
            Assert.Equal(432, w);
            Assert.Equal(768, h);
        }

        [Fact]
        public void CalculateTargetSize_SquareImage_ScalesToMinDimension()
        {
            // 2000×2000 → max 1024×768: scale = min(1024/2000, 768/2000) = 0.384 → 768×768.
            var (w, h) = VisionImageResizer.CalculateTargetSize(2000, 2000, 1024, 768);
            Assert.Equal(768, w);
            Assert.Equal(768, h);
        }

        [Fact]
        public void CalculateTargetSize_UltraWide_PreservesAspectRatio()
        {
            // 3840×1080 (4K UW) → max 1024×768: scaleW = 0.2667 → 1024×288.
            var (w, h) = VisionImageResizer.CalculateTargetSize(3840, 1080, 1024, 768);
            Assert.Equal(1024, w);
            Assert.Equal(288, h);
        }

        [Theory]
        [InlineData(0, 600, 1024, 768)]
        [InlineData(800, 0, 1024, 768)]
        [InlineData(800, 600, 0, 768)]
        [InlineData(800, 600, 1024, 0)]
        [InlineData(-1, 600, 1024, 768)]
        [InlineData(800, -1, 1024, 768)]
        public void CalculateTargetSize_NonPositive_Throws(
            int w, int h, int maxW, int maxH)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => VisionImageResizer.CalculateTargetSize(w, h, maxW, maxH));
        }

        [Fact]
        public void CalculateTargetSize_ExtremeSmallDimensions_AtLeastOne()
        {
            // 10000×1 → max 1024×768: scaleW = 0.1024 → 1024×1 (не 0).
            var (w, h) = VisionImageResizer.CalculateTargetSize(10000, 1, 1024, 768);
            Assert.Equal(1024, w);
            Assert.Equal(1, h);
        }

        // ============ Resize — Windows-only (runtime-skip в CI) ============

        [Fact]
        public void Resize_NotWindows_Skipped()
        {
            if (!OperatingSystem.IsWindows())
            {
                // Пропускаем на Linux/macOS (CI ubuntu-latest).
                // OperatingSystem.IsWindows() распознаётся анализатором CA1416
                // как guard — без него был бы warning на вызове Resize(...).
                return;
            }

            // На Windows — создаём PNG 4×4, downscale до 2×2.
            var png = CreatePng(4, 4);
            var resized = VisionImageResizer.Resize(png, 2, 2);
            Assert.NotNull(resized);
            Assert.True(resized.Length > 0);
            Assert.True(resized.Length <= png.Length * 2);   // sanity
        }

        [Fact]
        public void Resize_SmallerThanMax_ReturnsSameInstance()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var png = CreatePng(2, 2);
            var resized = VisionImageResizer.Resize(png, 100, 100);
            // Возвращает тот же массив — размеры не изменились.
            Assert.Same(png, resized);
        }

        [Fact]
        public void Resize_NullOrEmpty_Throws()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // CA1416: OperatingSystem.IsWindows() guard не проникает внутрь
            // лямбд Assert.Throws — анализатор не отслеживает поток в лямбду.
            // Guard уже выполнен выше, pragma защищает только два assertion'а.
#pragma warning disable CA1416
            Assert.Throws<ArgumentNullException>(
                () => VisionImageResizer.Resize(null, 100, 100));
            Assert.Throws<ArgumentException>(
                () => VisionImageResizer.Resize(Array.Empty<byte>(), 100, 100));
#pragma warning restore CA1416
        }

        /// <summary>
        /// Создаёт PNG заданного размера (Windows-only).
        /// </summary>
        private static byte[] CreatePng(int width, int height)
        {
#pragma warning disable CA1416   // Bitmap — Windows-only, вызов защищён выше.
            using var bmp = new System.Drawing.Bitmap(width, height);
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
#pragma warning restore CA1416
        }
    }
}