using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Провайдер координат целевого элемента для Vision Agent (KI-161).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161). Две реализации:
    /// <list type="bullet">
    ///   <item><description><c>DomCoordinateProvider</c> — координаты из DOM
    ///     через CDP (0 px ошибки);</description></item>
    ///   <item><description><c>VisionCoordinateProvider</c> — fallback
    ///     на bounds-center из VL (±20-30 px, KI-190).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Выбор провайдера — в <c>VisionAgentService.ResolveCoordinatesAsync</c>:</b>
    /// сначала пробуем DOM (если backend поддерживает CDP), при неудаче —
    /// VL-fallback.
    /// </para>
    /// </remarks>
    public interface ICoordinateProvider
    {
        /// <summary>
        /// Имя провайдера для логов. <c>"dom"</c> / <c>"vision"</c>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Резолвит координаты целевого элемента.
        /// </summary>
        /// <param name="request">
        /// Запрос (target id / label / type / bounds / scale).
        /// </param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// <see cref="CoordinateResult"/> с <c>Found=true</c> при успехе.
        /// При любой ошибке — <c>Found=false</c>, <c>Error</c> заполнен.
        /// <b>Никогда не бросает</b> (кроме <c>OperationCanceledException</c>) —
        /// вызывающий код делает fallback на следующий провайдер.
        /// </returns>
        Task<CoordinateResult> ResolveAsync(
            CoordinateRequest request,
            CancellationToken cancellationToken = default);
    }
}