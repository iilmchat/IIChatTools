using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Пропорциональный downscale PNG до <c>MaxImageWidth × MaxImageHeight</c>.
    /// Если исходник меньше — возвращает без изменений (не увеличивает).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф2.8). См. DESIGN § 5.1 (<c>MaxImageWidth</c>).
    /// </para>
    /// <para>
    /// <b>Метод <see cref="CalculateTargetSize"/> — чистый</b>: работает
    /// на любой ОС, тестируется в CI без Windows. Метод <see cref="Resize"/>
    /// использует <c>System.Drawing.Common</c> — только Windows.
    /// </para>
    /// </remarks>
    public static class VisionImageResizer
    {
        /// <summary>
        /// Вычисляет целевые размеры с сохранением пропорций.
        /// Не увеличивает изображение — если оно меньше лимита, возвращает
        /// исходные размеры.
        /// </summary>
        /// <param name="originalWidth">Ширина исходника, px (&gt; 0).</param>
        /// <param name="originalHeight">Высота исходника, px (&gt; 0).</param>
        /// <param name="maxWidth">Максимальная ширина, px (&gt; 0).</param>
        /// <param name="maxHeight">Максимальная высота, px (&gt; 0).</param>
        /// <returns>Целевые размеры (newWidth, newHeight).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Если любой из параметров ≤ 0.
        /// </exception>
        public static (int newWidth, int newHeight) CalculateTargetSize(
            int originalWidth, int originalHeight, int maxWidth, int maxHeight)
        {
            if (originalWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(originalWidth), originalWidth, "Ширина исходника должна быть > 0.");
            if (originalHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(originalHeight), originalHeight, "Высота исходника должна быть > 0.");
            if (maxWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "MaxWidth должен быть > 0.");
            if (maxHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxHeight), maxHeight, "MaxHeight должен быть > 0.");

            // Не увеличиваем — только сжимаем.
            if (originalWidth <= maxWidth && originalHeight <= maxHeight)
            {
                return (originalWidth, originalHeight);
            }

            // Сохраняем пропорции: коэффициент = min(maxW/W, maxH/H).
            var scaleW = (double)maxWidth / originalWidth;
            var scaleH = (double)maxHeight / originalHeight;
            var scale = Math.Min(scaleW, scaleH);

            var newWidth = Math.Max(1, (int)Math.Round(originalWidth * scale));
            var newHeight = Math.Max(1, (int)Math.Round(originalHeight * scale));
            return (newWidth, newHeight);
        }

        /// <summary>
        /// Пропорционально уменьшает PNG-байты до указанных размеров.
        /// Если исходник меньше — возвращает без изменений (не увеличивает).
        /// Если пропорции совпадают и размеры уже ≤ лимита — тоже без изменений.
        /// </summary>
        /// <param name="pngBytes">PNG-байты.</param>
        /// <param name="maxWidth">Максимальная ширина, px.</param>
        /// <param name="maxHeight">Максимальная высота, px.</param>
        /// <returns>PNG-байты (тот же массив или новый, уменьшенный).</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="pngBytes"/> = null.</exception>
        /// <exception cref="ArgumentException">Если PNG невалидный.</exception>
        [SupportedOSPlatform("windows")]
        public static byte[] Resize(byte[] pngBytes, int maxWidth, int maxHeight)
        {
            if (pngBytes == null) throw new ArgumentNullException(nameof(pngBytes));
            if (pngBytes.Length == 0)
                throw new ArgumentException("Пустой PNG.", nameof(pngBytes));

            using var input = new MemoryStream(pngBytes);
            using var source = new Bitmap(input);

            var (newW, newH) = CalculateTargetSize(source.Width, source.Height, maxWidth, maxHeight);

            // Если размеры не изменились — возвращаем исходный массив.
            if (newW == source.Width && newH == source.Height)
            {
                return pngBytes;
            }

            using var target = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(target))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(source, 0, 0, newW, newH);
            }

            using var output = new MemoryStream();
            target.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
    }
}