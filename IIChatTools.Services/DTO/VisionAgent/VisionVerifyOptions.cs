namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Конфигурация механизма Coordinate-then-Verify
    /// (KI-162) — уточнение координат через второй VL-вызов на кропе.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-162). Секция <c>VisionAgent:Verify</c> в appsettings.
    /// </para>
    /// <para>
    /// <b>Идея:</b> первый <c>describe</c> даёт bounds элемента с ошибкой
    /// ±30 px (Qwen2.5-VL-7B на полном 1280×720). Верификация снимает кроп
    /// вокруг bounds (padding = <see cref="CropPadding"/>), апскейлит ×
    /// <see cref="Upscale"/> и просит VL уточнить центр — на увеличенном
    /// фрагменте модель ошибается на ±3-5 px.
    /// </para>
    /// </remarks>
    public class VisionVerifyOptions
    {
        /// <summary>
        /// Включён ли механизм. Default: <c>true</c> (для dev).
        /// При <c>false</c> — используется старый путь (bounds center).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Отступ вокруг bounds элемента при crop, px (в системе исходного PNG).
        /// Default: 100. Малый padding рискует обрезать элемент, большой —
        /// вернуть ошибку grounding (VL видит лишний контекст).
        /// </summary>
        public int CropPadding { get; set; } = 100;

        /// <summary>
        /// Коэффициент апскейла кропа перед отправкой в VL. Default: 2.
        /// Кратно увеличивает и разрешение, и размер payload'а.
        /// </summary>
        public int Upscale { get; set; } = 2;

        /// <summary>
        /// Минимальная уверенность VL, чтобы использовать уточнённые координаты.
        /// Default: 0.6. Ниже — fallback на bounds center.
        /// </summary>
        public double MinConfidence { get; set; } = 0.6;
    }
}