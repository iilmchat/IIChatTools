using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Реализация сервиса поиска по векторным индексам
    /// (v1.5.0, KI-083, Шаг 5A).
    ///
    /// <para>
    /// Scoped — работает с <see cref="AppDbContext"/> для enrichment
    /// (загрузка <c>DocumentChunk.Text</c> + <c>MetadataJson</c>).
    /// </para>
    /// </summary>
    public sealed class RetrievalService : IRetrievalService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStore _vectorStore;
        private readonly AppDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RetrievalService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="embeddingService">Сервис эмбеддингов (Singleton)</param>
        /// <param name="vectorStore">Векторное хранилище (Singleton)</param>
        /// <param name="db">Контекст БД (Scoped)</param>
        /// <param name="configuration">Конфигурация (Rag:Retrieval)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public RetrievalService(
            IEmbeddingService embeddingService,
            IVectorStore vectorStore,
            AppDbContext db,
            IConfiguration configuration,
            ILogger<RetrievalService> logger)
        {
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RetrievedChunkDto>> SearchAsync(
            string query,
            string indexName,
            int topK = 5,
            int? chatId = null,
            int? userId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("query не может быть пустым", nameof(query));

            if (string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("indexName не может быть пустым", nameof(indexName));

            // --- Конфиг ---
            var effectiveTopK = topK > 0
                ? topK
                : GetIntConfig("Rag:Retrieval:DefaultTopK", 5);

            var overFetchMultiplier = GetIntConfig("Rag:Retrieval:OverFetchMultiplier", 2);
            if (overFetchMultiplier < 1) overFetchMultiplier = 1;

            var minScore = GetFloatConfig("Rag:Retrieval:MinScore", 0.3f);

            // --- 1. Embed query ---
            var queryVector = await _embeddingService.GetEmbeddingAsync(query, cancellationToken);

            // --- 2. Search vector store (over-fetch для фильтрации) ---
            var searchResults = _vectorStore.Search(indexName, queryVector, effectiveTopK * overFetchMultiplier);
            if (searchResults.Count == 0)
            {
                _logger.LogDebug(
                    "Retrieval: индекс пуст (index={Index}, queryLen={Len})",
                    indexName, query.Length);
                return Array.Empty<RetrievedChunkDto>();
            }

            // --- 3. Фильтрация по метаданным (chatId / userId) ---
            var filtered = new List<VectorSearchResult>(searchResults.Count);
            foreach (var r in searchResults)
            {
                if (chatId.HasValue && r.Metadata?.ChatId != chatId.Value)
                    continue;

                if (userId.HasValue && r.Metadata?.UserId != userId.Value)
                    continue;

                filtered.Add(r);
            }

            if (filtered.Count == 0)
            {
                _logger.LogDebug(
                    "Retrieval: после фильтрации (chatId={ChatId}, userId={UserId}) 0 результатов",
                    chatId, userId);
                return Array.Empty<RetrievedChunkDto>();
            }

            // --- 4. Фильтрация по MinScore ---
            var aboveScore = filtered
                .Where(r => r.Score >= minScore)
                .ToList();

            if (aboveScore.Count == 0)
            {
                _logger.LogDebug(
                    "Retrieval: все чанки ниже MinScore={MinScore}",
                    minScore);
                return Array.Empty<RetrievedChunkDto>();
            }

            // --- 5. Enrichment из БД (загрузка полного текста + MetadataJson) ---
            var chunkIds = aboveScore.Select(r => r.ChunkId).Distinct().ToList();
            var chunks = await _db.DocumentChunks
                .Where(c => chunkIds.Contains(c.Id))
                .ToListAsync(cancellationToken);

            var chunkDict = chunks.ToDictionary(c => c.Id);

            // --- 6. Сборка top-K (после всех фильтров) ---
            var result = new List<RetrievedChunkDto>(Math.Min(effectiveTopK, aboveScore.Count));
            foreach (var r in aboveScore)
            {
                if (result.Count >= effectiveTopK) break;

                if (!chunkDict.TryGetValue(r.ChunkId, out var chunk))
                {
                    // Чанк есть в векторе, но нет в БД — рассинхрон.
                    // Логируем и пропускаем (не падаем).
                    _logger.LogWarning(
                        "Retrieval: чанк {ChunkId} найден в VectorStore, но отсутствует в БД",
                        r.ChunkId);
                    continue;
                }

                result.Add(new RetrievedChunkDto
                {
                    ChunkId = r.ChunkId,
                    Text = chunk.Text ?? string.Empty,
                    Score = r.Score,
                    DocumentPath = chunk.DocumentPath,
                    ChunkIndex = chunk.ChunkIndex,
                    IndexName = chunk.IndexName,
                    Metadata = ParseMetadata(chunk.MetadataJson)
                });
            }

            _logger.LogDebug(
                "Retrieval: index={Index}, queryLen={Len}, over-fetched={Fetched}, " +
                "afterFilter={Filtered}, aboveScore={AboveScore}, returned={Returned}",
                indexName, query.Length, searchResults.Count, filtered.Count,
                aboveScore.Count, result.Count);

            return result;
        }

        /// <summary>
        /// Парсит JSON-метаданные чанка. Невалидный/null JSON → пустой словарь.
        /// </summary>
        /// <param name="metadataJson">JSON-строка или null</param>
        /// <returns>Словарь метаданных</returns>
        private static Dictionary<string, string> ParseMetadata(string metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
                return new Dictionary<string, string>();

            try
            {
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(metadataJson);
                return parsed ?? new Dictionary<string, string>();
            }
            catch
            {
                // Невалидный JSON — не критично, возвращаем пустой словарь.
                return new Dictionary<string, string>();
            }
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
        /// Читает float из конфига с fallback (InvariantCulture).
        /// </summary>
        private float GetFloatConfig(string key, float defaultValue)
        {
            var raw = _configuration[key];
            return float.TryParse(raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : defaultValue;
        }
    }
}