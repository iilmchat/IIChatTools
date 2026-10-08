using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Абстракция «поверхности», на которой выполняется задача Vision Agent.
    /// Три реализации: <c>LocalHarnessVisionBackend</c> (текущая машина),
    /// <c>SandboxVisionBackend</c> (Windows Sandbox), <c>VncMcpVisionBackend</c>
    /// (удалённая машина через MCP/VNC).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 3.1, § 4.1.
    /// </remarks>
    public interface IVisionBackend : System.IAsyncDisposable
    {
        /// <summary>
        /// Имя backend'а для логов и audit. Пример: <c>local-harness</c>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Открывает страницу / окно (browser-режим).
        /// Для desktop-режима — no-op (или фокус на нужное приложение).
        /// </summary>
        /// <param name="url">URL (http/https). Домен должен быть в whitelist.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task OpenAsync(string url, CancellationToken cancellationToken = default);

        /// <summary>
        /// Захватывает текущий экран и возвращает PNG-байты.
        /// Размер ограничен <c>VisionAgentOptions.Limits.MaxScreenshotBytes</c>.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>PNG-байты.</returns>
        Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Full-resolution скриншот экрана — БЕЗ downscale. Нужен для OCR
        /// мелкого текста (8-10 px шрифты не видны на downscaled 1280×720).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// <see cref="FullResolutionScreenshotDto"/> (PNG + Width + Height)
        /// или <c>null</c>, если backend не поддерживает full-res.
        /// </returns>
        /// <remarks>
        /// <para>
        /// v1.13.x (KI-137). Default-метод (C# 8+): возвращает <c>null</c>.
        /// <c>LocalHarnessVisionBackend</c> переопределяет — GDI-захват
        /// без downscale. <c>SandboxVisionBackend</c> / <c>VncMcpVisionBackend</c>
        /// могут не переопределять (OCR-функциональность опциональна).
        /// </para>
        /// <para>
        /// <b>RULES § 4.46:</b> default interface method виден только через
        /// интерфейсную переменную. <c>VisionAgentService</c> держит
        /// <c>IVisionBackend</c> — вызов корректен.
        /// </para>
        /// <para>
        /// <b>Отличие от <see cref="ScreenshotAsync"/>:</b> не применяет
        /// <c>VisionImageResizer.Resize</c> и не проверяет
        /// <c>MaxScreenshotBytes</c> — это внутренний PNG для OCR, не уходит
        /// в Vision LLM (base64 не тратится).
        /// </para>
        /// </remarks>
        Task<FullResolutionScreenshotDto> ScreenshotFullResolutionAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<FullResolutionScreenshotDto>(null);

        /// <summary>Клик по координатам (левая кнопка мыши).</summary>
        Task ClickAsync(int x, int y, CancellationToken cancellationToken = default);

        /// <summary>Двойной клик по координатам (левая кнопка мыши).</summary>
        Task DoubleClickAsync(int x, int y, CancellationToken cancellationToken = default);

        /// <summary>Клик правой кнопкой мыши по координатам (контекстное меню).</summary>
        Task RightClickAsync(int x, int y, CancellationToken cancellationToken = default);

        /// <summary>Наведение мыши на координаты (hover-меню). Без клика.</summary>
        Task MoveMouseAsync(int x, int y, CancellationToken cancellationToken = default);

        /// <summary>Ввод текста (в текущий фокус).</summary>
        Task TypeAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>Нажатие одиночной клавиши (<c>Enter</c>, <c>Tab</c>, <c>Escape</c>, ...).</summary>
        Task PressKeyAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>Нажатие комбинации клавиш (<c>["Ctrl", "C"]</c>).</summary>
        Task HotkeyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

        /// <summary>Прокрутка (deltaY &gt; 0 — вниз, deltaY &lt; 0 — вверх).</summary>
        Task ScrollAsync(int deltaY, CancellationToken cancellationToken = default);

        /// <summary>Пауза (мс) — для ожидания анимации / загрузки.</summary>
        Task WaitAsync(int milliseconds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает провайдер координат из DOM (KI-161), если backend
        /// поддерживает CDP-attach. <c>null</c> — DOM недоступен,
        /// используется VL-fallback (bounds-center, KI-190).
        /// </summary>
        /// <remarks>
        /// <para>
        /// v1.13.x (KI-161). Default-метод (C# 8+): возвращает <c>null</c>.
        /// <c>LocalHarnessVisionBackend</c> переопределяет — возвращает
        /// <c>DomCoordinateProvider</c>, если CDP подключён.
        /// </para>
        /// <para>
        /// <b>RULES § 4.46:</b> default interface method виден только через
        /// интерфейсную переменную. <c>VisionAgentService</c> держит
        /// <c>IVisionBackend</c> — вызов корректен.
        /// </para>
        /// </remarks>
        ICoordinateProvider GetCoordinateProvider() => null;

        /// <summary>
        /// Текущий коэффициент масштабирования «screen / screenshot».
        /// 1.0 — если скриншот не downscale'ится (реальные пиксели экрана
        /// совпадают с PNG-координатами). Пример: 1.875 — если 1920
        /// downscale'нут до 1024.
        /// </summary>
        /// <remarks>
        /// <para>
        /// v1.13.x (KI-161). Default-метод: возвращает <c>(1.0, 1.0)</c>.
        /// </para>
        /// <para>
        /// <b>Зачем:</b> <c>DomCoordinateProvider</c> получает координаты
        /// из DOM в физических px экрана. Чтобы вернуть их
        /// в <b>screenshot-space</b> (система, в которой работает
        /// <c>VisionAgentService</c>), он делит их на этот scale.
        /// </para>
        /// </remarks>
        (double X, double Y) GetScreenshotScale() => (1.0, 1.0);
    }
}