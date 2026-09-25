namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Один результат поиска в векторном хранилище
    /// (v1.5.0, KI-083, Шаг 2B).
    /// </summary>
    public class VectorSearchResult
    {
        /// <summary>
        /// ID чанка в БД (<c>DocumentChunk.Id</c>).
        /// </summary>
        public int ChunkId { get; set; }

        /// <summary>
        /// Cosine similarity. Обычно <c>0..1</c> (для нормализованных эмбеддингов),
        /// но может быть отрицательным для противоположных векторов.
        /// Чем выше — тем релевантнее.
        /// </summary>
        public float Score { get; set; }

        /// <summary>
        /// Метаданные чанка.
        /// </summary>
        public ChunkMetadata Metadata { get; set; }
    }
}