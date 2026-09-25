using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Rag;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты <see cref="InMemoryVectorStore"/>
    /// (v1.5.0, KI-083, Шаг 2C).
    ///
    /// <para>
    /// DESIGN § 4.2.4 (10 тестов) + 1 edge-case
    /// (<c>Remove_NonExisting_NoOp</c>) = 11.
    /// </para>
    /// </summary>
    public class InMemoryVectorStoreTests
    {
        /// <summary>Допуск для сравнения float-score.</summary>
        private const float Tolerance = 1e-5f;

        /// <summary>Хелпер: метаданные с заданным ChunkId.</summary>
        private static ChunkMetadata Meta(int chunkId, string index = "test")
            => new ChunkMetadata
            {
                DocumentChunkId = chunkId,
                IndexName = index,
                UserId = 1,
                DocumentPath = $"doc-{chunkId}.md",
                ChunkIndex = chunkId
            };

        /// <summary>
        /// Добавили вектор → поиск тем же вектором возвращает ту же запись со score ≈ 1.
        /// </summary>
        [Fact]
        public void Add_Then_Search_ReturnsSameVector()
        {
            var store = new InMemoryVectorStore();
            store.Add("test", 42, new[] { 1f, 0f, 0f }, Meta(42));

            var results = store.Search("test", new[] { 1f, 0f, 0f }, topK: 5);

            Assert.Single(results);
            Assert.Equal(42, results[0].ChunkId);
            Assert.Equal(1f, results[0].Score, Tolerance);
            Assert.Equal("doc-42.md", results[0].Metadata.DocumentPath);
        }

        /// <summary>
        /// Повторный Add с тем же ChunkId — overwrite (не дублируется).
        /// </summary>
        [Fact]
        public void Add_SameChunkId_Twice_Overwrites()
        {
            var store = new InMemoryVectorStore();
            store.Add("test", 42, new[] { 1f, 0f, 0f }, Meta(42));
            store.Add("test", 42, new[] { 0f, 1f, 0f }, Meta(42));

            Assert.Equal(1, store.Count("test"));

            // Поиск по первому вектору — score должен быть 0 (запись заменена).
            var results = store.Search("test", new[] { 1f, 0f, 0f }, topK: 5);

            Assert.Single(results);
            Assert.Equal(0f, results[0].Score, Tolerance);
        }

        /// <summary>
        /// Top-3 из 4 векторов, отсортированы по score (убывание).
        /// </summary>
        [Fact]
        public void Search_ReturnsTopK_SortedByScore()
        {
            var store = new InMemoryVectorStore();
            var query = new[] { 1f, 0f, 0f };

            store.Add("test", 1, new[] { 1f, 0f, 0f }, Meta(1));          // score = 1.0
            store.Add("test", 2, new[] { 0.7f, 0.7f, 0f }, Meta(2));      // score ≈ 0.707
            store.Add("test", 3, new[] { 0f, 1f, 0f }, Meta(3));          // score = 0.0
            store.Add("test", 4, new[] { -1f, 0f, 0f }, Meta(4));         // score = -1.0

            var results = store.Search("test", query, topK: 3);

            Assert.Equal(3, results.Count);
            Assert.Equal(1, results[0].ChunkId);
            Assert.Equal(2, results[1].ChunkId);
            Assert.Equal(3, results[2].ChunkId);
        }

        /// <summary>
        /// Пустой индекс (несуществующий) → пустой результат.
        /// </summary>
        [Fact]
        public void Search_EmptyIndex_ReturnsEmpty()
        {
            var store = new InMemoryVectorStore();

            var results = store.Search("no_such_index", new[] { 1f, 0f, 0f }, topK: 5);

            Assert.Empty(results);
        }

        /// <summary>
        /// Разные индексы изолированы: Add в один не влияет на другой.
        /// </summary>
        [Fact]
        public void Add_DifferentIndexes_IsolatedFromEachOther()
        {
            var store = new InMemoryVectorStore();
            store.Add("index_a", 1, new[] { 1f, 0f, 0f }, Meta(1, "index_a"));
            store.Add("index_b", 2, new[] { 1f, 0f, 0f }, Meta(2, "index_b"));

            Assert.Equal(1, store.Count("index_a"));
            Assert.Equal(1, store.Count("index_b"));

            var resA = store.Search("index_a", new[] { 1f, 0f, 0f }, topK: 5);
            Assert.Single(resA);
            Assert.Equal(1, resA[0].ChunkId);

            var resB = store.Search("index_b", new[] { 1f, 0f, 0f }, topK: 5);
            Assert.Single(resB);
            Assert.Equal(2, resB[0].ChunkId);
        }

        /// <summary>
        /// Remove удаляет вектор — Count уменьшается, Search не находит.
        /// </summary>
        [Fact]
        public void Remove_RemovesFromIndex()
        {
            var store = new InMemoryVectorStore();
            store.Add("test", 1, new[] { 1f, 0f, 0f }, Meta(1));
            store.Add("test", 2, new[] { 0f, 1f, 0f }, Meta(2));

            store.Remove("test", 1);

            Assert.Equal(1, store.Count("test"));
            var results = store.Search("test", new[] { 1f, 0f, 0f }, topK: 5);
            Assert.Single(results);
            Assert.Equal(2, results[0].ChunkId);
        }

        /// <summary>
        /// Remove несуществующего chunkId — no-op, не бросает.
        /// </summary>
        [Fact]
        public void Remove_NonExisting_NoOp()
        {
            var store = new InMemoryVectorStore();
            store.Add("test", 1, new[] { 1f, 0f, 0f }, Meta(1));

            var ex = Record.Exception(() => store.Remove("test", 999));

            Assert.Null(ex);
            Assert.Equal(1, store.Count("test"));
        }

        /// <summary>
        /// Clear очищает индекс полностью.
        /// </summary>
        [Fact]
        public void Clear_RemovesAllFromIndex()
        {
            var store = new InMemoryVectorStore();
            store.Add("test", 1, new[] { 1f, 0f, 0f }, Meta(1));
            store.Add("test", 2, new[] { 0f, 1f, 0f }, Meta(2));

            store.Clear("test");

            Assert.Equal(0, store.Count("test"));
            Assert.Empty(store.Search("test", new[] { 1f, 0f, 0f }, topK: 5));
        }

        /// <summary>
        /// Count корректно отражает Add/Remove.
        /// </summary>
        [Fact]
        public void Count_ReflectsAddsAndRemoves()
        {
            var store = new InMemoryVectorStore();

            Assert.Equal(0, store.Count("test"));

            store.Add("test", 1, new[] { 1f, 0f, 0f }, Meta(1));
            store.Add("test", 2, new[] { 0f, 1f, 0f }, Meta(2));
            store.Add("test", 3, new[] { 0f, 0f, 1f }, Meta(3));

            Assert.Equal(3, store.Count("test"));

            store.Remove("test", 2);
            Assert.Equal(2, store.Count("test"));
        }

        /// <summary>
        /// TopK ограничивает число результатов (10 векторов, topK=5 → 5).
        /// </summary>
        [Fact]
        public void Search_ReturnsAtMostTopK()
        {
            var store = new InMemoryVectorStore();
            var query = new[] { 1f, 0f, 0f };

            for (int i = 0; i < 10; i++)
            {
                // Все векторы одинаковой длины, но с разным углом к query.
                var angle = i * 0.1f;
                var vector = new[] { (float)Math.Cos(angle), (float)Math.Sin(angle), 0f };
                store.Add("test", i, vector, Meta(i));
            }

            var results = store.Search("test", query, topK: 5);

            Assert.Equal(5, results.Count);
        }

        /// <summary>
        /// Параллельные Add (1000 задач) — нет race condition, Count корректен.
        /// </summary>
        [Fact]
        public void ConcurrentAdds_ThreadSafe()
        {
            var store = new InMemoryVectorStore();
            var indices = Enumerable.Range(0, 1000).ToArray();

            Parallel.ForEach(indices, i =>
            {
                var vector = new[] { (float)i, (float)(i % 7 + 1), 1f };
                store.Add("concurrent", i, vector, Meta(i, "concurrent"));
            });

            Assert.Equal(1000, store.Count("concurrent"));
        }
    }
}