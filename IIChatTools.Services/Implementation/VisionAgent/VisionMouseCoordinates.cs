using System;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Нормализация координат мыши для <c>SendInput</c>.
    /// Win32 API <c>MOUSEEVENTF_ABSOLUTE</c> требует координаты в диапазоне
    /// 0..65535 (relative to primary monitor).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф2.5). Вынесено отдельно для unit-тестов.
    /// </remarks>
    public static class VisionMouseCoordinates
    {
        /// <summary>Максимальная абсолютная координата Win32.</summary>
        public const int MaxAbsolute = 65535;

        /// <summary>
        /// Конвертирует пиксельные координаты (0..screenWidth-1) в нормализованные
        /// Win32-абсолютные (0..65535).
        /// </summary>
        /// <param name="x">X в пикселях (0 = левый край).</param>
        /// <param name="y">Y в пикселях (0 = верхний край).</param>
        /// <param name="screenWidth">Ширина primary-экрана, px (> 1).</param>
        /// <param name="screenHeight">Высота primary-экрана, px (> 1).</param>
        /// <returns>Пара нормализованных координат (nx, ny) в диапазоне 0..65535.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Если <paramref name="x"/> / <paramref name="y"/> отрицательные,
        /// или <paramref name="screenWidth"/> / <paramref name="screenHeight"/> ≤ 1.
        /// </exception>
        public static (int nx, int ny) NormalizeToAbsolute(
            int x, int y, int screenWidth, int screenHeight)
        {
            if (x < 0)
                throw new ArgumentOutOfRangeException(nameof(x), x, "X не может быть отрицательным.");
            if (y < 0)
                throw new ArgumentOutOfRangeException(nameof(y), y, "Y не может быть отрицательным.");
            if (screenWidth <= 1)
                throw new ArgumentOutOfRangeException(nameof(screenWidth), screenWidth, "Ширина экрана должна быть > 1.");
            if (screenHeight <= 1)
                throw new ArgumentOutOfRangeException(nameof(screenHeight), screenHeight, "Высота экрана должна быть > 1.");

            if (x >= screenWidth)
                throw new ArgumentOutOfRangeException(nameof(x), x, $"X {x} вне экрана (ширина {screenWidth}).");
            if (y >= screenHeight)
                throw new ArgumentOutOfRangeException(nameof(y), y, $"Y {y} вне экрана (высота {screenHeight}).");

            // Формула Win32: (px * 65535) / (screenSize - 1).
            var nx = (int)((long)x * MaxAbsolute / (screenWidth - 1));
            var ny = (int)((long)y * MaxAbsolute / (screenHeight - 1));
            return (nx, ny);
        }
    }
}