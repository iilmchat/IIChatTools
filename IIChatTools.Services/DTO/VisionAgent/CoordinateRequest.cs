namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Запрос на резолв координат целевого элемента
    /// (KI-161, DOM+Vision hybrid).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161).
    /// </para>
    /// <para>
    /// Формируется <c>VisionAgentService.ResolveCoordinatesAsync</c>
    /// из семантического <c>target</c> + <c>ui_elements[].label/type/bounds</c>
    /// и передаётся в <c>ICoordinateProvider.ResolveAsync</c>.
    /// </para>
    /// </remarks>
    public class CoordinateRequest
    {
        /// <summary>
        /// Семантический id из <c>ui_elements</c> (например, <c>search_button</c>).
        /// Используется для логов; в DOM по нему не ищем
        /// (VL-модель его выдумывает).
        /// </summary>
        public string TargetId { get; set; }

        /// <summary>
        /// Видимый label элемента (например, «Найти»). Может быть пустым —
        /// тогда DOM-провайдер использует position-matching.
        /// </summary>
        public string TargetLabel { get; set; }

        /// <summary>
        /// Тип элемента из VL: <c>button</c> / <c>text_input</c> / <c>link</c> /
        /// <c>checkbox</c> / <c>radio</c> / <c>dropdown</c> / <c>other</c>.
        /// Используется для сужения списка кандидатов в DOM.
        /// </summary>
        public string TargetType { get; set; }

        /// <summary>
        /// VL-bounds (в системе downscale'нутого PNG). Применяется для
        /// position-matching, когда label пуст или неоднозначен.
        /// Может быть <c>null</c>.
        /// </summary>
        public UiElementBoundsDto TargetBounds { get; set; }

        /// <summary>
        /// Коэффициент масштабирования «screen / screenshot» по X.
        /// 1.875 — если скриншот downscale'нут с 1920 до 1024.
        /// <c>DomCoordinateProvider</c> делит результат (DOM-координаты
        /// в физических px экрана) на этот scale, чтобы вернуть координаты
        /// в screenshot-space (как ожидает <c>VisionAgentService</c>).
        /// </summary>
        public double ScreenshotScaleX { get; set; } = 1.0;

        /// <inheritdoc cref="ScreenshotScaleX"/>
        public double ScreenshotScaleY { get; set; } = 1.0;
    }
}