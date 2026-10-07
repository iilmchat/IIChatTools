namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Конфигурация провайдера координат Vision Agent (KI-161).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161). Секция <c>VisionAgent:CoordinateProvider</c>
    /// в appsettings.
    /// </para>
    /// </remarks>
    public class VisionCoordinateProviderOptions
    {
        /// <summary>
        /// Режим выбора провайдера координат:
        /// <list type="bullet">
        ///   <item><description><c>"dom"</c> — только DOM
        ///     (при неудаче — VL-fallback);</description></item>
        ///   <item><description><c>"vision"</c> — только VL
        ///     (старое поведение, KI-190);</description></item>
        ///   <item><description><c>"auto"</c> — DOM если CDP подключён,
        ///     иначе VL.</description></item>
        /// </list>
        /// Default: <c>"auto"</c>.
        /// </summary>
        public string Mode { get; set; } = "auto";

        /// <summary>
        /// Конфигурация CDP-подключения.
        /// </summary>
        public VisionCdpOptions Cdp { get; set; } = new VisionCdpOptions();
    }
}