using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    }
}