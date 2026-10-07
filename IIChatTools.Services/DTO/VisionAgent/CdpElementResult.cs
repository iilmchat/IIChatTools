namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Результат поиска DOM-элемента через CDP (KI-161).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161).
    /// </para>
    /// <para>
    /// Координаты — в <b>viewport CSS px</b> (система координат JavaScript:
    /// <c>getBoundingClientRect().left/top</c>). Конвертация в screen-координаты
    /// и затем в screenshot-space — на стороне <c>DomCoordinateProvider</c>
    /// (использует <see cref="CdpViewportInfo"/>).
    /// </para>
    /// </remarks>
    public class CdpElementResult
    {
        /// <summary>
        /// X-координата центра элемента в viewport CSS px.
        /// Валидно при <see cref="Found"/> = <c>true</c>.
        /// </summary>
        public double ViewportX { get; set; }

        /// <inheritdoc cref="ViewportX"/>
        public double ViewportY { get; set; }

        /// <summary>Ширина элемента в CSS px.</summary>
        public double Width { get; set; }

        /// <inheritdoc cref="Width"/>
        public double Height { get; set; }

        /// <summary>
        /// <c>true</c> — элемент найден и виден (<c>display</c> ≠ <c>none</c>,
        /// ненулевые размеры).
        /// </summary>
        public bool Found { get; set; }

        /// <summary>
        /// Как нашли элемент:
        /// <list type="bullet">
        ///   <item><description><c>"label"</c> — по видимому тексту /
        ///     aria-label;</description></item>
        ///   <item><description><c>"position"</c> — по близости
        ///     к VL-bounds;</description></item>
        ///   <item><description><c>"type"</c> — по типу (label пуст,
        ///     bounds null).</description></item>
        /// </list>
        /// </summary>
        public string MatchedBy { get; set; }

        /// <summary>
        /// CSS-селектор найденного элемента (для логов).
        /// Пример: <c>button#search-btn</c>.
        /// </summary>
        public string Selector { get; set; }

        /// <summary>
        /// Причина неудачи, если <see cref="Found"/> = <c>false</c>.
        /// </summary>
        public string Error { get; set; }
    }
}