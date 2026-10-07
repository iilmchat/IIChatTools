using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Провайдер координат из DOM через CDP (KI-161).
    /// Возвращает координаты с <b>0 px ошибки</b> для DOM-доступных
    /// элементов (button / link / text_input / checkbox / radio / select).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161). См. DESIGN § 2.1, § 6.
    /// </para>
    /// <para>
    /// <b>Создаётся через <c>new</c> внутри <c>LocalHarnessVisionBackend</c></b>
    /// (не через DI) — ему нужна конкретная <see cref="IChromeCdpSession"/>,
    /// привязанная к Chrome этой задачи (DESIGN § 2.5).
    /// </para>
    /// <para>
    /// <b>Конвертация координат.</b> <see cref="IChromeCdpSession.FindElementAsync"/>
    /// возвращает координаты в <b>viewport CSS px</b> (система JavaScript).
    /// Возвращаем в <b>screenshot-space</b> (система, в которой работает
    /// <c>VisionAgentService</c>) — обратная операция к той, что делает
    /// <c>PuppeteerSharpCdpSession.FindElementAsync</c> при конвертации
    /// VL-bounds → viewport-css:
    /// <code>
    /// css_x   = viewportX + WindowScreenX + ChromeUiWidth/2
    /// screenX = css_x * DPR
    /// shotX   = screenX / ScreenshotScaleX
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class DomCoordinateProvider : ICoordinateProvider
    {
        private readonly IChromeCdpSession _cdp;
        private readonly ILogger _logger;

        /// <summary>
        /// Создаёт провайдер.
        /// </summary>
        /// <param name="cdp">
        /// Подключённая CDP-сессия. Не должна быть <c>null</c> —
        /// создатель обязан проверить <see cref="IChromeCdpSession.IsConnected"/>
        /// перед созданием (DESIGN § 2.5).
        /// </param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null.</exception>
        public DomCoordinateProvider(IChromeCdpSession cdp, ILogger logger)
        {
            _cdp = cdp ?? throw new ArgumentNullException(nameof(cdp));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "dom";

        /// <inheritdoc />
        public async Task<CoordinateResult> ResolveAsync(
            CoordinateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (!_cdp.IsConnected)
            {
                return new CoordinateResult
                {
                    Found = false,
                    FromDom = false,
                    Error = "CDP-сессия не подключена"
                };
            }

            try
            {
                var query = new CdpElementQuery
                {
                    Label = request.TargetLabel,
                    Type = request.TargetType,
                    Bounds = request.TargetBounds,
                    ScreenshotScaleX = request.ScreenshotScaleX,
                    ScreenshotScaleY = request.ScreenshotScaleY,
                    PositionTolerancePx = 200
                };

                var cdpResult = await _cdp
                    .FindElementAsync(query, cancellationToken)
                    .ConfigureAwait(false);

                if (cdpResult == null || !cdpResult.Found)
                {
                    return new CoordinateResult
                    {
                        Found = false,
                        FromDom = false,
                        Error = cdpResult?.Error ?? "CDP вернул null"
                    };
                }

                // Конвертация viewport-css → screen-px → screenshot-space.
                var viewportInfo = await _cdp
                    .GetViewportInfoAsync(cancellationToken)
                    .ConfigureAwait(false);

                var dpr = viewportInfo.DevicePixelRatio > 0
                    ? viewportInfo.DevicePixelRatio
                    : 1.0;

                var scaleX = request.ScreenshotScaleX > 0 ? request.ScreenshotScaleX : 1.0;
                var scaleY = request.ScreenshotScaleY > 0 ? request.ScreenshotScaleY : 1.0;

                // viewport-css → window-css → screen-px → screenshot-space.
                var cssX = cdpResult.ViewportX + viewportInfo.WindowScreenX
                                              + viewportInfo.ChromeUiWidth / 2.0;
                var cssY = cdpResult.ViewportY + viewportInfo.WindowScreenY
                                              + viewportInfo.ChromeUiHeight;

                var screenPxX = cssX * dpr;
                var screenPxY = cssY * dpr;

                var shotX = (int)Math.Round(screenPxX / scaleX);
                var shotY = (int)Math.Round(screenPxY / scaleY);

                _logger.LogDebug(
                    "DomCoordinateProvider: target='{Target}' viewport=({Vx:F1},{Vy:F1}) " +
                    "→ window-css=({Cx:F1},{Cy:F1}) → screen-px=({Sx:F1},{Sy:F1}) " +
                    "→ screenshot=({ShotX},{ShotY}) [dpr={Dpr}, scale=({Sx:F2},{Sy:F2})]",
                    request.TargetId,
                    cdpResult.ViewportX, cdpResult.ViewportY,
                    cssX, cssY,
                    screenPxX, screenPxY,
                    shotX, shotY,
                    dpr, scaleX, scaleY);

                return new CoordinateResult
                {
                    X = shotX,
                    Y = shotY,
                    Found = true,
                    FromDom = true,
                    Selector = cdpResult.Selector,
                    Error = cdpResult.MatchedBy   // для логов "как нашли"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DomCoordinateProvider.ResolveAsync упал");
                return new CoordinateResult
                {
                    Found = false,
                    FromDom = false,
                    Error = $"DOM-resolve error: {ex.Message}"
                };
            }
        }
    }
}