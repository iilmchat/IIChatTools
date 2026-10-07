namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Запрос к CDP: поиск DOM-элемента по эвристикам (KI-161).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161).
    /// </para>
    /// <para>
    /// Используется <c>IChromeCdpSession.FindElementAsync</c> —
    /// JS-скрипт внутри страницы перебирает кандидатов и матчит по
    /// label / position / type.
    /// </para>
    /// </remarks>
    public class CdpElementQuery
    {
        /// <summary>
        /// Видимый label. Ищется в <c>textContent</c> / <c>value</c> /
        /// <c>placeholder</c> / <c>aria-label</c>
        /// (case-insensitive substring).
        /// Может быть пустым — тогда сразу position-matching.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Тип элемента: <c>button</c> / <c>text_input</c> / <c>link</c> /
        /// <c>checkbox</c> / <c>radio</c> / <c>dropdown</c> / <c>other</c>.
        /// Сужает список кандидатов для querySelectorAll.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// VL-bounds (в системе screenshot-PNG). Применяется для
        /// position-matching: центр кандидата должен быть в пределах
        /// ±<c>PositionTolerancePx</c> от центра bounds.
        /// Может быть <c>null</c> — тогда position-matching пропускается.
        /// </summary>
        public UiElementBoundsDto Bounds { get; set; }

        /// <summary>
        /// Коэффициент масштабирования «screen / screenshot». Для конвертации
        /// VL-bounds (screenshot-space) в viewport-CSS-px (screen-space).
        /// </summary>
        public double ScreenshotScaleX { get; set; } = 1.0;

        /// <inheritdoc cref="ScreenshotScaleX"/>
        public double ScreenshotScaleY { get; set; } = 1.0;

        /// <summary>
        /// Порог позиционного матчинга, px в screen-space. Default: 200.
        /// Если ближайший кандидат дальше — считаем, что это другой элемент
        /// (защита от «фантомных» DOM-координат, KI-161 § 9.2).
        /// </summary>
        public int PositionTolerancePx { get; set; } = 200;
    }
}