using System;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Трекер прогресса OCR (KI-204). Singleton, in-memory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Зачем:</b> OCR скана-PDF (3-10 страниц) занимает 20-60 сек. Клиент
    /// должен видеть прогресс «OCR: 2/3» — иначе UX непонятен.
    /// </para>
    /// <para>
    /// <b>Ключ:</b> <c>{chatId}:{fileName}</c>. Один ключ = один файл в одном чате.
    /// </para>
    /// <para>
    /// <b>AsyncLocal:</b> текущий ключ хранится в <c>AsyncLocal&lt;string&gt;</c> —
    /// чтобы <c>PdfParser</c> (Singleton) мог вызывать <see cref="Report"/>
    /// без явной передачи ключа через интерфейс парсера.
    /// </para>
    /// </remarks>
    public interface IOcrProgressTracker
    {
        /// <summary>
        /// Открывает scope прогресса: создаёт запись <c>{key}</c>, ставит
        /// <c>AsyncLocal</c> для текущей async-цепочки.
        /// </summary>
        /// <param name="key">Ключ файла (<c>{chatId}:{fileName}</c>).</param>
        /// <returns>
        /// Disposable-scope. При <c>Dispose</c> — восстанавливает предыдущий
        /// <c>AsyncLocal</c> и помечает запись как «завершено».
        /// </returns>
        IDisposable BeginScope(string key);

        /// <summary>
        /// Обновляет прогресс текущего scope (ключ из <c>AsyncLocal</c>).
        /// No-op, если scope не активен.
        /// </summary>
        /// <param name="currentPage">Номер текущей страницы (1-based).</param>
        /// <param name="totalPages">Всего страниц.</param>
        void Report(int currentPage, int totalPages);

        /// <summary>
        /// Помечает запись как завершённую (но не удаляет сразу — TTL 5 мин).
        /// </summary>
        /// <param name="key">Ключ файла.</param>
        void Complete(string key);

        /// <summary>
        /// Возвращает текущий прогресс или <c>null</c>, если ключа нет.
        /// </summary>
        /// <param name="key">Ключ файла.</param>
        OcrProgressDto Get(string key);
    }
}