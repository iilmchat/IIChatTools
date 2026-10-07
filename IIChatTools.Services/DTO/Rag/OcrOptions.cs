namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Настройки OCR-обработки (секция <c>Rag:Ingestion:Ocr</c> в appsettings).
    /// </summary>
    /// <remarks>
    /// v1.13.x (KI-203). См. <c>IOcrService</c>.
    /// </remarks>
    public class OcrOptions
    {
        /// <summary>
        /// Включён ли OCR-fallback. По умолчанию <c>false</c> (backward compat).
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Путь к tessdata (относительный — от ContentRoot).
        /// Содержит <c>rus.traineddata</c> + <c>eng.traineddata</c>.
        /// </summary>
        public string TessDataPath { get; set; } = "tools/tessdata";

        /// <summary>
        /// Языки распознавания (Tesseract формат: <c>rus+eng</c>).
        /// </summary>
        public string Languages { get; set; } = "rus+eng";

        /// <summary>
        /// DPI рендера PDF-страницы в PNG. 200 — стандарт (быстрее);
        /// 300 — точнее, ~2× медленнее. Range [100, 600].
        /// </summary>
        public int RenderDpi { get; set; } = 200;

        /// <summary>
        /// Минимум символов текстового слоя на странице, ниже которого
        /// страница считается сканом и уходит в OCR.
        /// </summary>
        public int MinTextCharsPerPage { get; set; } = 50;

        /// <summary>
        /// Максимум страниц PDF на OCR. Больше — fail-soft (остальные как есть).
        /// </summary>
        public int MaxPagesToOcr { get; set; } = 100;

        /// <summary>
        /// Таймаут OCR одной страницы (мс).
        /// </summary>
        public int PageTimeoutMs { get; set; } = 30000;
    }
}