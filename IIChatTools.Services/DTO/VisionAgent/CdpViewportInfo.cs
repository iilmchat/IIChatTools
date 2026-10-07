namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Viewport-метрики активной страницы Chrome (KI-161).
    /// Нужны для конвертации viewport-CSS-px (JavaScript) в screen-px
    /// (Win32 SendInput).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161).
    /// </para>
    /// <para>
    /// Формула конвертации:
    /// <code>
    /// screenX_css = WindowScreenX + ChromeUiWidth/2 + ViewportX
    /// screenX_px  = screenX_css * DevicePixelRatio
    /// shotX       = screenX_px / ScreenshotScaleX
    /// </code>
    /// </para>
    /// <para>
    /// <b>Chrome UI offset.</b> В window-space координаты viewport начинаются
    /// ниже адресной строки и правее левой рамки. <c>window.screenX</c>
    /// в Chrome — координаты левого-верхнего угла <b>окна</b>, включая
    /// chrome UI. Смещение viewport относительно окна = <c>(outerWidth - innerWidth)/2</c>
    /// по горизонтали и <c>(outerHeight - innerHeight)</c> по вертикали
    /// (сверху). См. DESIGN § 4.4.
    /// </para>
    /// </remarks>
    public class CdpViewportInfo
    {
        /// <summary>
        /// <c>window.devicePixelRatio</c> — масштаб DPI
        /// (1.0 / 1.25 / 1.5 / 2.0).
        /// </summary>
        public double DevicePixelRatio { get; set; } = 1.0;

        /// <summary>
        /// <c>window.screenX</c> — X левого-верхнего угла окна браузера
        /// (в CSS px, относительно primary-монитора).
        /// </summary>
        public double WindowScreenX { get; set; }

        /// <inheritdoc cref="WindowScreenX"/>
        public double WindowScreenY { get; set; }

        /// <summary>
        /// <c>window.outerWidth - window.innerWidth</c> — суммарная толщина
        /// chrome UI по горизонтали (левая + правая рамка), CSS px.
        /// </summary>
        public double ChromeUiWidth { get; set; }

        /// <summary>
        /// <c>window.outerHeight - window.innerHeight</c> — высота chrome UI
        /// (заголовок + табы + адресная строка + нижняя рамка), CSS px.
        /// </summary>
        public double ChromeUiHeight { get; set; }
    }
}