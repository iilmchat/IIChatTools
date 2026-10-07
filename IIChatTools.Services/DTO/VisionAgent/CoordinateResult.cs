namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Результат резолва координат целевого элемента (KI-161).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161).
    /// </para>
    /// <para>
    /// Координаты — <b>в системе downscale'нутого PNG</b> (той же, что
    /// у <c>ScreenDescriptionDto</c> и <c>UiElementDto.Bounds</c>).
    /// <c>VisionAgentService</c> передаёт их в <c>IVisionBackend.ClickAsync</c>,
    /// который применяет <c>ScaleToScreen</c> (умножает на
    /// <c>_screenshotScaleX</c>).
    /// </para>
    /// </remarks>
    public class CoordinateResult
    {
        /// <summary>X-координата центра элемента в системе screenshot-PNG.</summary>
        public int X { get; set; }

        /// <inheritdoc cref="X"/>
        public int Y { get; set; }

        /// <summary>
        /// <c>true</c> — элемент найден и координаты валидны.
        /// <c>false</c> — не найден; <see cref="Error"/> содержит причину.
        /// </summary>
        public bool Found { get; set; }

        /// <summary>
        /// <c>true</c> — координаты получены из DOM (0 px ошибки).
        /// <c>false</c> — из VL (bounds-center, ±20-30 px).
        /// </summary>
        public bool FromDom { get; set; }

        /// <summary>
        /// CSS-селектор или JS-описание найденного элемента (для логов).
        /// Пример: <c>button#search-btn</c> или
        /// <c>button[type=submit]:nth-child(3)</c>.
        /// Может быть <c>null</c> для VL-fallback.
        /// </summary>
        public string Selector { get; set; }

        /// <summary>
        /// Причина неудачи, если <see cref="Found"/> = <c>false</c>.
        /// Пример: <c>"CDP не подключён"</c>,
        /// <c>"элемент не найден в DOM"</c>.
        /// </summary>
        public string Error { get; set; }
    }
}