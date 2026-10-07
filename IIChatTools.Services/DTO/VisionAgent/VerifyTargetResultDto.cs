namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Результат верификации координат элемента Vision LLM
    /// (KI-162 Coordinate-then-Verify).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-162). См. DESIGN_VISION_AGENT § 3.3.
    /// </para>
    /// <para>
    /// VL-модель получает <b>кроп</b> вокруг bounds элемента (с upscale),
    /// возвращает уточнённый центр. Координаты — <b>в системе исходного
    /// (downscale'нутого) PNG</b>, той же, что и у <c>ScreenDescriptionDto</c>.
    /// </para>
    /// </remarks>
    public class VerifyTargetResultDto
    {
        /// <summary>
        /// Уточнённая X-координата центра элемента (в системе исходного PNG).
        /// Валидно только если <see cref="Found"/> = <c>true</c>.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Уточнённая Y-координата центра элемента (в системе исходного PNG).
        /// Валидно только если <see cref="Found"/> = <c>true</c>.
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Уверенность VL-модели в координатах (0.0–1.0).
        /// VL не всегда возвращает — default <c>0.7</c>.
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Нашла ли VL элемент в кропе. <c>false</c> — fallback на bounds center.
        /// </summary>
        public bool Found { get; set; }

        /// <summary>
        /// Причина неудачи (если <see cref="Found"/> = <c>false</c>).
        /// Пример: «VL вернула невалидный JSON», «confidence 0.4 &lt; 0.6».
        /// </summary>
        public string Error { get; set; }
    }
}