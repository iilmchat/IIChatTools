using System.Collections.Generic;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Векторное хранилище для RAG (v1.5.0, KI-083).
    ///
    /// <para>
    /// MVP (v1.5.0): <c>InMemoryVectorStore</c> (Singleton).
    /// v1.5.x: Qdrant (при достижении 100k+ чанков).
    /// </para>
    ///
    /// <para>
    /// Все методы потокобезопасны. Контракт: <c>Add</c> / <c>Remove</c> /
    /// <c>Clear</c> берут эксклюзивную блокировку; <c>Search</c> / <c>Count</c> —
    /// разделяемую. Реализация вправе нормализовать входящие векторы (L2)
    /// для оптимизации поиска.
    /// </para>
    /// </summary>
    public interface IVectorStore
    {
        /// <summary>
        /// Добавляет вектор в индекс.
        /// </summary>
        /// <param name="indexName">Имя индекса (project_docs, my_rag_docs, ...)</param>
        /// <param name="chunkId">ID чанка в БД (<c>DocumentChunk.Id</c>)</param>
        /// <param name="vector">Вектор (нормализуется внутри — L2)</param>
        /// <param name="metadata">Метаданные (источник, offset, docId)</param>
        void Add(string indexName, int chunkId, float[] vector, ChunkMetadata metadata);

        /// <summary>
        /// Ищет top-K ближайших чанков по cosine similarity.
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <param name="queryVector">Вектор запроса (нормализуется внутри — L2)</param>
        /// <param name="topK">Количество результатов (по умолчанию 5)</param>
        /// <returns>Отсортированный по score (убывание) список</returns>
        IReadOnlyList<VectorSearchResult> Search(string indexName, float[] queryVector, int topK = 5);

        /// <summary>
        /// Удаляет вектор по <paramref name="chunkId"/>.
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <param name="chunkId">ID чанка в БД</param>
        void Remove(string indexName, int chunkId);

        /// <summary>
        /// Полностью очищает индекс.
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        void Clear(string indexName);

        /// <summary>
        /// Количество векторов в индексе.
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <returns>Число векторов (<c>0</c>, если индекс не существует)</returns>
        int Count(string indexName);

        /// <summary>
        /// Список активных индексов.
        /// </summary>
        /// <returns>Имена индексов (может быть пустым)</returns>
        IReadOnlyList<string> GetIndexNames();
    }
}