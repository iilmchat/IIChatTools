using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Результат поиска в векторном индексе
    /// (v1.5.0, KI-083, Шаг 5A).
    ///
    /// <para>
    /// Возвращается <see cref="Interfaces.IRetrievalService.SearchAsync"/> —
    /// содержит текст чанка, score, путь документа и метаданные для citations (KI-086).
    /// </para>
    /// </summary>
    public class RetrievedChunkDto
    {
        /// <summary>
        /// ID чанка в БД (<c>DocumentChunk.Id</c>).
        /// </summary>
        public int ChunkId { get; set; }

        /// <summary>
        /// Текст чанка (для вставки в промпт LLM).
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Cosine similarity (0..1). Чем выше — тем релевантнее.
        /// </summary>
        public float Score { get; set; }

        /// <summary>
        /// Относительный путь документа (или URL).
        /// </summary>
        public string DocumentPath { get; set; }

        /// <summary>
        /// Порядковый номер чанка в документе (0-based).
        /// </summary>
        public int ChunkIndex { get; set; }

        /// <summary>
        /// Имя индекса (<c>project_docs</c>, <c>my_rag_docs</c>, ...).
        /// </summary>
        public string IndexName { get; set; }

        /// <summary>
        /// Доп. метаданные из <c>DocumentChunk.MetadataJson</c>
        /// (например, <c>{ "page": "3", "section": "..." }</c>).
        /// Пустой словарь, если метаданных не было.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
}