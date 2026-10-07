namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Прогресс OCR-обработки документа (KI-204).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Формируется <c>OcrProgressTracker</c> (Singleton) и отдаётся клиенту
    /// через <c>ChatAttachmentsController.GetListAsync</c> — для polling'а
    /// во время upload'а большого PDF-скана.
    /// </para>
    /// <para>
    /// <b>Фронт:</b> chip attachment'а показывает «OCR: 2/3 страниц», пока
    /// <see cref="IsProcessing"/> = <c>true</c>. Как только <c>false</c>
    /// (или список пуст) — polling останавливается.
    /// </para>
    /// </remarks>
    public class OcrProgressDto
    {
        /// <summary>
        /// <c>true</c> — OCR ещё идёт; <c>false</c> — завершён (не показываем чип).
        /// </summary>
        public bool IsProcessing { get; set; }

        /// <summary>
        /// Номер текущей обрабатываемой страницы (1-based).
        /// </summary>
        public int CurrentPage { get; set; }

        /// <summary>
        /// Всего страниц в PDF.
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Имя файла (для рендера).
        /// </summary>
        public string FileName { get; set; }
    }
}