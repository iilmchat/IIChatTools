using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Fallback-провайдер координат: bounds-center из VL-описания
    /// (KI-190). Используется, когда <c>DomCoordinateProvider</c>
    /// не нашёл элемент (canvas / WebGL / shadow-DOM / iframe),
    /// или когда CDP недоступен (<c>Mode="vision"</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161). См. DESIGN § 2.2.
    /// </para>
    /// <para>
    /// <b>Точность:</b> ±20-30 px (Qwen2.5-VL-7B на полном скриншоте).
    /// Хуже, чем DOM (0 px), но лучше, чем ничего — покрывает
    /// desktop-приложения и canvas.
    /// </para>
    /// <para>
    /// <b>Verify-путь (KI-162) остаётся в <c>VisionAgentService</c></b> —
    /// этот провайдер возвращает только bounds-center, а решение
    /// «доверять Verify или нет» принимается в loop'е.
    /// </para>
    /// <para>
    /// <b>Stateless.</b> Регистрируется в DI как Singleton.
    /// </para>
    /// </remarks>
    public sealed class VisionCoordinateProvider : ICoordinateProvider
    {
        /// <inheritdoc />
        public string Name => "vision";

        /// <inheritdoc />
        public Task<CoordinateResult> ResolveAsync(
            CoordinateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // 1. Bounds-center (KI-190) — предпочтительный источник.
            if (request.TargetBounds != null
                && request.TargetBounds.W > 0
                && request.TargetBounds.H > 0)
            {
                var x = request.TargetBounds.X + request.TargetBounds.W / 2;
                var y = request.TargetBounds.Y + request.TargetBounds.H / 2;

                return Task.FromResult(new CoordinateResult
                {
                    X = x,
                    Y = y,
                    Found = true,
                    FromDom = false,
                    Error = null
                });
            }

            // 2. Нечего возвращать: нет bounds. Вызывающий код
            //    сам решит, использовать ли Verify (KI-162) или упасть.
            return Task.FromResult(new CoordinateResult
            {
                Found = false,
                FromDom = false,
                Error = "Bounds не заданы — VisionCoordinateProvider не может резолвить"
            });
        }
    }
}