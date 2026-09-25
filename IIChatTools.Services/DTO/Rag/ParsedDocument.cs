using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Результат парсинга документа для RAG
    /// (v1.5.0, KI-083, Шаг 4A).
    ///
    /// <para>
    /// Промежуточная структура между парсером (<c>IRagDocumentParser</c>)
    /// и чанкером (<c>IChunkingStrategy</c>): содержит извлечённый текст
    /// + метаданные (формат, страницы, автор).
    /// </para>
    /// </summary>
    public class ParsedDocument
    {
        /// <summary>
        /// Полный текст документа (нормализованные переносы строк: <c>\n</c>).
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Метаданные: формат, количество страниц, автор, дата (опционально).
        /// Ключи — на английском (для стабильности JSON): <c>format</c>, <c>pageCount</c>, ...
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Размер исходного файла в байтах.
        /// </summary>
        public long OriginalSizeBytes { get; set; }

        /// <summary>
        /// Число «страниц» (PDF) или «секций» (по заголовкам MD).
        /// <c>null</c> для форматов, где страницы не применимы (TXT, CSV, ...).
        /// </summary>
        public int? PageCount { get; set; }
    }
}