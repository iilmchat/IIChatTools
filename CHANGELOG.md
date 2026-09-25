# Changelog

Все значимые изменения проекта IIChatTools документируются в этом файле.

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/).
Проект придерживается [Semantic Versioning](https://semver.org/lang/ru/).

Типы изменений:
- **Added** — новая функциональность
- **Changed** — изменения в существующей функциональности
- **Deprecated** — функции, помеченные как устаревшие
- **Removed** — удалённая функциональность
- **Fixed** — исправления
- **Security** — исправления уязвимостей и утечек
- **Documented** — Задокументировано багов

---

## [Unreleased]

_(в работе — см. KI-083, RAG / Knowledge Base, v1.5.0)_


### Added
- **RAG / Knowledge Base — Шаг 6D: UI вложений чата (v1.5.0, KI-083)**:
  - **Chat UI (`Index.cshtml`):**
    - 📎-кнопка в `.chat-input-box` слева от textarea (DeepSeek-style, SVG-icon).
    - `<input type="file" id="chat-file-input" multiple hidden>` — accept: 26 расширений PlainTextParser.
    - Панель `.chat-attachments-bar` над полем ввода: строка «RAG: N чанков [Очистить RAG]» + chips-контейнер.
    - `data-*`-атрибуты для локализации (8 ключей на панели).
  - **JS (`chat.js`):**
    - State: `attachments` (ChatAttachmentDto[]), `ragChunkCount` (число чанков).
    - `loadAttachments()` — GET вложений активного чата (вызывается в `selectChat`).
    - `uploadFiles(files)` — параллельная загрузка (POST multipart, по одному запросу на файл); toast на каждый файл; ошибка одного не отменяет остальные.
    - `deleteAttachment(id)` — DELETE.
    - `clearAttachments()` — POST clear + `confirm()`.
    - `renderAttachmentsBar()` — рендерит chips (📄 {name} ({size} · {N чанков}) [×]) и summary.
    - `formatFileSize(bytes)` — «N KB» / «N.N MB».
    - `formatChipChunks(count)` — «1 чанк» / «N чанков» через `data-label-*`.
    - `enableInput` теперь управляет также 📎-кнопкой.
    - `showEmptyState` сбрасывает `attachments`/`ragChunkCount`.
  - **CSS (`chat.css`):** секция `.chat-attachments-bar` / `.chat-attachment-chip` / `.chat-attachment-chip-remove` / `.chat-input-action-attach` (+ mobile: meta-chip скрыт).
  - **Локализация (12 ключей × 2 = 24 записи):**
    `RagAttachButton`, `RagAttachButtonTooltip`, `RagClearButton`,
    `RagChunksCountOne`, `RagChunksCountMany`,
    `RagUploadSuccess`, `RagUploadTooLarge`, `RagUploadUnsupportedFormat`,
    `RagUploadExceedsMaxFiles`, `RagUploadExceedsTotalSize`,
    `RagClearConfirm`, `RagClearSuccess`.
  - **Фаза 6 (Attached Files) закрыта.** UI: 6A + 6B + 6C + 6D — все Done.

### Added
- **RAG / Knowledge Base — Шаг 6C: auto-inject top-K в system prompt (v1.5.0, KI-083)**:
  - `ChatStreamService` инжектит `AppDbContext` + `IRetrievalService` (оба Scoped).
  - Новый приватный метод `BuildRagContextAsync(chatId, userId, startUserMessageId, ct)`:
    - Быстрая проверка `AnyAsync` — есть ли чанки в `my_rag_docs` для чата (если нет — retrieval не вызывается).
    - Берёт текст последнего user-сообщения из БД (по `startUserMessageId` — совместимо с regenerate).
    - Вызывает `IRetrievalService.SearchAsync(query, "my_rag_docs", topK, chatId, userId)`.
    - Фильтрует по `Rag:Attachments:AutoInjectMinScore` (default 0.35).
    - Формирует блок по DESIGN § 4.7.5: «Ниже — релевантные фрагменты… [1] source (фрагмент N): текст».
    - При ошибке — лог Warning, продолжаем без RAG.
  - `BuildMessagesAsync`: system prompt = RAG-блок + оригинальный `chat.SystemPrompt` (через `\n\n`).
  - Конфиг: `Rag:Attachments:{AutoInjectTopK=5, AutoInjectMinScore=0.35}` (fallback в коде).
  - Тесты: `ChatStreamServiceTests` +3:
    - `StreamAsync_AutoInject_RagContextInSystemPrompt` — чанки есть → блок в system prompt.
    - `StreamAsync_AutoInject_NoRagChunks_NoInjection` — нет чанков → retrieval не вызывается.
    - `StreamAsync_AutoInject_LowScoreFiltered` — score < MinScore → блок не вставляется.
  - Fake `FakeLmStudioClient` расширен: `CapturedMessages` (все входящие JArray).
  - Fake `FakeRetrievalService` добавлен (записывает вызовы, настраиваемый результат).

### Added
- **Docs — синхронизация README/RULES с RAG-прогрессом (v1.5.0, KI-083)**:
  - README: статус тестов 124/124 → **188/188**; Chat видит 10 инструментов (было 7).
  - README: раздел RAG — прогресс фаз 1-5, 6A/6B.
  - RULES 1.4.14 → 1.4.15: § 4.40 (GUID-имена файлов в тестах),
    § 4.41 (`JToken.Value<T>()` без key → CS7036).

### Added
- **RAG / Knowledge Base — Шаг 6B: ChatAttachmentsController + лимиты multipart (v1.5.0, KI-083)**:
  - `ChatAttachmentsController` (4 endpoints):
    - `POST /api/chat/{chatId}/attachments` — загрузка (multipart, `[FromForm] IFormFile`), `[RequestSizeLimit(40 MB)]`.
    - `GET /api/chat/{chatId}/attachments` — список вложений.
    - `DELETE /api/chat/{chatId}/attachments/{attachmentId}` — удалить одно.
    - `POST /api/chat/{chatId}/attachments/clear` — очистить все.
  - Все endpoints: проверка владения чатом через `IChatService.GetChatAsync`.
  - Формат ответов: `{ success, data }` / `{ success: false, message }` — единый с проектом (без ProblemDetails).
  - `Startup.cs`: подняты лимиты multipart до 40 MB
    (`FormOptions.MultipartBodyLengthLimit` + `KestrelServerOptions.Limits.MaxRequestBodySize`).
    Причина: Kestrel по умолчанию 30 MB → 32 MB файл обрезался бы до нашей валидации.
  - Тесты: `ChatAttachmentsControllerTests` (9, unit через fake `IChatAttachmentService`).
  - HTTP-smoke через `WebApplicationFactory` — запланирован на Шаг 8 (полная integration-серия).

### Added
- **RAG / Knowledge Base — Шаг 6A-тесты: ChatAttachmentServiceTests (v1.5.0, KI-083)**:
  - 11 тестов `ChatAttachmentServiceTests`:
    - Upload: ValidFile + ChunksCount, TooLarge, UnsupportedFormat,
      ExceedsFilesPerChat, ExceedsTotalSize, SameHash_Dedup, ChatNotOwned,
      SavesFileInUserWorkspace.
    - Delete: RemovesFileAndChunks (файл + ingestion + БД).
    - ClearForChat: RemovesAll (+ папка chatId удалена).
    - GetForChat: ReturnsOnlyOwnAttachments.
  - Fake: `FakeWorkspaceResolver` (temp-root + `users/{userId}`),
    `FakeIngestionService` (записывает вызовы, настраиваемый результат).
  - Реальные: `RagDocumentParserRegistry` + `PlainTextParser`,
    `AppDbContext` через `TestDbContextFactory`.

### Added
- **RAG / Knowledge Base — Шаг 6A: ChatAttachment entity + сервис (v1.5.0, KI-083)**:
  - Entity `ChatAttachment` (`IIChatTools.Data/Entities/ChatAttachment.cs`).
  - `AppDbContext`: `DbSet<ChatAttachment>` + конфигурация (FK на `Chat` Cascade, 2 индекса).
  - DTO `ChatAttachmentDto` (`Id, ChatId, UserId, FileName, ContentType, SizeBytes, ChunksCount, UploadedAt`).
  - `IChatAttachmentService` — контракт: Upload / GetForChat / Delete / ClearForChat.
  - `ChatAttachmentService` (Scoped):
    - **Upload:** валидация размера/формата/лимитов; SHA256-дедупликация (тот же файл в том же чате → возвращает существующий); сохранение файла в `{UserWorkspace}/chat-attachments/{chatId}/{guid}.ext`; `IngestionAsync(my_rag_docs, ChatId, UserId)`; при ошибке ingestion — attachment остаётся (ChunksCount=0).
    - **Delete:** файл + чанки (`my_rag_docs`) + запись БД (best-effort, не падаем на ошибках).
    - **ClearForChat:** всё то же, но bulk + удаление пустой папки `chat-attachments/{chatId}`.
  - Конфиг: `Rag:Attachments:{MaxFileSizeBytes, MaxFilesPerChat, MaxTotalSizePerChat, StorageSubfolder}`.
  - **DI (`Startup.cs`):** `IChatAttachmentService` → `ChatAttachmentService` (Scoped).
  - Тесты: отдельный Шаг 6A-тесты.
  - Миграция: `AddChatAttachments`.

### Added
- **RAG / Knowledge Base — Шаг 5C: search_workspace (v1.5.0, KI-083)**:
  - `SearchWorkspaceTool : ITool` (`search_workspace`) — read-only.
    - Индекс `workspace` (per-user, **opt-in**).
    - Параметры: `query` (required), `topK` (optional, 1–20), `filePattern` (optional, substring).
    - **UserId обязателен из context**: `UserId ≤ 0` → Fail.
    - **Opt-in check**: читает `Workspace.Index.Enabled` из `IUserSettingsService`
      (per-user). Если `false` → Fail «Workspace index отключён» (без вызова Retrieval).
    - `filePattern` — post-filter по `DocumentPath` (Contains, OrdinalIgnoreCase),
      с over-fetch (`topK × 3`) для компенсации потерь.
  - **DI (`Startup.cs`):** `RegisterRagTools` — 3 RAG-tool (было 2), все Scoped.
  - Тесты: `SearchWorkspaceToolTests` (5) — Name/Params, Disabled_Fail,
    Enabled_Searches, UsesUserIdFromContext, FilePatternFilters.
  - **Фаза 5 (Retrieval + 3 Tools) закрыта. Chat видит 10 инструментов.**

### Added
- **RAG / Knowledge Base — Шаг 5B: search_knowledge_base + search_chat_history (v1.5.0, KI-083)**:
  - `SearchKnowledgeBaseTool : ITool` (`search_knowledge_base`) — read-only.
    - Индекс `project_docs` (глобальный).
    - Параметры: `query` (required), `topK` (optional, 1–20, default 5, clamp).
  - `SearchChatHistoryTool : ITool` (`search_chat_history`) — read-only.
    - Индекс `chat_history` (per-user).
    - Параметры: `query` (required), `topK` (optional), `chatId` (optional).
    - **UserId обязателен из context**: `UserId ≤ 0` → Fail (защита от утечки).
  - Формат ответа (для LLM): `{ query, count, results: [{rank, score, source, chunkIndex, text}] }`.
  - **DI (`Startup.cs`):** новый метод `RegisterRagTools`; 2 инструмента зарегистрированы как Scoped `ITool`.
  - Тесты: `SearchKnowledgeBaseToolTests` (5) + `SearchChatHistoryToolTests` (5) = 10.

### Added
- **RAG / Knowledge Base — Шаг 5A: IRetrievalService + RetrievalService (v1.5.0, KI-083)**:
  - `RetrievedChunkDto` — ChunkId, Text, Score, DocumentPath, ChunkIndex, IndexName, Metadata.
  - `IRetrievalService` — контракт `SearchAsync(query, indexName, topK?, chatId?, userId?, ct)`.
  - `RetrievalService` (Scoped) — оркестрация:
    - `IEmbeddingService.GetEmbeddingAsync(query)` — эмбеддинг запроса.
    - `IVectorStore.Search` с over-fetch (`topK × OverFetchMultiplier`).
    - Фильтрация по метаданным (chatId / userId).
    - Фильтрация по `Rag:Retrieval:MinScore` (default 0.3).
    - Enrichment из БД: `DocumentChunk.Text` + `MetadataJson` (парсится в `Dictionary<string,string>`).
  - Конфиг: `Rag:Retrieval:{DefaultTopK, OverFetchMultiplier, MinScore}`.
  - **DI (`Startup.cs`):** `IRetrievalService` → `RetrievalService` (Scoped).
  - **Fake-helper:** `IIChatTools.Tests.Fakes.FakeEmbeddingService` (детерминированные векторы +
    `SetVector` для точного контроля score) — переиспользуется в 5A/5B/5C.
  - Тесты: `RetrievalServiceTests` (7) — EmptyQuery, NoVectors, TopK/Sorted,
    MinScore, FiltersByChatId, FiltersByUserId, EnrichesMetadataFromDb.

### Added
- **RAG / Knowledge Base — Шаг 4C.3: DocumentIngestionServiceTests (v1.5.0, KI-083)**:
  - 11 тестов: IngestAsync Text/File/UnsupportedFormat/FileTooLarge,
    SameHash Skips/Reindexes, UpdatesVectorStore,
    DeleteDocumentAsync DB/VectorStore, ClearIndexAsync, ChatId-изоляция.
  - Fake-зависимости: `FakeEmbeddingService` (детерминированные 3-компонентные векторы, без HTTP).
  - Реальные: `PlainTextParser`, `RagDocumentParserRegistry`, `RecursiveChunkingStrategy`,
    `ChunkingStrategyResolver`, `InMemoryVectorStore`, `TokenCounter`.
  - `AppDbContext` — InMemory через `TestDbContextFactory`.
  - **Фаза 4 (Parser + Ingestion) закрыта.**

### Added
- **RAG / Knowledge Base — Шаг 4C.2a: MaxFileSizeBytes проверка (v1.5.0, KI-083)**:
  - `DocumentIngestionService`: при `SourceType = File` — проверка размера файла.
  - Лимит: `Rag:Ingestion:MaxFileSizeBytes` (default **32 MB** = `33_554_432` байт).
  - Превышение → `ArgumentException` с указанием фактического размера и лимита.
  - Проверка **до** `ParseAsync` — не тратим ресурсы на парсинг слишком больших файлов.

### Added
- **RAG / Knowledge Base — Шаг 4C.2: DocumentIngestionService (v1.5.0, KI-083)**:
  - `DocumentIngestionService : IDocumentIngestionService` (Scoped).
  - Оркестрация: `parse → chunk → embed → store`.
    - parse: `IRagDocumentParserRegistry.Resolve(filePath)` → `ParseAsync`.
    - chunk: `IChunkingStrategyResolver.Resolve(config)` + `ChunkingOptions` из `Rag:Chunking`.
    - embed: `IEmbeddingService.GetEmbeddingsAsync(chunks)`.
    - store: `INSERT DocumentChunks` (EF Core) + `IVectorStore.Add` по каждому чанку.
  - Идемпотентность: SHA256 содержимого → skip при совпадении hash (без `ForceReindex`).
  - Источники: File (парсинг), Text (inline, path = `text://{hash12}`), Url — NotSupportedException (v1.5.x).
  - `DeleteDocumentAsync` / `ClearIndexAsync` — чистка БД + `IVectorStore`.
  - **DI (`Startup.cs`):** `IDocumentIngestionService` → `DocumentIngestionService` (Scoped).
  - Тесты — Шаг 4C.3.

### Added
- **RAG / Knowledge Base — Шаг 4C.1: IDocumentIngestionService контракты (v1.5.0, KI-083)**:
  - `IngestionSourceType` (enum) — File | Text | Url.
  - `IngestionRequest` DTO — IndexName, FilePath/Text/Url, ChatId?, UserId, SourceType, ForceReindex, Source.
  - `IngestionResultDto` — IndexName, DocumentChunksCreated, TokensTotal, DurationMs,
    DocumentHash, DocumentPath, Skipped, SkipReason.
  - `IDocumentIngestionService` — IngestAsync / DeleteDocumentAsync / ClearIndexAsync.
  - Реализация — Шаг 4C.2, тесты — Шаг 4C.3.

### Added
- **RAG / Knowledge Base — Шаг 4B: IRagDocumentParserRegistry + DI (v1.5.0, KI-083)**:
  - `IRagDocumentParserRegistry` — контракт (Resolve, GetAllSupportedExtensions).
  - `RagDocumentParserRegistry` — Singleton, получает `IEnumerable<IRagDocumentParser>` через DI.
    - `Resolve` — линейный обход (первый матч по расширению побеждает).
    - `GetAllSupportedExtensions` — union расширений всех парсеров (case-insensitive).
  - **DI (`Startup.cs`):** `IRagDocumentParser` → `PlainTextParser` (Singleton);
    `IRagDocumentParserRegistry` → `RagDocumentParserRegistry` (Singleton).
  - Тесты: `RagDocumentParserRegistryTests` (3) — Resolve Txt, UnknownExt, Union.

### Added
- **RAG / Knowledge Base — Шаг 4A: IRagDocumentParser + PlainTextParser (v1.5.0, KI-083)**:
  - DTO `ParsedDocument` (Text, Metadata, OriginalSizeBytes, PageCount).
  - `IRagDocumentParser` — контракт (Name, SupportedExtensions, CanParse, ParseAsync).
  - `PlainTextParser` — 28 расширений (текст, разметка, код, конфиги).
    - **Чтение байтами** + ручной BOM-детект (`EF BB BF` / `FF FE` / `FE FF`).
    - Strict UTF-8 → fallback Windows-1251 (`CodePagesEncodingProvider`).
    - Defensive strip **всех** ведущих `\uFEFF` (BOM-символов).
    - Нормализация переносов `\r\n` / `\r` → `\n`.
    - Strip Markdown frontmatter (`--- ... ---`).
  - Тесты: `PlainTextParserTests` (8) — CanParse T/F, UTF-8, BOM, CP1251, CRLF, MD, empty.

### Added
- **RAG / Knowledge Base — Шаг 3C: Sentence/Fixed стратегии + Resolver (v1.5.0, KI-083)**:
  - `SentenceChunkingStrategy` (Name="sentence") — split по `. ! ? \n`, группировка предложений до `ChunkSize`.
  - `FixedChunkingStrategy` (Name="fixed") — жёсткий разрез по токенам (`Encode` → срез → `Decode`).
  - `IChunkingStrategyResolver` + `ChunkingStrategyResolver` — выбор по имени (case-insensitive, fallback на recursive).
  - `Startup.cs`: 3 стратегии + resolver (Singleton).
  - Тесты: `SentenceChunkingStrategyTests` (2) + `FixedChunkingStrategyTests` (2) + `ChunkingStrategyResolverTests` (2).
- **RAG / Knowledge Base — Шаг 3B: RecursiveChunkingStrategy (v1.5.0, KI-083)**:
  - `RecursiveChunkingStrategy` (Name="recursive", default) — рекурсивное разбиение: `\n\n` → `\n` → `. ` → … → ` ` → token-fallback.
  - `ChunkOverlap` при склейке (`TakeLastTokens`), `MergeSmallChunks` (MinChunkSize), `SplitByTokens` (fallback).
  - Тесты: `RecursiveChunkingStrategyTests` (6).
- **RAG / Knowledge Base — Шаг 3A: IChunkingStrategy + ChunkingOptions (v1.5.0, KI-083)**:
  - DTO `ChunkingOptions` (ChunkSize=500, ChunkOverlap=64, MinChunkSize=100, Separators, Strategy=recursive).
  - `IChunkingStrategy` — контракт (`Name` + `Chunk`).
  - `ITokenCounter.Encode(string) → IReadOnlyList<int>` + `Decode(IReadOnlyList<int>) → string`.
  - `TokenCounter`: реализация через `TiktokenTokenizer.EncodeToIds/Decode`.
  - Тесты: `TokenCounterTests` +2 (`EncodeDecode_RoundTrip`, `EncodeDecode_Empty`).
- **RAG / Knowledge Base — Шаг 2C: InMemoryVectorStore + DI (v1.5.0, KI-083)**:
  - `InMemoryVectorStore : IVectorStore, IDisposable` — Singleton.
  - `ConcurrentDictionary<string, IndexData>` + `Dictionary<int, VectorEntry>` + `ReaderWriterLockSlim`.
  - Add: write-lock, L2-нормализация, overwrite по chunkId. Search: read-lock, dot product.
  - `Startup.cs`: `services.AddSingleton<IVectorStore, InMemoryVectorStore>()`.
  - Тесты: `InMemoryVectorStoreTests` (11, включая `ConcurrentAdds_ThreadSafe` ×1000).
- **RAG / Knowledge Base — Шаг 2B: IVectorStore + VectorMath + DTO (v1.5.0, KI-083)**:
  - DTO `ChunkMetadata` (DocumentChunkId, IndexName, ChatId?, UserId, DocumentPath, ChunkIndex, Source).
  - DTO `VectorSearchResult` (ChunkId, Score, Metadata).
  - `IVectorStore` — интерфейс (Add, Search, Remove, Clear, Count, GetIndexNames).
  - `VectorMath` — helper (L2Normalize, DotProduct, CosineSimilarity). double-аккумулятор в DotProduct, ZeroNormThreshold=1e-12.
  - Тесты: `VectorMathTests` (6).
- **RAG / Knowledge Base — fix: warning CS1574 + KI-091 (v1.5.0, KI-083)**:
  - `DocumentChunk.cs`: `<see cref="IVectorStore"/>` → `<c>IVectorStore</c>` (тип в Services, у Data нет ссылки на Services).
  - KI-091 (Deferred): SqlServer цепочка миграций повреждена (snapshot drift). План пересборки — v1.5.0-rc.
  - DESIGN.md § 5.5 — примечание про SqlServer долг.
- **RAG / Knowledge Base — Шаг 2A: DocumentChunk entity + миграция (v1.5.0, KI-083)**:
  - Entity `DocumentChunk` (`IIChatTools.Data/Entities/DocumentChunk.cs`).
  - Конфигурация в `AppDbContext.OnModelCreating`: 3 индекса + FK на `Chat` (Cascade, nullable).
  - **FK на ApplicationUser НЕТ** (UserId=0 — маркер «глобальный чанк»; см. DESIGN § 5.2).
  - Миграция `AddDocumentChunks` (`IIChatTools.Data/Migrations/SqlServer/`).
  - DESIGN.md § 5.2 — уточнение про FK-связи.

### Added
- **RAG / Knowledge Base — Фаза 1: Embedding Service (v1.5.0, KI-083)**:
  - DTO `EmbeddingResponse` / `EmbeddingData` / `EmbeddingUsage` (`IIChatTools.Services/DTO/LmStudio/`).
  - `ILmStudioClient.GetEmbeddingsAsync(inputs, model, ct)` — POST `/v1/embeddings` (LM Studio).
  - `IEmbeddingService` + `EmbeddingService` (Singleton, батч 64, модель `nomic-embed-text-v1.5`, 768 dim).
  - **DI:** `ILmStudioClient` → **Singleton** (для совместимости с `EmbeddingService`); `IEmbeddingService` → Singleton.
  - `appsettings.json` + `.Development.json` — секция `Rag:Embedding` (Model, Dimensions, BatchSize, TimeoutSeconds, CacheEnabled).
  - **Тесты:** `EmbeddingServiceTests` (6) + `LmStudioEmbeddingTests` (6, mock HTTP).
  - Design: `docs/development/v1.5/DESIGN.md` § 4.1.

### Added
- **Docs — финальная чистка legacy, часть 5 (v1.4.x, финал)**:
  - `scripts/migrations/` (2 legacy `.ps1`) → `scripts/migrations/archive/v1.0.x/`.
  - `scripts/migrations/README.md` — новый.
  - `docs/development/ARCHITECTURE.md` § 3.5 — добавлены команды для создания миграций (SqlServer / Sqlite).

### Added
- **Docs — финальная чистка legacy, часть 4 (v1.4.x)**:
  - `scripts/build/` (8 legacy `.bat`/`.ps1`) → `scripts/build/archive/v1.0.x/`.
  - `scripts/build/README.md` — новый.
  - **PROMPT_V2.md**: восстановлена секция «📐 Формат вывода кода и ответа» (пример через 4-пробельный отступ — не ломает MD).

### Added
- **Docs — финальная чистка legacy, часть 3 (v1.4.x)**:
  - `scripts/diagnostics/` (3 legacy в архив, 3 удалены) → `scripts/diagnostics/archive/v1.0.x/`.
  - `scripts/diagnostics/README.md` — новый.
  - **PROMPT_V2.md v2.1 → v2.2**: секция «Формат вывода кода и ответа» (структура блоков, обязательные секции, правило «не выдумывай»).

### Added
- **Docs — финальная чистка legacy, часть 2 (v1.4.x)**:
  - `scripts/git/` (5 legacy-скриптов + 2 обёртки) → `scripts/git/archive/v1.0.x/`.
  - `scripts/git/README.md` — новый (описание почему каталог пуст).
  - Завершена чистка `scripts/setup/` (удалены 4 одноразовые обёртки).
  - **KI-088** — Planned: `docs/TESTING.md` (чек-лист ручной приёмки, v1.5.0).

### Added
- **Docs — финальная чистка legacy (v1.4.x)**:
  - `docs/guides/` (3 файла) + `docs/testing/` (2 файла) → `docs/development/archive/v1.0.x/guides-testing/`.
  - `scripts/setup/archive/v1.0.x/` — 8 legacy-скриптов (downloader*.ps1, check-packages, create_structure).
  - Удалены: 4 обёртки в `scripts/setup/` (одноразовые `.sh`/`.ps1`).
  - `scripts/setup/README.md` — новый (описание актуальных скриптов + порядок offline-restore).
  - Версия в шапках актуальных скриптов: v1.1.1 → v1.4.1.
  - `archive/README.md` — обновлена таблица структуры.

### Added
- **Docs — актуальный обзор архитектуры (KI-087, v1.5.x)**:
  - `docs/development/ARCHITECTURE.md` — сводный документ (9 разделов): слои, схема БД, DI-lifetime, поток Chat (SSE + approvals + regenerate), 46 инструментов, внешние зависимости, 10 ADR-style решений.
  - `docs/architecture/` (12 legacy-файлов эпохи v1.0 → v1.1) перенесены в `docs/development/archive/v1.0.x/architecture/`.
  - `archive/README.md` — обновлена структура.
  - `PROMPT_V2.md` — усилено правило о форматировании MD (в начале).

### Planned
- **v1.4.x**: KI-047 (PATCH/DELETE fallback), KI-053 (multi-user approvals — крупная), KI-077 (model:null при PUT), KI-082 (модалка-редактор).
- **v1.5.0**: RAG / Knowledge Base (KI-083) — **в работе**. Прогресс:
  - Фаза 0 (DESIGN) — ✅
  - Фаза 1 (Embedding Service) — ✅
  - Фаза 2 (Vector Store + DocumentChunk) — ✅
  - Фаза 3 (Chunking) — ✅
  - Фазы 4-8 (Parser+Ingestion, Retrieval+Tools, Attachments, Admin UI, Tests) — впереди.
  - См. [`docs/development/v1.5/DESIGN.md`](docs/development/v1.5/DESIGN.md).
- **v1.5.0-rc**: KI-091 (SqlServer миграции), KI-088 (TESTING.md).
- **v1.6.0**: KI-086 (вывод источников / citations из tool_result).

---

## [1.4.1] — 2026-09-25

**Chat UX polish + per-user retention + tiktoken-статистика.**

### Added
- **Admin UI — статистика запусков агентов (KI-076)**:
  - `AgentStatsDto` (`AgentName`, `DisplayName`, `TotalRuns`, `SuccessRuns`, `ErrorRuns`, `AvgDurationMs`, `LastRunAt`, `SuccessRate`).
  - `IAgentStatsService` + `AgentStatsService` — агрегация `AuditLogs` с `ToolName LIKE 'agent.%'` (один SQL-запрос с `GroupBy`).
  - `AgentToolBase` → инжектит `IAuditService`, пишет запись при каждом запуске агента (`ToolName = "agent.{AgentName}"`, `Status = Success / Error / Cancelled`, `DurationMs`, `ResultJson`).
  - `AdminAgentsController`: `GET /api/admin/agents/stats`.
  - `Admin.cshtml` → вкладка «Агенты»: карточки статистики (`agent-stats-grid`).
  - `admin-agents.js`: `loadAgentStats()`, `formatDuration`, `formatPercent`, `formatLastRun`.
  - `site.css`: стили `.agent-stat-card`.
  - 2 новых ключа `.resx` (`AgentStatsLastRun`, `AgentStatsEmpty`).
  - 3 теста `AgentStatsServiceTests`.

### Added
- **Chat — расширенная статистика генерации (KI-084a, v1.4.x)**:
  - `ChatMessage` +3 nullable-поля: `DurationMs`, `FirstTokenMs`, `FinishReason`.
  - `ChatStreamService` — Stopwatch (общая длительность) + время до первого delta + последний `finish_reason`.
  - `ChatStreamEvent.Done` +3 параметра (`durationMs`, `firstTokenMs`, `finishReason`).
  - `ChatMessageDto` +3 поля; `ChatController.GetChatAsync` — маппинг.
  - `chat.js` — meta: `123 / 45 токенов · 7.4 tok/s · 9.1 с`.
  - `.resx` (RU + EN): +2 ключа (`ChatMessageTokPerSec`, `ChatMessageDuration`).
  - **Требует миграции** `AddChatMessageStats` (SqlServer) / удаления `.db` (Sqlite, EnsureCreated).
- **Chat — токены в SSE-событиях `start` и `done` (KI-084b, v1.4.x)**:
  - `ChatStreamEvent.Start` — опциональный 3-й параметр `userTokens` (tiktoken).
  - `ChatStreamService`: `start` передаёт токены user-сообщения (из tiktoken или из БД при Regenerate); `done` передаёт **посчитанные** `contextTokens`/`completionTokens` (раньше — сырые `usage`, часто null).
  - Limit-message (когда исчерпаны 5 итераций): токены тоже считаются и сохраняются.
  - `chat.js`: `updateBubbleMeta()` — обновление meta-строки live; `formatTokenMetaText()` — вынесена из `formatTokenMeta`.
  - Токены теперь видны **сразу** во время стрима (не только после F5).
  - `appendUserMessage`/`appendAssistantBubble`: сохраняют `dataset.time` и `dataset.roleLabel` для последующего обновления meta.
- **Chat UI — отображение токенов в meta-строке (KI-049b, v1.4.x)**:
  - `ChatMessageDto`: +2 nullable-поля (`TokensIn`, `TokensOut`).
  - `ChatController.GetChatAsync`: маппинг токенов.
  - `chat.js`: `formatTokenMeta()` — user `15 токенов`, assistant `123 / 45 токенов`.
  - `Chat/Index.cshtml`: `data-label-tokens-user` / `data-label-tokens-assistant`.
  - `.resx` (RU + EN): +2 ключа (`ChatTokensUser`, `ChatTokensAssistant`).
  - Токены отображаются только при наличии (не null).
- **Chat — подсчёт токенов через tiktoken (KI-049a, v1.4.x)**:
  - Пакеты `Microsoft.ML.Tokenizers` + `Microsoft.ML.Tokenizers.Data.Cl100kBase` 1.0.0
    (API + BPE-словарь; одно без другого не работает).
  - `ITokenCounter` + `TokenCounter` (Singleton): `CountTokens(string)`, `CountConversation(messages)`.
  - `ChatStreamService`: заполняет `TokensIn`/`TokensOut` для user и assistant сообщений.
    - Если LM Studio отдала `usage` (non-stream) — использует точные значения.
    - Иначе — считает через tiktoken (приближение ±5-10%).
  - 7 unit-тестов (`TokenCounterTests`).
- **RULES v1.4.4**: § 4.31 (Retention:Enabled в Development — намеренно false).

### Fixed
- **KI-049a (fix)**: добавлен пакет `Microsoft.ML.Tokenizers.Data.Cl100kBase` — без него `TiktokenTokenizer.CreateForEncoding("cl100k_base")` падает с `InvalidOperationException: The tokenizer data file ... could not be loaded`.
- **KI-067-2 (fix)**: `ExecuteDeleteAsync` не поддерживается InMemory-провайдером EF Core 10. Решение: fallback — загрузка сущностей в память + `RemoveRange` + `SaveChangesAsync` (для тестов). Для SqlServer/Sqlite — прежний bulk-DELETE.

### Added
- **Users — UI для per-user retention (KI-067-3, v1.4.x)**:
  - **Backend:**
    - `UserSettingsDto` (`RetentionDays`, `DoNotDelete`, `GlobalRetentionDays`, `MaxRetentionDays`).
    - `AdminController`:
      - `GET /api/admin/users/{id}/settings` — текущие настройки.
      - `PUT /api/admin/users/{id}/settings` — сохранение (валидация 1..MaxDays, `null` = сброс override).
    - `ProfileController`:
      - `GET /profile` — Razor-страница профиля.
      - `GET /api/profile/settings` — свои настройки.
      - `PUT /api/profile/settings` — сохранение своих настроек.
  - **Frontend:**
    - `Views/Profile/Index.cshtml` — карточка «Хранение чатов» с полями.
    - `wwwroot/js/modules/profile.js` — загрузка/сохранение, дизейбл поля при `DoNotDelete`.
    - `admin.js`: кнопка ⚙ в строке пользователя → модалка с полями retention.
    - `_Layout.cshtml`: пункт меню «Профиль» (для залогиненных).
    - `_LoginPartial.cshtml`: displayName — ссылка на `/profile`.
  - **Локализация:** +10 ключей (`ProfileTitle`, `ProfileMenu`, `ProfileRetentionSection`, `ProfileGlobalHint`, `ProfileRetentionDays`, `ProfileRetentionDaysPlaceholder`, `ProfileRetentionDaysHint`, `ProfileDoNotDelete`, `ProfileDoNotDeleteHint`, `ProfileSave`).
- **Users — per-user retention чатов (KI-067-2, v1.4.x)**:
  - `IUserSettingsService.GetAllWithKeyPrefixAsync(prefix)` — получить все настройки по префиксу (для фонового сервиса).
  - `IChatService.DeleteOldChatsAsync(retentionDays, excludedUserIds)` — bulk-DELETE с исключением пользователей.
  - `IChatService.DeleteOldChatsForUserAsync(userId, retentionDays)` — bulk-DELETE для одного пользователя.
  - `ChatRetentionService`: чтение per-user overrides `Chat.RetentionDays` (int) и `Chat.DoNotDelete` (bool).
    - `DoNotDelete = true` — пользователь исключается полностью.
    - `RetentionDays = N` — свой срок хранения (clamp к `Chat:Retention:MaxDays`).
    - `DoNotDelete` побеждает `RetentionDays`.
  - 6 новых тестов (2 — `UserSettingsServiceTests`, 4 — `ChatServiceRetentionTests`).
- **UI — фирменный логотип IIChatTools (KI-081, v1.4.x)**:
  - `wwwroot/images/logo-icon.svg` — иконка (шестиугольник + переплетение), inline SVG.
  - `wwwroot/images/logo-full.svg` — иконка + текст «IIRuChating».
  - `wwwroot/site.webmanifest` — PWA-манифест (иконки 192/512).
  - Favicon: SVG + ICO + PNG 96 + apple-touch-icon + manifest (от realfavicongenerator).
  - Navbar-brand: логотип-иконка + «IIChatTools vX.Y.Z».
  - Hero на главной: логотип с текстом (240px).
  - Login / Register: логотип с текстом (200px).
  - Empty state в `/chat` (нет чата / пустой чат): иконка-логотип (64px) вместо 💬.
  - `site.css`: `.navbar-brand-logo`, `.home-hero-logo`, `.auth-logo`.
  - `chat.css`: `.chat-empty-logo`.

### Changed
- **Chat UI — поле ввода на всю ширину (KI-080, DeepSeek-style, v1.4.x)**:
  - `.input-group` → `.chat-input-box`: закруглённое поле (`border-radius: 1.5rem`) на всю ширину `.chat-main`, светлый фон (`#f6f8fa`).
  - Textarea: прозрачная внутри поля, без бордера, растёт вверх (`max-height: 200px`).
  - Кнопки Send/Stop: круглые SVG-иконки (стрелка вверх / квадрат) внутри поля справа.
  - Focus-ring на `.chat-input-box:focus-within` (убраны Bootstrap `box-shadow` на textarea).
  - Удалён мёртвый CSS KI-061a (правила для `.input-group`).
  - `enableInput`/`setStreamingUI` без изменений (те же `#btn-send`/`#btn-stop`).

### Added
- **Tests — KI-078B (v1.4.x)**: 8 тестов для `SearchUserChatsWithSnippetAsync`:
  - `EmptyQuery_ReturnsEmpty` — семантика «пустой запрос → пусто» (в отличие от `SearchUserChatsAsync`).
  - `ByTitle_ReturnsMatchedFieldTitle` — `MatchedField="title"`, `Snippet=null`.
  - `ByContent_ReturnsSnippetWithOffsets` — `MatchedField="content"`, offsets указывают на совпадение.
  - `PrioritizesTitleOverContent` — при совпадении и в title, и в content приоритет у title.
  - `SnippetHasEllipsis` — «…» по краям, когда совпадение в середине.
  - `DoesNotLeakOtherUsersChats` — фильтр по userId.
  - `NoMatches_ReturnsEmpty` — пустой результат.
  - `TruncatesLongQuery` — обрезка > 200 символов.
  - Всего тестов: **47 → 55**.
- **Chat UI — ⌘K-модалка поиска по чатам (KI-078B-2, v1.4.x)**:
  - Модалка по центру (`Ctrl+K` или SVG 🔍 в collapsed sidebar).
  - Input с debounce 200ms → `GET /api/chats?search=` (backend KI-078B-1).
  - Список результатов: title + относительная дата + snippet с подсветкой `<mark>`.
  - Навигация ↑/↓ (циклическая), Enter — открыть чат, Esc / клик вне / ✕ — закрыть.
  - Защита от гонок fetch (`globalSearchRequestId`).
  - Fix: SVG 🔍 в collapsed sidebar теперь открывает ⌘K-модалку (было — разворот sidebar).
  - Блокировка скролла body на время открытой модалки.
  - Fix: кнопка «Очистить поле» (появляется при непустом input; сброс без закрытия модалки).
  - Локализация RU + EN (6 ключей: `ChatSearchGlobal*`).
- **Chat API — расширенный поиск с snippet (KI-078B-1, v1.4.x)**:
  - `ChatSearchResultDto` (`Id`, `Title`, `Model`, `UpdatedAt`, `MatchedField`, `Snippet`, `SnippetMatchStart`, `SnippetMatchLength`).
  - `IChatService.SearchUserChatsWithSnippetAsync(userId, search)` — поиск с превью совпадения (≈30 символов до + 100 после), позиция совпадения для подсветки.
  - Приоритет: сначала title, потом content. Один чат — один результат.
  - `ChatListItemDto` расширен 4 nullable-полями (`MatchedField`, `Snippet`, `SnippetMatchStart`, `SnippetMatchLength`) — заполняются только при `search`.
  - `ChatController.GetChatsAsync`: при `search != null` — использует расширенный метод.
- **Chat UI — внутричатовый поиск (KI-078A, v1.4.x)**:
  - Панель поиска в правом верхнем углу `.chat-main` (Ctrl+F / кнопка 🔍 в header).
  - Подсветка совпадений `<mark class="chat-search-hit">` в ленте активного чата (user + assistant).
  - Активное совпадение — `.chat-search-hit-active` (жёлтый фон + синяя рамка) + авто-скролл.
  - Счётчик «N из M», кнопки ↑ / ↓ (циклический переход), Enter / Shift+Enter.
  - Закрытие: `Esc`, клик вне панели, кнопка ✕.
  - Debounce 150ms; поиск по `.chat-message-content` (tool-блоки не трогаем).
  - Авто-закрытие при переключении чата.
  - Локализация RU + EN (7 ключей: `ChatSearchInChat*`).
- **Chat UI — collapse/expand sidebar (KI-079, DeepSeek-style, v1.4.x)**:
  - Collapsed-состояние — mini-rail 56px с иконками (SVG): «+» (новый чат), 🔍 (поиск), `«`/`»` (toggle).
  - Кнопка «+ Новый чат» — текст в expanded, SVG `+` в collapsed.
  - Кнопка 🔍 — только в collapsed (в expanded используется input `#chat-search`).
  - Кнопка toggle — одна, работает в обоих состояниях (title меняется по data-атрибутам).
  - Анимация `width .2s ease`; collapsed → `.chat-container.chat-sidebar-collapsed` (`width: 0`).
  - Сохранение состояния в `localStorage["chat.sidebarCollapsed"]`, восстановление при загрузке.
  - Горячая клавиша `Ctrl+B` (toggle, `preventDefault` — не конфликтует с закладками браузера).
  - Mobile (< 768px): collapse отключён, sidebar всегда виден.
  - Локализация RU + EN (`ChatSidebarCollapse` / `ChatSidebarExpand`).

### Planned
- **v1.4.x**: KI-047 (PATCH/DELETE fallback), KI-049 (tokensIn/Out через tiktoken), KI-053 (multi-user approvals), KI-067 (per-user retention), KI-076 (статистика по агентам), KI-077 (model:null при PUT), KI-078 (внутричатовый поиск + подсветка), KI-079 (collapse sidebar), KI-080 (поле ввода на всю ширину).
- **v1.5.0**: RAG (Qdrant / embeddings), Knowledge base.

---

## [1.4.0] — 2026-09-24

**Multi-Agent (KI-052)** — Chat работает через 6 специализированных суб-агентов + универсальный `consult_secondary_agent`.

### Added
- **v1.4.0 Фаза 1 (KI-052)**: реестр специализированных суб-агентов.
  - DTO `SubAgentDescriptor` — Name, DisplayName (RU), Description, SystemPrompt, AllowedTools, Model, MaxSteps, RequiresApprovalByDefault, Disabled.
  - `SubAgentTaskRequest.SystemPromptOverride` + `ModelOverride` (обратносовместимо).
  - `ISubAgentRegistry` + `SubAgentRegistry` (singleton, читает `SubAgents:*` из appsettings.json).
  - 6 агентов в `appsettings.json` (+ в Development): `file_system_agent`, `code_agent`, `web_agent`, `git_agent`, `github_agent`, `planner_agent`.
  - 4 unit-теста (`SubAgentRegistryTests`).
  - `docs/development/v1.4/DESIGN.md` — дизайн-документ фазы.
- **v1.4.0 Фаза 6.1-6.4 (KI-052)**: backend админки агентов.
  - DTO: `AgentListItemDto`, `UpdateAgentRequest`.
  - `AdminAgentsController`: `GET /api/admin/agents`, `PUT /api/admin/agents/{name}`, `POST /api/admin/agents/{name}/reset`.
  - Persist override'ов: JSON в `AppSettings` по ключу `SubAgents.{name}` (upsert).
  - Восстановление при старте: `Program.LoadSubAgentOverridesAsync`.
  - Локализация: 15 ключей в `.resx` (RU + EN).
- **v1.4.0 Фаза 6.5-6.6 (KI-052)**: frontend админки агентов.
  - `Admin.cshtml`: 7-я вкладка «Агенты» (таблица: техническое имя, отображаемое, модель, инструментов, approval, вкл/выкл, действия).
  - `admin-agents.js`: модуль вкладки (загрузка, редактирование, сброс); ленивая инициализация через `shown.bs.tab`.
  - `admin.js`: `showModal` экспортирован для переиспользования.
  - Модалка редактирования: DisplayName, Description, Model, MaxSteps, SystemPrompt, AllowedTools (textarea построчно), RequiresApproval, Disabled.
- **v1.4.0 Фаза 7 (KI-052)**: тесты `AgentToolBase` (7 новых).
  - `AgentToolBaseTests` (Integration): пустой/длинный task, unknown/disabled агент, передача дескриптора в `SubAgentTaskRequest`, clamp maxSteps к 30.
  - Всего тестов: **47/47** (было 40).

### Changed
- **v1.4.0 Фаза 2 (KI-052)**: `ModelOverride` + `SystemPromptOverride` в суб-агентах.
  - `ILmStudioClient.CompleteAsync(..., string model = null)` — перегрузка с явной моделью (обратносовместимо).
  - `LmStudioClient`: если `model` передан — используется он; иначе `LmStudio:Model`.
  - `SubAgentService`: `BuildSystemMessage(maxSteps, overridePrompt)` — `SystemPromptOverride` имеет приоритет над `SubAgent:SystemPrompt`.
  - Основной цикл, финальное резюме и reviewer — все используют `request.ModelOverride`.
- **v1.4.0 Фаза 3 (KI-052)**: `AgentToolBase` + 6 инструментов-обёрток.
  - `AgentToolBase` — единый базовый класс (params, resolve дескриптора, вызов `SubAgentService` через `Func<ISubAgentService>` — разрыв DI-цикла).
  - 6 наследников: `FileSystemAgentTool`, `CodeAgentTool`, `WebAgentTool`, `GitAgentTool`, `GitHubAgentTool`, `PlannerAgentTool`.
  - `RequiresApprovalByDefault` резолвится из дескриптора (`SubAgents:X.RequiresApproval`).
  - `SubAgentService`: расширена защита от рекурсии — запрет `consult_secondary_agent` + любого `*_agent` внутри суб-агента.
- **v1.4.0 Фаза 4 (KI-052)**: усилены system-промпты всех 6 агентов.
  - `web_agent`: обязательно использовать инструменты перед ответом, указывать источник.
  - `file_system_agent`: проверять `list_directory` перед операциями.
  - `code_agent`: обязательно проверять код запуском.
  - `git_agent`: начинать с `git_status`, не делать force-push.
  - `github_agent`: начинать с `gh_auth_status`.
  - `planner_agent`: `save_memory` / `get_system_info` по назначению.
- **v1.4.0 Фаза 5 (KI-052)**: Chat использует `SubAgentRegistry` вместо `SubAgent:DefaultAllowedTools`.
  - `ChatStreamService`: в конструктор добавлен `ISubAgentRegistry`.
  - Список tools = `SubAgentRegistry.GetEnabled()` (6 агентов) + `consult_secondary_agent` (fallback).
  - **Эффект:** Chat видит **7 инструментов** вместо 12. LLM вызывает `file_system_agent` вместо `list_directory` + `read_file` + `save_file` по отдельности.

### Fixed
- **KI-073 (Fixed)**: первый `dotnet test` на Windows после cold build занимал ~44-60 с. Диагностика: **Defender — не главная причина** — виноват **testhost boot** (30+ DLL из API, PuppeteerSharp). Решено: `scripts/setup/configure-defender.ps1` + отключение Dev Drive protection + reboot → **1.4 с**.
- **KI-075**: `ToolRegistry` логировал «инициализирован» на `LogInformation` при каждом scope — спам в проде. Понижено до `LogDebug`.

### Documented
- **KI-076** (Deferred, v1.4.x): статистика по агентам (TotalRuns, AvgTime, SuccessRate) в админке.
- **KI-077** (Documented): `model: null` при PUT агента = «сбросить на default», а не «не менять».
- **KI-078, KI-079, KI-080** (Deferred, v1.4.x): по мотивам DeepSeek — внутричатовый поиск + подсветка, collapse sidebar, поле ввода на всю ширину.

---

## [1.3.1] — 2026-09-24

### Added
- **KI-068**: новый метод `IChatService.SearchUserChatsAsync(userId, search)` — регистронезависимый поиск (кросс-провайдерно). Пустой запрос эквивалентен `GetUserChatsAsync`.

### Changed
- **KI-069**: переименование чата в sidebar теперь через **inline-edit** (ChatGPT-style) вместо `prompt()`. Двойной клик по названию → `<input>`, **Enter** = сохранить, **Esc** = отмена, **blur** = сохранить. Кнопка ✏️ вызывает тот же inline-edit. Backend (`PATCH /api/chats/{id}`) без изменений.
- **KI-068**: поиск по чатам теперь **server-side** (по названию + содержимому сообщений). Запрос `GET /api/chats?search={q}`. Frontend — debounce 300ms, убран клиентский фильтр.

### Fixed
- **KI-068 (регрессия)**: поиск по чатам возвращал неполный список при кириллице. Причина: SQLite `LOWER()` не обрабатывает не-ASCII — `LOWER('Привет') = 'Привет'`, поэтому `LIKE '%прив%'` не матчил. Решение: фильтрация в памяти через `string.Contains(term, StringComparison.OrdinalIgnoreCase)`. См. RULES § 4.26.
- **KI-071**: даты в JSON сериализовались без суффикса `Z` (Sqlite + EF Core возвращают `DateTime` с `Kind=Unspecified`). JS `new Date()` парсил их как local → только что созданные чаты показывались как «3 ч назад» (UTC+3). Решение: `AddNewtonsoftJson(o => o.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc)` + то же для `SseJsonSettings`. См. RULES § 4.27.
- **KI-072**: при активном поиске новый чат попадал в отфильтрованный sidebar. Решение: `createChat()` сбрасывает поиск и перезагружает полный список перед созданием.
- **KI-064**: `wikipedia_search` через корпоративный прокси зависал на ~43 секунды. Решение: явный `Timeout = 15s` + retry 1 раз с задержкой 1s. См. RULES § 4.28.
- **KI-043**: утечка памяти в `RateLimitingMiddleware`. Решение: `Timer` каждые 2 минуты удаляет `LimiterEntry` с `LastUsedUtc` > 5 минут. Middleware реализует `IDisposable`.

---

## [1.3.0] — 2026-09-23

### Added
- **Chat UI (v1.3 Фаза 1.1)**: сущности `Chat` и `ChatMessage` + миграция `AddChatAndChatMessages`. Таблицы `Chats`, `ChatMessages` в БД; индексы `IX_Chats_UserId_UpdatedAt`, `IX_ChatMessages_ChatId_CreatedAt`.
- **ChatService (v1.3 Фаза 1.2)**: `IChatService` + `ChatService` — CRUD чатов и сообщений, проверка владения (`userId`), `DeleteOldChatsAsync` для retention.
- **LM Studio SSE (v1.3 Фаза 1.3)**: `ILmStudioClient.ChatStreamAsync` — `IAsyncEnumerable<ChatCompletionChunk>` для стриминга. DTO `ChatCompletionChunk` (DeltaContent, DeltaReasoning, DeltaToolCall, FinishReason, Usage, IsDone). Парсер SSE-формата (`data: {...}`, `[DONE]`, tool_calls частями). Сохранён `CompleteAsync` для `SubAgentService`.
- **Design doc v1.3**: `docs/development/v1.3/DESIGN.md` — Chat UI (sidebar, SSE, инлайн tool calls), API-контракты, схема данных, план работ.
- **Unit-тесты**: 5 новых на парсинг SSE (`LmStudioSseParseTests`). Всего: **24/24**.
- **ChatController (v1.3 Фаза 1.4)**: REST API чатов — `GET /api/chats` (список), `GET /api/chats/{id}` (детали + история), `POST /api/chats` (создать), `PATCH /api/chats/{id}` (обновить title/model/systemPrompt), `DELETE /api/chats/{id}` (удалить). DTO: `ChatListItemDto`, `ChatDetailDto`, `ChatMessageDto`, `CreateChatRequest`, `UpdateChatRequest`. Проверка владения (`userId` из claims), `[Authorize]` на всех методах.
- **Tool calling infrastructure (v1.3 Фаза 1.6.A)**: `ToolDefinitionsBuilder` — единый построитель JSON-схем инструментов в формате OpenAI Function Calling. Используется Chat (10 инструментов из `SubAgent:DefaultAllowedTools`) и SubAgent. Поддерживает белый список и список исключений.
- **Tool calling — SSE events (v1.3 Фаза 1.6.A.2.1)**: `ChatStreamEvent.ToolCall` и `ChatStreamEvent.ToolResult`. DTO `ChatToolCallDto` (`id`, `name`, `arguments`, `requiresApproval`) и `ChatToolResultDto` (`id`, `name`, `success`, `content`, `message`). Готовит почву для multi-turn loop в `ChatStreamService`.
- **Tool calling — аккумулятор (v1.3 Фаза 1.6.A.2.2)**: `ToolCallsAccumulator` — накопление инкрементальных `delta.tool_calls` из SSE-стрима LM Studio. Складывает `arguments` по `index`, собирает завершённые вызовы формата OpenAI.
- **Tool calling — multi-turn loop (v1.3 Фаза 1.6.A.2.3)**: `ChatStreamService.StreamAsync` расширен до multi-turn цикла (до 5 итераций). Стрим → накопление `tool_calls` → выполнение через `IToolRegistry` → SSE-события `tool_call` / `tool_result` → сохранение в БД → повторный стрим. При `Request.UseTools == false` — старое поведение (без tools). Инструменты с `RequiresApprovalByDefault == true` возвращают `ToolResult.Fail` (полная реализация — Фаза 1.7).
- **Тесты tool calling (v1.3 Фаза 1.6.B)**: 2 новых интеграционных теста в `ChatStreamServiceTests` — успешный multi-turn loop (list_directory → результат → финальный ответ) и approval-инструмент (save_file → ToolResult.Fail без выполнения). FakeLmStudioClient расширен — поддержка итераций. Всего: **29/29**.
- **Approvals в чате — каркас (v1.3 Фаза 1.7.1)**: `ChatApprovalDecision` (enum), `ChatApprovalRequiredDto`, `ChatApprovalResolvedDto`, `IChatApprovalCoordinator`. Дополнены `ChatStreamEvent.ToolApprovalRequired` и `.ToolApprovalResolved`. Готовит почву для Singleton-координатора (Шаг 1.7.2).
- **Approvals в чате — REST-endpoints (v1.3 Фаза 1.7.4)**: `POST /api/chat/approvals/{callId}/approve` и `POST /api/chat/approvals/{callId}/reject`. Вызывают `IChatApprovalCoordinator.ResolveAsync`, будят ожидающий SSE-стрим. `[Authorize]` — только аутентифицированные. Если ожидающий не найден (таймаут) — `{ success: false }`.
- **Approvals в чате (v1.3 Фаза 1.7 — полная реализация)**: 
  - `POST /api/chat/approvals/{callId}/approve` — подтвердить вызов инструмента.
  - `POST /api/chat/approvals/{callId}/reject` — отклонить.
  - SSE-события `tool_approval_required` / `tool_approval_resolved`.
  - Координатор Singleton + cleanup (KI-043 учтён).
  - camelCase в SSE-событиях (единый стиль).
  - Smoke-тест end-to-end: `save_file` → approval_required → reject → tool_result(fail) → финал.
- **Фаза 1.7 v1.3 закрыта.** Backend полностью готов к Chat UI.
- **Chat UI — каркас страницы `/chat` (v1.3 Фаза 2.0.1)**: 
  - `ChatViewController` + Razor-страница `Views/Chat/Index.cshtml`.
  - Layout: sidebar (список чатов) + область сообщений + поле ввода.
  - `wwwroot/css/chat.css` — стили чата.
  - `wwwroot/js/modules/chat.js` — заглушка (инициализация без логики).
  - `_Layout.cshtml`: пункт меню «Чат» + `@RenderSection("Styles")`.
  - **Chat UI — локализация (v1.3 Фаза 2.0.1)**: 16 новых ключей в `SharedResources.resx` (EN) и `SharedResources.ru.resx` (RU). Ключи для страницы `/chat`: `Чат`, `Новый чат`, `Удалить чат`, `Выберите чат или создайте новый`, `Введите сообщение…`, `Отправить`. Резерв для Шагов 2.0.2–2.0.4 (sidebar CRUD, SSE-ошибки). Соответствует правилу 1.14.
- **Chat UI — endpoint моделей (v1.3 Фаза 2.0.2a)**: `GET /api/models` — возвращает список моделей LM Studio (`/v1/models`) + модель по умолчанию из `LmStudio:Model` (всегда первая, `isDefault: true`). Fallback: если LM Studio недоступен — возвращается только default (UI работает в offline-режиме). `[Authorize]`. DTO `ModelInfoDto` (`id`, `name`, `isDefault`). Готовит почву для UI-селектора модели (DESIGN.md § 13.1).
- **Chat UI — sidebar (v1.3 Фаза 2.0.2b)**: `chat.js` реализован полностью для sidebar:
  - `loadChats()` / `loadModels()` — загрузка списка чатов и моделей.
  - `createChat()` — создание (модель по умолчанию из `/api/models`).
  - `selectChat(id)` — переключение с загрузкой истории.
  - `deleteChat(id)` — удаление с подтверждением.
  - `renderChatList()` / `renderMessages()` — рендер sidebar и ленты.
  - Синхронизация активного чата с URL через `history.replaceState` (`?chatId=N`).
  - Относительные даты (`только что`, `5 мин назад`, `вчера`, `22.09`).
  - Минимальное отображение tool calls (серые блоки с именем инструмента).
  - Автоскролл к последнему сообщению.
  - `chat.css` дополнен стилями сообщений и tool-блоков.
- **Chat UI — SSE-стриминг (v1.3 Фаза 2.0.3)**: `chat.js` умеет отправлять сообщения и стримить ответ:
  - `sendMessage()` — `POST /api/chat/stream` с `useTools: true`.
  - `readSseStream()` — чтение SSE через `fetch` + `ReadableStream`.
  - `handleSseEvent()` — обработка `start`/`delta`/`done`/`error`/`tool_call`/`tool_result`.
  - `tool_approval_required` / `tool_approval_resolved` — заглушки (полная обработка — Фаза 2.0.4).
  - Оптимистичное отображение user-пузыря, индикатор «Печатает…» (три точки), потоковая отрисовка delta.
  - Блокировка input/кнопки на время стрима, автоскролл (только если пользователь был внизу).
  - Enter — отправка, Shift+Enter — новая строка, автоувеличение textarea.
  - Локальное обновление `UpdatedAt` в sidebar после `done`.
  - `chat.css`: стили `chat-typing-indicator`, `chat-message-streaming`, `chat-message-error`, `chat-tool-result.success/error`.
- **Chat UI — Approvals (v1.3 Фаза 2.0.4)**: интеграция `_ApprovalModal` в чат.
  - `approvals.js`: рефакторинг — общая логика вынесена в `_showApprovalModal({ approveUrl, rejectUrl, askReason })`.
  - Новый экспорт `requestChatApproval(callId, ...)` — для flow `/api/chat/approvals/{callId}/...` (без prompt причины, см. Q6 Фазы 1.7).
  - `requestApproval(actionId, ...)` — сохранён (обратная совместимость с `/test`).
  - `chat.js`: обработка SSE-события `tool_approval_required` — показ модалки (fire-and-forget, не блокирует SSE reader).
  - `chat.js`: `tool_approval_resolved` — логирование (UI-индикатор — v1.3.x).
  - Стрим продолжается автоматически после решения: `tool_approval_resolved` → `tool_result` → `delta` → `done`.
- **Chat UI — переименование + авто-нумерация (v1.3 Фаза 2.0.5a)**:
  - Кнопка ✏️ в sidebar рядом с 🗑 (появляется при hover / на активном чате).
  - `renameChat(id)` — `prompt()` с текущим именем → `PATCH /api/chats/{id}` (`{ title }`). Пустое имя отклоняется; при совпадении с текущим — no-op.
  - Локальное обновление `state.chats` + синхронизация с header для активного чата.
  - `generateNextChatTitle()` — авто-нумерация «Новый чат», «Новый чат 2», «Новый чат 3», … (учитывает максимальный существующий N).
  - `chat.css`: `.chat-list-item-delete` → `.chat-list-item-action` (общий класс для edit/delete), разные цвета hover (edit — синий, delete — красный).
- **Chat UI — ChatGPT-style скроллинг (v1.3 Фаза 2.0.5b)**:
  - `state.autoScroll` — флаг прилипания к низу ленты.
  - Scroll listener на `#chat-messages` — если пользователь отскроллил > 80px от низа → `autoScroll = false`, вернулся к низу → `autoScroll = true`.
  - `scrollToBottom(force)` — при `force === true` скроллит принудительно и включает autoScroll; иначе — только при `autoScroll === true`.
  - Кнопка «↓ Вниз» (`.chat-scroll-down`) — появляется при `autoScroll === false`, клик → принудительный скролл. Создаётся динамически в JS (Razor не тронут).
  - При переключении чата (`selectChat`) `autoScroll` сбрасывается в `true`.
  - `chat.css`: `.chat-main` → `position: relative`, стили `.chat-scroll-down`.
- **Chat UI — Фаза 2.0 закрыта (v1.3)**: Chat UI реализован end-to-end.
  - `/chat` — Razor-страница с sidebar, лентой сообщений, полем ввода.
  - Sidebar: список чатов (относительные даты), создание, удаление, переименование (✏️), авто-нумерация «Новый чат N».
  - SSE-стриминг: `POST /api/chat/stream`, потоковая отрисовка delta с мигающим курсором.
  - Tool calling: до 5 итераций, SSE-события `tool_call` / `tool_result`.
  - Approvals: модалка `_ApprovalModal` с drag-and-drop, X=Reject, countdown 5 минут.
  - ChatGPT-style скроллинг: кнопка «↓ Вниз», автоскролл отключается при ручной прокрутке вверх.
  - Enter — отправка, Shift+Enter — новая строка, автоувеличение textarea.
  - Синхронизация активного чата с URL (`?chatId=N`).
  - Локализация RU/EN.
  - Скриншоты и подробности — `docs/development/v1.3/DESIGN.md` § 14.
- **Chat UI — Copy message (v1.3 Фаза 2.1.1)**: на каждом сообщении (user/assistant) — кнопка 📋 под контентом. Появляется при hover, копирует текст в clipboard. Inline-фидбек (✅ на 1.5 сек). Работает и во время стриминга (dataset.copyText синхронизируется с накопленным текстом). Fallback — `execCommand('copy')` для HTTP / старых браузеров. Tool-блоки не копируются.
- **Chat UI — Markdown + code blocks (v1.3 Фаза 2.1.4)**: рендеринг ответов LLM через `marked` + `DOMPurify` (bundled локально в `wwwroot/lib/marked/` и `wwwroot/lib/dompurify/`).
  - `renderMarkdown(text)` — `DOMPurify.sanitize(marked.parse(text))` (XSS-защита обязательна; LLM-вывод никогда не вставляется без sanitize).
  - Поддерживаются: `**bold**`, `*italic*`, `## headings`, списки, `code`, ` ```code blocks``` `, ссылки, таблицы.
  - Во время стрима — plain-text (без мерцания); после `done` — финальный Markdown-рендер (`finalizeAssistantBubble`).
  - **Code blocks** — ChatGPT-style: wrapper `.chat-code-block` с шапкой (язык + кнопки Copy/Download).
    - Copy — копирует код, inline ✅ на 1.5 сек.
    - Download — скачивает как файл с расширением по языку (`langToExtension`).
- `chat.css`: стили `.chat-markdown` (GitHub-like) и `.chat-code-block`.
- **Chat UI — подсветка синтаксиса (v1.3 Фаза 2.1.4.1)**: `highlight.js` 11.9.0 (UMD, полный бандл) + тема `github.min.css`, bundled локально в `wwwroot/lib/highlight/`. Подсветка срабатывает в `enhanceCodeBlocks()` через `hljs.highlightElement(code)`. Автодетект языка по `class="language-xxx"` или auto-detect. Фон/паддинг темы нейтрализованы, чтобы wrapper `.chat-code-block` (#f6f8fa) оставался единым — работает только палитра токенов. Fallback: если `hljs` не загружен — code block без подсветки, Copy/Download работают.
- **Chat UI — Regenerate endpoint (v1.3 Фаза 2.1.2.2)**:
  - `ChatStreamRequest.Regenerate` (bool) — если `true`, стрим не сохраняет новое user-сообщение, а удаляет последний assistant-exchange (assistant + tool) и генерирует ответ заново от последнего user.
  - `POST /api/chat/regenerate` — принимает `{ chatId }`, эмулирует `StreamAsync` с `Regenerate = true` и `UseTools = true`. Возвращает SSE-поток, идентичный `/api/chat/stream`.
  - `ChatStreamController`: общая логика вынесена в private `StreamInternalAsync` (используется и `stream`, и `regenerate`).
  - `ChatStreamService.StreamAsync`: при `Regenerate = true` вызывает `DeleteLastAssistantExchangeAsync`, находит последний user-message и стартует общий multi-turn loop.
- **Chat UI — Regenerate (v1.3 Фаза 2.1.2.3)**: кнопка 🔄 в `.chat-message-actions` на **последнем** assistant-сообщении (рядом с 📋). Клик: удаляет последний assistant-пузырь из DOM, вызывает `POST /api/chat/regenerate`, стримит новый ответ через существующий `readSseStream`. Кнопка 🔄 автоматически перевешивается на новый bubble в `finalizeAssistantBubble()` и снимается со всех остальных.
- **Chat UI — Stop (v1.3 Фаза 2.1.3.2)**: кнопка ⏹ рядом с input для прерывания стрима.
  - `#btn-stop` — отдельный элемент в `.input-group`; во время стрима `#btn-send` скрыта, `#btn-stop` показана.
  - `state.abortController` + `fetch(..., { signal })` — при Stop рвётся SSE-соединение.
  - `AbortError` — частичный assistant-пузырь удаляется из DOM; **частичный ответ не сохраняется в БД**.
  - Работает для обычного стрима и Regenerate.
  - **Audit при Stop (2.1.3.3)**: при отмене в `AuditLogs` пишется запись `Status = "Cancelled"`, `ToolName = chat_stream | chat_regenerate`, `DurationMs` (Stopwatch), `ClientIp`. Текст сообщения не логируется (без PII).
- **Chat UI — Retry после Stop (v1.3 Фаза 2.1.3.5, KI-065)**:
  - Кнопка «🔄 Повторить» под последним user-сообщением — если ответ был прерван через Stop или не сгенерирован.
  - Backend: `ChatStreamService.StreamAsync` (Regenerate) — убрана жёсткая проверка `deleted == 0`; best-effort удаление; работает при последнем user (когда ответ не сохранён).
  - Frontend: `regenerateLastMessage({ allowNoAssistant: true })` — переиспользован для Retry; `showRetryOnLastUser()` вызывается из `AbortError`; `renderMessages` показывает кнопку при загрузке истории, если последнее — user.
  - CSS: `.chat-message-action-with-text`, `.chat-message-actions-always-visible`. 
- **Chat UI — Audit Stop (v1.3 Фаза 2.1.3.3)**: `ChatStreamController` инжектит `IAuditService`; при `OperationCanceledException` (клиент нажал Stop) пишется запись в `AuditLogs`:
  - `ToolName`: `chat_stream` (обычный) или `chat_regenerate` (Regenerate).
  - `Status`: `Cancelled`.
  - `ParametersJson`: `{ chatId, regenerate, hasMessage }` — **без текста сообщения** (правило 5.x — без PII).
  - `ResultJson`: `{ reason: "user_stop" }`.
  - `DurationMs`: длительность стрима (Stopwatch).
  - `ClientIp`: из `HttpContext.Connection.RemoteIpAddress`.
- **Chat UI — AI-title frontend (v1.3 Фаза 2.2.1b)**:
  - `maybeGenerateTitle()` — фоновый `POST /api/chats/{id}/generate-title` после **первого** успешного ответа в пустом чате.
  - Триггер: `state.activeChatMessageCount === 0` до отправки + `dataset.streamCompleted === '1'` после `done`.
  - Защита от повторного вызова: title должен матчить `/^Новый чат(\s+\d+)?$/` — иначе пропускаем (пользователь переименовал вручную).
  - Race-safe: `chatIdAtRequest` фиксируется — если пользователь переключится, пока идёт запрос, обновится правильный чат.
  - При успехе: `renderChatList()` + `renderChatHeader()` — без F5.
  - `state.activeChatMessageCount` — новый счётчик для активного чата (init в `selectChat`).
- **Chat UI — селектор модели (v1.3 Фаза 2.2.4, DESIGN § 13.1)**: `<select id="chat-model-select">` в header чата (вместо `<small>`).
  - Заполняется из `state.models` (уже загружено в `initChatPage` → `loadModels`).
  - Формат: `<id>` + ` (по умолчанию)` у `isDefault: true`. Если текущей модели нет в списке — disabled option `<id> (недоступна)`.
  - `onChatModelChanged`: `PATCH /api/chats/{id}` с `{ model }`; оптимистично обновляет `state.activeChat.model` + `state.chats[i].model`; при ошибке — откат.
  - Селектор **disabled во время стрима** (`setStreamingUI`). Попытка смены в этот момент игнорируется.
  - `showEmptyState`: очистка селекта (нет активного чата).
  - Локализация: 4 новых ключа (`Модель`, `по умолчанию`, `недоступна`, `Нет моделей`) — передаются в JS через `data-*`-атрибуты.
  - `chat.css`: `.chat-model-select` (моноширинный, компактный, hover/focus/disabled).
- **Chat retention (v1.3 Фаза 2.2.5, DESIGN § 13.2)**: фоновый `ChatRetentionService` (по образцу `AuditRetentionService`).
  - Удаляет чаты старше `Chat:Retention:DefaultDays` (по умолчанию — 30 дней) через `IChatService.DeleteOldChatsAsync` (bulk DELETE через `ExecuteDeleteAsync`).
  - Первый запуск — через 2 минуты после старта; далее раз в `CleanupIntervalHours` (по умолчанию — 24 ч).
  - Защита от опечаток: `DefaultDays` ограничивается сверху `MaxDays` (365).
  - `Chat:Retention:Enabled = false` — полностью отключает retention (рекомендуется в dev).
  - Метрика Prometheus: `iichattools_chat_cleanup_total` (label `reason="retention"`).
  - Логирование: `Retention чатов: удалено {Count} чатов старше {Cutoff}`.
  - Per-user override — отложено в v1.3.x (**KI-067**).
- **Chat UI — поиск по чатам (v1.3 Фаза 2.2.3)**: input `#chat-search` в sidebar (между кнопкой «+ Новый чат» и списком).
  - Клиентский фильтр по `chat.title` (case-insensitive, `includes`).
  - `state.searchQuery` сохраняется при createChat/deleteChat/renameChat — фильтр не сбрасывается.
  - «Ничего не найдено» — если ни один чат не матчит (локализация через `data-label-no-results`).
  - Поиск по содержимому сообщений — отложено в v1.3.x (**KI-068**).
- **Chat UI — Backend edit user-message (v1.3 Фаза 2.2.6a)**:
  - `IChatService.EditUserMessageAsync(messageId, userId, newContent)` — обновляет `Content` user-сообщения + удаляет все сообщения после (ассистент + tool + последующие) + обновляет `Chat.UpdatedAt`.
  - `POST /api/chat/messages/{id}/edit` — принимает `{ content }`. Возвращает `{ success, data: { messageId, deletedCount } }`.
  - Валидация: не пустое, role == "user", владение через `chat.UserId`. Обобщённый ответ «Сообщение не найдено» — не палим чужие чаты.
  - `ChatStreamController`: + `IChatService` в конструктор.
  - DTO: `EditUserMessageRequest` (request), `EditUserMessageResult` (сервис).
- **Chat UI — inline-edit user-message (v1.3 Фаза 2.2.6b)**:
  - Кнопка ✏️ в `.chat-message-actions` на **user**-сообщениях (при hover).
  - Для свежих сообщений ✏️ добавляется в SSE-событии `start` (после optimistic-рендера id ещё не известен). Для истории (F5) — сразу в `renderMessage`.
  - Клик по ✏️ → `.chat-message-content` заменяется на `<textarea>` + кнопки Save/Cancel.
  - **Enter** = Save, **Shift+Enter** = новая строка, **Esc** = Cancel.
  - Save: `POST /api/chat/messages/{id}/edit` (Фаза 2.2.6a) → обновление UI → удаление DOM-сообщений после → `regenerateLastMessage({ allowNoAssistant: true })`.
  - Edit доступен только в `state.isStreaming === false`.
  - `chat.css`: `.chat-message-edit` (textarea), `.chat-message-edit-actions`.
  
### Changed
- **ChatStreamService (v1.3 Фаза 1.6.A.2.4)**: добавлена зависимость `IWorkspaceResolver`. `ToolExecutionContext.WorkspaceRoot` теперь реально резолвится (было `null`) — FS-инструменты в чате работают.
- **Config (v1.3 Фаза 1.6.A.2.4)**: `SubAgent:DefaultAllowedTools` обновлён — только **read-only** инструменты без approval: `read_file`, `find_files`, `get_file_metadata`, `fuzzy_find_local_files`, `git_status`, `git_log`, `git_diff`, `web_search`, `wikipedia_search`, `get_system_info`. Mutating-инструменты (`list_directory`, `save_file`, `replace_text_in_file`, `execute_command`, `git_add`, …) требуют approval и станут доступны в чате после Фазы 1.7.
- **`appsettings.Development.json`**: добавлен `SubAgent:DefaultAllowedTools` (ранее отсутствовал — все 40 инструментов попадали в чат).
- **`ListDirectoryTool` (v1.3 Фаза 1.6.A.2.4)**: `RequiresApprovalByDefault` → `false`. `list_directory` — read-only операция (возвращает список файлов, ничего не меняет), не требует подтверждения. Добавлен обратно в `SubAgent:DefaultAllowedTools`.
- **Документация (v1.3 Фаза 1.6.B)**: обновлены `docs/KNOWN_ISSUES.md` — KI-051 → Resolved, KI-049 дополнен, добавлены KI-054 (approvals в чате, Фаза 1.7) и KI-055 (tool calling реализован). KI-048 дополнен планом автоматизации git config.
- **Тест `StreamAsync_ToolCalling_RequiresApproval_DoesNotExecute` (v1.3 Фаза 1.7.3)**: обновлён под новую логику — проверяет SSE-события `tool_approval_required` и `tool_approval_resolved` + сообщение «Пользователь отклонил вызов инструмента» (ранее — «Требуется подтверждение»).
- **`.gitignore` (v1.3 Фаза 2.0.2b)**: добавлены правила для `cookies.txt` и `.commit-msg.txt` — артефакты локальных smoke-тестов и here-string-коммитов.
- **Chat UI — sidebar (v1.3 Фаза 2.0.2b)**: кнопка удаления чата добавлена в каждый элемент sidebar (появляется при hover / на активном чате). `<a>` → `<div role="button" tabindex="0">` — позволяет вкладывать `<button>` без семантических конфликтов. Кнопка 🗑 в header (`#btn-delete-chat`) сохранена для активного чата.
- **Chat UI — AI-title cleanup (v1.3 Фаза 2.2.1b)**: удалены диагностические `console.log` из `sendMessage`/`maybeGenerateTitle` после подтверждения работы. Оставлен один информативный `[chat] AI-title: "..."` + `console.warn` на реальные проблемы (нет чата, regex не матчит, response failed).

### Fixed
- **v1.3 Фаза 1.1**: warnings CS0108 (Chat.UpdatedAt скрывает BaseEntity.UpdatedAt — намеренно, `new`), CS0618 (HasName → HasDatabaseName в EF Core 10).
- **v1.3 Фаза 1.3**: warnings CS1574 (cref ArgumentNullException и др. — добавлен `using System;`), CA2024 (reader.EndOfStream в async — заменён на проверку `line == null`).
- **KI-057 (v1.3 Фаза 2.0.2a)**: `GET /api/models` — embedding-модели (например, `text-embedding-nomic-embed-text-v1.5`) исключаются из списка heuristic-фильтром по подстроке `embed`. Причина: эти модели не поддерживают `/v1/chat/completions` и приводят к 400 при выборе в чате.
- **Chat UI — approvals UX (v1.3 Фаза 2.0.4)**: две проблемы, выявленные на smoke-тесте:
- **Модалку нельзя было подвинуть** — добавлен drag-and-drop по `.modal-header` (курсор `move`, `position: fixed` на время drag, сброс стилей при `hidden.bs.modal`).
- **Закрытие крестиком (X) подвешивало UI** — модалка закрывалась, но reject на сервер не отправлялся; SSE-стрим ждал 5 минут, input/btn-send оставались заблокированными. Теперь **X = Reject**: при `hidden.bs.modal` без решения автоматически отправляется reject на сервер (в /chat — без причины, в /test — с reason «Закрыто пользователем»).
- Исправлена утечка listener'ов `hidden.bs.modal` — `{ once: true }`.
- `getOrCreateInstance` вместо `new bootstrap.Modal(...)` — нет warning'ов при повторных открытиях.
- **KI-046 (v1.3 Фаза 2.0.6a + hotfix)**: `MessageCount` в `ChatListItemDto` — реализован подсчёт через `IChatService.GetMessageCountsAsync(userId)` (один SQL `GROUP BY ChatId`). Sidebar отображает meta-строку в формате «<дата> · N сообщ.» через `formatChatMeta(chat)` (пустые чаты — только дата).
- **KI-059**: placeholder `CHANGE_ME_VIA_USER_SECRETS` в `Browser:ProxyServer` трактовался как реальный прокси. Из-за этого `web_search` и `wikipedia_search` падали с `SocketException 11001` (хост `change_me_via_user_secrets:80` неизвестен). Исправлено: helper `IsRealProxyUrl()` в `Startup.cs` и `Program.cs` — placeholder / пустые / невалидные URL игнорируются, прокси не устанавливается.
- **KI-060**: user-сообщения рендерились как Markdown (Фаза 2.1.4). Если пользователь писал ` ``` ` или `**bold**` в обычном тексте, они интерпретировались как разметка → пустой code block с шапкой «без» в user-пузыре. Теперь user-сообщения — plain text (`renderUserContent`), Markdown применяется только к assistant.
- **KI-061**: кнопка «Отправить» перекрывала textarea при одном ряде текста. Фикс: `min-height` для textarea (стандарт Bootstrap `.form-control`), `align-items: stretch` в `.input-group`, `.btn { align-self: stretch; height: auto }`.
- **KI-062**: отсутствовал автофокус на поле ввода при создании/выборе чата. Фикс: `input.focus()` в `selectChat()` (покрывает оба сценария, т.к. `createChat` вызывает `selectChat`).
- **KI-061a**: box-shadow фокуса на textarea перекрывал кнопку «Отправить». Focus-ring перенесён на `.input-group:focus-within`; `textarea:focus` и `.btn:focus` → `box-shadow: none`.
- **KI-066**: кнопка Copy (📋) пропадала под ответом ассистента до F5. Причина: `renderMessageActions('')` в `appendAssistantBubble()` не создаёт кнопку при пустом тексте (правка 2.2.1a). Фикс: `finalizeAssistantBubble()` пересоздаёт `.chat-message-actions` с финальным текстом после Markdown-рендера.

### Security
- Н/Д

### Documented
- **KI-064**: `wikipedia_search` иногда падает с `SSL connection could not be established` (SocketException 10054) через корпоративный прокси — внешняя сетевая проблема, не баг приложения. LLM переключается на `web_search`. Планируется уменьшение таймаута + retry в v1.3.x.

---

## [1.2.0] — 2026-09-21

### Added
- **Docker**: `Dockerfile` (multi-stage, SDK 10.0 → ASP.NET Runtime 10.0), `docker-compose.yml` (prod + опциональный SQL Server), `docker-compose.override.yml` (dev), `.dockerignore`, `.env.example`. Опциональный Chromium через build-arg `INSTALL_BROWSER`.
- **CI/CD**: GitHub Actions — `ci.yml` (build + test + coverage artifacts), `docker-publish.yml` (образ в ghcr.io на main и теги v*), `dependabot.yml` (авто-обновления NuGet + Actions).
- **Health checks**: `/health/live` (liveness), `/health/ready` (БД + Workspace), `/health` (полный JSON-отчёт). Анонимные endpoints.
- **Rate limiting**: собственный `RateLimitingMiddleware` на `System.Threading.RateLimiting`. Политики: per-user (100/min), tools-execute (30/min), auth (5/min). JSON-ответ 429 с `retryAfterSeconds`. `/health/*` без лимита.
- **Prometheus метрики**: `/metrics` (анонимный). Стандартные `http_requests_received_total`, `http_request_duration_seconds`, `dotnet_collection_count_total`, `process_*`. Кастомные: `iichattools_tool_executions_total`, `iichattools_tool_execution_duration_seconds`, `iichattools_pending_approvals`, `iichattools_active_users`, `iichattools_audit_entries_total`, `iichattools_lmstudio_requests_total`, `iichattools_audit_cleanup_total`.
- **Audit retention**: `AuditRetentionService` — фоновый BackgroundService чистит `AuditLogs` (ExecuteDeleteAsync) и `logs/audit/*.jsonl` по retention policy. Настраивается через `Audit:CleanupIntervalHours`, `Audit:DatabaseRetentionDays`, `Audit:FileRetentionDays`.
- **MSSQL migrations**: `IIChatTools.Data/Migrations/SqlServer/20260921102238_InitialSqlServer` — версионирование схемы для SQL Server. `__EFMigrationsHistory` в БД.
- **Скрипты setup**: `scripts/setup/enable-online-restore.ps1` (создаёт `NuGet.Config.online`), `scripts/setup/fill-local-packages.ps1` (наполняет `LocalPackages`).
- **Config**: `NuGet.Config.example.online` — шаблон онлайн-конфига.

### Changed
- **Directory.Build.props**: версии Microsoft-пакетов синхронизированы с ref-pack SDK 10.0.401 → **10.0.12**.
- **`AppMetrics`** перенесён в `IIChatTools.Services/Metrics` — восстановлена слоистость (Data → Services → API).
- **`MetricsRefreshBackgroundService`** перенесён в `IIChatTools.API/BackgroundServices`.
- **`Startup.cs`**: `UseMiddleware<RateLimitingMiddleware>()` вместо `UseRateLimiter()` (см. KI-042).

### Fixed
- **KI-042**: `Microsoft.AspNetCore.RateLimiting` недоступен в SDK 10.0.401 — реализован собственный `RateLimitingMiddleware`.
- **KI-045**: HELP-описания метрик переведены на английский (устранены кракозябры в PowerShell с CP866).
- **Auth**: `[FromForm]` для `LoginAsync` и `RegisterAsync` — устранён HTTP 415 при работе с Razor-формой (побочный эффект `[ApiController]`).

### Documented
- **KI-043**: утечка памяти в `RateLimitingMiddleware` (лимитеры не очищаются при истечении окна) — запланировано на v1.2.x.
- **KI-044**: `iichattools_audit_entries_total` и `iichattools_lmstudio_requests_total` пока не инкрементируются — запланировано на v1.2.x.

---

## [1.1.1] — 2026-09-18

### Changed
- **KI-037**: удалён legacy-ключ `ConnectionStrings:DefaultConnection`. Единственный источник строки SqlServer — `Database:SqlServerConnectionString`. Fallback в `DbContextOptionsExtensions` заменён на явную ошибку.
- **KI-038**: `Workspace:RootPath` вынесен в User Secrets. `WorkspaceResolver` раскрывает env-переменные через `Environment.ExpandEnvironmentVariables`. Устранены мержи локальных путей.
- **KI-005** (API-изменение): `git_add` требует явного `all: true` для добавления всех изменений или непустой `files`. Устранён неявный `git add -A` при пустом `files`.

### Fixed
- **KI-001**: на `/test` при отклонении действия в поле «Результат» отображается причина отклонения. `requestApproval` возвращает `{ decision, reason }`; `ToolsController` при `Rejected` передаёт `data.rejectionReason`. Отмена диалога причины (`Cancel`) больше не вешает модалку.
- **KI-005**: `git_add` — исправлено формирование CLI: `--` добавляется один раз перед списком файлов (а не перед каждым).

### Security
- **KI-022**: обновлён `SQLitePCLRaw.bundle_e_sqlite3` до 2.1.13 (GHSA-2m69-gcr7-jv3q, High).
- **KI-036**: удалён хардкод прокси-credentials из `Program.cs` и скриптов. Значения — только через env/User Secrets.
- **KI-039**: устранена утечка прокси-credentials и email автора в git-истории. `git filter-repo --replace-text` + `--mailmap` + `--invert-paths`, force-push всех веток и тегов. GitHub Secret Scanning: «No secrets found».

### Removed
- Папка `UPDATES/` (устаревшие копии).
- Дубли `configs/NuGet1.Config`, `configs/NuGet111.Config`.
- Ad-hoc скрипты `scripts/setup/download-missing*.ps1`, `downloader*.ps1`.
- Локальные SQLite-БД `IIChatTools.API/Data/*.db` (не коммитились).

---

## [1.1.0] — 2026-09-18

### Added
- **Native LM Studio tool calling** — подтверждено end-to-end.
- **Subagent E2E loop** — многошаговые задачи с автоотладкой.
- **Offline-развёртывание** через `LocalPackages/` (без интернета).
- **`LmStudioTestController`** — endpoint для тестирования LM Studio.
- **`SubAgentController`** — API для суб-агентов.
- **`scripts/diagnostics/*.ps1`** — диагностические скрипты для tool calls.
- **`docs/KNOWN_ISSUES.md`**: KI-015 … KI-038 (24 новых записи).

### Changed
- **.NET Core 3.1 → .NET 10 LTS** (KI-015).
  - `TargetFramework`: `netcoreapp3.1` → `net10.0`, `LangVersion latest`.
  - EF Core 3.1.32 → **10.0.4**.
  - ASP.NET Core / Microsoft.Extensions → **10.0.4**.
  - IdentityModel 6.35.0 → **8.14.0**, HtmlAgilityPack 1.11.61 → **1.12.1**.
- **Версионирование**: `AppVersion.Current` читается из `AssemblyInformationalVersion` (источник — `<Version>` в `Directory.Build.props`).
- **Локализация**: ключ `WelcomeTitle` с плейсхолдером `{0}` вместо версии в ключе (KI-031a).
- **`global.json` и `NuGet.Config`** перенесены в корень репозитория (KI-016, KI-017).
- **`_Layout.cshtml`**: `@(AppVersion.Current)` вместо `@AppVersion.Current` (Razor-парсер путал с email).
- **`Localizer["..."]`** → **`Localizer["..."].Value`** во всех контроллерах (JSON-сериализация `LocalizedString`).
- **`README.md`** полностью переписан под .NET 10 LTS.

### Fixed
- **KI-018**: дублирование `PuppeteerSharpVersion` в `Directory.Build.props`.
- **KI-019**: избыточные ссылки на `Microsoft.Extensions.Logging.*` (NU1510).
- **KI-020**: `LocalPackages` в формате global-packages folder вместо source.
- **KI-021**: кодировка PowerShell искажала вывод `dotnet`.
- **KI-030**: двойной `©` в логе запуска.
- **KI-031**: версия `1.0.2` / `v1.0` в UI вместо `1.1.0`.
- **KI-033**: `favicon.ico` → 404.
- **KI-034**: ASP.NET Core developer certificate не доверен.
- **KI-035**: 4 интеграционных теста `ApprovalService` падали (сидирование пользователей в `TestDbContextFactory`).

### Removed
- Устаревший пакет `Microsoft.AspNetCore.Identity` 2.2.0.
- Избыточные `Microsoft.Extensions.Logging`, `.Console`, `.Debug` из API-проекта.
- Файл `IIChatTools.API/Resources/Как использовать в коде.txt`.

### Security
- Пароль прокси сменён (инцидент 2026-09-18, см. v1.1.1).

---

## [1.0.2] — 2026-09-16

### Added
- Гибридная БД: SqlServer / Sqlite / InMemory (выбор через `Database:Provider`).
- `CompositeAuditService` — аудит в БД + JSONL-файл (`logs/audit/`).
- `.gitattributes` для нормализации LF/CRLF.
- `Directory.Build.props` — централизованные версии пакетов.
- **ChatStream DTO (v1.3 Фаза 1.5)**: `ChatStreamRequest` и `ChatStreamEvent` (start/delta/done/error) — типы для SSE-стриминга ответов LLM.
- **ChatStreamService (v1.3 Фаза 1.5)**: `IChatStreamService` + `ChatStreamService` — координатор SSE-стриминга. Сохраняет user message → строит историю (JArray, OpenAI-формат) → вызывает `ILmStudioClient.ChatStreamAsync` → сохраняет assistant message с токенами → возвращает `IAsyncEnumerable<ChatStreamEvent>`.
- **ChatStreamController (v1.3 Фаза 1.5)**: `POST /api/chat/stream` — SSE-эндпоинт. Заголовки `text/event-stream`, `X-Accel-Buffering: no` (обход прокси/nginx), flush после каждого события. События: `start`, `delta`, `done`, `error`.
- **Unit-тесты ChatStreamService (v1.3 Фаза 1.5)**: 3 теста с `FakeLmStudioClient` — успешный стрим (start/delta/done + сохранение), отсутствие чата (error), пустое сообщение (error). Всего: **27/27**.

### Changed
- Git-инструменты: параметр `path` (каталог репо) + `filePath` (файл) — KI-004.
- GitHub-инструменты: параметр `path` для работы в подкаталогах — KI-006.
- Браузер: `--remote-allow-origins=*`, `--proxy-bypass-list=<-loopback>`, `--ignore-certificate-errors` (Chromium 111+).
- Реалистичный User-Agent для обхода headless-детекции (KI-003).

### Fixed
- **KI-002**: 407 Proxy Authentication Required для веб-инструментов.
- **KI-003**: 403 Forbidden от Wikipedia API.
- **KI-004**: git-инструменты не работали в подкаталогах.
- **KI-006**: gh-инструменты не работали в подкаталогах.
- **KI-050**: rate limiting на HTML-эндпоинтах (`/auth/login`, `/auth/register`) — теперь возвращает **303 redirect** с query `error=ratelimit&retryAfter=N` вместо сырого JSON. `Login.cshtml` / `Register.cshtml` показывают красный banner «Слишком много попыток входа. Попробуйте снова через N секунд». Для API-клиентов (`Accept: application/json`) сохранён прежний 429 JSON.
- **`AuthController`**: удалён атрибут `[ApiController]` — это UI-контроллер (возвращает Razor-Views), и автоматическая модель-валидация возвращала `ProblemDetails` 400 вместо формы с ошибками.

### Removed
- Бинарные артефакты из истории (`bin/`, `obj/`, `LocalPackages/`, `logs/`, `Workspace/`, `*.db`).
  - `git filter-repo --invert-paths` + `git gc --prune=now --aggressive`.
  - Размер `.git`: **154 МБ → 1.51 МБ** (KI-010).
- Gitlink `Workspace/users/1/git-test/` (KI-011).
- Множественные `obj/` из индекса (KI-012).

---

## [1.0.1] — 2026-09-14

### Changed
- Реорганизация структуры репозитория: `docs/`, `scripts/`, `configs/`, `assets/images/` (KI-013).
- Перемещение временных файлов отладки в `docs/development/archive/` (KI-014).

### Fixed
- Мелкие правки после v1.0.

---

## [1.0.0] — 2026-09-13

### Added
- Первый публичный релиз.
- **40 инструментов**: файловая система (13), выполнение кода (3), веб (3), Git (7), GitHub (7), браузер (4), суб-агенты (1), утилиты (2).
- **Мультипользовательность**: роли Admin/User, изоляция workspace.
- **Аутентификация**: cookie (Razor) + JWT (API).
- **Система подтверждений**: `PendingAction` + polling `/api/approvals/pending`.
- **Локализация RU/EN**: `SharedResources.resx` + `IStringLocalizer<SharedResources>`.
- **Суб-агенты**: `ISubAgentService` + `Func<ISubAgentService>` (разрыв DI-цикла).
- **Браузерная автоматизация**: PuppeteerSharp 7.1.0 + Edge/Chrome через прокси.
- **Аудит**: `AuditLog` в БД.
- **Razor UI**: главная, статус, админка, тест, логин/регистрация.
- **ES-модули**: `api`, `ui`, `status`, `approvals`, `admin`, `test`.

---

## Ссылки

- [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/)
- [Semantic Versioning](https://semver.org/lang/ru/)
- [GitHub Releases](https://github.com/iilmchat/IIChatTools/releases)
- [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) — реестр проблем

---

## Как обновлять

1. **В процессе работы** — добавляйте записи в секцию `[Unreleased]`.
2. **При релизе**:
   - Замените `[Unreleased]` на `[X.Y.Z] — YYYY-MM-DD`.
   - Создайте пустую `[Unreleased]` сверху.
   - Обновите `AppVersion.Current` и `<Version>` в `Directory.Build.props`.
   - Создайте git-тег: `git tag -a vX.Y.Z -m "..."`.
3. **Типы записей** — только из списка: Added / Changed / Deprecated / Removed / Fixed / Security.
4. **Ссылки на KI** — обязательно, если изменение связано с реестром проблем.