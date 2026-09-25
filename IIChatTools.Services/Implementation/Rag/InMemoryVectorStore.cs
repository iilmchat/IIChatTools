using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// In-memory реализация <see cref="IVectorStore"/>
    /// (v1.5.0, KI-083, Шаг 2C).
    ///
    /// <para>
    /// <b>Singleton.</b> Хранит векторы в RAM, теряет их при рестарте приложения.
    /// После рестарта — пустые индексы; для <c>project_docs</c> возможна
    /// авто-переиндексация (см. <c>Rag:AutoIndexProjectDocs</c>), для остальных —
    /// пользователь/LLM переиндексирует вручную.
    /// </para>
    ///
    /// <para>
    /// <b>Потокобезопасность:</b> на каждом индексе — <see cref="ReaderWriterLockSlim"/>.
    /// Параллельные <see cref="Search"/> работают параллельно (read-lock),
    /// <see cref="Add"/>/<see cref="Remove"/>/<see cref="Clear"/> — эксклюзивно (write-lock).
    /// </para>
    ///
    /// <para>
    /// <b>Структура:</b> <c>Dictionary&lt;int, VectorEntry&gt;</c> (а не List) —
    /// даёт O(1) на Add/Remove по ChunkId. Поиск всё равно O(N) с dot product,
    /// но для 10k × 768 dim это ~7.7M multiply-add = &lt;50 мс на CPU.
    /// </para>
    /// </summary>
    public sealed class InMemoryVectorStore : IVectorStore, IDisposable
    {
        /// <summary>
        /// Индексы: имя → <see cref="IndexData"/>. Ordinal comparer — имена
        /// индексов чувствительны к регистру (project_docs ≠ PROJECT_DOCS).
        /// </summary>
        private readonly ConcurrentDictionary<string, IndexData> _indexes =
            new ConcurrentDictionary<string, IndexData>(StringComparer.Ordinal);

        /// <summary>Признак disposed (защита от использования после Dispose).</summary>
        private bool _disposed;

        /// <inheritdoc />
        public void Add(string indexName, int chunkId, float[] vector, ChunkMetadata metadata)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("Имя индекса не может быть пустым", nameof(indexName));

            if (vector == null)
                throw new ArgumentNullException(nameof(vector));

            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            // Нормализуем вектор — в Search используется dot product
            // (после L2-нормализации cosine similarity == dot product).
            var normalized = VectorMath.L2Normalize(vector);

            var index = _indexes.GetOrAdd(indexName, _ => new IndexData());

            index.Lock.EnterWriteLock();
            try
            {
                // Overwrite семантика: повторный Add с тем же chunkId
                // заменяет запись (для переиндексации).
                index.Entries[chunkId] = new VectorEntry
                {
                    ChunkId = chunkId,
                    NormalizedVector = normalized,
                    Metadata = metadata
                };
            }
            finally
            {
                index.Lock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<VectorSearchResult> Search(
            string indexName, float[] queryVector, int topK = 5)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(indexName))
                return Array.Empty<VectorSearchResult>();

            if (queryVector == null)
                throw new ArgumentNullException(nameof(queryVector));

            if (topK <= 0)
                return Array.Empty<VectorSearchResult>();

            if (!_indexes.TryGetValue(indexName, out var index))
                return Array.Empty<VectorSearchResult>();

            // Нормализуем query вне блокировки (чистая функция от queryVector).
            var normalizedQuery = VectorMath.L2Normalize(queryVector);

            index.Lock.EnterReadLock();
            try
            {
                if (index.Entries.Count == 0)
                    return Array.Empty<VectorSearchResult>();

                // Считаем score для всех, затем partial sort.
                var scored = new List<VectorSearchResult>(index.Entries.Count);
                foreach (var entry in index.Entries.Values)
                {
                    var score = VectorMath.DotProduct(normalizedQuery, entry.NormalizedVector);
                    scored.Add(new VectorSearchResult
                    {
                        ChunkId = entry.ChunkId,
                        Score = score,
                        Metadata = entry.Metadata
                    });
                }

                return scored
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();
            }
            finally
            {
                index.Lock.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public void Remove(string indexName, int chunkId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(indexName))
                return;

            if (!_indexes.TryGetValue(indexName, out var index))
                return;

            index.Lock.EnterWriteLock();
            try
            {
                index.Entries.Remove(chunkId);
            }
            finally
            {
                index.Lock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public void Clear(string indexName)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(indexName))
                return;

            if (!_indexes.TryGetValue(indexName, out var index))
                return;

            index.Lock.EnterWriteLock();
            try
            {
                index.Entries.Clear();
            }
            finally
            {
                index.Lock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public int Count(string indexName)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(indexName))
                return 0;

            if (!_indexes.TryGetValue(indexName, out var index))
                return 0;

            index.Lock.EnterReadLock();
            try
            {
                return index.Entries.Count;
            }
            finally
            {
                index.Lock.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetIndexNames()
        {
            ThrowIfDisposed();
            return _indexes.Keys.ToList();
        }

        /// <summary>
        /// Освобождает <see cref="ReaderWriterLockSlim"/> каждого индекса.
        /// Вызывается хостом при shutdown приложения (Singleton disposal).
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var index in _indexes.Values)
            {
                index.Dispose();
            }
            _indexes.Clear();
        }

        /// <summary>
        /// Бросает <see cref="ObjectDisposedException"/>, если store уже освобождён.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(InMemoryVectorStore));
        }

        /// <summary>
        /// Данные одного индекса: словарь чанков + reader/writer lock.
        /// </summary>
        private sealed class IndexData : IDisposable
        {
            /// <summary>
            /// Векторы индекса: chunkId → запись. Dictionary — O(1) Add/Remove.
            /// </summary>
            public Dictionary<int, VectorEntry> Entries { get; } = new Dictionary<int, VectorEntry>();

            /// <summary>
            /// Блокировка: read — параллельные Search, write — эксклюзивный Add/Remove/Clear.
            /// </summary>
            public ReaderWriterLockSlim Lock { get; } = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

            /// <inheritdoc />
            public void Dispose() => Lock.Dispose();
        }

        /// <summary>
        /// Одна запись индекса: ChunkId + нормализованный вектор + метаданные.
        /// </summary>
        private sealed class VectorEntry
        {
            /// <summary>ID чанка в БД (DocumentChunk.Id).</summary>
            public int ChunkId { get; init; }

            /// <summary>L2-нормализованный вектор (длина = 1).</summary>
            public float[] NormalizedVector { get; init; }

            /// <summary>Метаданные чанка.</summary>
            public ChunkMetadata Metadata { get; init; }
        }
    }
}