namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Метаданные чанка, хранимые рядом с вектором в <c>IVectorStore</c>
    /// (v1.5.0, KI-083, Шаг 2B).
    ///
    /// <para>
    /// Embedding (вектор) хранится в <c>IVectorStore</c>, а сам текст
    /// чанка — в БД (<c>DocumentChunk.Text</c>). Metadata служит «мостиком»
    /// между вектором и БД: содержит ID чанка + поля для фильтрации
    /// (IndexName, ChatId, UserId) + поле Source для citations (KI-086, v1.6.0).
    /// </para>
    /// </summary>
    public class ChunkMetadata
    {
        /// <summary>
        /// ID чанка в БД (<c>DocumentChunk.Id</c>).
        /// </summary>
        public int DocumentChunkId { get; set; }

        /// <summary>
        /// Имя индекса (<c>project_docs</c>, <c>my_rag_docs</c>,
        /// <c>chat_history</c>, <c>workspace</c>).
        /// </summary>
        public string IndexName { get; set; }

        /// <summary>
        /// ID чата (для <c>my_rag_docs</c> / <c>chat_history</c>),
        /// <c>null</c> для глобальных индексов.
        /// </summary>
        public int? ChatId { get; set; }

        /// <summary>
        /// ID пользователя-владельца (<c>0</c> — маркер «глобальный чанк»).
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Относительный путь документа или URL.
        /// </summary>
        public string DocumentPath { get; set; }

        /// <summary>
        /// Позиция чанка в документе (0-based).
        /// </summary>
        public int ChunkIndex { get; set; }

        /// <summary>
        /// Источник для citations (KI-086, v1.6.0). Например,
        /// <c>docs/development/RULES.md § 4.33</c> или <c>contract.pdf (стр. 3)</c>.
        /// </summary>
        public string Source { get; set; }
    }
}