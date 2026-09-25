using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Сервис индексации документов для RAG
    /// (v1.5.0, KI-083, Шаг 4C.2).
    ///
    /// <para>
    /// Оркестрирует полный цикл: <c>parse → chunk → embed → store</c>.
    /// Реализация — <b>Scoped</b> (работает с <see cref="AppDbContext"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Идемпотентность:</b> повторная индексация того же документа
    /// (совпадение SHA256 содержимого) без <c>ForceReindex</c> пропускается.
    /// С <c>ForceReindex = true</c> — старые чанки удаляются и создаются заново.
    /// </para>
    ///
    /// <para>
    /// <b>Транзакционность:</b> используем <c>SaveChangesAsync</c> (EF Core
    /// оборачивает в транзакцию сам). Явную транзакцию не открываем, так как
    /// InMemory-провайдер (используется в тестах) не поддерживает
    /// <c>BeginTransactionAsync</c>.
    /// </para>
    /// </summary>
    public sealed class DocumentIngestionService : IDocumentIngestionService
    {
        /// <summary>Длина сокращённого hash для генерации DocumentPath из чистого текста.</summary>
        private const int ShortHashLength = 12;

        private readonly IRagDocumentParserRegistry _parserRegistry;
        private readonly IChunkingStrategyResolver _chunkingResolver;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStore _vectorStore;
        private readonly ITokenCounter _tokenCounter;
        private readonly AppDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DocumentIngestionService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="parserRegistry">Реестр парсеров (по расширению)</param>
        /// <param name="chunkingResolver">Резолвер стратегий чанкинга</param>
        /// <param name="embeddingService">Сервис эмбеддингов (LM Studio)</param>
        /// <param name="vectorStore">Векторное хранилище (InMemory MVP)</param>
        /// <param name="tokenCounter">Счётчик токенов (для поля DocumentChunk.Tokens)</param>
        /// <param name="db">Контекст БД (DocumentChunks)</param>
        /// <param name="configuration">Конфигурация (Rag:Chunking)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public DocumentIngestionService(
            IRagDocumentParserRegistry parserRegistry,
            IChunkingStrategyResolver chunkingResolver,
            IEmbeddingService embeddingService,
            IVectorStore vectorStore,
            ITokenCounter tokenCounter,
            AppDbContext db,
            IConfiguration configuration,
            ILogger<DocumentIngestionService> logger)
        {
            _parserRegistry = parserRegistry ?? throw new ArgumentNullException(nameof(parserRegistry));
            _chunkingResolver = chunkingResolver ?? throw new ArgumentNullException(nameof(chunkingResolver));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IngestionResultDto> IngestAsync(
            IngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ValidateRequest(request);

            var stopwatch = Stopwatch.StartNew();

            // 1. Получить текст (parse файла / использовать готовый / URL).
            var (rawText, documentPath, parsedMetadata) = await GetTextFromSourceAsync(request, cancellationToken);

            // 2. SHA256 hash содержимого.
            var documentHash = ComputeSha256Hex(rawText);

            // 3. Проверка: уже проиндексировано с этим hash?
            if (!request.ForceReindex)
            {
                var existingHash = await GetExistingHashAsync(request, documentPath, cancellationToken);
                if (string.Equals(existingHash, documentHash, StringComparison.OrdinalIgnoreCase))
                {
                    stopwatch.Stop();
                    _logger.LogInformation(
                        "Ingestion пропущен (hash совпадает): index={Index}, path={Path}, hash={Hash}",
                        request.IndexName, documentPath, documentHash);

                    return new IngestionResultDto
                    {
                        IndexName = request.IndexName,
                        DocumentChunksCreated = 0,
                        TokensTotal = 0,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        DocumentHash = documentHash,
                        DocumentPath = documentPath,
                        Skipped = true,
                        SkipReason = "Содержимое не изменилось (hash совпадает)"
                    };
                }
            }

            // 4. Удалить старые чанки документа (если есть).
            await DeleteExistingChunksAsync(request, documentPath, cancellationToken);

            // 5. Chunking.
            var chunkingOptions = BuildChunkingOptions();
            var strategy = _chunkingResolver.Resolve(chunkingOptions.Strategy);
            var chunks = strategy.Chunk(rawText, chunkingOptions, _tokenCounter);

            if (chunks.Count == 0)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "Ingestion: после chunking 0 чанков (index={Index}, path={Path})",
                    request.IndexName, documentPath);

                return new IngestionResultDto
                {
                    IndexName = request.IndexName,
                    DocumentChunksCreated = 0,
                    TokensTotal = 0,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    DocumentHash = documentHash,
                    DocumentPath = documentPath
                };
            }

            // 6. Embeddings (батчами внутри IEmbeddingService).
            var vectors = await _embeddingService.GetEmbeddingsAsync(chunks, cancellationToken);

            if (vectors.Count != chunks.Count)
            {
                throw new InvalidOperationException(
                    $"Embeddings вернули {vectors.Count} векторов, ожидалось {chunks.Count}");
            }

            // 7. INSERT в БД.
            var source = request.Source ?? documentPath;
            var entities = new List<DocumentChunk>(chunks.Count);
            int totalTokens = 0;

            for (int i = 0; i < chunks.Count; i++)
            {
                var tokens = _tokenCounter.CountTokens(chunks[i]);
                totalTokens += tokens;

                entities.Add(new DocumentChunk
                {
                    IndexName = request.IndexName,
                    ChatId = request.ChatId,
                    UserId = request.UserId,
                    DocumentPath = documentPath,
                    DocumentHash = documentHash,
                    ChunkIndex = i,
                    Text = chunks[i],
                    Tokens = tokens,
                    MetadataJson = BuildMetadataJson(parsedMetadata)
                });
            }

            _db.DocumentChunks.AddRange(entities);
            await _db.SaveChangesAsync(cancellationToken);

            // 8. Add в векторное хранилище.
            //    ВАЖНО: после SaveChanges у сущностей проставлены Id.
            //    При падении VectorStore.Add чанки в БД остаются — известно
            //    ограничение MVP (DESIGN § 4.3.3); ForceReindex пересоберёт.
            try
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    var entity = entities[i];

                    var metadata = new ChunkMetadata
                    {
                        DocumentChunkId = entity.Id,
                        IndexName = entity.IndexName,
                        ChatId = entity.ChatId,
                        UserId = entity.UserId,
                        DocumentPath = entity.DocumentPath,
                        ChunkIndex = entity.ChunkIndex,
                        Source = source
                    };

                    _vectorStore.Add(entity.IndexName, entity.Id, vectors[i], metadata);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "VectorStore.Add упал после INSERT в БД: index={Index}, path={Path}, " +
                    "чанков в БД={Chunks}, docHash={Hash}. Переиндексация с ForceReindex=true " +
                    "исправит состояние.",
                    request.IndexName, documentPath, entities.Count, documentHash);
                throw;
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Ingestion завершён: index={Index}, path={Path}, chunks={Chunks}, " +
                "tokens={Tokens}, durationMs={Duration}, hash={Hash}",
                request.IndexName, documentPath, entities.Count, totalTokens,
                stopwatch.ElapsedMilliseconds, documentHash);

            return new IngestionResultDto
            {
                IndexName = request.IndexName,
                DocumentChunksCreated = entities.Count,
                TokensTotal = totalTokens,
                DurationMs = stopwatch.ElapsedMilliseconds,
                DocumentHash = documentHash,
                DocumentPath = documentPath,
                Skipped = false
            };
        }

        /// <inheritdoc />
        public async Task<int> DeleteDocumentAsync(
            string indexName,
            string documentPath,
            int? chatId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("indexName не может быть пустым", nameof(indexName));

            if (string.IsNullOrWhiteSpace(documentPath))
                return 0;

            var query = _db.DocumentChunks
                .Where(c => c.IndexName == indexName && c.DocumentPath == documentPath);

            if (chatId.HasValue)
                query = query.Where(c => c.ChatId == chatId.Value);

            var entities = await query.ToListAsync(cancellationToken);
            if (entities.Count == 0)
                return 0;

            foreach (var entity in entities)
            {
                _vectorStore.Remove(entity.IndexName, entity.Id);
            }

            _db.DocumentChunks.RemoveRange(entities);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Удалён документ: index={Index}, path={Path}, chatId={ChatId}, chunks={Chunks}",
                indexName, documentPath, chatId, entities.Count);

            return entities.Count;
        }

        /// <inheritdoc />
        public async Task<int> ClearIndexAsync(
            string indexName,
            int? chatId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("indexName не может быть пустым", nameof(indexName));

            var query = _db.DocumentChunks.Where(c => c.IndexName == indexName);
            if (chatId.HasValue)
                query = query.Where(c => c.ChatId == chatId.Value);

            var entities = await query.ToListAsync(cancellationToken);

            foreach (var entity in entities)
            {
                _vectorStore.Remove(entity.IndexName, entity.Id);
            }

            _db.DocumentChunks.RemoveRange(entities);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Индекс очищен: index={Index}, chatId={ChatId}, chunks={Chunks}",
                indexName, chatId, entities.Count);

            return entities.Count;
        }

        // ============================================================
        // Внутренние методы
        // ============================================================

        /// <summary>
        /// Валидирует запрос: обязательные поля, соответствие SourceType.
        /// </summary>
        private static void ValidateRequest(IngestionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.IndexName))
                throw new ArgumentException("IndexName обязателен", nameof(request));

            switch (request.SourceType)
            {
                case IngestionSourceType.File:
                    if (string.IsNullOrWhiteSpace(request.FilePath))
                        throw new ArgumentException(
                            "FilePath обязателен при SourceType = File", nameof(request));
                    break;

                case IngestionSourceType.Text:
                    if (string.IsNullOrEmpty(request.Text))
                        throw new ArgumentException(
                            "Text обязателен при SourceType = Text", nameof(request));
                    break;

                case IngestionSourceType.Url:
                    if (string.IsNullOrWhiteSpace(request.Url))
                        throw new ArgumentException(
                            "Url обязателен при SourceType = Url", nameof(request));
                    break;

                default:
                    throw new ArgumentException(
                        $"Неизвестный SourceType: {request.SourceType}", nameof(request));
            }
        }

        /// <summary>
        /// Возвращает текст (parse файла / готовый текст / URL) + путь документа + метаданные парсера.
        /// </summary>
        private async Task<(string text, string documentPath, Dictionary<string, string> metadata)>
            GetTextFromSourceAsync(IngestionRequest request, CancellationToken cancellationToken)
        {
            switch (request.SourceType)
            {
                case IngestionSourceType.File:
                {
                    if (!File.Exists(request.FilePath))
                        throw new FileNotFoundException("Файл не найден", request.FilePath);

                    var parser = _parserRegistry.Resolve(request.FilePath)
                        ?? throw new ArgumentException(
                            $"Формат файла не поддерживается: {Path.GetExtension(request.FilePath)}");

                    var parsed = await parser.ParseAsync(request.FilePath, cancellationToken);
                    return (parsed.Text ?? string.Empty, request.FilePath, parsed.Metadata);
                }

                case IngestionSourceType.Text:
                {
                    var text = request.Text;
                    var hash = ComputeSha256Hex(text);
                    var documentPath = $"text://{hash.Substring(0, ShortHashLength)}";
                    return (text, documentPath, new Dictionary<string, string>
                    {
                        ["format"] = "text",
                        ["parser"] = "Inline"
                    });
                }

                case IngestionSourceType.Url:
                    throw new NotSupportedException(
                        "SourceType = Url не реализован в MVP (запланирован на v1.5.x)");

                default:
                    throw new ArgumentException(
                        $"Неизвестный SourceType: {request.SourceType}", nameof(request));
            }
        }

        /// <summary>
        /// Возвращает SHA256-хэш текста (hex, нижний регистр).
        /// </summary>
        private static string ComputeSha256Hex(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Возвращает существующий DocumentHash для документа (или null, если не найден).
        /// Проверяет по (IndexName, DocumentPath, ChatId).
        /// </summary>
        private async Task<string> GetExistingHashAsync(
            IngestionRequest request,
            string documentPath,
            CancellationToken cancellationToken)
        {
            var query = _db.DocumentChunks
                .Where(c => c.IndexName == request.IndexName && c.DocumentPath == documentPath);

            if (request.ChatId.HasValue)
                query = query.Where(c => c.ChatId == request.ChatId.Value);

            return await query
                .Select(c => c.DocumentHash)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Удаляет все чанки документа (по IndexName + DocumentPath + ChatId)
        /// из БД и векторного хранилища.
        /// </summary>
        private async Task DeleteExistingChunksAsync(
            IngestionRequest request,
            string documentPath,
            CancellationToken cancellationToken)
        {
            var query = _db.DocumentChunks
                .Where(c => c.IndexName == request.IndexName && c.DocumentPath == documentPath);

            if (request.ChatId.HasValue)
                query = query.Where(c => c.ChatId == request.ChatId.Value);

            var existing = await query.ToListAsync(cancellationToken);
            if (existing.Count == 0)
                return;

            foreach (var entity in existing)
            {
                _vectorStore.Remove(entity.IndexName, entity.Id);
            }

            _db.DocumentChunks.RemoveRange(existing);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Удалены старые чанки перед переиндексацией: index={Index}, path={Path}, " +
                "chatId={ChatId}, chunks={Chunks}",
                request.IndexName, documentPath, request.ChatId, existing.Count);
        }

        /// <summary>
        /// Собирает <see cref="ChunkingOptions"/> из конфига (<c>Rag:Chunking</c>).
        /// </summary>
        private ChunkingOptions BuildChunkingOptions()
        {
            return new ChunkingOptions
            {
                Strategy = _configuration["Rag:Chunking:Strategy"] ?? "recursive",
                ChunkSize = GetIntConfig("Rag:Chunking:ChunkSize", 500),
                ChunkOverlap = GetIntConfig("Rag:Chunking:ChunkOverlap", 64),
                MinChunkSize = GetIntConfig("Rag:Chunking:MinChunkSize", 100)
                // Separators — берём default из ChunkingOptions.
            };
        }

        /// <summary>
        /// Читает int из конфига с fallback.
        /// </summary>
        private int GetIntConfig(string key, int defaultValue)
        {
            var raw = _configuration[key];
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        /// <summary>
        /// Сериализует метаданные парсера в JSON (для поля DocumentChunk.MetadataJson).
        /// Пустая/отсутствующая коллекция → null.
        /// </summary>
        private static string BuildMetadataJson(Dictionary<string, string> metadata)
        {
            if (metadata == null || metadata.Count == 0)
                return null;

            return Newtonsoft.Json.JsonConvert.SerializeObject(metadata);
        }
    }
}