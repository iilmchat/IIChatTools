# DESIGN v1.5.0 — RAG / Knowledge Base (Retrieval-Augmented Generation)

**Версия:** 1.5.0
**Дата:** 2026-09-25
**Автор:** IIChatTools Team
**Статус:** **Draft** (согласование § 1-3)
**Связанные KI:** KI-083 (RAG / embeddings), KI-086 (sources / citations — v1.6.0)
**Не входит:** KI-082 (модалка-редактор длинных сообщений), KI-084 (расширенная статистика — сделано)

---

## § 1. Контекст

### § 1.1. Текущее состояние (v1.4.1)

Chat UI работает, но **LLM отвечает из своих знаний**. Это создаёт 3 проблемы:

1. **Галлюцинации про внутренние документы.** Спросите «Что у нас в RULES про yield return?» — LLM не знает содержимое `RULES.md`, отвечает общими словами или выдумывает.
2. **Нет цитирования.** Даже если LLM вызывала `web_search` — источник «закопан» в JSON `tool_result` и не виден пользователю.
3. **Нет работы с локальными файлами.** Пользователь не может приложить PDF/TXT к сообщению и спросить по нему.

### § 1.2. Что решает RAG

**RAG (Retrieval-Augmented Generation)** — архитектурный паттерн:
1. Документы → **чанки** (~500 токенов) → **embeddings** (векторы).
2. Запрос пользователя → embedding → **поиск** top-K похожих чанков.
3. Чанки → **вставка в промпт** → LLM отвечает **с опорой на них** и цитирует источники.

### § 1.3. Цели v1.5.0

| # | Цель | Метрика |
|---|------|---------|
| 1 | LLM может искать по **глобальным docs проекта** (README, RULES, KNOWN_ISSUES) | Tool `search_knowledge_base` возвращает релевантные чанки |
| 2 | LLM может искать по **истории чатов** пользователя | Tool `search_chat_history` |
| 3 | LLM может искать по **workspace** (файлы пользователя) | Tool `search_workspace` |
| 4 | Пользователь может **приложить файл** к сообщению (PDF/TXT/MD/CSV/code) | Файл → индексируется per-chat → доступен для поиска |
| 5 | **My RAG docs** — per-chat база документов, очищаемая | Кнопка «Очистить RAG» в UI |
| 6 | **Sources / citations** под ответом ассистента | Блок «Источники: [1] [2]» (KI-086) |

### § 1.4. Что НЕ входит в v1.5.0

- Qdrant / внешний vector store (только InMemory → Qdrant в v1.5.x).
- OCR сканов PDF (Tesseract).
- Fine-tuning / re-ranking (cross-encoder).
- Multi-user KB (шаринг между пользователями).

---

## § 2. Проблема

### § 2.1. LLM не знает про внутренние документы

**Пример:** пользователь спрашивает «Какие правила в RULES § 4.26?»

**Сейчас:** LLM отвечает из своих знаний (галлюцинации) или `web_search` (не найдёт внутренний файл).

**Хочется:** LLM вызывает `search_knowledge_base("RULES § 4.26")` → получает чанк из `docs/development/RULES.md` → отвечает точно + ссылается на источник.

### § 2.2. Нет работы с локальными файлами

**Пример:** у пользователя есть PDF с контрактом. Спрашивает «Какие условия оплаты?»

**Сейчас:** нужно вручную читать PDF, копировать текст в чат.

**Хочется:** приложил PDF → задал вопрос → LLM ищет по чанкам PDF → отвечает.

### § 2.3. Нет цитирования

**Пример:** LLM ответила на вопрос по Wikipedia. Пользователь хочет **проверить источник**.

**Сейчас:** URL «закопан» в `tool_result` (JSON), не виден.

**Хочется:** блок «Источники» под ответом — кликабельные ссылки.

### § 2.4. Ограничения существующих инструментов

| Инструмент | Что делает | Чего не хватает |
|---|---|---|
| `web_search` | Ищет в интернете | Нет локальных документов |
| `fetch_web_content` | Загружает страницу | Не индексирует, не кэширует |
| `read_file` | Читает файл | Нужно **знать путь**, нет семантического поиска |
| `list_directory` | Список файлов | Нет поиска по содержимому |
| `find_files` | Поиск по имени | Нет поиска по содержимому |

**RAG заполняет пробел:** «найти релевантное **по смыслу** в большом корпусе».

---

## § 3. Архитектура

### § 3.1. Общая диаграмма

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Пользователь (Chat UI)                            │
│  /chat                                                              │
│   ┌─────────────────────────────────────────────────────────┐      │
│   │ [+ Прикрепить файл]  RAG: myDocs (3 файла)  [Очистить]  │      │
│   └─────────────────────────────────────────────────────────┘      │
└──────────────────────────┬──────────────────────────────────────────┘
                           │ SSE
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│              ChatStreamService (оркестратор)                        │
│  - видит 7 агентов + 3 RAG-tool:                                    │
│    search_knowledge_base, search_chat_history, search_workspace     │
│  - multi-turn loop (до 5 итераций)                                  │
└──────────────────────────┬──────────────────────────────────────────┘
                           │ tool_call(name="search_knowledge_base")
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│       SearchKnowledgeBaseTool : ITool                               │
│  - параметры: { query, topK?, index? }                              │
│  - внутри: IRetrievalService.SearchAsync(query, index)              │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│       IRetrievalService (Singleton)                                 │
│  1. Embed query → IEmbeddingService.GetEmbeddingAsync(query)        │
│  2. Поиск top-K → IVectorStore.SearchAsync(embedding, topK, index)  │
│  3. Возвращает List<RetrievedChunk> с метаданными                   │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
       ┌───────────┐ ┌──────────┐ ┌──────────┐
       │ Embedding │ │  Vector  │ │ Ingestion│
       │  Service  │ │  Store   │ │ Service  │
       └─────┬─────┘ └────┬─────┘ └────┬─────┘
             │            │            │
             ▼            ▼            ▼
       ┌──────────────────────────────────────────┐
       │      ILmStudioClient                     │
       │  POST /v1/embeddings                     │
       │  model=text-embedding-nomic-...          │
       └──────────────────────────────────────────┘
```

### § 3.2. Слоистость (IIChatTools.Services)

```
IIChatTools.Services/
  ├── DTO/Rag/
  │   ├── ChunkDto.cs                   — { id, text, tokens, metadata }
  │   ├── RetrievedChunkDto.cs          — { chunk, score, source }
  │   ├── RagIndexDto.cs                — { name, description, count }
  │   └── IngestionResultDto.cs         — { chunks, tokens, durationMs }
  │
  ├── Interfaces/
  │   ├── IEmbeddingService.cs          — GetEmbeddingAsync / GetEmbeddingsAsync
  │   ├── IVectorStore.cs               — Add / Search / Delete / Clear / Count
  │   ├── IDocumentIngestionService.cs  — IngestAsync / DeleteAsync
  │   ├── IRetrievalService.cs          — SearchAsync
  │   └── IRagDocumentParser.cs         — CanParse / ParseAsync (TXT/PDF/DOCX/...)
  │
  ├── Implementation/Rag/
  │   ├── EmbeddingService.cs           — обёртка над ILmStudioClient
  │   ├── InMemoryVectorStore.cs        — Singleton, Dictionary + cosine
  │   ├── DocumentIngestionService.cs   — chunking + embed + store
  │   ├── ChunkingStrategy.cs           — Recursive / Sentence / Fixed
  │   ├── RetrievalService.cs           — embed query + top-K
  │   └── Parsers/
  │       ├── PlainTextParser.cs        — .txt, .md, .csv, code
  │       └── (v1.5.x) PdfParser.cs     — PdfPig
  │
  └── Implementation/Tools/Rag/
      ├── SearchKnowledgeBaseTool.cs    — глобальные docs проекта
      ├── SearchChatHistoryTool.cs      — история чатов пользователя
      └── SearchWorkspaceTool.cs        — файлы workspace
```

### § 3.3. Поток данных

**A. Индексация (ingestion):**

```
Document (file / URL / chat history)
    ↓ IRagDocumentParser.ParseAsync → text
    ↓ IChunkingStrategy.Chunk → List<Chunk> (~500 токенов)
    ↓ IEmbeddingService.GetEmbeddingsAsync(chunks) → List<float[]>
    ↓ IVectorStore.AddAsync(indexName, chunks, embeddings)
    ↓ (v1.5.x) Persist в БД / Qdrant
```

**B. Поиск (retrieval):**

```
Query (от LLM через tool)
    ↓ IEmbeddingService.GetEmbeddingAsync(query) → float[]
    ↓ IVectorStore.SearchAsync(indexName, embedding, topK) → List<(chunk, score)>
    ↓ (v1.5.x) Re-rank
    ↓ Возврат top-K чанков + метаданные (source, docId, offset)
```

**C. Chat flow (RAG-приложен к чату):**

```
1. Пользователь приложил файлы → ChatAttachment[] → IngestionAsync(chatId)
2. Отправляет сообщение
3. ChatStreamService:
   a. Если chatId имеет attached chunks → автоматически добавить top-K в system prompt
   b. Иначе — LLM сама решает вызвать search_* tool
4. LLM отвечает + Sources (из metadata чанков) → SSE done
```

### § 3.4. Индексы (namespace-ы) — 4 шт

| Index name | Per | Описание | Управление |
|---|---|---|---|
| `project_docs` | Global | README, RULES, KNOWN_ISSUES, CHANGELOG | `/admin → Knowledge Base` |
| `my_rag_docs` | Chat | Документы, приложенные к конкретному чату | `/chat` — «Очистить RAG» |
| `chat_history` | User | Все сообщения пользователя (user + assistant) | Авто при retention / ручное |
| `workspace` | User | Файлы workspace (opt-in) | `/profile → Workspace index` |

**Приоритет поиска (если LLM вызывает tool без указания index):**
1. `my_rag_docs` (если приложен к чату) — highest priority.
2. `project_docs` — global.
3. `chat_history` / `workspace` — по запросу.

### § 3.5. Хранение (MVP → v1.5.x)

| Компонент | MVP (v1.5.0) | v1.5.x (Qdrant) |
|---|---|---|
| Vector store | InMemory: `ConcurrentDictionary<string, List<(int chunkId, float[] vector)>>` | Qdrant collection |
| Chunk metadata | БД (`DocumentChunk` entity) | Qdrant payload |
| Embedding размер | 768 (nomic-embed-text-v1.5) | То же |
| Persistence | **Нет** (теряется при рестарте) | Да |
| Cosine similarity | In-memory LINQ (`VectorMath.Cosine`) | Qdrant native |

**Компромисс MVP:** embeddings теряются при рестарте. При старте — либо переиндексация (для `project_docs`), либо пустой индекс (для `my_rag_docs` — предупреждение пользователю).

---

## § 4. Компоненты

### § 4.1. Embedding Service

#### § 4.1.1. Контракт

```csharp
namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис генерации эмбеддингов для RAG (v1.5.0, KI-083).
    /// Обёртка над LM Studio: POST /v1/embeddings.
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Размерность вектора текущей модели (768 для nomic-embed-text-v1.5).
        /// </summary>
        int Dimensions { get; }

        /// <summary>
        /// Возвращает эмбеддинг для одного текста.
        /// </summary>
        /// <param name="text">Текст (не пустой)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Вектор float[N] (N = Dimensions)</returns>
        Task<float[]> GetEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает эмбеддинги для набора текстов (batch, до <c>BatchSize</c> за раз).
        /// Внутри разбивает на чанки по лимиту LM Studio.
        /// </summary>
        /// <param name="texts">Список текстов (не пустой)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список векторов в порядке исходных текстов</returns>
        Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default);
    }
}
```

#### § 4.1.2. Реализация

`EmbeddingService : IEmbeddingService` — **Singleton** (не держит состояния, но кэш потокобезопасен).

**Алгоритм:**
1. Валидация: пустой текст → `ArgumentException`.
2. Проверка кэша (опционально, `ConcurrentDictionary<string, float[]>` по SHA256 текста).
3. Batch-разбивка: если `texts.Count > BatchSize (64)` — цикл по батчам.
4. `ILmStudioClient.GetEmbeddingsAsync(batch, model, ct)`.
5. Сборка результатов в порядке `index` из response.
6. Кэширование.

#### § 4.1.3. Расширение `ILmStudioClient`

Новый метод (дополняет существующий `ChatStreamAsync`):

```csharp
/// <summary>
/// Возвращает эмбеддинги для списка входов (POST /v1/embeddings).
/// </summary>
/// <param name="inputs">Список текстов (до 64 за раз — лимит LM Studio)</param>
/// <param name="model">Модель (null → LmStudio:EmbeddingModel из конфига)</param>
/// <param name="cancellationToken">Токен отмены</param>
/// <returns>DTO с векторами + usage</returns>
Task<EmbeddingResponse> GetEmbeddingsAsync(
    IReadOnlyList<string> inputs,
    string model = null,
    CancellationToken cancellationToken = default);
```

**DTO** (`IIChatTools.Services/DTO/LmStudio/EmbeddingResponse.cs`):

```csharp
public class EmbeddingResponse
{
    public List<EmbeddingData> Data { get; set; } = new();
    public EmbeddingUsage Usage { get; set; }
}

public class EmbeddingData
{
    public int Index { get; set; }
    public float[] Embedding { get; set; }
}

public class EmbeddingUsage
{
    public int PromptTokens { get; set; }
    public int TotalTokens { get; set; }
}
```

**HTTP-контракт LM Studio:**

```
POST /v1/embeddings
{
  "model": "text-embedding-nomic-embed-text-v1.5",
  "input": ["текст 1", "текст 2"]
}

200 OK
{
  "data": [
    { "index": 0, "embedding": [0.123, -0.456, ...] },
    { "index": 1, "embedding": [...] }
  ],
  "usage": { "prompt_tokens": 42, "total_tokens": 42 }
}
```

#### § 4.1.4. Конфигурация

```jsonc
"LmStudio": {
  "BaseUrl": "http://localhost:8034",
  "Model": "qwen/qwen3-4b-2507",
  "EmbeddingModel": "text-embedding-nomic-embed-text-v1.5",
  "EmbeddingDimensions": 768,
  "EmbeddingBatchSize": 64,
  "EmbeddingTimeoutSeconds": 60
}
```

#### § 4.1.5. Тесты

- `EmbeddingServiceTests`:
  - `GetEmbeddingAsync_EmptyText_Throws`
  - `GetEmbeddingAsync_ReturnsVector_WithDimensions`
  - `GetEmbeddingsAsync_Batch_ReturnsInOrder`
  - `GetEmbeddingsAsync_LargeBatch_SplitsIntoChunks`
  - `GetEmbeddingsAsync_Caches_DuplicateText`
  - `GetEmbeddingsAsync_LmStudioError_Throws`

---

### § 4.2. Vector Store

#### § 4.2.1. Контракт

```csharp
namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Векторное хранилище для RAG (v1.5.0, KI-083).
    /// MVP: InMemory. v1.5.x: Qdrant.
    /// </summary>
    public interface IVectorStore
    {
        /// <summary>
        /// Добавляет вектор в индекс.
        /// </summary>
        /// <param name="indexName">Имя индекса (project_docs, my_rag_docs, ...)</param>
        /// <param name="chunkId">ID чанка в БД (DocumentChunk.Id)</param>
        /// <param name="vector">Вектор (нормализуется внутри)</param>
        /// <param name="metadata">Метаданные (источник, offset, docId)</param>
        void Add(string indexName, int chunkId, float[] vector, ChunkMetadata metadata);

        /// <summary>
        /// Ищет top-K ближайших чанков по cosine similarity.
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <param name="queryVector">Вектор запроса (нормализуется внутри)</param>
        /// <param name="topK">Количество результатов (по умолчанию 5)</param>
        /// <returns>Отсортированный по score список (убывание)</returns>
        IReadOnlyList<VectorSearchResult> Search(string indexName, float[] queryVector, int topK = 5);

        /// <summary>
        /// Удаляет вектор по chunkId.
        /// </summary>
        void Remove(string indexName, int chunkId);

        /// <summary>
        /// Полностью очищает индекс.
        /// </summary>
        void Clear(string indexName);

        /// <summary>
        /// Количество векторов в индексе.
        /// </summary>
        int Count(string indexName);

        /// <summary>
        /// Список активных индексов.
        /// </summary>
        IReadOnlyList<string> GetIndexNames();
    }
}
```

**Вспомогательные DTO:**

```csharp
public class ChunkMetadata
{
    public int DocumentChunkId { get; set; }
    public string IndexName { get; set; }
    public int? ChatId { get; set; }
    public int UserId { get; set; }
    public string DocumentPath { get; set; }    // относительный путь или URL
    public int ChunkIndex { get; set; }         // позиция в документе
    public string Source { get; set; }          // для citations (KI-086)
}

public class VectorSearchResult
{
    public int ChunkId { get; set; }
    public float Score { get; set; }            // cosine similarity (0..1)
    public ChunkMetadata Metadata { get; set; }
}
```

#### § 4.2.2. Реализация (InMemory)

`InMemoryVectorStore : IVectorStore` — **Singleton**.

**Внутренняя структура:**

```csharp
private readonly ConcurrentDictionary<string, IndexData> _indexes
    = new(StringComparer.Ordinal);

private sealed class IndexData
{
    public List<VectorEntry> Entries { get; } = new();
    public ReaderWriterLockSlim Lock { get; } = new();
}

private sealed class VectorEntry
{
    public int ChunkId { get; init; }
    public float[] NormalizedVector { get; init; }
    public ChunkMetadata Metadata { get; init; }
}
```

**Алгоритм Add:**
1. Получить/создать `IndexData` для `indexName`.
2. Взять write-lock.
3. Нормализовать вектор (L2): `v / ||v||`.
4. Добавить `VectorEntry` в `Entries` (под write-lock).

**Алгоритм Search:**
1. Взять read-lock.
2. Нормализовать query-вектор (L2).
3. Для каждой `Entry`: `score = DotProduct(query, entry.NormalizedVector)`.
   (после L2-нормализации cosine similarity = dot product — экономия на делении в цикле)
4. Отсортировать по `score` (убывание).
5. Взять `topK`.
6. Отпустить read-lock.
7. Вернуть `List<VectorSearchResult>`.

**Cosine similarity:**
```
cosine(a, b) = dot(a, b) / (||a|| · ||b||)

Если a и b нормализованы (||a|| = ||b|| = 1), то:
cosine(a, b) = dot(a, b) = Σ(a[i] · b[i])
```

**L2-нормализация:**
```
||v|| = sqrt(Σ v[i]²)
v_normalized[i] = v[i] / ||v||
```

**Thread-safety:** `ReaderWriterLockSlim` на `IndexData.Lock` — параллельные чтения (Search), эксклюзивная запись (Add/Remove/Clear).

**Оценка производительности:**
- 10 000 чанков × 768 dim × 4 байт = ~30 MB RAM.
- Cosine для 10k векторов: ~10M multiply-add = **< 50 ms** на CPU. Приемлемо.
- Для 100k+ — Qdrant (v1.5.x).

#### § 4.2.3. Конфигурация

```jsonc
"Rag": {
  "VectorStore": {
    "Provider": "InMemory",          // InMemory | Qdrant (v1.5.x)
    "DefaultTopK": 5,
    "MinScore": 0.3                  // порог — чанки ниже не возвращаются
  }
}
```

#### § 4.2.4. Тесты

- `InMemoryVectorStoreTests`:
  - `Add_Then_Search_ReturnsSameVector`
  - `Add_SameChunkId_Twice_OverwritesOrIgnores` (зависит от семантики)
  - `Search_ReturnsTopK_SortedByScore`
  - `Search_RespectsMinScore` (если чанк < порога — не возвращается)
  - `Search_EmptyIndex_ReturnsEmpty`
  - `Add_DifferentIndexes_IsolatedFromEachOther`
  - `Remove_RemovesFromIndex`
  - `Clear_RemovesAllFromIndex`
  - `Count_ReflectsAddsAndRemoves`
  - `ConcurrentAdds_ThreadSafe` (Parallel.For × 1000)

---

### § 4.3. Document Ingestion Service

#### § 4.3.1. Контракт

```csharp
namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис индексации документов: parse → chunk → embed → store (v1.5.0, KI-083).
    /// </summary>
    public interface IDocumentIngestionService
    {
        /// <summary>
        /// Индексирует документ (файл / текст / URL).
        /// </summary>
        /// <param name="request">Запрос индексации</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Результат: количество чанков, токенов, длительность</returns>
        Task<IngestionResultDto> IngestAsync(
            IngestionRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет все чанки документа из индекса и БД.
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <param name="documentPath">Путь документа (для идентификации)</param>
        /// <param name="chatId">ID чата (для my_rag_docs), null для global</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Количество удалённых чанков</returns>
        Task<int> DeleteDocumentAsync(
            string indexName,
            string documentPath,
            int? chatId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Полностью очищает индекс (например, «Очистить RAG» в UI).
        /// </summary>
        Task<int> ClearIndexAsync(
            string indexName,
            int? chatId = null,
            CancellationToken cancellationToken = default);
    }
}
```

**DTO:**

```csharp
public class IngestionRequest
{
    /// <summary>Имя индекса (project_docs, my_rag_docs, ...)</summary>
    public string IndexName { get; set; }

    /// <summary>Путь к файлу (если source = File)</summary>
    public string FilePath { get; set; }

    /// <summary>Готовый текст (если source = Text)</summary>
    public string Text { get; set; }

    /// <summary>URL (если source = Url)</summary>
    public string Url { get; set; }

    /// <summary>ID чата (для my_rag_docs)</summary>
    public int? ChatId { get; set; }

    /// <summary>ID пользователя-владельца</summary>
    public int UserId { get; set; }

    /// <summary>Тип источника</summary>
    public IngestionSourceType SourceType { get; set; }

    /// <summary>Перезаписать, если уже индексировался (по hash)</summary>
    public bool ForceReindex { get; set; }
}

public enum IngestionSourceType { File, Text, Url }

public class IngestionResultDto
{
    public int DocumentChunksCreated { get; set; }
    public int TokensTotal { get; set; }
    public long DurationMs { get; set; }
    public string DocumentHash { get; set; }
    public bool Skipped { get; set; }               // если hash совпал и ForceReindex = false
    public string SkipReason { get; set; }
}
```

#### § 4.3.2. Схема БД — `DocumentChunk`

```csharp
namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Чанк документа для RAG (v1.5.0, KI-083).
    /// Embedding хранится НЕ здесь, а в IVectorStore (InMemory MVP).
    /// </summary>
    public class DocumentChunk : BaseEntity
    {
        /// <summary>Имя индекса (project_docs, my_rag_docs, chat_history, workspace).</summary>
        public string IndexName { get; set; }

        /// <summary>ID чата (для my_rag_docs / chat_history), null для global.</summary>
        public int? ChatId { get; set; }

        /// <summary>Владелец (для workspace / chat_history).</summary>
        public int UserId { get; set; }

        /// <summary>Относительный путь документа или URL.</summary>
        public string DocumentPath { get; set; }

        /// <summary>SHA256 содержимого — для re-indexing (skip если не изменился).</summary>
        public string DocumentHash { get; set; }

        /// <summary>Порядковый номер чанка в документе (0-based).</summary>
        public int ChunkIndex { get; set; }

        /// <summary>Текст чанка.</summary>
        public string Text { get; set; }

        /// <summary>Количество токенов (tiktoken).</summary>
        public int Tokens { get; set; }

        /// <summary>Метаданные (JSON): { page, section, offset }.</summary>
        public string MetadataJson { get; set; }
    }
}
```

**Индексы:**
- `(IndexName, DocumentHash)` — для проверки «уже индексировался».
- `(IndexName, ChatId, UserId)` — для фильтрации при поиске.
- `(DocumentPath)` — для удаления.

**Миграция:** `AddDocumentChunks`.

#### § 4.3.3. Алгоритм Ingest

```
1. Валидация:
   - FilePath / Text / Url — в зависимости от SourceType.
   - File существует, размер ≤ MaxFileSizeBytes.
   - Формат поддерживается (IParser.CanParse).

2. Чтение + parse:
   - File → StreamReader / PdfPig / OpenXml.
   - Text → напрямую.
   - Url → fetch (переиспользуем FetchWebContentTool).
   - → string rawText.

3. Hash: SHA256(rawText).

4. Проверка существования:
   - Query DocumentChunk WHERE (IndexName, DocumentPath, ChatId).
   - Если hash совпадает и !ForceReindex → Skip.

5. Удаление старых чанков (если есть):
   - DB: DELETE FROM DocumentChunks WHERE ... 
   - VectorStore: Remove для каждого chunkId.

6. Chunking:
   - IChunkingStrategy.Chunk(rawText, options) → List<string>.

7. Embeddings:
   - IEmbeddingService.GetEmbeddingsAsync(chunks) → List<float[]>.

8. Транзакция (БД):
   - INSERT DocumentChunk[] в БД.
   - SaveChanges.
   - Получить IDs.

9. VectorStore:
   - Для каждого chunk → Add(indexName, id, vector, metadata).

10. Возврат IngestionResultDto.
```

**Транзакционность:**
- Если parse упал → ничего не меняется.
- Если embedding упал → ничего не сохранено.
- Если БД INSERT упал → ничего.
- Если VectorStore.Add упал → чанки в БД есть, но в индексе нет. Нужна **переиндексация** (idempotent) или компенсация (rollback БД вручную).
  - **Решение MVP:** при следующем `ForceReindex=true` → удалить старые, вставить заново. Не транзакционно, но практично.

#### § 4.3.4. Конфигурация

```jsonc
"Rag": {
  "Ingestion": {
    "MaxFileSizeBytes": 33554432,       // 32 MB
    "MaxFilesPerChat": 5,
    "MaxTotalSizePerChat": 31457280,    // 30 MB (LM Studio style)
    "AllowedExtensions": [
      ".txt", ".md", ".csv", ".json", ".xml", ".yaml", ".yml", ".html",
      ".cs", ".py", ".js", ".ts", ".java", ".go", ".rs", ".sql",
      ".sh", ".ps1", ".dockerfile", ".razor", ".cshtml", ".css"
    ],
    "ProjectDocsPaths": [
      "README.md",
      "CHANGELOG.md",
      "docs/development/RULES.md",
      "docs/KNOWN_ISSUES.md",
      "docs/development/RELEASES.md"
    ]
  }
}
```

#### § 4.3.5. Тесты

- `DocumentIngestionServiceTests`:
  - `IngestAsync_Text_CreatesChunks`
  - `IngestAsync_File_CreatesChunks`
  - `IngestAsync_UnsupportedFormat_Throws`
  - `IngestAsync_FileTooLarge_Throws`
  - `IngestAsync_SameHash_SkipsWhenNotForced`
  - `IngestAsync_SameHash_ReindexesWhenForced`
  - `IngestAsync_UpdatesVectorStore`
  - `DeleteDocumentAsync_RemovesChunksFromDb`
  - `DeleteDocumentAsync_RemovesVectorsFromStore`
  - `ClearIndexAsync_RemovesAllChunks`
  - `IngestAsync_ChatIdScoped_IsolatesPerChat`

---

### § 4.4. Chunking

#### § 4.4.1. Контракт

```csharp
namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Стратегия разбиения текста на чанки (v1.5.0, KI-083).
    /// </summary>
    public interface IChunkingStrategy
    {
        /// <summary>
        /// Имя стратегии (recursive, sentence, fixed).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Разбивает текст на чанки.
        /// </summary>
        /// <param name="text">Исходный текст</param>
        /// <param name="options">Параметры чанкинга (size, overlap, ...)</param>
        /// <param name="tokenCounter">Счётчик токенов (tiktoken)</param>
        /// <returns>Список чанков (порядок сохранён)</returns>
        IReadOnlyList<string> Chunk(
            string text,
            ChunkingOptions options,
            ITokenCounter tokenCounter);
    }
}
```

**Параметры:**

```csharp
public class ChunkingOptions
{
    /// <summary>Целевой размер чанка в токенах (по умолчанию 500).</summary>
    public int ChunkSize { get; set; } = 500;

    /// <summary>Перекрытие между чанками (по умолчанию 64).</summary>
    public int ChunkOverlap { get; set; } = 64;

    /// <summary>Минимальный размер чанка (мельче — отбрасывается, 100).</summary>
    public int MinChunkSize { get; set; } = 100;

    /// <summary>Разделители в порядке приоритета (recursive strategy).</summary>
    public string[] Separators { get; set; } =
        new[] { "\n\n", "\n", ". ", "! ", "? ", "; ", ", ", " " };

    /// <summary>Стратегия (recursive | sentence | fixed).</summary>
    public string Strategy { get; set; } = "recursive";
}
```

#### § 4.4.2. Реализации

**A. `RecursiveChunkingStrategy` (по умолчанию)**

Алгоритм (по мотивам LangChain):
1. Взять первый разделитель из `Separators` (`\n\n`).
2. Разбить текст по нему.
3. Для каждого фрагмента:
   - Если токенов ≤ `ChunkSize` → объединять с соседями до лимита.
   - Иначе — рекурсивно разбить следующим разделителем.
4. При склейке чанков — добавлять `ChunkOverlap` токенов из предыдущего.
5. Финальная проверка: чанки < `MinChunkSize` — присоединять к предыдущему (не отбрасывать; отбрасывать только пустые).

**Псевдокод:**

```
function ChunkRecursive(text, separators, maxTokens, overlap):
    if tokenCount(text) <= maxTokens:
        return [text]

    if separators empty:
        // fallback: жёстко по токенам
        return SplitByTokens(text, maxTokens, overlap)

    sep = separators[0]
    parts = text.Split(sep)

    chunks = []
    current = ""

    for part in parts:
        candidate = current.IsEmpty ? part : current + sep + part
        if tokenCount(candidate) <= maxTokens:
            current = candidate
        else:
            if not current.IsEmpty:
                chunks.Add(current)
                current = TakeLastTokens(current, overlap) + sep + part
                if tokenCount(current) > maxTokens:
                    chunks.AddRange(ChunkRecursive(current, separators[1:], maxTokens, overlap))
                    current = ""
            else:
                chunks.AddRange(ChunkRecursive(part, separators[1:], maxTokens, overlap))

    if not current.IsEmpty:
        chunks.Add(current)

    return MergeSmallChunks(chunks, minSize=100)
```

**B. `SentenceChunkingStrategy`**

- Split по `. ! ? \n`.
- Группировать предложения до `ChunkSize` токенов.
- Overlap — последнее предложение предыдущего чанка.

**C. `FixedChunkingStrategy`**

- Просто по `ChunkSize` токенов (жёстко, через tiktoken `Encode` → декодирование среза).
- Быстро, но режет посередине предложения.

**D. Регистрация стратегий**

```csharp
// Startup.cs
services.AddSingleton<IChunkingStrategy, RecursiveChunkingStrategy>(); // default
services.AddSingleton<SentenceChunkingStrategy>();
services.AddSingleton<FixedChunkingStrategy>();

// + resolver
services.AddSingleton<IChunkingStrategyResolver, ChunkingStrategyResolver>();
```

`IChunkingStrategyResolver.Resolve(name)` — возвращает нужную стратегию по имени из конфига.

#### § 4.4.3. Расширение `ITokenCounter`

Нужен метод для «жёсткого разреза по токенам» (FixedChunkingStrategy):

```csharp
/// <summary>
/// Декодирует токены обратно в текст (для FixedChunkingStrategy).
/// </summary>
string Decode(IReadOnlyList<int> tokens);

/// <summary>
/// Кодирует текст в токены.
/// </summary>
IReadOnlyList<int> Encode(string text);
```

В `TokenCounter` — прокидка к `TiktokenTokenizer.EncodeToIds` / `.Decode`.

#### § 4.4.4. Конфигурация

```jsonc
"Rag": {
  "Chunking": {
    "Strategy": "recursive",
    "ChunkSize": 500,
    "ChunkOverlap": 64,
    "MinChunkSize": 100,
    "Separators": ["\n\n", "\n", ". ", "! ", "? ", "; ", ", ", " "]
  }
}
```

#### § 4.4.5. Тесты

- `RecursiveChunkingStrategyTests`:
  - `Chunk_SmallText_ReturnsOneChunk`
  - `Chunk_EmptyText_ReturnsEmpty`
  - `Chunk_LongText_RespectsChunkSize`
  - `Chunk_OverlapIsApplied`
  - `Chunk_ParagraphsSplitPreferred`
  - `Chunk_MinChunkSize_MergesSmall`
- `SentenceChunkingStrategyTests`:
  - `Chunk_SplitsBySentences`
  - `Chunk_GroupsUntilLimit`
- `FixedChunkingStrategyTests`:
  - `Chunk_ExactTokenCount`
  - `Chunk_OverlapCorrect`

---

### § 4.5. Document Parser

#### § 4.5.1. Контракт

```csharp
namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Парсер одного формата документа (v1.5.0, KI-083).
    /// Реализации: PlainTextParser (MVP), PdfParser / DocxParser (v1.5.x).
    /// </summary>
    public interface IRagDocumentParser
    {
        /// <summary>
        /// Имя парсера (для логов).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Поддерживаемые расширения (с точкой, lowercase).
        /// </summary>
        IReadOnlyList<string> SupportedExtensions { get; }

        /// <summary>
        /// Может ли парсер обработать файл.
        /// </summary>
        bool CanParse(string filePath);

        /// <summary>
        /// Извлекает текст из файла.
        /// </summary>
        /// <param name="filePath">Абсолютный путь (уже валидирован)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Извлечённый текст + метаданные</returns>
        Task<ParsedDocument> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default);
    }
}
```

**DTO:**

```csharp
public class ParsedDocument
{
    /// <summary>Полный текст документа.</summary>
    public string Text { get; set; }

    /// <summary>Метаданные: количество страниц, автор, дата (опционально).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>Размер исходного файла в байтах.</summary>
    public long OriginalSizeBytes { get; set; }

    /// <summary>Число «страниц» (PDF) или «секций» (по заголовкам MD).</summary>
    public int? PageCount { get; set; }
}
```

#### § 4.5.2. Регистрация парсеров

```csharp
// Startup.cs
services.AddSingleton<IRagDocumentParser, PlainTextParser>();
// v1.5.x:
// services.AddSingleton<IRagDocumentParser, PdfParser>();
// services.AddSingleton<IRagDocumentParser, DocxParser>();

services.AddSingleton<IRagDocumentParserRegistry, RagDocumentParserRegistry>();
```

`IRagDocumentParserRegistry` — резолвит парсер по расширению:

```csharp
public interface IRagDocumentParserRegistry
{
    /// <summary>
    /// Возвращает парсер для файла или null, если формат не поддержан.
    /// </summary>
    IRagDocumentParser Resolve(string filePath);

    /// <summary>
    /// Все поддерживаемые расширения (union всех парсеров).
    /// </summary>
    IReadOnlyList<string> GetAllSupportedExtensions();
}
```

#### § 4.5.3. `PlainTextParser` (MVP)

**Поддерживаемые расширения (21 шт):**

| Категория | Расширения |
|---|---|
| **Текст** | `.txt`, `.md`, `.csv`, `.tsv`, `.log` |
| **Разметка** | `.json`, `.xml`, `.yaml`, `.yml`, `.html`, `.htm` |
| **Код** | `.cs`, `.py`, `.js`, `.ts`, `.java`, `.go`, `.rs`, `.sql`, `.sh`, `.ps1` |
| **Web** | `.razor`, `.cshtml`, `.css`, `.scss` |
| **Конфиги** | `.dockerfile`, `.gitignore`, `.editorconfig` |

**Алгоритм:**
1. Открыть `FileStream` (read, shared).
2. Определить кодировку:
   - BOM (UTF-8 / UTF-16) → использовать.
   - Нет BOM → попытка UTF-8, fallback на Windows-1251 (для русских файлов).
3. Прочитать весь текст (`StreamReader.ReadToEndAsync`).
4. Нормализовать переносы (`\r\n` → `\n`).
5. Для `.md` — опционально: strip frontmatter (`--- ... ---` в начале).
6. Вернуть `ParsedDocument`.

**Ограничения:**
- Не обрабатывает `.doc`, `.docx`, `.pdf`, `.xlsx`, `.pptx` — это v1.5.x.
- Бинарные файлы (изображения, архивы) — `CanParse = false`.

#### § 4.5.4. Кодировка — детали

```csharp
private static Encoding DetectEncoding(Stream stream)
{
    // BOM detection
    Span<byte> bom = stackalloc byte[4];
    int read = stream.Read(bom);
    stream.Seek(0, SeekOrigin.Begin);

    if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        return new UTF8Encoding(true);
    if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
        return new UnicodeEncoding(false, true);
    if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
        return new UnicodeEncoding(true, true);

    // Fallback: пробуем UTF-8 со strict decoding
    try
    {
        using var test = new StreamReader(stream, new UTF8Encoding(false, throwOnInvalidBytes: true));
        test.ReadToEnd();
        stream.Seek(0, SeekOrigin.Begin);
        return new UTF8Encoding(false);
    }
    catch
    {
        stream.Seek(0, SeekOrigin.Begin);
        return Encoding.GetEncoding(1251); // Windows-1251
    }
}
```

> **Примечание:** пакет `System.Text.Encoding.CodePages` уже в shared framework (.NET 10), `Encoding.GetEncoding(1251)` работает без доп. PackageReference.

#### § 4.5.5. PDF (v1.5.x — фаза 3.5)

**`PdfParser` на `PdfPig` (Apache 2.0):**

```csharp
public class PdfParser : IRagDocumentParser
{
    public string Name => "PdfPig";
    public IReadOnlyList<string> SupportedExtensions => new[] { ".pdf" };

    public bool CanParse(string filePath) =>
        Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedDocument> ParseAsync(
        string filePath, CancellationToken ct = default)
    {
        using var doc = PdfDocument.Open(filePath);
        var sb = new StringBuilder();

        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
            sb.AppendLine();  // разделитель страниц
        }

        return new ParsedDocument
        {
            Text = sb.ToString(),
            OriginalSizeBytes = new FileInfo(filePath).Length,
            PageCount = doc.NumberOfPages,
            Metadata = { ["format"] = "pdf" }
        };
    }
}
```

**Пакет:** `PdfPig` (Apache 2.0, ~500 KB).

**Ограничения (осознанные):**
- OCR сканов — **не поддерживается** (нет текстового слоя → пустой результат).
- Сложная вёрстка (таблицы, multi-column) — упрощённо, текст «склеивается».
- Шифрованные PDF → `PdfDocument.Open` бросает исключение.

#### § 4.5.6. DOCX (v1.5.x — фаза 3.5)

**`DocxParser` на `DocumentFormat.OpenXml` (MIT):**

```csharp
public class DocxParser : IRagDocumentParser
{
    public string Name => "OpenXml";
    public IReadOnlyList<string> SupportedExtensions => new[] { ".docx" };

    public bool CanParse(string filePath) =>
        Path.GetExtension(filePath).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedDocument> ParseAsync(
        string filePath, CancellationToken ct = default)
    {
        using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return new ParsedDocument { Text = "", OriginalSizeBytes = 0 };

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            sb.AppendLine(para.InnerText);
        }

        return new ParsedDocument
        {
            Text = sb.ToString(),
            OriginalSizeBytes = new FileInfo(filePath).Length,
            Metadata = { ["format"] = "docx" }
        };
    }
}
```

**Пакет:** `DocumentFormat.OpenXml` (MIT, ~1.5 MB).

**Ограничения:**
- `.doc` (старый формат) — **не поддерживается** (OpenXml работает только с `.docx`).
- Таблицы — текст извлекается, но структура теряется.
- Встроенные изображения — игнорируются.

#### § 4.5.7. Конфигурация

```jsonc
"Rag": {
  "Parsers": {
    "PlainText": {
      "Enabled": true,
      "EncodingFallback": "windows-1251",
      "StripMarkdownFrontmatter": true
    },
    "Pdf": {
      "Enabled": false,       // v1.5.x
      "MaxPages": 500
    },
    "Docx": {
      "Enabled": false        // v1.5.x
    }
  }
}
```

#### § 4.5.8. Тесты

- `PlainTextParserTests`:
  - `CanParse_TxtMdCsv_ReturnsTrue`
  - `CanParse_PdfDocx_ReturnsFalse`
  - `ParseAsync_Utf8File_ReadsCorrectly`
  - `ParseAsync_Utf8BomFile_StripsBom`
  - `ParseAsync_Windows1251File_ReadsCyrillic`
  - `ParseAsync_NormalizesLineEndings`
  - `ParseAsync_MarkdownWithFrontmatter_Strips`
  - `ParseAsync_EmptyFile_ReturnsEmpty`
- `RagDocumentParserRegistryTests`:
  - `Resolve_TxtFile_ReturnsPlainTextParser`
  - `Resolve_UnknownExt_ReturnsNull`
  - `GetAllSupportedExtensions_ReturnsUnion`

---

### § 4.8. Retrieval Service

> **Порядок:** § 4.8 идёт перед § 4.6-4.7, потому что Tools зависят от Retrieval. Нумерация сохранена из outline для единообразия.

#### § 4.8.1. Контракт

```csharp
namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис поиска по векторным индексам (v1.5.0, KI-083).
    /// Оркестрирует: embed query → search vector store → enrich metadata.
    /// </summary>
    public interface IRetrievalService
    {
        /// <summary>
        /// Ищет top-K релевантных чанков по запросу.
        /// </summary>
        /// <param name="query">Текст запроса</param>
        /// <param name="indexName">Имя индекса (обязательно)</param>
        /// <param name="topK">Сколько результатов вернуть (default 5)</param>
        /// <param name="chatId">ID чата (для my_rag_docs / chat_history), null для global</param>
        /// <param name="userId">ID пользователя (для workspace / chat_history)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список найденных чанков с score, отсортированный по убыванию</returns>
        Task<IReadOnlyList<RetrievedChunkDto>> SearchAsync(
            string query,
            string indexName,
            int topK = 5,
            int? chatId = null,
            int? userId = null,
            CancellationToken cancellationToken = default);
    }
}
```

**DTO:**

```csharp
public class RetrievedChunkDto
{
    /// <summary>ID чанка в БД (DocumentChunk.Id).</summary>
    public int ChunkId { get; set; }

    /// <summary>Текст чанка (для вставки в промпт).</summary>
    public string Text { get; set; }

    /// <summary>Cosine similarity (0..1, чем выше — тем релевантнее).</summary>
    public float Score { get; set; }

    /// <summary>Путь документа (для citations — KI-086).</summary>
    public string DocumentPath { get; set; }

    /// <summary>Позиция чанка в документе (0-based).</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Имя индекса (project_docs, my_rag_docs, ...).</summary>
    public string IndexName { get; set; }

    /// <summary>Доп. метаданные (JSON): { page, section }.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
```

#### § 4.8.2. Алгоритм

```
1. Валидация:
   - query не пустой.
   - indexName из { project_docs, my_rag_docs, chat_history, workspace }.

2. Embed query:
   - IEmbeddingService.GetEmbeddingAsync(query).
   - Если текст длинный (> max embedding input ~8192 токенов) — обрезать/разбить.

3. Search vector store:
   - IVectorStore.Search(indexName, queryVector, topK * 2).  // берём больше для фильтрации

4. Фильтрация по метаданным:
   - Для my_rag_docs / chat_history: только чанки с данным chatId / userId.
   - Для workspace: только чанки с данным userId.
   - Для project_docs: без фильтра (global).

5. Фильтрация по MinScore (из конфига).

6. Enrichment метаданных:
   - Загрузить DocumentChunk[] из БД по chunkIds (batch).
   - Собрать RetrievedChunkDto с полным text + documentPath + metadata.

7. Возврат top-K после всех фильтров.
```

#### § 4.8.3. Кэширование

**Опционально (v1.5.x):**
- `ConcurrentDictionary<(indexName, chatId, userId, queryHash), CachedResult>` с TTL 60 сек.
- Для повторяющихся запросов от LLM (например, при multi-turn loop с одной задачей).

В MVP — **без кэша** (простота).

#### § 4.8.4. Конфигурация

```jsonc
"Rag": {
  "Retrieval": {
    "DefaultTopK": 5,
    "OverFetchMultiplier": 2,       // берём topK * 2 из store, потом фильтруем
    "MinScore": 0.3,
    "CacheEnabled": false           // v1.5.x
  }
}
```

#### § 4.8.5. Тесты

- `RetrievalServiceTests`:
  - `SearchAsync_EmptyQuery_Throws`
  - `SearchAsync_NoVectors_ReturnsEmpty`
  - `SearchAsync_ReturnsTopK_SortedByScore`
  - `SearchAsync_RespectsMinScore`
  - `SearchAsync_FiltersByChatId`
  - `SearchAsync_FiltersByUserId`
  - `SearchAsync_EnrichesMetadataFromDb`

---

### § 4.6. Search Tools (3 шт)

Три инструмента для LLM, которые вызывают `IRetrievalService`.

#### § 4.6.1. `SearchKnowledgeBaseTool`

**Назначение:** поиск по **глобальным документам проекта** (README, RULES, KNOWN_ISSUES, CHANGELOG, RELEASES).

```csharp
public class SearchKnowledgeBaseTool : ITool
{
    public string Name => "search_knowledge_base";
    public string Description =>
        "Ищет релевантные фрагменты в документации проекта IIChatTools " +
        "(README, RULES, KNOWN_ISSUES, CHANGELOG, RELEASES). " +
        "Используй для вопросов про правила разработки, известные проблемы, " +
        "архитектуру, API, конфигурацию. НЕ используй для вопросов по внешним темам " +
        "(для них — web_search).";
    public bool RequiresApprovalByDefault => false;   // read-only

    public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
    {
        new ToolParameterDescriptor
        {
            Name = "query",
            Type = "string",
            Description = "Поисковый запрос на естественном языке.",
            Required = true
        },
        new ToolParameterDescriptor
        {
            Name = "topK",
            Type = "integer",
            Description = "Сколько чанков вернуть (1–20, по умолчанию 5).",
            Required = false,
            Default = 5
        }
    };

    // ExecuteAsync: IRetrievalService.SearchAsync(query, "project_docs", topK)
}
```

**Индекс:** `project_docs` (global).

**Формат ответа (для LLM):**

```json
{
  "success": true,
  "data": {
    "query": "yield return scope",
    "count": 3,
    "results": [
      {
        "rank": 1,
        "score": 0.87,
        "source": "docs/development/RULES.md",
        "chunkIndex": 142,
        "text": "..."
      }
    ]
  }
}
```

#### § 4.6.2. `SearchChatHistoryTool`

**Назначение:** поиск по **истории чатов пользователя**.

```csharp
public string Name => "search_chat_history";
public string Description =>
    "Ищет релевантные фрагменты в истории чатов текущего пользователя " +
    "(все диалоги с LLM). Используй, когда пользователь ссылается на прошлые " +
    "обсуждения: «мы говорили про…», «в прошлый раз ты…».";
```

**Индекс:** `chat_history` (per-user).

**Параметры:** `query`, `topK`, опционально `chatId` (ограничить конкретным чатом).

**Контекст:** `context.UserId` — обязателен.

#### § 4.6.3. `SearchWorkspaceTool`

**Назначение:** семантический поиск по **файлам workspace** (opt-in).

```csharp
public string Name => "search_workspace";
public string Description =>
    "Ищет фрагменты в файлах рабочей области (workspace) пользователя. " +
    "Используй для вопросов вида «где у меня в проекте X?», «найди код, " +
    "который делает Y». НЕ используй, если известен точный путь — для этого " +
    "есть read_file.";
```

**Индекс:** `workspace` (per-user).

**Параметры:** `query`, `topK`, опционально `filePattern` (glob для фильтрации по пути).

**Важно:** должен быть явно **включён пользователем** в `/profile → Workspace index`, иначе возвращает `fail("Workspace index отключён")`.

#### § 4.6.4. Регистрация в DI

```csharp
// Startup.cs
services.AddScoped<ITool, SearchKnowledgeBaseTool>();
services.AddScoped<ITool, SearchChatHistoryTool>();
services.AddScoped<ITool, SearchWorkspaceTool>();
```

**Chat видит** их наравне с 6 агентами + `consult_secondary_agent` → **10 инструментов** (было 7).

**Или** внутри `consult_secondary_agent` (не на верхнем уровне)? — решим на фазе 4 (см. § 10).

#### § 4.6.5. Тесты

- `SearchKnowledgeBaseToolTests`:
  - `Name_IsSearchKnowledgeBase`
  - `ExecuteAsync_EmptyQuery_ReturnsFail`
  - `ExecuteAsync_ValidQuery_CallsRetrievalService`
  - `ExecuteAsync_TopKRespected`
- `SearchChatHistoryToolTests`:
  - `ExecuteAsync_UsesUserIdFromContext`
  - `ExecuteAsync_OptionalChatIdFilter`
- `SearchWorkspaceToolTests`:
  - `ExecuteAsync_WorkspaceDisabled_ReturnsFail`
  - `ExecuteAsync_WorkspaceEnabled_Searches`

---

### § 4.7. Attached Files (Chat)

#### § 4.7.1. UX-флоу

```
1. Пользователь в /chat → кнопка 📎 (paperclip) рядом с полем ввода.
2. Файловый диалог: 1–5 файлов, до 30 MB суммарно.
3. Frontend загружает каждый файл:
   POST /api/chat/{chatId}/attachments (multipart/form-data)
4. Backend:
   - Валидация (размер, формат, лимит).
   - Сохраняет файл в workspace/chat-attachments/{chatId}/{guid}.ext
   - IngestionAsync → my_rag_docs (chunk + embed + store)
   - Возвращает { attachmentId, fileName, chunks, tokens }
5. Frontend показывает чип под полем ввода:
   ┌────────────────────────────────────────┐
   │ [PDF] contract.pdf (3 чанка) [×]       │
   │ [TXT] notes.txt    (1 чанк)  [×]       │
   │ RAG: my_rag_docs — 4 чанка [Очистить]  │
   └────────────────────────────────────────┘
6. Отправка сообщения:
   - ChatStreamService: если у chatId есть чанки в my_rag_docs →
     автоматически вставляет top-K в system prompt (см. § 4.7.5)
7. Ответ содержит Sources (из metadata) → блок «Источники» (KI-086).

8. Кнопка «Очистить RAG»:
   POST /api/chat/{chatId}/attachments/clear
   → DeleteAllChunksAsync(my_rag_docs, chatId)
```

#### § 4.7.2. Схема БД — `ChatAttachment`

```csharp
public class ChatAttachment : BaseEntity
{
    public int ChatId { get; set; }
    public virtual Chat Chat { get; set; }

    /// <summary>ID пользователя-владельца (защита).</summary>
    public int UserId { get; set; }

    /// <summary>Имя файла (отображается в UI).</summary>
    public string FileName { get; set; }

    /// <summary>MIME type (application/pdf, text/plain, ...).</summary>
    public string ContentType { get; set; }

    /// <summary>Размер в байтах.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Относительный путь в workspace (chat-attachments/{chatId}/{guid}.ext).</summary>
    public string StoragePath { get; set; }

    /// <summary>SHA256 содержимого (защита от дублей).</summary>
    public string ContentHash { get; set; }

    /// <summary>Количество проиндексированных чанков.</summary>
    public int ChunksCount { get; set; }
}
```

**Индексы:**
- `(ChatId)` — для списка вложений чата.
- `(UserId, ContentHash)` — для дедупликации.

**Миграция:** `AddChatAttachments`.

#### § 4.7.3. Контракт сервиса

```csharp
public interface IChatAttachmentService
{
    /// <summary>
    /// Загружает и индексирует файл.
    /// </summary>
    Task<ChatAttachmentDto> UploadAsync(
        int chatId, int userId, Stream content,
        string fileName, string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Список вложений чата.
    /// </summary>
    Task<IReadOnlyList<ChatAttachmentDto>> GetForChatAsync(
        int chatId, int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет вложение (файл + чанки + vectors).
    /// </summary>
    Task<bool> DeleteAsync(
        int attachmentId, int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Очищает все вложения и чанки чата («Очистить RAG»).
    /// </summary>
    Task<int> ClearForChatAsync(
        int chatId, int userId, CancellationToken cancellationToken = default);
}
```

#### § 4.7.4. API endpoints

| Метод | URL | Назначение |
|---|---|---|
| `POST` | `/api/chat/{chatId}/attachments` | Загрузить файл (multipart) |
| `GET` | `/api/chat/{chatId}/attachments` | Список вложений |
| `DELETE` | `/api/chat/{chatId}/attachments/{attachmentId}` | Удалить |
| `POST` | `/api/chat/{chatId}/attachments/clear` | Очистить всё |

**Валидация на POST:**
- Размер файла ≤ `MaxFileSizeBytes` (32 MB).
- Сумма файлов в чате ≤ `MaxTotalSizePerChat` (30 MB).
- Количество вложений ≤ `MaxFilesPerChat` (5).
- Расширение в `AllowedExtensions` (из § 4.5.3).

#### § 4.7.5. Интеграция в ChatStreamService

**Авто-подстановка при attached files:**

```
При построении messages (BuildMessagesAsync):
1. Проверить: есть ли чанки в my_rag_docs для chatId?
2. Если да:
   a. Взять последнее user-сообщение (или все новые с момента последнего ответа).
   b. IRetrievalService.SearchAsync(lastUserText, "my_rag_docs", topK: 5, chatId).
   c. Отфильтровать score < MinScore.
   d. Вставить в system prompt:
      "Ниже — релевантные фрагменты из прикреплённых пользователем документов.
       Используй их для ответа. Ссылайся на них как [1], [2] — в конце ответа
       дай список источников.

       [1] contract.pdf (фрагмент 3):
       <текст чанка>

       [2] notes.txt (фрагмент 1):
       <текст чанка>"
3. Sources (из metadata) сохранить в ChatMessage.MetadataJson — для UI (KI-086).
```

**Fallback:** если attached files нет — LLM сама решает, вызывать ли `search_*` tools.

#### § 4.7.6. Размещение файлов

```
%Workspace:RootPath%/
  chat-attachments/
    1/                           ← chatId
      a1b2c3d4-...-guid.pdf
      e5f6g7h8-...-guid.txt
    2/
      ...
```

- Файлы пользователя — в его workspace (изоляция по `UserId`).
- Удаление чата → каскадное удаление файлов (через `ChatRetentionService` или на delete endpoint).

#### § 4.7.7. Конфигурация

```jsonc
"Rag": {
  "Attachments": {
    "MaxFileSizeBytes": 33554432,           // 32 MB per file
    "MaxTotalSizePerChat": 31457280,        // 30 MB total
    "MaxFilesPerChat": 5,
    "StorageSubfolder": "chat-attachments",
    "AutoInjectTopK": 5,                    // сколько чанков в system prompt
    "AutoInjectMinScore": 0.35
  }
}
```

#### § 4.7.8. Тесты

- `ChatAttachmentServiceTests`:
  - `UploadAsync_ValidFile_CreatesAttachmentAndChunks`
  - `UploadAsync_TooLarge_Throws`
  - `UploadAsync_UnsupportedFormat_Throws`
  - `UploadAsync_ExceedsFilesPerChat_Throws`
  - `UploadAsync_ExceedsTotalSize_Throws`
  - `UploadAsync_SameHash_DeduplicatesOrAllows` (решить: dedup / отказ)
  - `DeleteAsync_RemovesFileAndChunks`
  - `ClearForChatAsync_RemovesAll`
- `ChatAttachmentControllerTests` (integration):
  - `POST_ValidFile_Returns201WithDto`
  - `POST_TooLarge_Returns400`
  - `DELETE_RemovesAttachment`

---

## § 5. Схема данных

### § 5.1. Новые entity

| Entity | Назначение | Миграция |
|---|---|---|
| `DocumentChunk` | Чанк документа (текст + метаданные) | `AddDocumentChunks` |
| `ChatAttachment` | Вложение чата (файл + hash + chunks count) | `AddChatAttachments` |

**Существующие entity — без изменений:**
- `Chat`, `ChatMessage` — не трогаем.
- `AppSetting`, `AuditLog`, `AgentState` — не трогаем.

### § 5.2. `DocumentChunk`

```csharp
public class DocumentChunk : BaseEntity
{
    public string IndexName { get; set; }       // project_docs | my_rag_docs | chat_history | workspace
    public int? ChatId { get; set; }            // для my_rag_docs; null для остальных
    public int UserId { get; set; }             // 0 — маркер «глобальный чанк» (project_docs)
    public string DocumentPath { get; set; }    // относительный путь или URL
    public string DocumentHash { get; set; }    // SHA256
    public int ChunkIndex { get; set; }
    public string Text { get; set; }            // nvarchar(max)
    public int Tokens { get; set; }
    public string MetadataJson { get; set; }    // { page, section, offset }

    public virtual Chat Chat { get; set; }      // навигация для my_rag_docs
}
```

**FK-связи (реализовано в `AppDbContext.OnModelCreating`):**
- **`ChatId` → `Chat`** — `DeleteBehavior.Cascade`, **nullable**.
  - Удаление чата автоматически удаляет его чанки в `my_rag_docs`.
  - Для `project_docs` / `chat_history` / `workspace` — `ChatId = null`.
- **`UserId` — БЕЗ FK на `ApplicationUser`.**
  - Причина: `project_docs` использует `UserId = 0` как маркер «глобальный чанк». Заводить FK пришлось бы через nullable `int?` или фейкового пользователя Id=0 — оба варианта хуже. Фильтрация по пользователю делается на уровне запросов (`IRetrievalService`).
  - Для per-user индексов (`chat_history`, `workspace`) `UserId` = реальный ID пользователя.

**Индексы:**
- `IX_DocumentChunks_Index_Hash` — `(IndexName, DocumentHash)` — skip re-index.
- `IX_DocumentChunks_Index_Chat_User` — `(IndexName, ChatId, UserId)` — фильтрация при поиске.
- `IX_DocumentChunks_DocumentPath` — `(DocumentPath)` — удаление по документу.

### § 5.3. `ChatAttachment`

```csharp
public class ChatAttachment : BaseEntity
{
    public int ChatId { get; set; }
    public virtual Chat Chat { get; set; }
    public int UserId { get; set; }
    public string FileName { get; set; }        // max 255
    public string ContentType { get; set; }     // max 100
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; }     // max 500
    public string ContentHash { get; set; }     // SHA256, max 64
    public int ChunksCount { get; set; }
}
```

**Индексы:**
- `(ChatId)` — список вложений.
- `(UserId, ContentHash)` — дедупликация.

### § 5.4. Embeddings — где хранятся

| Слой | MVP (v1.5.0) | v1.5.x (Qdrant) |
|---|---|---|
| Vector data | `InMemoryVectorStore` (RAM) | Qdrant collection |
| Chunk text | `DocumentChunk.Text` (БД) | `DocumentChunk.Text` (БД) |
| Metadata | `DocumentChunk.MetadataJson` | Qdrant payload + БД |
| Persistence | **Нет** (теряется при рестарте) | Да |

**При старте приложения (MVP):**
- `project_docs` — авто-reindex (если `Rag:AutoIndexProjectDocs = true`).
- `my_rag_docs` / `chat_history` / `workspace` — пустые (пользователь переиндексирует).

### § 5.5. Список миграций

Для SqlServer:
1. `AddDocumentChunks` — таблица `DocumentChunks` + 3 индекса.
2. `AddChatAttachments` — таблица `ChatAttachments` + 2 индекса + FK на Chat.

**Для Sqlite (dev):** удалить `.db` (RULES § 4.25 — `EnsureCreated` не мигрирует).

---

## § 6. API endpoints

### § 6.1. Chat attachments

| Метод | URL | Назначение | Response |
|---|---|---|---|
| `POST` | `/api/chat/{chatId}/attachments` | Загрузить файл (multipart) | `{ success, data: ChatAttachmentDto }` |
| `GET` | `/api/chat/{chatId}/attachments` | Список вложений | `{ success, data: ChatAttachmentDto[] }` |
| `DELETE` | `/api/chat/{chatId}/attachments/{id}` | Удалить | `{ success }` |
| `POST` | `/api/chat/{chatId}/attachments/clear` | Очистить все | `{ success, data: { removed } }` |

**`ChatAttachmentDto`:**

```csharp
public class ChatAttachmentDto
{
    public int Id { get; set; }
    public string FileName { get; set; }
    public string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public int ChunksCount { get; set; }
    public DateTime UploadedAt { get; set; }
}
```

### § 6.2. Admin — Knowledge Base

| Метод | URL | Назначение | Response |
|---|---|---|---|
| `GET` | `/api/admin/knowledge/indexes` | Список индексов + count | `{ success, data: RagIndexDto[] }` |
| `POST` | `/api/admin/knowledge/indexes/project-docs/reindex` | Переиндексировать project docs | `{ success, data: IngestionResultDto }` |
| `GET` | `/api/admin/knowledge/chunks?index=&page=` | Просмотр чанков (пагинация) | `{ success, data: { items, page, totalPages } }` |
| `DELETE` | `/api/admin/knowledge/chunks/{id}` | Удалить один чанк | `{ success }` |
| `GET` | `/api/admin/knowledge/settings` | Текущие настройки RAG | `{ success, data: RagSettingsDto }` |
| `PUT` | `/api/admin/knowledge/settings` | Сохранить настройки | `{ success }` |

**`RagIndexDto`:**

```csharp
public class RagIndexDto
{
    public string Name { get; set; }
    public string Description { get; set; }
    public int ChunkCount { get; set; }
    public int DocumentCount { get; set; }
    public DateTime? LastIndexedAt { get; set; }
}
```

**`RagSettingsDto`:**

```csharp
public class RagSettingsDto
{
    public string ChunkingStrategy { get; set; }    // recursive | sentence | fixed
    public int ChunkSize { get; set; }
    public int ChunkOverlap { get; set; }
    public int MinChunkSize { get; set; }
    public int DefaultTopK { get; set; }
    public float MinScore { get; set; }
    public string EmbeddingModel { get; set; }
    public bool AutoIndexProjectDocs { get; set; }
}
```

### § 6.3. Profile — Workspace index

| Метод | URL | Назначение |
|---|---|---|
| `GET` | `/api/profile/workspace-index` | Статус (enabled, chunk count) |
| `POST` | `/api/profile/workspace-index/enable` | Включить + запустить индексацию |
| `POST` | `/api/profile/workspace-index/disable` | Отключить + очистить |
| `POST` | `/api/profile/workspace-index/reindex` | Переиндексировать |
| `GET` | `/api/profile/workspace-index/status` | Прогресс (для polling) |

### § 6.4. Retrieval (для отладки)

| Метод | URL | Назначение |
|---|---|---|
| `POST` | `/api/rag/search` | Прямой поиск (admin/test) |
| `GET` | `/api/rag/health` | Проверка embedding-модели |

**`POST /api/rag/search`:**

```jsonc
// Request
{
  "query": "yield return scope",
  "indexName": "project_docs",
  "topK": 5
}

// Response
{
  "success": true,
  "data": {
    "count": 3,
    "results": [
      { "rank": 1, "score": 0.87, "source": "docs/development/RULES.md", "text": "..." }
    ]
  }
}
```

---

## § 7. UI

### § 7.1. Chat (`/chat`)

#### § 7.1.1. Chips вложений под полем ввода

```
┌──────────────────────────────────────────────┐
│ [📎 Прикрепить]  RAG: myDocs — 4 чанка  [Очистить]│
│ ┌──────────────────────────────────────────┐ │
│ │ 📄 contract.pdf (3 чанка)             [×]│ │
│ │ 📄 notes.txt    (1 чанк)              [×]│ │
│ └──────────────────────────────────────────┘ │
├──────────────────────────────────────────────┤
│ [Введите сообщение...]              [↑]      │
└──────────────────────────────────────────────┘
```

**Компоненты:**
- Кнопка 📎 — открывает файловый диалог.
- Строка статуса: `RAG: myDocs — 4 чанка` + кнопка `[Очистить]`.
- Chips — по одному на файл, с кнопкой ✕.

**Состояние (JS):**

```javascript
state.attachments = [];        // ChatAttachmentDto[]
state.ragChunkCount = 0;       // количество чанков в my_rag_docs
```

**События:**
- `input[type=file]` change → `POST /api/chat/{id}/attachments` для каждого файла.
- Клик ✕ → `DELETE /api/chat/{id}/attachments/{aid}` → update state.
- Клик «Очистить» → `POST /api/chat/{id}/attachments/clear`.

#### § 7.1.2. Sources / citations (KI-086)

Под assistant-сообщением — блок «Источники»:

```
┌────────────────────────────────────────┐
│ Ассистент · 15:31 · 123 / 45 токенов   │
│                                        │
│ [Текст ответа...]                      │
│                                        │
│ 📚 Источники:                          │
│ [1] docs/development/RULES.md          │
│ [2] contract.pdf (стр. 3)              │
└────────────────────────────────────────┘
```

**Источник данных:** `ChatMessage.MetadataJson` → `{ sources: [{ path, chunkIndex, score, anchor }] }`.

> **Примечание:** реализация Sources — в KI-086 (v1.6.0). В v1.5.0 сохраняем в `MetadataJson`, но UI-блок не рисуем.

#### § 7.1.3. Индикатор «RAG активен»

В шапке чата (рядом с моделью):

```
┌────────────────────────────────────────────────┐
│ Название чата                                  │
│ qwen/qwen3-4b-2507 (default) · 🔍 RAG: 4 чанка│
└────────────────────────────────────────────────┘
```

При клике → выпадающий список приложенных файлов + [Очистить].

### § 7.2. Admin (`/admin` → вкладка «Knowledge Base»)

**8-я вкладка** (после «Агенты»).

#### § 7.2.1. Верхняя панель

```
┌──────────────────────────────────────────────────────┐
│ [Обновить индекс проекта]  [Настройки RAG]          │
└──────────────────────────────────────────────────────┘
```

**«Обновить индекс проекта»** → `POST /api/admin/knowledge/indexes/project-docs/reindex` → показывает прогресс (spinner + toast «Индексировано 245 чанков за 8.3 с»).

**«Настройки RAG»** → модалка (переиспользует `showModal` из `admin.js`):
- Chunking Strategy (select: recursive / sentence / fixed).
- Chunk Size (number, 100–2000).
- Chunk Overlap (number, 0–500).
- Min Score (range, 0.0–1.0).
- Embedding Model (text).
- Auto-index project docs (checkbox).

#### § 7.2.2. Таблица индексов

| Index name | Документов | Чанков | Последняя индексация | Действия |
|---|---|---|---|---|
| `project_docs` | 5 | 245 | 2 мин назад | Обновить / Очистить |
| `my_rag_docs` | 12 | 48 | — | Просмотр |
| `chat_history` | 34 | 890 | — | Просмотр |
| `workspace` | 5 | 120 | — | Просмотр |

#### § 7.2.3. Просмотр чанков

Клик по строке → таблица чанков (пагинация 20):
- Column: `# | Index | Document | ChunkIndex | Tokens | Text (preview)`
- Кнопка ✕ — удалить чанк.

### § 7.3. Profile (`/profile`)

**Новая карточка** «Workspace index»:

```
┌──────────────────────────────────────────┐
│ Индексация workspace                     │
│                                          │
│ [ ] Включить семантический поиск         │
│     по файлам workspace                  │
│                                          │
│ Статус: отключён                         │
│ Файлов: — | Чанков: —                    │
│ [Переиндексировать]                      │
└──────────────────────────────────────────┘
```

**Логика:**
- Checkbox включить → `POST /api/profile/workspace-index/enable` (запускает индексацию в фоне).
- Прогресс через `GET /api/profile/workspace-index/status` (polling 2 сек).
- При выключении — очистка `workspace` индекса.

### § 7.4. Локализация UI (13 ключей)

См. § 9 — все ключи `.resx`.

### § 7.5. CSS-классы

```css
/* Chat */
.chat-attachments-bar { ... }       /* строка «📎 / RAG: N чанков / Очистить» */
.chat-attachment-chip { ... }       /* чип одного файла */
.chat-attachment-chip-remove { ... }/* кнопка ✕ */
.chat-message-sources { ... }       /* блок «Источники» (KI-086) */
.chat-rag-indicator { ... }         /* индикатор в шапке */

/* Admin */
.rag-indexes-table { ... }
.rag-chunks-table { ... }
.rag-settings-modal { ... }

/* Profile */
.profile-workspace-card { ... }
```

---

## § 8. Конфигурация

### § 8.1. `appsettings.json` — полная секция `Rag:*`

```jsonc
"Rag": {
  // Общие
  "Enabled": true,
  "AutoIndexProjectDocs": false,      // при старте (MVP: false, ручное через /admin)

  // Embedding
  "Embedding": {
    "Model": "text-embedding-nomic-embed-text-v1.5",
    "Dimensions": 768,
    "BatchSize": 64,
    "TimeoutSeconds": 60,
    "CacheEnabled": true,
    "CacheMaxEntries": 10000
  },

  // Vector store
  "VectorStore": {
    "Provider": "InMemory",           // InMemory | Qdrant (v1.5.x)
    "DefaultTopK": 5,
    "MinScore": 0.3
  },

  // Chunking
  "Chunking": {
    "Strategy": "recursive",          // recursive | sentence | fixed
    "ChunkSize": 500,
    "ChunkOverlap": 64,
    "MinChunkSize": 100,
    "Separators": ["\n\n", "\n", ". ", "! ", "? ", "; ", ", ", " "]
  },

  // Ingestion
  "Ingestion": {
    "MaxFileSizeBytes": 33554432,     // 32 MB
    "MaxFilesPerChat": 5,
    "MaxTotalSizePerChat": 31457280,  // 30 MB
    "AllowedExtensions": [
      ".txt", ".md", ".csv", ".tsv", ".log",
      ".json", ".xml", ".yaml", ".yml", ".html", ".htm",
      ".cs", ".py", ".js", ".ts", ".java", ".go", ".rs", ".sql",
      ".sh", ".ps1", ".razor", ".cshtml", ".css", ".scss"
    ],
    "ProjectDocsPaths": [
      "README.md",
      "CHANGELOG.md",
      "docs/development/RULES.md",
      "docs/KNOWN_ISSUES.md",
      "docs/development/RELEASES.md"
    ]
  },

  // Parsers
  "Parsers": {
    "PlainText": {
      "Enabled": true,
      "EncodingFallback": "windows-1251",
      "StripMarkdownFrontmatter": true
    },
    "Pdf": {
      "Enabled": false,               // v1.5.x (фаза 3.5)
      "MaxPages": 500
    },
    "Docx": {
      "Enabled": false                // v1.5.x (фаза 3.5)
    }
  },

  // Retrieval
  "Retrieval": {
    "DefaultTopK": 5,
    "OverFetchMultiplier": 2,         // берём topK*2 из store, потом фильтруем
    "MinScore": 0.3,
    "CacheEnabled": false             // v1.5.x
  },

  // Attachments (chat)
  "Attachments": {
    "MaxFileSizeBytes": 33554432,     // 32 MB
    "MaxTotalSizePerChat": 31457280,  // 30 MB
    "MaxFilesPerChat": 5,
    "StorageSubfolder": "chat-attachments",
    "AutoInjectTopK": 5,
    "AutoInjectMinScore": 0.35
  }
}
```

### § 8.2. `appsettings.Development.json` — override

```jsonc
"Rag": {
  "AutoIndexProjectDocs": false,      // в dev — ручное, не при старте
  "Embedding": {
    "CacheEnabled": false              // в dev — без кэша (легче отлаживать)
  },
  "Retrieval": {
    "MinScore": 0.25                   // мягче для тестовых данных
  }
}
```

### § 8.3. User Secrets (опционально)

Не требуется — embedding-модель настраивается через `appsettings.json`. Если нужна отдельная модель:

```powershell
dotnet user-secrets set "Rag:Embedding:Model" "my-embedding-model-v2"
```

---

## § 9. Локализация

### § 9.1. Ключи `.resx` (RU + EN)

**Правило 1.14 + 4.16:** все ключи — camelCase, оба `.resx` синхронны.

| Ключ | RU | EN |
|---|---|---|
| `RagAttachButton` | Прикрепить файл | Attach file |
| `RagAttachButtonTooltip` | Прикрепить PDF / TXT / MD / CSV / код | Attach PDF / TXT / MD / CSV / code |
| `RagClearButton` | Очистить RAG | Clear RAG |
| `RagChunksCount` | RAG: {0} чанков | RAG: {0} chunks |
| `RagChunksCountOne` | RAG: 1 чанк | RAG: 1 chunk |
| `RagAttachmentsHeader` | Вложения к чату | Chat attachments |
| `RagUploadSuccess` | Файл «{0}» проиндексирован ({1} чанков) | File "{0}" indexed ({1} chunks) |
| `RagUploadTooLarge` | Файл больше {0} МБ | File exceeds {0} MB |
| `RagUploadUnsupportedFormat` | Формат «{0}» не поддерживается | Format "{0}" is not supported |
| `RagUploadExceedsMaxFiles` | Максимум {0} файлов на чат | Max {0} files per chat |
| `RagUploadExceedsTotalSize` | Суммарный размер больше {0} МБ | Total size exceeds {0} MB |
| `RagClearConfirm` | Удалить все вложения и очистить индекс чата? | Remove all attachments and clear chat index? |
| `RagClearSuccess` | RAG-индекс чата очищен | Chat RAG index cleared |
| `RagDeleteAttachmentConfirm` | Удалить вложение «{0}»? | Delete attachment "{0}"? |
| `RagSourcesHeader` | Источники | Sources |
| `AdminTabKnowledgeBase` | База знаний | Knowledge Base |
| `RagReindexProjectDocs` | Обновить индекс проекта | Reindex project docs |
| `RagReindexSuccess` | Проиндексировано {0} чанков за {1} с | Indexed {0} chunks in {1} s |
| `RagSettingsButton` | Настройки RAG | RAG settings |
| `RagSettingsTitle` | Настройки RAG | RAG settings |
| `RagSettingsChunkStrategy` | Стратегия чанкинга | Chunking strategy |
| `RagSettingsChunkSize` | Размер чанка (токенов) | Chunk size (tokens) |
| `RagSettingsChunkOverlap` | Перекрытие (токенов) | Chunk overlap (tokens) |
| `RagSettingsMinScore` | Минимальный score | Min score |
| `RagSettingsEmbeddingModel` | Модель эмбеддингов | Embedding model |
| `RagSettingsAutoIndex` | Индексировать project docs при старте | Auto-index project docs on startup |
| `RagIndexColumnName` | Индекс | Index |
| `RagIndexColumnDocs` | Документов | Documents |
| `RagIndexColumnChunks` | Чанков | Chunks |
| `RagIndexColumnLastIndexed` | Последняя индексация | Last indexed |
| `RagIndexColumnActions` | Действия | Actions |
| `RagWorkspaceEnable` | Включить семантический поиск по workspace | Enable semantic search over workspace |
| `RagWorkspaceDisabled` | Отключён | Disabled |
| `RagWorkspaceEnabled` | Включён | Enabled |
| `RagWorkspaceReindex` | Переиндексировать | Reindex |
| `RagWorkspaceIndexing` | Индексация… | Indexing… |

**Итого:** 35 новых ключей × 2 `.resx` = 70 записей.

### § 9.2. JS-локализация

Через `data-*` (RULES § 4.17):

```html
<div id="chat-attachments-bar"
     data-label-attach="@Localizer["RagAttachButton"]"
     data-label-clear="@Localizer["RagClearButton"]"
     data-label-chunks-template="@Localizer["RagChunksCount"]"
     data-label-upload-success="@Localizer["RagUploadSuccess"]"
     ...>
```

`chat.js` читает `bar.dataset.labelAttach` и т.д.

---

## § 10. План работ (фазы 0-7)

### § 10.1. Общий таймлайн

| Фаза | Что | Оценка | Зависимости |
|:---:|---|:---:|---|
| **0** | DESIGN.md (этот документ) | — | ✅ **Done** |
| **1** | Embedding Service + `ILmStudioClient.GetEmbeddingsAsync` | 4 ч | — |
| **2** | Vector Store (InMemory) + `DocumentChunk` entity | 6 ч | Фаза 1 |
| **3** | Chunking Strategy (Recursive / Sentence / Fixed) | 4 ч | Фаза 2 |
| **4** | Document Parser (PlainText) + `DocumentIngestionService` | 6 ч | Фазы 2-3 |
| **5** | `IRetrievalService` + 3 Tools (`search_*`) | 5 ч | Фаза 4 |
| **6** | Attached Files (chat) + API + UI | 8 ч | Фаза 5 |
| **7** | Admin Knowledge Base UI + Profile Workspace UI | 6 ч | Фаза 5 |
| **8** | Тесты (unit + integration) + документация | 6 ч | Фазы 1-7 |

**Итого:** ~45 ч (≈5.5 рабочих дней).

### § 10.2. Детализация фаз

#### **Фаза 1 — Embedding Service** (4 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 1.1 | `DTO/LmStudio/EmbeddingResponse.cs` (новый) | — |
| 1.2 | `ILmStudioClient.GetEmbeddingsAsync` (расширение) | `LmStudioEmbeddingTests` (mock HTTP) |
| 1.3 | `IEmbeddingService` + `EmbeddingService` | `EmbeddingServiceTests` (6) |
| 1.4 | DI: `services.AddSingleton<IEmbeddingService, EmbeddingService>()` | — |
| 1.5 | `Rag:Embedding` в appsettings | — |

**DoD:** smoke — `curl /v1/embeddings` через `IEmbeddingService` даёт вектор [768].

#### **Фаза 2 — Vector Store** (6 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 2.1 | `DocumentChunk` entity + миграция `AddDocumentChunks` | — |
| 2.2 | `IVectorStore` + DTO (`ChunkMetadata`, `VectorSearchResult`) | — |
| 2.3 | `InMemoryVectorStore` (Singleton, ReaderWriterLockSlim) | `InMemoryVectorStoreTests` (10) |
| 2.4 | `VectorMath` helper (Cosine, L2Normalize) | `VectorMathTests` (5) |
| 2.5 | DI + Sqlite `.db` delete | — |

**DoD:** добавить 1000 векторов, найти top-5 за <50 мс.

#### **Фаза 3 — Chunking** (4 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 3.1 | `IChunkingStrategy` + `ChunkingOptions` | — |
| 3.2 | `RecursiveChunkingStrategy` (default) | 6 тестов |
| 3.3 | `SentenceChunkingStrategy` | 2 теста |
| 3.4 | `FixedChunkingStrategy` | 2 теста |
| 3.5 | `IChunkingStrategyResolver` + DI | 1 тест |
| 3.6 | Расширить `ITokenCounter` (`Encode`/`Decode`) | 2 теста |

**DoD:** чанк 500 токенов на RULES.md — не режет посреди абзаца.

#### **Фаза 4 — Parser + Ingestion** (6 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 4.1 | `IRagDocumentParser` + `ParsedDocument` | — |
| 4.2 | `PlainTextParser` (21 расширение, кодировки) | 8 тестов |
| 4.3 | `IRagDocumentParserRegistry` + DI | 3 теста |
| 4.4 | `IDocumentIngestionService` + `IngestionRequest`/`Result` | — |
| 4.5 | `DocumentIngestionService` (parse → chunk → embed → store) | 11 тестов |
| 4.6 | `Rag:Ingestion` + `Rag:Parsers` в appsettings | — |

**DoD:** загрузить RULES.md → 50+ чанков в `project_docs`; повторная загрузка с тем же hash → Skip.

#### **Фаза 5 — Retrieval + 3 Tools** (5 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 5.1 | `IRetrievalService` + `RetrievedChunkDto` | — |
| 5.2 | `RetrievalService` | 7 тестов |
| 5.3 | `SearchKnowledgeBaseTool` | 4 теста |
| 5.4 | `SearchChatHistoryTool` | 2 теста |
| 5.5 | `SearchWorkspaceTool` | 2 теста |
| 5.6 | Регистрация в Startup + Chat видит 10 tools | — |
| 5.7 | Smoke: «Что у нас в RULES про yield return?» → LLM вызывает tool | — |

**DoD:** LLM отвечает на вопрос по RULES с цитатой из чанка.

#### **Фаза 6 — Attached Files** (8 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 6.1 | `ChatAttachment` entity + миграция | — |
| 6.2 | `IChatAttachmentService` + `ChatAttachmentService` | 10 тестов |
| 6.3 | `ChatAttachmentsController` (4 endpoints) | 3 integration |
| 6.4 | `Rag:Attachments` в appsettings | — |
| 6.5 | Auto-inject top-K в `ChatStreamService.BuildMessagesAsync` | 3 теста |
| 6.6 | UI: 📎 кнопка + chips + [Очистить] | — |
| 6.7 | `chat.js`: загрузка файлов, state, обработчики | — |
| 6.8 | `.resx` + `chat.css` (12 ключей × 2 + CSS) | `LocalizationSyncTests` |

**DoD:** приложил PDF → задал вопрос → ответ с опорой на PDF.

#### **Фаза 7 — Admin + Profile UI** (6 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 7.1 | `AdminKnowledgeController` (6 endpoints) | 3 integration |
| 7.2 | `Admin.cshtml`: 8-я вкладка «Knowledge Base» | — |
| 7.3 | `admin-knowledge.js`: таблица индексов, настройки, просмотр чанков | — |
| 7.4 | `ProfileController`: workspace-index endpoints (5) | 2 integration |
| 7.5 | `profile.js`: карточка workspace + polling | — |
| 7.6 | `.resx` (12 ключей × 2) + CSS | `LocalizationSyncTests` |

**DoD:** admin видит 4 индекса, может обновить project_docs; profile — включить workspace index.

#### **Фаза 8 — Тесты + документация** (6 ч)

| Шаг | Что |
|---|---|
| 8.1 | Все unit-тесты зелёные (**+40 новых**) |
| 8.2 | Integration-тесты (2-3) |
| 8.3 | Smoke-чек: 7 сценариев (см. § 11) |
| 8.4 | README: раздел RAG |
| 8.5 | CHANGELOG `[1.5.0]` |
| 8.6 | KNOWN_ISSUES: KI-083 → Fixed, KI-086 → Active |
| 8.7 | RULES § 7 (KI-выжимка) + § 8 (история) |
| 8.8 | DESIGN.md → статус **Implemented** |

**DoD:** `dotnet test` — **~120/120**.

---

## § 11. Definition of Done (v1.5.0)

### § 11.1. Функциональные требования

- [ ] `IEmbeddingService` работает с LM Studio `/v1/embeddings` (768-dim).
- [ ] `InMemoryVectorStore` хранит и ищет по cosine за <50 мс на 10k чанков.
- [ ] `IChunkingStrategy` — 3 стратегии (recursive / sentence / fixed).
- [ ] `PlainTextParser` — 21 расширение, детект кодировок.
- [ ] `DocumentIngestionService` — parse → chunk → embed → store.
- [ ] 3 tool: `search_knowledge_base`, `search_chat_history`, `search_workspace`.
- [ ] Chat видит **10 инструментов** (7 + 3).
- [ ] Загрузка attachments в чат (📎, 1-5 файлов, ≤30 MB).
- [ ] Auto-inject top-K из `my_rag_docs` в system prompt.
- [ ] Кнопка «Очистить RAG» работает.
- [ ] Admin `/admin → Knowledge Base`: 4 индекса, reindex, настройки, просмотр чанков.
- [ ] Profile: включение / выключение workspace index.
- [ ] Локализация RU + EN (35 ключей).

### § 11.2. Нефункциональные

- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — ~120/120.
- [ ] CI + Docker Publish — зелёные.
- [ ] RULES § 7 + § 8 обновлены.
- [ ] CHANGELOG `[1.5.0] — YYYY-MM-DD`.
- [ ] README раздел «RAG» + API endpoints.
- [ ] DESIGN.md статус **Implemented**.
- [ ] Smoke — 7 сценариев:

| # | Smoke | Ожидание |
|---|---|---|
| 1 | «Что у нас в RULES § 4.26?» | LLM вызывает `search_knowledge_base` → цитата |
| 2 | Прикрепить `RULES.md` → «Найди про yield return» | Ответ с опорой на чанки |
| 3 | Кнопка «Очистить RAG» | my_rag_docs пуст, индикатор скрыт |
| 4 | Admin → переиндексировать project_docs | 200+ чанков, LastIndexed обновлён |
| 5 | Profile → включить workspace index | Через 30 сек — ChunkCount > 0 |
| 6 | Прикрепить PDF | Файл в чате, chunks > 0 |
| 7 | Запрос без RAG → LLM сама решает искать | `search_knowledge_base` в tool_call |

---

## § 12. Ограничения (осознанные)

| # | Ограничение | Причина | Решение (когда) |
|:---:|---|---|---|
| 1 | Vector store в RAM → embeddings теряются при рестарте | MVP, нет внешних зависимостей | Qdrant (v1.5.x) |
| 2 | Нет re-ranking (cross-encoder) | Сложно, требует отдельной модели | v1.5.x |
| 3 | Только `PlainTextParser` в MVP | Не тащим PDF/DOCX пакеты в первый релиз | Фаза 3.5 (v1.5.x) |
| 4 | OCR сканов PDF не поддерживается | Tesseract ~30 MB, не для MVP | v1.6+ |
| 5 | Embedding — один запрос на чанк (batch до 64) | Лимит LM Studio `/v1/embeddings` | — |
| 6 | Auto-inject top-K работает только для `my_rag_docs` | Для `project_docs` — LLM решает сама | — |
| 7 | Sources / citations — только сохраняются, UI в KI-086 (v1.6.0) | Отдельная фича | v1.6.0 |
| 8 | Multi-user KB (шаринг) | — | v1.6+ |
| 9 | Работа с большими файлами (>32 MB) — вручную | UI лимит | — |
| 10 | Индексация — синхронная (API блокируется) | Простота | Hangfire / фоновые задачи (v1.6+) |

---

## § 13. Ссылки

### § 13.1. KI

- **KI-083** — RAG / embeddings (этот документ).
- **KI-086** — Sources / citations (v1.6.0).
- **KI-049** — tiktoken (сделано, v1.4.1) — база для chunking.
- **KI-067** — UserSettings — база для per-user настроек RAG.
- **KI-052** — Multi-Agent — паттерн для 3 tools.

### § 13.2. Правила (RULES.md)

- § 1.14 — локализация (RU + EN).
- § 4.16 — camelCase ключи `.resx`.
- § 4.17 — JS-локализация через `data-*`.
- § 4.25 — Sqlite `EnsureCreated` не мигрирует.
- § 4.29 — InMemory + `ExecuteDeleteAsync`.
- § 4.32 — `Microsoft.ML.Tokenizers` — 2 пакета.

### § 13.3. Внешние источники

- [Anthropic RAG best practices](https://www.anthropic.com/news/contextual-retrieval)
- [Azure AI Search — Chunking](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-chunk-documents)
- [DLR research — Chunking strategies](https://arxiv.org/abs/2402.xxxx)
- [PdfPig](https://github.com/UglyToad/PdfPig) — Apache 2.0
- [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) — MIT
- [Qdrant](https://qdrant.tech/) — v1.5.x
- [text-embedding-nomic-embed-text-v1.5](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5)

### § 13.4. Внутренние документы

- `docs/development/v1.4/DESIGN.md` — эталон формата (Multi-Agent).
- `docs/development/RULES.md` — правила разработки (v1.4.8+).
- `docs/development/RELEASES.md` — процесс релиза.
- `docs/KNOWN_ISSUES.md` — реестр проблем.
- `CHANGELOG.md` — история версий.

---

**Конец DESIGN.md v1.5.0.**