using System;
using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис распознавания текста с изображений (OCR).
    /// Используется RAG для сканов PDF (KI-203).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-203). Реализация — <c>TesseractOcrService</c>.
    /// </para>
    /// <para>
    /// <b>Платформа:</b> Tesseract native lib доступна только для Windows
    /// в NuGet-пакете. На Linux <see cref="IsReady"/> = false — вызывающий
    /// код должен gracefully пропустить OCR.
    /// </para>
    /// </remarks>
    public interface IOcrService
    {
        /// <summary>
        /// Готов ли OCR к работе: включён в конфиге + engine успешно инициализирован.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Имя движка для логов (<c>"tesseract"</c>).
        /// </summary>
        string EngineName { get; }

        /// <summary>
        /// Распознаёт текст на изображении.
        /// </summary>
        /// <param name="imageBytes">PNG/JPEG-байты изображения.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Распознанный текст (может быть пустым).</returns>
        /// <exception cref="InvalidOperationException">Если <see cref="IsReady"/> = false.</exception>
        /// <exception cref="ArgumentException">Если <paramref name="imageBytes"/> пуст.</exception>
        Task<string> RecognizeAsync(
            byte[] imageBytes,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Распознаёт текст на изображении с сохранением bbox'ов каждого слова
        /// (v1.13.x, KI-207 — text layer).
        ///
        /// <para>
        /// Используется для построения text layer поверх PNG: слова
        /// позиционируются абсолютно, что позволяет выделять текст и искать
        /// по странице (как в Acrobat).
        /// </para>
        /// </summary>
        /// <param name="imageBytes">PNG-байты.</param>
        /// <param name="imageWidth">Ширина изображения в пикселях (для DTO).</param>
        /// <param name="imageHeight">Высота изображения в пикселях (для DTO).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// Text layer (слова + bbox). Слова — в natural PNG pixels,
        /// top-left origin, y↓.
        /// </returns>
        /// <exception cref="InvalidOperationException">Если <see cref="IsReady"/> = false.</exception>
        /// <exception cref="ArgumentException">Если <paramref name="imageBytes"/> пуст.</exception>
        Task<DTO.Rag.PageTextLayerDto> RecognizeWithLayoutAsync(
            byte[] imageBytes,
            int imageWidth,
            int imageHeight,
            CancellationToken cancellationToken = default);
    }
}