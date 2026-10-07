namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Опции парсинга документа для RAG (v1.13.x, KI-205).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Расширяемый DTO — добавляется в <c>IRagDocumentParser.ParseAsync</c>
    /// как overload. Default-метод (C# 8+) сохраняет обратную совместимость:
    /// старые вызовы <c>ParseAsync(file, ct)</c> продолжают работать.
    /// </para>
    /// </remarks>
    public class ParseOptions
    {
        /// <summary>
        /// Директория для сохранения PNG-страниц (KI-205).
        /// <c>null</c> — не сохранять (default, prod-behavior).
        /// <para>
        /// Используется <c>PdfParser</c>: при заданном значении каждая
        /// отрендеренная страница сохраняется как <c>{dir}/page-N.png</c>.
        /// Директория создаётся вызывающим кодом
        /// (<c>DocumentIngestionService</c>) — парсер её не создаёт.
        /// </para>
        /// </summary>
        public string SavePagesDirectory { get; set; }

        /// <summary>
        /// DPI для рендера страниц. <c>null</c> — использовать OcrOptions.RenderDpi.
        /// </summary>
        public int? RenderDpi { get; set; }
    }
}