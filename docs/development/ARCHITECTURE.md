# Архитектура IIChatTools

**Версия:** 1.7.0 (обновлено 2026-09-29)
**Статус:** Living document — обновляется при значимых архитектурных изменениях.
**Связанные документы:** [RULES.md](RULES.md), [RELEASES.md](RELEASES.md), [DESIGN v1.3](v1.3/DESIGN.md), [DESIGN v1.4](v1.4/DESIGN.md), [DESIGN v1.5 (RAG)](v1.5/DESIGN.md), [DESIGN v1.7 (Database Agent)](v1.7/DESIGN_DB_AGENT.md).

---

## § 1. Обзор

**IIChatTools** — серверное приложение на .NET 10 LTS, предоставляющее LLM (через LM Studio)
широкий набор безопасных инструментов: файловая система, выполнение кода, веб, Git/GitHub,
браузерная автоматизация, делегирование суб-агентам, **RAG (v1.5)**,
**Database Agent — read-only SQL (v1.7)**.

**Ключевая идея:** LLM работает в **изолированной песочнице** (`Workspace`) и не имеет
прямого доступа к системе. Все действия — через инструменты с подтверждениями (`Approvals`).

**Стек:**
- .NET 10 LTS (SDK 10.0.401)
- ASP.NET Core (Razor + JWT + Cookie)
- EF Core 10 (SqlServer / Sqlite / InMemory)
- LM Studio (OpenAI-совместимый API + `/v1/embeddings` в v1.5)
- PuppeteerSharp 7.1, Prometheus-net, `Microsoft.ML.Tokenizers` (tiktoken)
- **ADO.NET-провайдеры (v1.7)**: `Microsoft.Data.Sqlite` 10.0.12 +
  `Microsoft.Data.SqlClient` 6.1.6 — для `SqlConnectionProvider`

---

## § 2. Слои

```
┌─────────────────────────────────────────────────────────────────────┐
│              LM Studio (LLM) :8034                                  │
│  - POST /v1/chat/completions  (стриминг + tool calling)             │
│  - POST /v1/embeddings         (v1.5, RAG)                          │
└──────────────────────────▲──────────────────────────────────────────┘
                           │ HTTP (OpenAI-совместимый)
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│  IIChatTools.API  (net10.0)                                         │
│  ├── Controllers: Home, Auth, Tools, Approvals, Admin, AdminAgents, │
│  │                AdminKnowledge, AdminSqlAgent (v1.7),             │
│  │                Status, Chat, ChatStream, ChatView, ChatAttach,   │
│  │                Profile, ProfileWorkspace, Models                 │
│  ├── Views (Razor + RU/EN через IStringLocalizer<SharedResources>)  │
│  ├── ES-модули: api, ui, status, approvals, admin, admin-agents,    │
│  │              admin-knowledge, admin-sql-agent (v1.7),            │
│  │              test, chat, profile, profile-workspace              │
│  ├── Program.cs: ConfigureDefaultProxy + миграции + LoadSubAgent    │
│  │              + LoadSqlAgentOverrides (v1.7)                      │
│  └── Startup.cs: DI + 50 инструментов + Chat services               │
└──────────────────────────▲──────────────────────────────────────────┘
                           │ DI
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│  IIChatTools.Services  (net10.0)                                    │
│  ├── ToolRegistry (50 = 40 raw + 6 агентов + 3 RAG + 1 SqlAgent)    │
│  ├── SubAgentRegistry (Singleton, v1.4.0)                           │
│  ├── Chat: ChatService, ChatStreamService, ChatApprovalCoordinator, │
│  │         ChatTitleService, ChatRetentionService                   │
│  ├── RAG (v1.5): EmbeddingService, InMemoryVectorStore,             │
│  │              DocumentIngestionService, RetrievalService          │
│  ├── SqlAgent (v1.7): SqlAgentService, SqlQueryValidator,           │
│  │              SqlConnectionProvider, SqlAgentOptionsProvider,     │
│  │              AdminSqlAgentService, AppPathProvider               │
│  ├── LmStudioClient (Singleton — SSE + tools + ModelOverride)       │
│  ├── Cross-cutting: Audit, Approval, Workspace, Browser, Token      │
│  ├── Tools/ (9 групп: FileSystem, CodeExecution, Web, Git, GitHub,  │
│  │           Browser, SubAgent, SqlAgent, Utils)                    │
│  ├── Tools/SubAgent/ (AgentToolBase + 6 наследников)                │
│  └── Tools/SqlAgent/ (DatabaseAgentTool — top-level ITool)          │
└──────────────────────────▲──────────────────────────────────────────┘
                           │ EF Core 10
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│  IIChatTools.Data  (net10.0)                                        │
│  ├── Identity: ApplicationUser (Admin, User)                        │
│  ├── Chat: Chat + ChatMessage (v1.3)                                │
│  ├── RAG: DocumentChunk + ChatAttachment (v1.5)                     │
│  └── Cross-cutting: AuditLog, AppSetting, PendingAction,            │
│                    AgentState, MemoryEntry, UserSetting (v1.4)      │
└──────────────────────────▲──────────────────────────────────────────┘
                           │
        ┌──────────────────┼──────────────────┐
        ▼                  ▼                  ▼
   SqlServer          Sqlite / InMemory    logs/audit/*.jsonl
   (prod)             (dev / test)         (опционально)
```

**Принцип слоистости:** `API → Services → Data`. Обратных зависимостей нет:
- `AppVersionHolder` разрывает `Services → API` (передача версии).
- `Func<ISubAgentService>` разрывает DI-цикл `SubAgent` ↔ `ToolRegistry`.
- `IEmbeddingService` (v1.5) — Singleton, используется и в Chat, и в Ingestion.

---

## § 3. Схема БД

### § 3.1. Identity

| Entity | Назначение |
|---|---|
| `ApplicationUser : IdentityUser<int>` | Пользователь (+ `FullName`, `IsActive`, `RegisteredAt`) |
| `IdentityRole<int>` | Роли: `Admin`, `User` (SeedData) |
| `IdentityUserRole<int>` | Связь user ↔ role |

### § 3.2. Chat (v1.3+)

| Entity | Ключевые поля | Индексы |
|---|---|---|
| `Chat` | `UserId`, `Title`, `Model`, `SystemPrompt`, `UpdatedAt` | `(UserId, UpdatedAt)` |
| `ChatMessage` | `ChatId`, `Role`, `Content`, `ToolCallsJson`, `ToolCallId`, `ToolName`, `TokensIn/Out`, `DurationMs`, `FirstTokenMs`, `FinishReason` | `(ChatId, CreatedAt)` |

### § 3.3. RAG (v1.5.0)

| Entity | Ключевые поля | Индексы |
|---|---|---|
| `DocumentChunk` | `IndexName`, `ChatId`, `UserId`, `DocumentPath`, `DocumentHash`, `ChunkIndex`, `Text`, `Tokens`, `MetadataJson` | `(IndexName, DocumentHash)`, `(IndexName, ChatId, UserId)` |
| `ChatAttachment` | `ChatId`, `UserId`, `FileName`, `ContentType`, `SizeBytes`, `StoragePath`, `ContentHash`, `ChunksCount` | `(ChatId)`, `(UserId, ContentHash)` |

### § 3.4. Cross-cutting

| Entity | Назначение |
|---|---|
| `AuditLog` | Аудит действий (tool calls, approvals, admin actions) |
| `AppSetting` | Настройки (ключ-значение) — в т.ч. overrides агентов |
| `UserSetting` | Per-user настройки (v1.4, retention чатов) |
| `PendingAction` | Ожидающие подтверждения (для `/test`) |
| `AgentState` | Состояние сессии суб-агента |
| `MemoryEntry` | Долговременная память пользователя (planner_agent) |

### § 3.5. Миграции

| Миграция | Версия | Что |
|---|---|---|
| `InitialSqlServer` | v1.1.0 | Начальная схема (для SqlServer) |
| `AddChatAndChatMessages` | v1.3.0 | Chat + ChatMessage |
| `AddUserSettings` | v1.4.1 | UserSetting |
| `AddChatMessageStats` | v1.4.1 | +3 поля в ChatMessage |
| `AddDocumentChunks` | v1.5.0 | DocumentChunk |
| `AddChatAttachments` | v1.5.0 | ChatAttachment |

**Sqlite (dev):** `EnsureCreatedAsync` — не мигрирует. При изменении модели — удалять `.db` (RULES § 4.25, KI-070).

**Команды для создания миграций (отдельные папки по провайдеру):**

# SqlServer (используется по умолчанию)
dotnet ef migrations add <Name> --project IIChatTools.Data --startup-project IIChatTools.API --output-dir Migrations/SqlServer

# Sqlite (запланировано, KI-070)
dotnet ef migrations add <Name> --project IIChatTools.Data --startup-project IIChatTools.API --output-dir Migrations/Sqlite

# Применение
dotnet ef database update --project IIChatTools.Data --startup-project IIChatTools.API

---

## § 4. DI-контейнер (ключевые lifetime)

| Сервис | Lifetime | Почему |
|---|---|---|
| `IToolRegistry` | **Scoped** | Каждый запрос — свой экземпляр (KI-075 — log spam понижен до Debug) |
| `ISubAgentRegistry` | **Singleton** | Читается из appsettings один раз; `Update`/`Reset` через `AppSettings` |
| `IChatApprovalCoordinator` | **Singleton** | Связывает SSE-стрим и REST-endpoint в разных HTTP-scope |
| `IChatStreamService` | **Scoped** | Один запрос — один стрим |
| `IChatService` | **Scoped** | Через `AppDbContext` |
| `ILmStudioClient` | **Singleton** | Stateless + `HttpClientFactory` (с v1.5 — из-за совместимости с `EmbeddingService`) |
| `IAuditService` | **Scoped** | Через `AppDbContext` |
| `ITokenCounter` (v1.4.1) | **Singleton** | Тяжёлая инициализация токенизатора |
| `IEmbeddingService` (v1.5) | **Singleton** | Stateless + кэш |
| `IVectorStore` (v1.5) | **Singleton** | In-memory индекс (MVP) |
| `IRetrievalService` (v1.5) | **Scoped** | Тонкая обёртка |
| `SqlAgentOptionsProvider` (v1.7) | **Singleton** | Baseline + runtime overrides подключений |
| `ISqlConnectionProvider` (v1.7) | **Singleton** | Stateless фабрика `DbConnection` (кэш connection strings) |
| `ISqlQueryValidator` (v1.7) | **Singleton** | Stateless валидатор SQL |
| `ISqlAgentService` (v1.7) | **Scoped** | Оркестратор 4 операций Database Agent |
| `IAdminSqlAgentService` (v1.7) | **Scoped** | Persist override в `AppSettings` + runtime |
| `IAppPathProvider` (v1.7) | **Singleton** | `ContentRootPath` (KI-100 — относительный Sqlite-путь) |
| `IBrowserSessionManager` | **Singleton** | Кэш Puppeteer-сессий per-user |
| `BackgroundService`s | **Singleton** (HostedService) | AuditRetention, ChatRetention, MetricsRefresh |

**Background services** (`BackgroundService`):
- `AuditRetentionService` — чистит `AuditLogs` + JSONL-файлы.
- `ChatRetentionService` — чистит `Chats` по retention policy (v1.3+).
- `MetricsRefreshBackgroundService` — обновляет `PendingApprovals`, `ActiveUsers` (Prometheus).

---

## § 5. Поток запроса Chat (end-to-end)

### § 5.1. Отправка сообщения


1. User → POST /api/chat/stream  { chatId, message, useTools: true }
   ↓
2. ChatStreamController.StreamAsync:
   - SSE-заголовки (text/event-stream, X-Accel-Buffering: no)
   - await foreach (evt in ChatStreamService.StreamAsync(...))
   - WriteEventAsync(evt): "event: {type}\ndata: {json}\n\n" + flush
   ↓
3. ChatStreamService.StreamAsync:
   a. Валидация + проверка владения чатом
   b. Сохранение user-сообщения (TokensIn = tiktoken, v1.4.1)
   c. yield ChatStreamEvent.Start(userMsgId, chatId, userTokens)
   d. BuildMessagesAsync (history + system prompt + RAG-inject v1.5)
   e. Формирование tools (11 в v1.7: 6 агентов + consult + 3 RAG + database_agent)
   f. Multi-turn loop (до 5 итераций):
      - LM Studio ChatStreamAsync (SSE)
      - delta → yield Delta
      - tool_call → yield ToolCall → approval? → ExecuteAsync → yield ToolResult
      - assistant без tool_calls → финальный ответ → yield Done
   ↓
4. LM Studio → StreamAsync → HttpContext.Response (SSE)
   ↓
5. Browser: chat.js readSseStream() → handleSseEvent() → DOM update


### § 5.2. Approvals (v1.3 Фаза 1.7)


tool_call (RequiresApprovalByDefault = true)
   ↓
yield ToolApprovalRequired({ callId, name, arguments, expiresAt })
   ↓
ChatApprovalCoordinator.WaitForDecisionAsync(callId, 5 min)
   ↓ (ждёт)
User → POST /api/chat/approvals/{callId}/approve
   ↓
ChatApprovalCoordinator.ResolveAsync(callId, Approved)
   ↓
WaitForDecisionAsync разбужен → ExecuteAsync инструмента → yield ToolResult


**Singleton `ChatApprovalCoordinator`** — `ConcurrentDictionary<string, TaskCompletionSource<ChatApprovalDecision>>`.
- `RunContinuationsAsynchronously` — защита от deadlock.
- Cleanup в `finally` — защита от утечки (KI-043).

### § 5.3. Regenerate / Retry / Stop

| Сценарий | Endpoint | Логика |
|---|---|---|
| **Regenerate** | `POST /api/chat/regenerate` | Удалить последний assistant-exchange → стрим заново |
| **Retry после Stop** | `regenerateLastMessage({ allowNoAssistant: true })` | Best-effort удаление, если assistant отсутствует (KI-065) |
| **Stop** | `abortController.abort()` | Рвёт SSE → `CancellationToken` → `OperationCanceledException` → audit `Cancelled` (2.1.3.3) |
| **Edit user-message** | `POST /api/chat/messages/{id}/edit` | Обновить content + удалить всё после → regenerate (2.2.6) |

---

## § 6. Инструменты

### § 6.1. Группы (50 = 40 raw + 6 агентов + 3 RAG + 1 SqlAgent)

| Группа | Кол-во | Требуют approval |
|---|:---:|:---:|
| Файловая система | 13 | 7 |
| Выполнение кода | 3 | 3 |
| Веб | 3 | 0 |
| Git | 7 | 4 |
| GitHub | 7 | 2 |
| Браузер (PuppeteerSharp) | 4 | 1 |
| Суб-агенты (`consult_secondary_agent`) | 1 | 1 |
| Утилиты | 2 | 0 |
| **Raw-инструменты** | **40** | **18** |
| + Агенты (v1.4.0) | +6 | (по агенту) |
| + RAG (v1.5.0) | +3 | — |
| + SqlAgent (v1.7.0) | +1 | ✅ |
| **Итого (ToolRegistry)** | **50** | — |

### § 6.2. Multi-Agent (v1.4.0)

Chat видит **11 инструментов** в v1.7.0: 6 агентов + `consult_secondary_agent`
+ 3 RAG-tool + `database_agent` (см. § 6.3 и § 6.4).

| Агент | Инструментов | Модель | Approval |
|:---|:---:|:---:|:---:|
| `file_system_agent` | 13 | `qwen/qwen3-4b-2507` | ✅ |
| `code_agent` | 3 | `gemma-4-12b-coder...` | ✅ |
| `web_agent` | 3 | `qwen/qwen3-4b-2507` | ❌ |
| `git_agent` | 7 | `qwen/qwen3-4b-2507` | ✅ |
| `github_agent` | 7 | `qwen/qwen3-4b-2507` | ✅ |
| `planner_agent` | 2 | `gemma-4-12b-coder...` | ❌ |
| `consult_secondary_agent` | browser + fallback | `LmStudio:Model` | ✅ |

**Зачем:** одна модель (особенно 4B) плохо выбирает из 40 инструментов.
Внутри агента — узкий набор + свой system prompt → точнее выбор.

### § 6.3. RAG-инструменты (v1.5.0)

Chat видит **3 RAG-tool** (в дополнение к агентам и `database_agent`):
- `search_knowledge_base` — глобальные docs проекта.
- `search_chat_history` — история чатов пользователя.
- `search_workspace` — семантический поиск по workspace (opt-in).

Все три — read-only (`RequiresApprovalByDefault = false`).

### § 6.4. SqlAgent (v1.7.0)

Chat видит **1 top-level инструмент** `database_agent` (не наследник
`AgentToolBase` — DESIGN_DB_AGENT § 4.1):

| Action | Назначение | Approval |
|:---|:---|:---:|
| `list_databases` | Список подключений (метаданные) | — |
| `list_tables` | Whitelist-таблицы + row count | — |
| `describe_table` | Колонки + типы + пример значения | — |
| `execute_query` | Read-only SQL (`SELECT`/`WITH`), auto-LIMIT | ✅ |

**5 уровней безопасности** (DESIGN_DB_AGENT § 3.3 и § 6):

1. **Read-only роль в БД** (`Mode=ReadOnly` для Sqlite; `db_datareader` +
   `DENY INSERT/UPDATE/DELETE` для SqlServer) — самый надёжный барьер.
2. **`ISqlQueryValidator`** — `SELECT`/`WITH` only, запрет keywords
   (`INSERT`, `DELETE`, `DROP`, ...), запрет функций (`load_extension`,
   `readfile`), single-statement.
3. **Whitelist / blacklist таблиц** — `AllowedTables` / `DeniedTables`
   в конфиге (Denied перебивает).
4. **Timeout + Auto-LIMIT** — 15 сек на запрос, ≤ 100 строк по умолчанию.
5. **Approval + Audit** — per-action approval (KI-101) для `execute_query`;
   все вызовы — в `AuditLogs` (`admin.sqlagent.connection.*`).

**Управление runtime:** `AdminSqlAgentService` + `/admin → SQL Agent`
(9-я вкладка). Изменения whitelist / MaxRows / Timeout / Enabled
сохраняются в `AppSettings` (`SqlAgent.{name}.{field}`) и сразу применяются
к `SqlAgentOptionsProvider` — без перезапуска. При старте — восстанавливаются
через `Program.LoadSqlAgentOverrides`.

---

## § 7. Внешние зависимости

| Зависимость | Роль | Fallback |
|---|---|---|
| **LM Studio** :8034 | LLM + embeddings | `/api/models` возвращает default |
| **SqlServer** (prod) | БД | — |
| **Sqlite** (dev/test) | БД | Встроен |
| **PuppeteerSharp** | Браузер | Chromium/Edge авто-детект |
| **Git CLI** | git-инструменты | — |
| **GitHub CLI `gh`** | gh-инструменты | — |
| **Python 3** | `run_python` | — |
| **Node.js** | `run_javascript` | — |
| **Prometheus** | Метрики | `/metrics` публичный |

---

## § 8. Известные архитектурные решения (ADR-style)

### ADR-001. `AppVersionHolder` — разрыв слоёв
**Проблема:** `Services` не должен зависеть от `API`.
**Решение:** `AppVersionHolder.Current` устанавливается в `Program.cs` при старте.

### ADR-002. `Func<ISubAgentService>` — разрыв DI-цикла
**Проблема:** `ToolRegistry → ITool → ISubAgentService → IToolRegistry`.
**Решение:** инжектить `Func<ISubAgentService>` (ленивая фабрика).

### ADR-003. `ChatApprovalCoordinator` — Singleton с TCS
**Проблема:** SSE-стрим и REST-endpoint в разных scope.
**Решение:** Singleton + `ConcurrentDictionary<callId, TaskCompletionSource>`.
- `RunContinuationsAsynchronously` — защита от deadlock (RULES § 4.23).
- Cleanup в `finally` — защита от утечки (KI-043).

### ADR-004. SubAgent per-user model override
**Проблема:** разные типы задач требуют разных моделей.
**Решение:** `SubAgentTaskRequest.ModelOverride` + `SubAgentDescriptor.Model`.
Chat-модель остаётся `LmStudio:Model`.

### ADR-005. InMemory + Qdrant (v1.5)
**Проблема:** Qdrant — внешняя зависимость.
**Решение:** MVP — InMemory (`IVectorStore`), v1.5.x — Qdrant (тот же интерфейс).

### ADR-006. `ChatStreamService.StreamAsync` — `yield return` вне `try-catch`
**Проблема:** CS1631 (yield в try-catch запрещён).
**Решение:** ошибки в локальные переменные → `yield return` после блока.

### ADR-007. SSE — camelCase + `DateTimeZoneHandling.Utc`
**Проблема:** Sqlite возвращает `DateTime Kind=Unspecified`, JS парсит как local (KI-071).
**Решение:** `SseJsonSettings.DateTimeZoneHandling = Utc` + camelCase.

### ADR-008. tiktoken через `Microsoft.ML.Tokenizers` (v1.4.1)
**Проблема:** LM Studio не отдаёт usage в stream-режиме (KI-049).
**Решение:** `ITokenCounter` + `Cl100kBase` (2 пакета — API + Data).

### ADR-009. Rate Limiting — собственный middleware (KI-042)
**Проблема:** `Microsoft.AspNetCore.RateLimiting` недоступен в SDK 10.0.401.
**Решение:** `RateLimitingMiddleware` на `System.Threading.RateLimiting`.

### ADR-010. Per-user retention через `UserSetting` (v1.4.1)
**Проблема:** глобальный retention не учитывает предпочтения.
**Решение:** таблица `UserSetting` (ключи `Chat.RetentionDays`, `Chat.DoNotDelete`).
`ChatRetentionService` читает overrides + исключает `DoNotDelete`.

### ADR-011. `SqlAgentOptionsProvider` — baseline + runtime overrides (v1.7.0)
**Проблема:** админ должен менять whitelist таблиц для Database Agent
**без перезапуска** приложения. Также нужно при старте восстанавливать
сохранённые ранее override'ы.
**Решение:** Singleton `SqlAgentOptionsProvider`:
- **baseline** — из `IOptions<SqlAgentOptions>` (appsettings.json);
- **runtime overrides** — `Dictionary<string, SqlAgentConnectionOptions>`,
  заполняется через `UpdateConnection` / `UpdateEnabled` / `Reset`;
- все читатели (`ISqlConnectionProvider`, `ISqlQueryValidator`,
  `SqlAgentService`) вызывают `Get(name)` **при каждом запросе** (без кэша).
- Потокобезопасность — `lock` (методы редкие, contention низкий).
- Persist — в `AppSettings` (`SqlAgent.{name}.{field}` — **отдельными
  ключами**, не одним JSON'ом, как `SubAgents.*`). Аналогично — `Program.LoadSqlAgentOverrides`.

### ADR-012. Default interface method для per-action approval (v1.7.0)
**Проблема:** `DatabaseAgentTool` требует approval **только** для `execute_query`
(метаданные — read-only, approval = лишний клик). Но `RequiresApprovalByDefault`
в `ChatStreamService` — **свойство всего tool**, не per-call.
**Решение:** в `ITool` добавлен default-метод
`RequiresApprovalForCall(JObject args)` с fallback на `RequiresApprovalByDefault`.
Все 46 существующих инструментов работают без изменений (C# 8+
default interface method). `DatabaseAgentTool` переопределяет:
`action == "execute_query"`. См. KI-101, RULES § 4.46.

---

## § 9. Ссылки

### Дизайн-документы фаз
- [DESIGN v1.3](v1.3/DESIGN.md) — Chat UI
- [DESIGN v1.4](v1.4/DESIGN.md) — Multi-Agent
- [DESIGN v1.4 Sidebar/Search](v1.4/DESIGN_SIDEBAR_SEARCH.md) — UX polish
- [DESIGN v1.5](v1.5/DESIGN.md) — RAG / Knowledge Base (✅ Done)
- [DESIGN v1.7](v1.7/DESIGN_DB_AGENT.md) — Database Agent (✅ Done)

### Правила и процессы
- [RULES.md](RULES.md) — правила разработки (v1.4.20)
- [RELEASES.md](RELEASES.md) — чек-лист релиза
- [PROMPT_V2.md](PROMPT_V2.md) — стартовый промпт для новых чатов

### Реестры
- [`../KNOWN_ISSUES.md`](../KNOWN_ISSUES.md) — реестр проблем
- [`../../CHANGELOG.md`](../../CHANGELOG.md) — история версий
- [`../../README.md`](../../README.md) — обзор проекта

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.7.0**
