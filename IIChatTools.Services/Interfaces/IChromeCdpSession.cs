using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сессия CDP (Chrome DevTools Protocol) к уже запущенному Chrome
    /// (KI-161, PuppeteerSharp CDP-attach).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161).
    /// </para>
    /// <para>
    /// <b>Жизненный цикл:</b> создаётся <c>LocalHarnessVisionBackend</c>
    /// (Scoped, per-task) лениво — при первом <c>GetCoordinateProvider()</c>.
    /// Подключается к Chrome, запущенному самим backend'ом
    /// с флагом <c>--remote-debugging-port=9222</c>.
    /// Закрывается в <c>DisposeAsync</c> при закрытии backend'а.
    /// </para>
    /// <para>
    /// <b>Thread-safety:</b> один экземпляр на задачу; параллельные вызовы
    /// <c>FindElementAsync</c> не предполагаются.
    /// </para>
    /// </remarks>
    public interface IChromeCdpSession : IAsyncDisposable
    {
        /// <summary>
        /// <c>true</c> — сессия подключена к Chrome и готова к запросам.
        /// <c>false</c> — не подключена (или Chrome закрылся).
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Подключается к запущенному Chrome через CDP.
        /// </summary>
        /// <param name="browserUrl">
        /// URL CDP-эндпоинта (например, <c>http://127.0.0.1:9222</c>).
        /// PuppeteerSharp сам резолвит WebSocket-URL из этого адреса.
        /// </param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// <c>true</c> — подключение успешно; <c>false</c> — Chrome недоступен
        /// (порт занят / Chrome не отвечает / таймаут).
        /// <b>Не бросает</b> — при ошибке логирует и возвращает <c>false</c>.
        /// </returns>
        Task<bool> ConnectAsync(
            string browserUrl,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Ищет элемент в DOM активной страницы по эвристикам
        /// (label / position / type).
        /// </summary>
        /// <param name="query">Запрос (label / type / bounds).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// Результат с <c>Found=true</c> и координатами
        /// в <b>viewport CSS px</b>. При любой ошибке
        /// (не подключён / элемент не найден / JS упал) —
        /// <c>Found=false</c>, <c>Error</c> заполнен.
        /// <b>Не бросает</b> (кроме <c>OperationCanceledException</c>).
        /// </returns>
        Task<CdpElementResult> FindElementAsync(
            CdpElementQuery query,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает viewport-метрики страницы: DPR, screen offset,
        /// chrome UI offset. Нужно для конвертации viewport-координат
        /// в screen-координаты.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// Метрики. При ошибке — дефолтный объект
        /// (DPR=1, screenX/Y=0, offset=0). <b>Не бросает</b>.
        /// </returns>
        Task<CdpViewportInfo> GetViewportInfoAsync(
            CancellationToken cancellationToken = default);
    }
}