using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Rag;
using IIChatTools.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты <see cref="RetrievalService"/>
    /// (v1.5.0, KI-083, Шаг 5A).
    ///
    /// <para>
    /// 7 тестов из DESIGN § 4.8.5. Реальный <see cref="InMemoryVectorStore"/>,
    /// реальный <c>AppDbContext</c> (через <see cref="TestDbContextFactory"/>),
    /// fake <see cref="FakeEmbeddingService"/>.
    /// </para>
    /// </summary>
    public class RetrievalServiceTests
    {
        /// <summary>
        /// Создаёт сервис с реальными зависимостями (store, db) и fake-embedding.
        /// </summary>
        private static (RetrievalService Service, InMemoryVectorStore Store, Data.AppDbContext Db, FakeEmbeddingService Embedding)
            CreateService(IDictionary<string, string> configOverrides = null)
        {
            var db = TestDbContextFactory.Create();
            var store = new InMemoryVectorStore();
            var embedding = new FakeEmbeddingService();

            var settings = new Dictionary<string, string>
            {
                ["Rag:Retrieval:DefaultTopK"] = "5",
                ["Rag:Retrieval:OverFetchMultiplier"] = "2",
                ["Rag:Retrieval:MinScore"] = "0.3"
            };

            if (configOverrides != null)
            {
                foreach (var kv in configOverrides)
                    settings[kv.Key] = kv.Value;
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var service = new RetrievalService(
                embedding,
                store,
                db,
                config,
                NullLogger<RetrievalService>.Instance);

            return (service, store, db, embedding);
        }

        /// <summary>
        /// Хелпер: добавляет чанк в БД + векторный стор.
        /// </summary>
        private static async Task<int> AddChunkAsync(
            Data.AppDbContext db,
            InMemoryVectorStore store,
            string indexName,
            string text,
            float[] vector,
            int? chatId = null,
            int userId = 1,
            string metadataJson = null,
            string documentPath = "doc.md",
            int chunkIndex = 0)
        {
            var entity = new DocumentChunk
            {
                IndexName = indexName,
                ChatId = chatId,
                UserId = userId,
                DocumentPath = documentPath,
                DocumentHash = "hash-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                ChunkIndex = chunkIndex,
                Text = text,
                Tokens = 10,
                MetadataJson = metadataJson
            };
            db.DocumentChunks.Add(entity);
            await db.SaveChangesAsync();

            var metadata = new ChunkMetadata
            {
                DocumentChunkId = entity.Id,
                IndexName = indexName,
                ChatId = chatId,
                UserId = userId,
                DocumentPath = documentPath,
                ChunkIndex = chunkIndex,
                Source = documentPath
            };
            store.Add(indexName, entity.Id, vector, metadata);

            return entity.Id;
        }

        // ============================================================
        // Тесты
        // ============================================================

        /// <summary>
        /// Пустой query → ArgumentException.
        /// </summary>
        [Fact]
        public async Task SearchAsync_EmptyQuery_Throws()
        {
            var (service, _, _, _) = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SearchAsync("", "test"));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SearchAsync("   ", "test"));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SearchAsync("hello", ""));
        }

        /// <summary>
        /// Пустой индекс → пустой результат.
        /// </summary>
        [Fact]
        public async Task SearchAsync_NoVectors_ReturnsEmpty()
        {
            var (service, _, _, _) = CreateService();

            var result = await service.SearchAsync("hello", "no_such_index");

            Assert.Empty(result);
        }

        /// <summary>
        /// Top-K сортирует по score (убывание) и ограничивает число результатов.
        /// </summary>
        [Fact]
        public async Task SearchAsync_ReturnsTopK_SortedByScore()
        {
            var (service, store, db, embedding) = CreateService();

            // Query: [1, 0, 0].
            // Документы: [1,0,0] score=1.0; [0.7,0.7,0] score≈0.707; [0,1,0] score=0.
            embedding.SetVector("query", new[] { 1f, 0f, 0f });

            await AddChunkAsync(db, store, "test", "best", new[] { 1f, 0f, 0f }, documentPath: "a.md");
            await AddChunkAsync(db, store, "test", "medium", new[] { 0.7f, 0.7f, 0f }, documentPath: "b.md");
            await AddChunkAsync(db, store, "test", "worst", new[] { 0f, 1f, 0f }, documentPath: "c.md");

            var result = await service.SearchAsync("query", "test", topK: 2);

            Assert.Equal(2, result.Count);
            Assert.Equal("a.md", result[0].DocumentPath);  // score = 1.0
            Assert.Equal("b.md", result[1].DocumentPath);  // score ≈ 0.707
            Assert.True(result[0].Score > result[1].Score);
        }

        /// <summary>
        /// MinScore отсеивает нерелевантные чанки.
        /// </summary>
        [Fact]
        public async Task SearchAsync_RespectsMinScore()
        {
            var (service, store, db, embedding) = CreateService(configOverrides:
                new Dictionary<string, string>
                {
                    ["Rag:Retrieval:MinScore"] = "0.9"
                });

            embedding.SetVector("query", new[] { 1f, 0f, 0f });

            // Вектор ортогонален query → score = 0 < 0.9 → отсеивается.
            await AddChunkAsync(db, store, "test", "orthogonal", new[] { 0f, 1f, 0f });

            var result = await service.SearchAsync("query", "test");

            Assert.Empty(result);
        }

        /// <summary>
        /// Фильтр по ChatId: возвращаются только чанки нужного чата.
        /// </summary>
        [Fact]
        public async Task SearchAsync_FiltersByChatId()
        {
            var (service, store, db, embedding) = CreateService(configOverrides:
                new Dictionary<string, string>
                {
                    ["Rag:Retrieval:MinScore"] = "0"
                });

            embedding.SetVector("query", new[] { 1f, 0f, 0f });

            await AddChunkAsync(db, store, "my_rag_docs", "chat1 doc", new[] { 1f, 0f, 0f },
                chatId: 1, documentPath: "chat1.md");
            await AddChunkAsync(db, store, "my_rag_docs", "chat2 doc", new[] { 1f, 0f, 0f },
                chatId: 2, documentPath: "chat2.md");

            var result = await service.SearchAsync("query", "my_rag_docs", chatId: 1);

            Assert.Single(result);
            Assert.Equal("chat1.md", result[0].DocumentPath);
        }

        /// <summary>
        /// Фильтр по UserId: возвращаются только чанки нужного пользователя.
        /// </summary>
        [Fact]
        public async Task SearchAsync_FiltersByUserId()
        {
            var (service, store, db, embedding) = CreateService(configOverrides:
                new Dictionary<string, string>
                {
                    ["Rag:Retrieval:MinScore"] = "0"
                });

            embedding.SetVector("query", new[] { 1f, 0f, 0f });

            await AddChunkAsync(db, store, "workspace", "user1 doc", new[] { 1f, 0f, 0f },
                userId: 1, documentPath: "u1.md");
            await AddChunkAsync(db, store, "workspace", "user2 doc", new[] { 1f, 0f, 0f },
                userId: 2, documentPath: "u2.md");

            var result = await service.SearchAsync("query", "workspace", userId: 2);

            Assert.Single(result);
            Assert.Equal("u2.md", result[0].DocumentPath);
        }

        /// <summary>
        /// Метаданные из БД (MetadataJson) попадают в RetrievedChunkDto.Metadata.
        /// </summary>
        [Fact]
        public async Task SearchAsync_EnrichesMetadataFromDb()
        {
            var (service, store, db, embedding) = CreateService(configOverrides:
                new Dictionary<string, string>
                {
                    ["Rag:Retrieval:MinScore"] = "0"
                });

            embedding.SetVector("query", new[] { 1f, 0f, 0f });

            await AddChunkAsync(db, store, "test", "text with metadata", new[] { 1f, 0f, 0f },
                metadataJson: "{\"page\":\"3\",\"section\":\"intro\"}",
                documentPath: "doc.md");

            var result = await service.SearchAsync("query", "test");

            Assert.Single(result);
            Assert.Equal(2, result[0].Metadata.Count);
            Assert.Equal("3", result[0].Metadata["page"]);
            Assert.Equal("intro", result[0].Metadata["section"]);
        }
    }
}