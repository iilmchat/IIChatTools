namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Full-resolution скриншот (KI-137): PNG + размеры в пикселях.
    /// Возвращается <c>IVisionBackend.ScreenshotFullResolutionAsync</c>
    /// и используется OCR'ом (для распознавания мелкого текста — 8-10 px
    /// шрифтов, которые не видны на downscaled PNG 1280×720).
    /// </summary>
    /// <remarks>
    /// v1.13.x (KI-137). См.
    /// <c>docs/development/v1.13/DESIGN_VISION_OCR.md</c> § 4.2.
    /// </remarks>
    public sealed class FullResolutionScreenshotDto
    {
        /// <summary>
        /// PNG-байты (полное разрешение экрана, без downscale).
        /// </summary>
        public byte[] Png { get; set; }

        /// <summary>
        /// Ширина PNG в пикселях (natural resolution).
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Высота PNG в пикселях (natural resolution).
        /// </summary>
        public int Height { get; set; }
    }
}