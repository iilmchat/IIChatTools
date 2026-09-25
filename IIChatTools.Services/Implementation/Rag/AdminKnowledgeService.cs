using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Реализация сервиса администрирования RAG Knowledge Base
    /// (v1.5.0, KI-083, Шаг 7A).
    ///
    /// <para>
    /// Scoped — работает с <c>AppDbContext</c>, <c>IDocumentIngestionService</c>,
    /// <c>IVectorStore</c>, <c>IAppSettingsService</c>.
    /// </para>
    /// </summary>
    public sealed class AdminKnowledgeService : IAdminKnowledgeService
    {
        /// <summary>Имя индекса глобальных документов проекта.</summary>
        private const string ProjectDocsIndex = "project_docs";

        /// <summary>Максимальный pageSize (защита от больших запросов).</summary>
        private const int MaxPageSize = 100;

        /// <summary>Длина превью текста чанка в UI.</summary>
        private const int PreviewLength = 120;

        /// <summary>Описания 4 индексов (для UI).</summary>
        private static readonly (string Name, string Description)[] IndexCatalog = new[]
        {
            ("project_docs", "Project documentation (README, RULES, KNOWN_ISSUES, CHANGELOG)"),
            ("my_rag_docs",  "Files attached to specific chats"),
            ("chat_history", "Chat history of the current user"),
            ("workspace",    "Files from the user workspace (opt-in)")
        };

        private readonly AppDbContext _db;
        private readonly IDocumentIngestionService _ingestionService;
        private readonly IVectorStore _vectorStore;
        private readonly IAppSettingsService _appSettings;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AdminKnowledgeService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="db">Контекст БД</param>
        /// <param name="ingestionService">Сервис индексации документов</param>
        /// <param name="vectorStore">Векторное хранилище (для удаления чанков)</param>
        /// <param name="appSettings">Сервис настроек (persist override'ов)</param>
        /// <param name="configuration">Конфигурация (fallback на defaults)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public AdminKnowledgeService(
            AppDbContext db,
            IDocumentIngestionService ingestionService,
            IVectorStore vectorStore,
            IAppSettingsService appSettings,
            IConfiguration configuration,
            ILogger<AdminKnowledgeService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ============================================================
        // GetIndexesAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<IReadOnlyList<RagIndexDto>> GetIndexesAsync(
            CancellationToken cancellationToken = default)
        {
            // Один SQL-запрос с GROUP BY — метрики по всем индексам сразу.
            var aggregated = await _db.DocumentChunks
                .AsNoTracking()
                .GroupBy(c => c.IndexName)
                .Select(g => new
                {
                    IndexName = g.Key,
                    ChunkCount = g.Count(),
                    DocumentCount = g.Select(c => c.DocumentPath).Distinct().Count(),
                    LastIndexedAt = g.Max(c => (DateTime?)c.CreatedAt)
                })
                .ToListAsync(cancellationToken);

            var byName = aggregated.ToDictionary(x => x.IndexName, StringComparer.Ordinal);

            // Возвращаем все 4 индекса (даже если чанков нет — с нулями).
            return IndexCatalog.Select(catalog =>
            {
                byName.TryGetValue(catalog.Name, out var stats);
                return new RagIndexDto
                {
                    Name = catalog.Name,
                    Description = catalog.Description,
                    ChunkCount = stats?.ChunkCount ?? 0,
                    DocumentCount = stats?.DocumentCount ?? 0,
                    LastIndexedAt = stats?.LastIndexedAt
                };
            }).ToList();
        }

        // ============================================================
        // ReindexProjectDocsAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<IngestionResultDto> ReindexProjectDocsAsync(
            CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var paths = _configuration.GetSection("Rag:Ingestion:ProjectDocsPaths")
                .Get<string[]>() ?? Array.Empty<string>();

            if (paths.Length == 0)
            {
                _logger.LogWarning("Rag:Ingestion:ProjectDocsPaths пуст — нечего индексировать");
                return new IngestionResultDto
                {
                    IndexName = ProjectDocsIndex,
                    DocumentChunksCreated = 0,
                    TokensTotal = 0,
                    DurationMs = sw.ElapsedMilliseconds,
                    Skipped = true,
                    SkipReason = "Список ProjectDocsPaths пуст"
                };
            }

            var projectRoot = ResolveProjectRoot();
            _logger.LogInformation(
                "Reindex project_docs: root={Root}, paths={Count}",
                projectRoot, paths.Length);

            int totalChunks = 0;
            int totalTokens = 0;
            int filesProcessed = 0;
            int filesSkipped = 0;
            var errors = new List<string>();

            foreach (var relPath in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var absPath = Path.IsPathRooted(relPath)
                    ? relPath
                    : Path.Combine(projectRoot, relPath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(absPath))
                {
                    _logger.LogWarning("Reindex: файл не найден {Path}", absPath);
                    errors.Add($"{relPath}: не найден");
                    continue;
                }

                try
                {
                    var result = await _ingestionService.IngestAsync(new IngestionRequest
                    {
                        IndexName = ProjectDocsIndex,
                        SourceType = IngestionSourceType.File,
                        FilePath = absPath,
                        UserId = 0,          // маркер «глобальный чанк» (DESIGN § 5.2)
                        ChatId = null,
                        ForceReindex = true, // явный reindex — принудительно
                        Source = relPath     // относительный путь для citations
                    }, cancellationToken);

                    if (result.Skipped)
                    {
                        filesSkipped++;
                    }
                    else
                    {
                        totalChunks += result.DocumentChunksCreated;
                        totalTokens += result.TokensTotal;
                        filesProcessed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reindex: ошибка индексации {Path}", absPath);
                    errors.Add($"{relPath}: {ex.Message}");
                }
            }

            sw.Stop();

            var skipReason = errors.Count > 0
                ? $"Обработано {filesProcessed} файлов, ошибок {errors.Count}: {string.Join("; ", errors.Take(3))}"
                : (filesProcessed == 0 ? "Все файлы без изменений (hash)" : null);

            _logger.LogInformation(
                "Reindex project_docs завершён: files={Files}, skipped={Skipped}, " +
                "chunks={Chunks}, tokens={Tokens}, durationMs={Duration}",
                filesProcessed, filesSkipped, totalChunks, totalTokens, sw.ElapsedMilliseconds);

            return new IngestionResultDto
            {
                IndexName = ProjectDocsIndex,
                DocumentChunksCreated = totalChunks,
                TokensTotal = totalTokens,
                DurationMs = sw.ElapsedMilliseconds,
                DocumentPath = projectRoot,
                Skipped = filesProcessed == 0,
                SkipReason = skipReason
            };
        }

        // ============================================================
        // GetChunksAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<(IReadOnlyList<RagChunkDto> Items, int Page, int PageSize, int TotalCount, int TotalPages)>
            GetChunksAsync(
                string indexName,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(indexName))
                indexName = ProjectDocsIndex;

            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, MaxPageSize);

            var query = _db.DocumentChunks
                .AsNoTracking()
                .Where(c => c.IndexName == indexName);

            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            var items = await query
                .OrderBy(c => c.DocumentPath)
                .ThenBy(c => c.ChunkIndex)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new RagChunkDto
                {
                    Id = c.Id,
                    IndexName = c.IndexName,
                    DocumentPath = c.DocumentPath,
                    ChatId = c.ChatId,
                    UserId = c.UserId,
                    ChunkIndex = c.ChunkIndex,
                    Tokens = c.Tokens,
                    TextPreview = c.Text != null && c.Text.Length > PreviewLength
                        ? c.Text.Substring(0, PreviewLength) + "…"
                        : c.Text,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return (items, page, pageSize, totalCount, totalPages);
        }

        // ============================================================
        // DeleteChunkAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<bool> DeleteChunkAsync(
            int chunkId,
            CancellationToken cancellationToken = default)
        {
            var entity = await _db.DocumentChunks
                .FirstOrDefaultAsync(c => c.Id == chunkId, cancellationToken);

            if (entity == null)
                return false;

            // Сначала — VectorStore (best-effort, не падаем).
            try
            {
                _vectorStore.Remove(entity.IndexName, entity.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось удалить вектор чанка {ChunkId} (index={Index}). Продолжаем.",
                    chunkId, entity.IndexName);
            }

            _db.DocumentChunks.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Чанк удалён: id={Id}, index={Index}, path={Path}, chunkIndex={ChunkIndex}",
                entity.Id, entity.IndexName, entity.DocumentPath, entity.ChunkIndex);

            return true;
        }

        // ============================================================
        // GetSettingsAsync / UpdateSettingsAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<RagSettingsDto> GetSettingsAsync(
            CancellationToken cancellationToken = default)
        {
            // Override'ы из AppSettings (persist) с fallback на appsettings.json.
            var overrides = await _appSettings.GetAllAsync();
            var map = overrides
                .Where(s => s.Key.StartsWith("Rag.", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);

            return new RagSettingsDto
            {
                ChunkingStrategy = ReadString(map, "Rag.Chunking.Strategy",
                    _configuration["Rag:Chunking:Strategy"] ?? "recursive"),
                ChunkSize = ReadInt(map, "Rag.Chunking.ChunkSize",
                    ParseInt(_configuration["Rag:Chunking:ChunkSize"], 500)),
                ChunkOverlap = ReadInt(map, "Rag.Chunking.ChunkOverlap",
                    ParseInt(_configuration["Rag:Chunking:ChunkOverlap"], 64)),
                MinChunkSize = ReadInt(map, "Rag.Chunking.MinChunkSize",
                    ParseInt(_configuration["Rag:Chunking:MinChunkSize"], 100)),
                DefaultTopK = ReadInt(map, "Rag.Retrieval.DefaultTopK",
                    ParseInt(_configuration["Rag:Retrieval:DefaultTopK"], 5)),
                MinScore = ReadFloat(map, "Rag.Retrieval.MinScore",
                    ParseFloat(_configuration["Rag:Retrieval:MinScore"], 0.3f)),
                EmbeddingModel = ReadString(map, "Rag.Embedding.Model",
                    _configuration["Rag:Embedding:Model"] ?? "text-embedding-nomic-embed-text-v1.5"),
                AutoIndexProjectDocs = ReadBool(map, "Rag.AutoIndexProjectDocs",
                    ParseBool(_configuration["Rag:AutoIndexProjectDocs"], false))
            };
        }

        /// <inheritdoc />
        public async Task UpdateSettingsAsync(
            RagSettingsDto dto,
            CancellationToken cancellationToken = default)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            // --- Валидация ---
            var validStrategies = new[] { "recursive", "sentence", "fixed" };
            if (Array.IndexOf(validStrategies, dto.ChunkingStrategy) < 0)
                throw new ArgumentException(
                    $"ChunkingStrategy должен быть одним из: {string.Join(", ", validStrategies)}");

            if (dto.ChunkSize < 100 || dto.ChunkSize > 2000)
                throw new ArgumentException("ChunkSize должен быть в диапазоне 100–2000");

            if (dto.ChunkOverlap < 0 || dto.ChunkOverlap > 500)
                throw new ArgumentException("ChunkOverlap должен быть в диапазоне 0–500");

            if (dto.MinChunkSize < 0 || dto.MinChunkSize > dto.ChunkSize)
                throw new ArgumentException("MinChunkSize должен быть 0..ChunkSize");

            if (dto.DefaultTopK < 1 || dto.DefaultTopK > 20)
                throw new ArgumentException("DefaultTopK должен быть в диапазоне 1–20");

            if (dto.MinScore < 0f || dto.MinScore > 1f)
                throw new ArgumentException("MinScore должен быть в диапазоне 0.0–1.0");

            if (string.IsNullOrWhiteSpace(dto.EmbeddingModel))
                throw new ArgumentException("EmbeddingModel обязателен");

            // --- Persist в AppSettings ---
            await UpsertSettingAsync("Rag.Chunking.Strategy", dto.ChunkingStrategy, "string");
            await UpsertSettingAsync("Rag.Chunking.ChunkSize", dto.ChunkSize.ToString(), "int");
            await UpsertSettingAsync("Rag.Chunking.ChunkOverlap", dto.ChunkOverlap.ToString(), "int");
            await UpsertSettingAsync("Rag.Chunking.MinChunkSize", dto.MinChunkSize.ToString(), "int");
            await UpsertSettingAsync("Rag.Retrieval.DefaultTopK", dto.DefaultTopK.ToString(), "int");
            await UpsertSettingAsync("Rag.Retrieval.MinScore",
                dto.MinScore.ToString(System.Globalization.CultureInfo.InvariantCulture), "float");
            await UpsertSettingAsync("Rag.Embedding.Model", dto.EmbeddingModel, "string");
            await UpsertSettingAsync("Rag.AutoIndexProjectDocs", dto.AutoIndexProjectDocs.ToString(), "bool");

            _logger.LogInformation(
                "Настройки RAG сохранены: strategy={Strategy}, size={Size}, topK={TopK}, minScore={Score}",
                dto.ChunkingStrategy, dto.ChunkSize, dto.DefaultTopK, dto.MinScore);
        }

        // ============================================================
        // Внутренние
        // ============================================================

        /// <summary>
        /// Resolve project root: конфиг → auto-detect по IIChatTools.sln → cwd.
        /// </summary>
        private string ResolveProjectRoot()
        {
            // 1. Явная конфигурация.
            var configured = _configuration["Rag:Ingestion:ProjectRootPath"];
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
                return Path.GetFullPath(configured);

            // 2. Auto-detect: поднимаемся до папки с IIChatTools.sln (не более 5 уровней).
            var dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "IIChatTools.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }

            // 3. Fallback: текущая директория.
            return Directory.GetCurrentDirectory();
        }

        /// <summary>Читает строку: override → default.</summary>
        private static string ReadString(IReadOnlyDictionary<string, string> map, string key, string defaultValue)
        {
            if (map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
            return defaultValue;
        }

        /// <summary>Читает int: override → default.</summary>
        private static int ReadInt(IReadOnlyDictionary<string, string> map, string key, int defaultValue)
        {
            if (map.TryGetValue(key, out var v) && int.TryParse(v, out var n))
                return n;
            return defaultValue;
        }

        /// <summary>Читает float: override → default (InvariantCulture).</summary>
        private static float ReadFloat(IReadOnlyDictionary<string, string> map, string key, float defaultValue)
        {
            if (map.TryGetValue(key, out var v)
                && float.TryParse(v,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
                return n;
            return defaultValue;
        }

        /// <summary>Читает bool: override → default.</summary>
        private static bool ReadBool(IReadOnlyDictionary<string, string> map, string key, bool defaultValue)
        {
            if (map.TryGetValue(key, out var v) && bool.TryParse(v, out var b))
                return b;
            return defaultValue;
        }

        /// <summary>Parse int с fallback.</summary>
        private static int ParseInt(string raw, int defaultValue)
            => int.TryParse(raw, out var v) ? v : defaultValue;

        /// <summary>Parse float с fallback (InvariantCulture).</summary>
        private static float ParseFloat(string raw, float defaultValue)
            => float.TryParse(raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v : defaultValue;

        /// <summary>Parse bool с fallback.</summary>
        private static bool ParseBool(string raw, bool defaultValue)
            => bool.TryParse(raw, out var v) ? v : defaultValue;

        /// <summary>
        /// Upsert настройки в AppSettings (создать или обновить).
        /// </summary>
        private async Task UpsertSettingAsync(string key, string value, string type)
        {
            var all = await _appSettings.GetAllAsync();
            var existing = all.FirstOrDefault(s =>
                string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                await _appSettings.UpdateAsync(existing.Id, new SettingDto
                {
                    Id = existing.Id,
                    Key = key,
                    Value = value,
                    Type = type,
                    Category = "RAG"
                });
            }
            else
            {
                await _appSettings.CreateAsync(new SettingDto
                {
                    Key = key,
                    Value = value,
                    Type = type,
                    Category = "RAG"
                });
            }
        }
    }
}