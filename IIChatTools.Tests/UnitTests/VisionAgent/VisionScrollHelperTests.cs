using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionScrollHelper"/> (v1.12.0, KI-131, Ф2.7).
    /// </summary>
    public class VisionScrollHelperTests
    {
        // ============ Clamp ============

        [Theory]
        [InlineData(0, 2000, 0)]
        [InlineData(120, 2000, 120)]         // Один щелчок вниз.
        [InlineData(-120, 2000, -120)]       // Один щелчок вверх.
        [InlineData(2000, 2000, 2000)]       // Ровно на границе.
        [InlineData(-2000, 2000, -2000)]
        [InlineData(5000, 2000, 2000)]       // Clamp верх.
        [InlineData(-5000, 2000, -2000)]     // Clamp низ.
        public void Clamp_WithinOrOutside_ReturnsExpected(int deltaY, int maxAbs, int expected)
        {
            Assert.Equal(expected, VisionScrollHelper.Clamp(deltaY, maxAbs));
        }

        [Theory]
        [InlineData(500, 0, 1)]              // maxAbs <= 0 → fallback к 1.
        [InlineData(-500, 0, -1)]
        [InlineData(500, -100, 1)]
        [InlineData(0, 0, 0)]                // 0 остаётся 0.
        public void Clamp_InvalidMaxAbs_FallsBackToOne(int deltaY, int maxAbs, int expected)
        {
            Assert.Equal(expected, VisionScrollHelper.Clamp(deltaY, maxAbs));
        }

        // ============ ToWheelMouseData (инверсия знака) ============

        [Theory]
        [InlineData(0, 0)]
        [InlineData(120, -120)]              // User scroll down → Win32 -120.
        [InlineData(-120, 120)]              // User scroll up → Win32 +120.
        [InlineData(360, -360)]              // Три щелчка вниз.
        [InlineData(-360, 360)]
        public void ToWheelMouseData_InvertsSign_ReturnsExpected(int deltaY, int expected)
        {
            Assert.Equal(expected, VisionScrollHelper.ToWheelMouseData(deltaY));
        }

        [Fact]
        public void ToWheelMouseData_IsInvolution()
        {
            // Двойное применение возвращает исходное значение.
            Assert.Equal(120, VisionScrollHelper.ToWheelMouseData(
                VisionScrollHelper.ToWheelMouseData(120)));
        }
    }
}