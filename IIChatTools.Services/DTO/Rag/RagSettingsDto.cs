namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Настройки RAG для админки
    /// (v1.5.0, KI-083, Шаг 7A).
    ///
    /// <para>
    /// Persist в <c>AppSettings</c> (override'ы поверх appsettings.json).
    /// Ключи: <c>Rag.Chunking.Strategy</c>, <c>Rag.Chunking.ChunkSize</c> и т.д.
    /// </para>
    /// </summary>
    public class RagSettingsDto
    {
        /// <summary>
        /// Стратегия чанкинга (<c>recursive</c> / <c>sentence</c> / <c>fixed</c>).
        /// </summary>
        public string ChunkingStrategy { get; set; } = "recursive";

        /// <summary>Размер чанка в токенах (100–2000).</summary>
        public int ChunkSize { get; set; } = 500;

        /// <summary>Перекрытие между чанками (0–500 токенов).</summary>
        public int ChunkOverlap { get; set; } = 64;

        /// <summary>Минимальный размер чанка (токенов).</summary>
        public int MinChunkSize { get; set; } = 100;

        /// <summary>Количество top-K по умолчанию для retrieval (1–20).</summary>
        public int DefaultTopK { get; set; } = 5;

        /// <summary>Минимальный cosine score (0.0–1.0).</summary>
        public float MinScore { get; set; } = 0.3f;

        /// <summary>Модель эмбеддингов LM Studio.</summary>
        public string EmbeddingModel { get; set; } = "text-embedding-nomic-embed-text-v1.5";

        /// <summary>Индексировать <c>project_docs</c> при старте приложения.</summary>
        public bool AutoIndexProjectDocs { get; set; }
    }
}