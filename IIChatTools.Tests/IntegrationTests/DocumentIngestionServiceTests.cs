using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.Rag;
using IIChatTools.Services.Implementation.Rag.Parsers;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты <see cref="DocumentIngestionService"/>
    /// (v1.5.0, KI-083, Шаг 4C.3).
    ///
    /// <para>
    /// 11 тестов из DESIGN § 4.3.5. Используют реальные
    /// <see cref="PlainTextParser"/>, <see cref="RagDocumentParserRegistry"/>,
    /// <see cref="RecursiveChunkingStrategy"/>, <see cref="ChunkingStrategyResolver"/>,
    /// <see cref="InMemoryVectorStore"/>, <see cref="TokenCounter"/>.
    /// <see cref="IEmbeddingService"/> заменён на <see cref="FakeEmbeddingService"/>
    /// (детерминированные векторы, без HTTP к LM Studio).
    /// </para>
    ///
    /// <para>
    /// <see cref="AppDbContext"/> — через <see cref="TestDbContextFactory"/>
    /// (InMemory-провайдер, 3 пользователя).
    /// </para>
    /// </summary>
    public class DocumentIngestionServiceTests
    {
        // ============================================================
        // Fake-зависимости
        // ============================================================

        /// <summary>
        /// Fake-сервис эмбеддингов: возвращает детерминированный
        /// ненулевой вектор для каждого текста (без HTTP).
        /// </summary>
        private sealed class FakeEmbeddingService : IEmbeddingService
        {
            /// <summary>Размерность (маленькая — тесты быстрее).</summary>
            public int Dimensions => 3;

            /// <summary>Счётчик вызовов (для ассертов).</summary>
            public int CallCount { get; private set; }

            public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
                => Task.FromResult(VectorFromText(text));

            public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
                IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
            {
                CallCount++;
                var result = new List<float[]>(texts.Count);
                foreach (var t in texts)
                    result.Add(VectorFromText(t));
                return Task.FromResult<IReadOnlyList<float[]>>(result);
            }

            /// <summary>
            /// Детерминированный ненулевой вектор из текста.
            /// Разные тексты → разные векторы (в первых 2 компонентах).
            /// </summary>
            private static float[] VectorFromText(string text)
            {
                var hash = (text ?? string.Empty).GetHashCode();
                // Компонент [0] = 1.0f — гарантирует ненулевую норму.
                return new[]
                {
                    1.0f,
                    Math.Abs(hash % 1000) / 1000.0f,
                    Math.Abs((hash / 1000) % 1000) / 1000.0f
                };
            }
        }

        // ============================================================
        // Хелперы
        // ============================================================

        /// <summary>
        /// Создаёт тестовый экземпляр сервиса с реальными зависимостями
        /// (кроме fake-embedding).
        /// </summary>
        /// <param name="vectorStore">Векторный стор (по умолчанию — новый InMemory)</param>
        /// <param name="configOverrides">Опциональные overrides конфига</param>
        private static (DocumentIngestionService Service,
                        AppDbContext Db,
                        InMemoryVectorStore VectorStore,
                        FakeEmbeddingService Embedding)
            CreateService(
                InMemoryVectorStore vectorStore = null,
                IDictionary<string, string> configOverrides = null)
        {
            var db = TestDbContextFactory.Create();

            var parsers = new IRagDocumentParser[] { new PlainTextParser() };
            var parserRegistry = new RagDocumentParserRegistry(parsers);

            var chunkingResolver = new ChunkingStrategyResolver(new IChunkingStrategy[]
            {
                new RecursiveChunkingStrategy(),
                new SentenceChunkingStrategy(),
                new FixedChunkingStrategy()
            });

            var embedding = new FakeEmbeddingService();
            var vs = vectorStore ?? new InMemoryVectorStore();
            var tokenCounter = new TokenCounter();

            // Базовый конфиг: маленькие чанки, fixed-стратегия — тесты быстрее и детерминированнее.
            var settings = new Dictionary<string, string>
            {
                ["Rag:Chunking:Strategy"] = "fixed",
                ["Rag:Chunking:ChunkSize"] = "10",
                ["Rag:Chunking:ChunkOverlap"] = "0",
                ["Rag:Chunking:MinChunkSize"] = "1",
                ["Rag:Ingestion:MaxFileSizeBytes"] = "1048576"   // 1 MB
            };

            if (configOverrides != null)
            {
                foreach (var kv in configOverrides)
                    settings[kv.Key] = kv.Value;
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var service = new DocumentIngestionService(
                parserRegistry,
                chunkingResolver,
                embedding,
                vs,
                tokenCounter,
                db,
                config,
                NullLogger<DocumentIngestionService>.Instance);

            return (service, db, vs, embedding);
        }

        /// <summary>
        /// Хелпер: создаёт временный файл с заданным расширением и текстом.
        /// </summary>
        private static string WriteTempFile(string extension, string content)
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + extension);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// Текст достаточной длины, чтобы fixed-стратегия дала несколько чанков.
        /// </summary>
        private const string LongText =
            "alpha beta gamma delta epsilon zeta eta theta iota kappa " +
            "lambda mu nu xi omicron pi rho sigma tau upsilon phi chi psi omega";

        // ============================================================
        // Тесты: IngestAsync (Text source)
        // ============================================================

        /// <summary>
        /// Индексация текста создаёт чанки в БД.
        /// </summary>
        [Fact]
        public async Task IngestAsync_Text_CreatesChunks()
        {
            var (service, db, _, _) = CreateService();

            var result = await service.IngestAsync(new IngestionRequest
            {
                IndexName = "test",
                SourceType = IngestionSourceType.Text,
                Text = LongText,
                UserId = 1
            });

            Assert.False(result.Skipped);
            Assert.True(result.DocumentChunksCreated > 0);
            Assert.Equal(result.DocumentChunksCreated, db.DocumentChunks.Count());
            Assert.StartsWith("text://", result.DocumentPath);
            Assert.False(string.IsNullOrEmpty(result.DocumentHash));
        }

        /// <summary>
        /// Индексация файла (.txt) создаёт чанки в БД.
        /// </summary>
        [Fact]
        public async Task IngestAsync_File_CreatesChunks()
        {
            var path = WriteTempFile(".txt", LongText);
            try
            {
                var (service, db, _, _) = CreateService();

                var result = await service.IngestAsync(new IngestionRequest
                {
                    IndexName = "test",
                    SourceType = IngestionSourceType.File,
                    FilePath = path,
                    UserId = 1
                });

                Assert.False(result.Skipped);
                Assert.True(result.DocumentChunksCreated > 0);
                Assert.Equal(result.DocumentChunksCreated, db.DocumentChunks.Count());
                Assert.Equal(path, result.DocumentPath);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Неподдерживаемый формат → ArgumentException.
        /// </summary>
        [Fact]
        public async Task IngestAsync_UnsupportedFormat_Throws()
        {
            // Создаём файл с расширением, которое PlainTextParser не поддерживает.
            var path = WriteTempFile(".xyz", "some content");
            try
            {
                var (service, _, _, _) = CreateService();

                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.IngestAsync(new IngestionRequest
                    {
                        IndexName = "test",
                        SourceType = IngestionSourceType.File,
                        FilePath = path,
                        UserId = 1
                    }));

                Assert.Contains("не поддерживается", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Файл больше лимита MaxFileSizeBytes → ArgumentException.
        /// Лимит задаём маленьким (10 байт), чтобы не создавать большой файл.
        /// </summary>
        [Fact]
        public async Task IngestAsync_FileTooLarge_Throws()
        {
            var path = WriteTempFile(".txt", "content longer than 10 bytes");
            try
            {
                var (service, _, _, _) = CreateService(configOverrides:
                    new Dictionary<string, string>
                    {
                        ["Rag:Ingestion:MaxFileSizeBytes"] = "10"
                    });

                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.IngestAsync(new IngestionRequest
                    {
                        IndexName = "test",
                        SourceType = IngestionSourceType.File,
                        FilePath = path,
                        UserId = 1
                    }));

                Assert.Contains("слишком большой", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ============================================================
        // Тесты: Идемпотентность (hash)
        // ============================================================

        /// <summary>
        /// Повторная индексация того же текста без ForceReindex → Skipped=true.
        /// </summary>
        [Fact]
        public async Task IngestAsync_SameHash_SkipsWhenNotForced()
        {
            var (service, db, _, _) = CreateService();

            var request = new IngestionRequest
            {
                IndexName = "test",
                SourceType = IngestionSourceType.Text,
                Text = LongText,
                UserId = 1
            };

            var first = await service.IngestAsync(request);
            var chunksAfterFirst = db.DocumentChunks.Count();

            var second = await service.IngestAsync(request);

            Assert.False(first.Skipped);
            Assert.True(second.Skipped);
            Assert.Equal(0, second.DocumentChunksCreated);
            Assert.NotNull(second.SkipReason);

            // В БД по-прежнему столько же чанков (не продублировались).
            Assert.Equal(chunksAfterFirst, db.DocumentChunks.Count());
        }

        /// <summary>
        /// Повторная индексация того же текста с ForceReindex → Skipped=false,
        /// чанки пересозданы (не дублируются).
        /// </summary>
        [Fact]
        public async Task IngestAsync_SameHash_ReindexesWhenForced()
        {
            var (service, db, _, _) = CreateService();

            var baseRequest = new IngestionRequest
            {
                IndexName = "test",
                SourceType = IngestionSourceType.Text,
                Text = LongText,
                UserId = 1
            };

            var first = await service.IngestAsync(baseRequest);
            var chunksAfterFirst = db.DocumentChunks.Count();

            baseRequest.ForceReindex = true;
            var second = await service.IngestAsync(baseRequest);

            Assert.False(first.Skipped);
            Assert.False(second.Skipped);
            Assert.True(second.DocumentChunksCreated > 0);

            // Не дубликаты — столько же чанков.
            Assert.Equal(chunksAfterFirst, db.DocumentChunks.Count());
        }

        // ============================================================
        // Тесты: Vector Store интеграция
        // ============================================================

        /// <summary>
        /// После индексации в векторном сторе появляются записи.
        /// </summary>
        [Fact]
        public async Task IngestAsync_UpdatesVectorStore()
        {
            var (service, _, vs, _) = CreateService();

            var result = await service.IngestAsync(new IngestionRequest
            {
                IndexName = "test",
                SourceType = IngestionSourceType.Text,
                Text = LongText,
                UserId = 1
            });

            Assert.True(result.DocumentChunksCreated > 0);
            Assert.Equal(result.DocumentChunksCreated, vs.Count("test"));
        }

        // ============================================================
        // Тесты: DeleteDocumentAsync
        // ============================================================

        /// <summary>
        /// DeleteDocumentAsync удаляет чанки из БД.
        /// </summary>
        [Fact]
        public async Task DeleteDocumentAsync_RemovesChunksFromDb()
        {
            var (service, db, _, _) = CreateService();

            var ingest = await service.IngestAsync(new IngestionRequest
            {
                IndexName = "test",
                SourceType = IngestionSourceType.Text,
                Text = LongText,
                UserId = 1
            });

            var removed = await service.DeleteDocumentAsync("test", ingest.DocumentPath);

            Assert.Equal(ingest.DocumentChunksCreated, removed);
            Assert.Equal(0, db.DocumentChunks.Count());
        }

        /// <summary>
        /// DeleteDocumentAsync удаляет векторы из IVectorStore.
        /// </summary>
        [Fact]
        public async Task DeleteDocumentAsync_RemovesVectorsFromStore()
        {
            var (service, _, vs, _) = CreateService();

            var ingest = await service.IngestAsync(new IngestionRequest
            {
                IndexName = "test",
                SourceType = IngestionSourceType.Text,
                Text = LongText,
                UserId = 1
            });

            Assert.True(vs.Count("test") > 0);

            await service.DeleteDocumentAsync("test", ingest.DocumentPath);

            Assert.Equal(0, vs.Count("test"));
        }

        // ============================================================
        // Тесты: ClearIndexAsync
        // ============================================================

        /// <summary>
        /// ClearIndexAsync удаляет все чанки и векторы индекса.
        /// </summary>
        [Fact]
        public async Task ClearIndexAsync_RemovesAllChunks()
        {
            var (service, db, vs, _) = CreateService();

            // Два разных документа в один индекс.
            await service.IngestAsync(new IngestionRequest
            {
                IndexName = "test",
                SourceType = IngestionSourceType.Text,
                Text = "first document content with several tokens",
                UserId = 1
            });
            await service.IngestAsync(new IngestionRequest
            {
                IndexName = "test",
                SourceType = IngestionSourceType.Text,
                Text = "second document content with several tokens",
                UserId = 1
            });

            Assert.True(db.DocumentChunks.Count() > 0);
            Assert.True(vs.Count("test") > 0);

            var removed = await service.ClearIndexAsync("test");

            Assert.True(removed > 0);
            Assert.Equal(0, db.DocumentChunks.Count());
            Assert.Equal(0, vs.Count("test"));
        }

        // ============================================================
        // Тесты: Изоляция по ChatId
        // ============================================================

        /// <summary>
        /// Чанки с разными ChatId изолированы: одинаковый текст в двух чатах
        /// индексируется в каждый отдельно.
        /// </summary>
        [Fact]
        public async Task IngestAsync_ChatIdScoped_IsolatesPerChat()
        {
            var (service, db, _, _) = CreateService();

            await service.IngestAsync(new IngestionRequest
            {
                IndexName = "my_rag_docs",
                SourceType = IngestionSourceType.Text,
                Text = LongText,
                UserId = 1,
                ChatId = 1
            });

            await service.IngestAsync(new IngestionRequest
            {
                IndexName = "my_rag_docs",
                SourceType = IngestionSourceType.Text,
                Text = LongText,
                UserId = 1,
                ChatId = 2
            });

            var chunksChat1 = db.DocumentChunks.Count(c => c.ChatId == 1);
            var chunksChat2 = db.DocumentChunks.Count(c => c.ChatId == 2);

            Assert.True(chunksChat1 > 0);
            Assert.True(chunksChat2 > 0);
            Assert.Equal(chunksChat1, chunksChat2);
        }
    }
}