# DESIGN — Background Jobs + Todo Tracking (v2.1.0)

**Версия:** v1.0 (Draft)
**Дата:** 2026-10-19
**Статус:** Draft — ждёт согласования перед реализацией.
**Релиз-цель:** v2.1.0 (Minor — фоновая работа + планирование задач).
**Связанные документы:** [RULES.md](../RULES.md) · [ARCHITECTURE.md](../ARCHITECTURE.md) · [DESIGN v2.0 Orchestrator-Worker](../v2.0/DESIGN_ORCHESTRATOR_WORKER.md) · [DESIGN v1.12 Vision Agent](../v1.12/DESIGN_VISION_AGENT.md) · [KNOWN_ISSUES.md](../../KNOWN_ISSUES.md) · KI-256 (browser smoke), KI-257…260 (предлагаемые, § 12).

---

## § 0. Резюме

Сейчас длинные операции выполняются **синхронно внутри SSE-стрима чата**:

- `orchestrate_task` держит стрим **10–15 минут**;
- `vision_agent(run_task)` — до **900 секунд** (`VisionAgent:Limits:MaxTaskSeconds`);
- пользователь не может задать новый вопрос, пока инструмент не вернёт `ToolResult`;
- F5/Stop обрывает HTTP-соединение и вместе с ним — `CancellationToken` инструмента;
- у LLM нет явного плана задачи: она держит шаги «в голове» и теряет их в длинном контексте.

**v2.1.0 вводит два независимых, но связанных механизма:**

1. **Background Jobs** — долгие операции выполняются в фоновом раннере, а чат получает `jobId` сразу. Статус, прогресс, результат и отмена доступны через инструменты, REST и SSE.
2. **Todo Tracking** — плоский чек-лист шагов, привязанный к чату. LLM составляет план, отмечает прогресс, UI показывает панель.

**Ключевые решения (кратко):**

| Решение | Выбор |
| :--- | :--- |
| Очередь | `Channel<int>` (bounded, 100) + `BackgroundService` |
| Состояние | БД `BackgroundJobs` — source of truth; очередь только доставка |
| Прогресс | `IJobProgressSink` + мост из существующих SSE-событий |
| Отмена | `CancellationTokenSource` на job + `CancelRequestedAt` в БД |
| Перезапуск | `Running → Failed`, `Pending → повторный enqueue` (не durable queue) |
| Retention | 24 часа для терминальных задач; очистка раз в час |
| Лимиты | 3 активных job на пользователя, 2 параллельных глобально |
| Todo | отдельная сущность `ChatTodo`, 3 tool, панель над лентой |
| Новые tool | 6 (3 job + 3 todo), Chat: **19 → 25**, ToolRegistry: **66 → 72** |

---

## § 1. Контекст и проблема

### § 1.1. Как работает сейчас

```
Browser ──POST /api/chat/stream──► ChatStreamController
                                      │
                                      ▼
                            ChatStreamService.StreamAsync
                                      │  tool_call (orchestrate_task)
                                      ▼
                            ExecuteToolWithStreamingAsync
                                      │  await toolTask (10–15 мин)
                                      ▼
                            OrchestrateTaskTool.ExecuteAsync
                                      │  SSE: orchestrator_started/round/completed
                                      ▼
                            ToolResult → LLM → done
```

`ChatStreamService` ждёт `ToolResult`, удерживая HTTP-соединение и `CancellationToken` запроса. Пока инструмент работает, новый запрос в том же чате технически возможен, но UI занят стримом; F5/Stop убивает токен, и инструмент завершается с `OperationCanceledException` (частичный результат теряется).

### § 1.2. Что болит

| # | Проблема | Последствие |
| :---: | :--- | :--- |
| 1 | Долгий tool блокирует SSE | Нельзя общаться, пока идёт Orchestrator/Vision |
| 2 | F5/Stop = отмена | Случайное обновление страницы убивает 15-минутную работу |
| 3 | Нет статуса после отключения | Пользователь не знает, завершилась ли задача |
| 4 | Нет фонового прогресса | Только «крутится спиннер» в одном tool-блоке |
| 5 | Нет плана | LLM забывает шаги, todo нет |
| 6 | Нет переиспользования результата | Результат живёт только в контексте одного ответа |

### § 1.3. Требования

- **R1.** `start_background_job` возвращает `jobId` за < 1 с; работа продолжается после закрытия SSE.
- **R2.** `get_job_status` отдаёт статус/прогресс/результат; `cancel_job` отменяет свою задачу.
- **R3.** Прогресс виден в UI: старт → % → результат/ошибка.
- **R4.** Результат сохраняется в БД и переживает F5; TTL — 24 часа.
- **R5.** Todo привязаны к чату; LLM создаёт/обновляет их через tools; UI показывает панель.
- **R6.** 3 активных job на пользователя; очередь не растёт бесконечно.
- **R7.** Никакого нового top-level «универсального исполнителя»: только 3 типа job, каждый — обёртка над существующим tool.

---

## § 2. Цели и не-цели

### § 2.1. Цели

- Фоновое выполнение `orchestrate_task`, `vision_agent(run_task)`, `browser_session_control(run_smoke)`.
- Персистентный статус, прогресс, результат, ошибка, отмена.
- SSE-панель активных задач + REST для F5.
- Todo-план чата + панель + правила в `DefaultSystemPrompt`.
- Аккуратная деградация: перезапуск сервера, отмена, переполнение очереди.

### § 2.2. Не-цели (v2.1.0)

- Распределённая/durable очередь (RabbitMQ, Hangfire, Quartz) — см. KI-257.
- Мульти-нодовый раннер и горизонтальное масштабирование.
- Фоновые задачи без чата (web-кабинет `/jobs`) — только API-совместимость.
- Произвольный запуск любого `ITool` по имени — только whitelist трёх типов.
- Вложенные todo (подзадачи подзадач) и зависимости между todo.
- Push-уведомления (email/telegram).

---

## § 3. Архитектура

### § 3.1. Компоненты

```
┌──────────────────────────── Chat UI ────────────────────────────┐
│  Панель Jobs (SSE /api/jobs/{id}/stream)   Панель Todos          │
└───────────────▲──────────────────────────────▲──────────────────┘
                │ job_started (chat SSE)        │ todo_* (tool_result)
┌───────────────┴──────────────────────────────┴──────────────────┐
│ ChatStreamService → StartBackgroundJobTool / TodoTools           │
│        │                    │                                    │
│        │            IBackgroundJobService (Scoped) ──► AppDbContext
│        │                    │                                    │
│        │            IBackgroundJobQueue (Singleton, Channel)     │
│        ▼                    ▼                                    │
│ BackgroundJobRunner (HostedService)                              │
│   └─ scope per job → IToolRegistry → orchestrate_task / vision_agent
│                                      / browser_session_control   │
│        │                                                        │
│        ├─ IJobProgressSink ──► IJobEventHub (Singleton) ──► SSE  │
│        └─ IJobCancellationRegistry (Singleton)                   │
└──────────────────────────────────────────────────────────────────┘
```

### § 3.2. DI-lifetimes

| Компонент | Lifetime | Почему |
| :--- | :--- | :--- |
| `IBackgroundJobService` | **Scoped** | Работает с `AppDbContext` (scoped) |
| `IBackgroundJobQueue` | **Singleton** | `Channel<int>` живёт всё приложение |
| `BackgroundJobRunner` | **HostedService (Singleton)** | Один consumer-пул |
| `IJobEventHub` | **Singleton** | Подписчики SSE из разных запросов |
| `IJobCancellationRegistry` | **Singleton** | `CancellationTokenSource` на job |
| `IJobConcurrencyLimiter` | **Singleton** | Атомарный счётчик per-user |
| `BackgroundJobRetentionService` | **HostedService** | Периодическая очистка |
| `BackgroundJobRecoveryService` | **HostedService** | Восстановление после рестарта |
| `StartBackgroundJobTool` и Todo-tools | **Scoped** | Как все `ITool` |

### § 3.3. Почему `Channel<T>`, а не `ConcurrentQueue` / `BlockingCollection`

- `Channel` даёт **async backpressure** (`ReadAllAsync`, `WaitToReadAsync`) без блокировки потока.
- Bounded-канал ограничивает очередь и позволяет `TryWrite` вернуть `false` → `start_background_job` сразу отдаёт ошибку, а не копит задачи.
- `Channel` — стандартный примитив .NET; не тащит внешнюю зависимость (в проекте уже используются каналы для SSE, `ChatStreamService`).
- `BlockingCollection` блокирует поток; `ConcurrentQueue` не умеет ждать без polling.

---

## § 4. Сущности БД

### § 4.1. `BackgroundJob`

```csharp
using System;
using IIChatTools.Data.Entities;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Фоновая задача (v2.1.0, Background Jobs).
    /// </summary>
    public class BackgroundJob : BaseEntity
    {
        /// <summary>Владелец задачи.</summary>
        public int UserId { get; set; }

        /// <summary>Чат, из которого запущена задача (null — API/вне чата).</summary>
        public int? ChatId { get; set; }

        /// <summary>Orchestrator | Vision | Smoke.</summary>
        public string Type { get; set; }

        /// <summary>Pending | Running | Completed | Failed | Cancelled.</summary>
        public string Status { get; set; }

        /// <summary>0…100; монотонно растёт.</summary>
        public int Progress { get; set; }

        /// <summary>Человекочитаемое описание текущего шага (для UI).</summary>
        public string ProgressMessage { get; set; }

        /// <summary>
        /// JSON параметров запуска. Нужен для:
        /// 1) отображения в UI; 2) восстановления Pending после рестарта;
        /// 3) аудита. Ограничение — 64 KB (валидируется сервисом).
        /// </summary>
        public string ParametersJson { get; set; }

        /// <summary>JSON результата (санитизированный, ≤ 256 KB).</summary>
        public string ResultJson { get; set; }

        /// <summary>Текст ошибки для Failed.</summary>
        public string Error { get; set; }

        /// <summary>Время фактического старта (null у Pending).</summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>Время завершения (Completed/Failed/Cancelled).</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>Когда пользователь запросил отмену (для UI «Отмена…»).</summary>
        public DateTime? CancelRequestedAt { get; set; }

        /// <summary>Навигация на пользователя.</summary>
        public virtual ApplicationUser User { get; set; }

        /// <summary>Навигация на чат (nullable).</summary>
        public virtual Chat Chat { get; set; }
    }
}
```

> **Отличие от постановки:** добавлены `ParametersJson`, `ProgressMessage`,
> `CancelRequestedAt`. Без `ParametersJson` невозможно восстановить Pending
> после рестарта и показать параметры в UI; без `ProgressMessage` прогресс
> будет только числом. Это осознанное расширение, а не «лишние поля».

### § 4.2. `ChatTodo`

```csharp
using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Пункт плана чата (v2.1.0, Todo Tracking).
    /// </summary>
    public class ChatTodo : BaseEntity
    {
        /// <summary>Чат-владелец (каскадное удаление).</summary>
        public int ChatId { get; set; }

        /// <summary>Короткий заголовок (1…200).</summary>
        public string Title { get; set; }

        /// <summary>Опциональное описание (≤ 2000).</summary>
        public string Description { get; set; }

        /// <summary>Pending | InProgress | Completed.</summary>
        public string Status { get; set; }

        /// <summary>
        /// Порядок в плане (0-based, уникален в рамках чата).
        /// В БД — колонка SortOrder, НЕ Order: ORDER — reserved word SQL.
        /// Сервис выдаёт max+1; при гонке двух add_todo — retry.
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>Время выполнения (для Completed).</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>Навигация на чат.</summary>
        public virtual Chat Chat { get; set; }
    }
}
```

> **`Order` vs `SortOrder`.** В API/JSON поле называется `order` (как в
> постановке), в C#/БД — `SortOrder`. Причина: `ORDER` — зарезервированное
> слово SQL; EF Core его экранирует, но raw SQL/CLI-запросы и будущие
> провайдеры ломаются. Маппинг: `entity.Property(e => e.SortOrder).HasColumnName("SortOrder")`,
> DTO — `order = todo.SortOrder`.

### § 4.3. Константы статусов

```csharp
public static class BackgroundJobTypes
{
    public const string Orchestrator = "Orchestrator";
    public const string Vision       = "Vision";
    public const string Smoke        = "Smoke";
}

public static class BackgroundJobStatuses
{
    public const string Pending   = "Pending";
    public const string Running   = "Running";
    public const string Completed = "Completed";
    public const string Failed    = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class ChatTodoStatuses
{
    public const string Pending    = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed  = "Completed";
}
```

Статусы — **строки**, как в `AgentDebateSession`/`AgentDebateRound`. Причины:
- существующая схема уже использует строки + `HasMaxLength`;
- JSON/SSE отдаёт стабильные значения без `enum.ToString()`-сюрпризов;
- проще добавлять `Cancelling`/`TimedOut` без миграции enum.

### § 4.4. EF Core-конфигурация

```csharp
// AppDbContext.cs
public DbSet<BackgroundJob> BackgroundJobs { get; set; }
public DbSet<ChatTodo> ChatTodos { get; set; }

modelBuilder.Entity<BackgroundJob>(entity =>
{
    entity.ToTable("BackgroundJobs");
    entity.HasKey(e => e.Id);

    entity.Property(e => e.Type).HasMaxLength(30).IsRequired();
    entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
    entity.Property(e => e.ProgressMessage).HasMaxLength(500);
    entity.Property(e => e.ParametersJson);      // nvarchar(max) / TEXT
    entity.Property(e => e.ResultJson);          // nvarchar(max) / TEXT
    entity.Property(e => e.Error);               // nvarchar(max) / TEXT

    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    // Чат удалили, а job ещё нужен для истории/аудита → SetNull,
    // а не Cascade. Retention удалит его через 24 ч.
    entity.HasOne(e => e.Chat)
        .WithMany()
        .HasForeignKey(e => e.ChatId)
        .IsRequired(false)
        .OnDelete(DeleteBehavior.SetNull);

    entity.HasIndex(e => new { e.UserId, e.Status })
        .HasDatabaseName("IX_BackgroundJobs_User_Status");
    entity.HasIndex(e => new { e.ChatId, e.CreatedAt })
        .HasDatabaseName("IX_BackgroundJobs_Chat_CreatedAt");
    entity.HasIndex(e => new { e.Status, e.CompletedAt })
        .HasDatabaseName("IX_BackgroundJobs_Status_CompletedAt");
});

modelBuilder.Entity<ChatTodo>(entity =>
{
    entity.ToTable("ChatTodos");
    entity.HasKey(e => e.Id);

    entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
    entity.Property(e => e.Description).HasMaxLength(2000);
    entity.Property(e => e.Status).HasMaxLength(20).IsRequired();

    entity.HasOne(e => e.Chat)
        .WithMany()
        .HasForeignKey(e => e.ChatId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasIndex(e => new { e.ChatId, e.SortOrder })
        .IsUnique()
        .HasDatabaseName("IX_ChatTodos_Chat_SortOrder");
});
```

### § 4.5. Миграция

**SqlServer — обязательна.**

```powershell
# RULES § 3.15: перед генерацией выставить Database:Provider = "SqlServer"
dotnet ef migrations add AddBackgroundJobsAndTodos `
  --project IIChatTools.Data `
  --startup-project IIChatTools.API `
  --output-dir Migrations/SqlServer
```

**Sqlite:**

- В dev/prod сейчас используется `EnsureCreated` (KI-070), а не миграции.
- Новая БД создаст таблицы автоматически. **Существующая** dev-БД не обновится
  (`no such table: BackgroundJobs`) — по RULES § 4.25 удалить
  `IIChatTools.API/Data/iichattools-dev.db` и перезапустить.
- Для прод-Sqlite нужен `Migrations/Sqlite` (сейчас каталога нет; KI-070).
  В Ф1 добавить отдельную миграцию или зафиксировать «удалить/пересоздать БД»
  как единственный путь для v2.1.0.

**Down-миграция:** `DropTable("ChatTodos")`, `DropTable("BackgroundJobs")` (в обратном порядке).

---

## § 5. Сервисный слой

### § 5.1. `IBackgroundJobService` (Scoped)

```csharp
public interface IBackgroundJobService
{
    Task<BackgroundJob> CreateAsync(
        int userId, int? chatId, string type, string parametersJson,
        CancellationToken cancellationToken);

    Task<BackgroundJob> GetAsync(
        int jobId, int userId, CancellationToken cancellationToken);

    Task<BackgroundJob> GetInternalAsync(
        int jobId, CancellationToken cancellationToken); // без userId — только runner

    Task<IReadOnlyList<BackgroundJob>> ListAsync(
        int userId, int? chatId, bool activeOnly,
        CancellationToken cancellationToken);

    Task MarkRunningAsync(int jobId, CancellationToken cancellationToken);

    Task ReportProgressAsync(
        int jobId, int progress, string message,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        int jobId, string resultJson, CancellationToken cancellationToken);

    Task FailAsync(
        int jobId, string error, CancellationToken cancellationToken);

    Task RequestCancelAsync(
        int jobId, int userId, CancellationToken cancellationToken);

    Task MarkCancelledAsync(
        int jobId, string reason, CancellationToken cancellationToken);

    Task<int> CleanupAsync(
        DateTime completedBefore, CancellationToken cancellationToken);
}
```

Правила сервиса:

- `GetAsync`/`ListAsync` **всегда** фильтруют по `UserId` (IDOR-защита).
- `ReportProgressAsync` клампит `0..100`, не уменьшает значение
  (`Math.Max(current, progress)`), не пишет чаще 1 раза в секунду на job.
- `CompleteAsync`/`FailAsync`/`MarkCancelledAsync` ставят `CompletedAt=UtcNow`
  и `Progress=100` только для Completed.
- `CreateAsync` валидирует `type` по whitelist и `parametersJson` ≤ 64 KB.

```csharp
public sealed class BackgroundJobsOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxActivePerUser { get; set; } = 3;
    public int MaxConcurrentJobs { get; set; } = 2;
    public int QueueCapacity { get; set; } = 100;
    public int MaxJobsPerDayPerUser { get; set; } = 50;
    public int MaxResultBytes { get; set; } = 262_144;
    public int MaxParametersBytes { get; set; } = 65_536;
    public int RetentionHours { get; set; } = 24;
    public int CleanupIntervalMinutes { get; set; } = 60;

    public Dictionary<string, int> MaxRuntimeSeconds { get; set; } = new()
    {
        [BackgroundJobTypes.Orchestrator] = 1200,
        [BackgroundJobTypes.Vision]       = 900,
        [BackgroundJobTypes.Smoke]        = 120
    };

    public int GetMaxRuntimeSeconds(string type)
        => MaxRuntimeSeconds.TryGetValue(type, out var seconds) ? seconds : 300;
}
```

### § 5.2. `IBackgroundJobQueue` (Singleton)

```csharp
public interface IBackgroundJobQueue
{
    /// <summary>Пытается поставить jobId в очередь. false — очередь заполнена.</summary>
    bool TryEnqueue(int jobId);

    /// <summary>Читатель для BackgroundJobRunner.</summary>
    ChannelReader<int> Reader { get; }
}
```

```csharp
// реализация
private readonly Channel<int> _channel = Channel.CreateBounded<int>(
    new BoundedChannelOptions(capacity: 100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });
```

`TryEnqueue` использует `_channel.Writer.TryWrite(jobId)`; при `false`
`start_background_job` возвращает `ToolResult.Fail("Очередь фоновых задач переполнена")`
и помечает job как `Failed` (или не создаёт запись вовсе — см. § 6.2).

### § 5.3. `BackgroundJobRunner` (HostedService)

```csharp
public sealed class BackgroundJobRunner : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobEventHub _events;
    private readonly IJobCancellationRegistry _cancellations;
    private readonly IJobConcurrencyLimiter _limiter;
    private readonly BackgroundJobsOptions _options;
    private readonly ILogger<BackgroundJobRunner> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Parallel.ForEachAsync(
            _queue.Reader.ReadAllAsync(stoppingToken),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrentJobs, // 2
                CancellationToken = stoppingToken
            },
            async (jobId, ct) => await RunJobAsync(jobId, ct));
    }
}
```

`RunJobAsync`:

```csharp
using var scope = _scopeFactory.CreateScope();
var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();
var registry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();
var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceResolver>();

var job = await jobs.GetInternalAsync(jobId, ct);
if (job == null || job.Status != BackgroundJobStatuses.Pending) return;

// Pending мог быть отменён, пока лежал в очереди.
if (job.CancelRequestedAt.HasValue)
{
    await jobs.MarkCancelledAsync(jobId, "Отменена до запуска", ct);
    return;
}

await jobs.MarkRunningAsync(jobId, ct);

var userToken = _cancellations.Register(jobId, ct);
using var timeoutCts = new CancellationTokenSource(
    TimeSpan.FromSeconds(_options.GetMaxRuntimeSeconds(job.Type)));
using var typeCts = CancellationTokenSource.CreateLinkedTokenSource(userToken, timeoutCts.Token);

var progress = new JobProgressSink(jobId, jobs, _events);
var toolName = ResolveToolName(job.Type);
var args = BuildToolArguments(job);
var ctx = new ToolExecutionContext
{
    UserId = job.UserId,
    ChatId = job.ChatId,
    WorkspaceRoot = await workspace.GetWorkspacePathAsync(job.UserId),
    CancellationToken = typeCts.Token,
    JobProgress = progress
};

try
{
    // Мост для legacy SSE-событий Orchestrator (orchestrator_round и т.п.).
    await using var bridge = _events.CreateToolEventBridge(jobId, progress);
    ctx.EventWriter = bridge.Writer;

    var result = await registry.ExecuteAsync(toolName, ctx, args);

    if (result.Success)
        await jobs.CompleteAsync(jobId, SanitizeAndCap(result.Data, _options.MaxResultBytes), ct);
    else if (timeoutCts.IsCancellationRequested && !userToken.IsCancellationRequested)
        await jobs.FailAsync(jobId,
            $"Превышен лимит времени ({_options.GetMaxRuntimeSeconds(job.Type)} с)", ct);
    else if (userToken.IsCancellationRequested)
        await jobs.MarkCancelledAsync(jobId, "Отменена пользователем", ct);
    else
        await jobs.FailAsync(jobId, result.Message ?? "Инструмент вернул ошибку", ct);
}
catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !userToken.IsCancellationRequested)
{
    await jobs.FailAsync(jobId,
        $"Превышен лимит времени ({_options.GetMaxRuntimeSeconds(job.Type)} с)", ct);
}
catch (OperationCanceledException) when (typeCts.IsCancellationRequested)
{
    await jobs.MarkCancelledAsync(jobId, "Отменена пользователем", ct);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Background job {JobId} failed", jobId);
    await jobs.FailAsync(jobId, ex.Message, ct);
}
finally
{
    _cancellations.Unregister(jobId);
    _limiter.Release(job.UserId);
    _events.Complete(jobId);
}
```

> Во **всех** терминальных ветках (`CompleteAsync` / `FailAsync` /
> `MarkCancelledAsync`) runner сначала публикует `job_completed`
> (`status`, `progress`, `resultPreview`, `error`, `durationMs`),
> и только затем вызывает `_events.Complete(jobId)`. Иначе подписчик SSE
> закроет поток, не увидев финального события.

### § 5.4. Тип → инструмент

| `Type` | Tool | Аргументы |
| :--- | :--- | :--- |
| `Orchestrator` | `orchestrate_task` | `{ task, parallel? }` |
| `Vision` | `vision_agent` | `{ action: "run_task", task, url?, maxSteps? }` |
| `Smoke` | `browser_session_control` | `{ sessionId, command: "run_smoke", steps }` |

> `start_background_job` **не** принимает имя инструмента от LLM. Тип —
> строгий enum, аргументы валидируются по схеме. Это защита от запуска
> произвольного `ITool` в фоне.

### § 5.5. `IJobEventHub` (Singleton)

```csharp
public interface IJobEventHub
{
    IDisposable Subscribe(int jobId, out ChannelReader<BackgroundJobEventDto> reader);
    void Publish(int jobId, BackgroundJobEventDto evt);
    void Complete(int jobId);

    /// <summary>Мост legacy ChatStreamEvent → job_progress (для Orchestrator).</summary>
    IJobToolEventBridge CreateToolEventBridge(int jobId, IJobProgressSink progress);
}

public interface IJobToolEventBridge : IAsyncDisposable
{
    /// <summary>Writer, который runner передаёт в ToolExecutionContext.EventWriter.</summary>
    ChannelWriter<ChatStreamEvent> Writer { get; }
}
```

- На каждого подписчика — **отдельный** bounded-канал (64 события,
  `DropOldest`), чтобы медленный браузер не блокировал runner.
- `Publish` не бросает исключений; ошибки подписчика логируются.
- После `Complete(jobId)` канал закрывается; запись держится ещё 60 секунд
  (поздний F5 успевает получить финал), затем удаляется.
- **Никакого replay** истории в памяти: при подключении SSE-эндпоинт сначала
  отдаёт снимок из БД (`job_progress` с текущим статусом), потом live-события.
  Это ограничивает память O(подписчики), а не O(история).

### § 5.6. `IJobCancellationRegistry` (Singleton)

```csharp
public interface IJobCancellationRegistry
{
    CancellationToken Register(int jobId, CancellationToken applicationToken);
    bool TryCancel(int jobId);
    void Unregister(int jobId);
}
```

- `ConcurrentDictionary<int, CancellationTokenSource>`.
- `TryCancel` вызывает `Cancel()`; runner ловит `OperationCanceledException`.
- `Unregister` в `finally` — иначе CTS утекает.
- `CancellationTokenSource` создаётся с `CreateLinkedTokenSource(applicationToken)`.

### § 5.7. `IJobConcurrencyLimiter` (Singleton)

```csharp
public interface IJobConcurrencyLimiter
{
    bool TryAcquire(int userId);
    void Release(int userId);
}
```

- `ConcurrentDictionary<int, int>` + `Interlocked`; лимит
  `BackgroundJobs:MaxActivePerUser` (3).
- Захват — **до** создания `BackgroundJob`, чтобы два одновременных
  `start_background_job` не проскочили лимит.
- `Release` — в `finally` runner'а. Если job не поставлен в очередь —
  release в `StartBackgroundJobTool`.

### § 5.8. Прогресс: `IJobProgressSink`

```csharp
public interface IJobProgressSink
{
    void Report(int progress, string message);
}
```

`ToolExecutionContext` расширяется **опциональным** свойством:

```csharp
/// <summary>v2.1.0: приёмник прогресса фоновой задачи (null в обычном Chat).</summary>
public IJobProgressSink JobProgress { get; set; }
```

- `JobProgressSink.Report` → `IBackgroundJobService.ReportProgressAsync`
  (throttle 1 c) + `IJobEventHub.Publish`.
- `Orchestrator`: после декомпозиции — 5 %, после каждой подзадачи —
  `10 + 80 * completed / total`, в конце — 95 %.
- `Vision`: `VisionAgentService` на каждом шаге цикла вызывает
  `context.JobProgress?.Report(5 + 90 * step / maxSteps, "Шаг N/M: ...")`.
- `Smoke`: `BrowserSmokeRunner` после каждого шага — `5 + 90 * i / steps.Count`.
- Это лучше, чем парсить SSE-события Orchestrator: единый контракт для всех
  типов и нет зависимости от формата `ChatStreamEvent`.

**Fallback-мост** для существующих tool'ов без `JobProgress`:
runner подписывается на `EventWriter` и переводит
`orchestrator_started` → 5 %, `orchestrator_round` → 10 + 15×N (cap 90),
`orchestrator_completed` → 95 %. Мост удаляется в Ф3, когда все три tool
научатся `JobProgress`.

### § 5.9. Восстановление после рестарта

`BackgroundJobRecoveryService` (HostedService, `StartAsync`):

| Состояние в БД | Действие |
| :--- | :--- |
| `Running` | → `Failed`, `Error="Прервано перезапуском сервера"` (побочные эффекты неизвестны, авто-ретрай опасен) |
| `Pending` + `CancelRequestedAt != null` | → `Cancelled` |
| `Pending` (валидный `ParametersJson`) | повторный `TryEnqueue` (до 20 задач), статус остаётся `Pending` |
| `Pending` (битый JSON) | → `Failed` |

**Почему не durable queue:** `Channel` живёт в памяти; полноценный outbox —
отдельная задача (KI-257). Для v2.1.0 достаточно восстановить Pending и
честно пометить Running.

### § 5.10. Retention

`BackgroundJobRetentionService` (HostedService):

- интервал — `BackgroundJobs:CleanupIntervalMinutes` (60);
- удаляет `Completed/Failed/Cancelled` c `CompletedAt < UtcNow - RetentionHours` (24);
- батчами по 500, чтобы не лочить SQLite;
- `ChatTodos` не удаляются отдельно — каскад при удалении чата;
- метрики (Ф4): `iichattools_background_jobs_active`,
  `iichattools_background_jobs_completed_total{status}`.

### § 5.11. Поток запуска и прогресса

```
UI                ChatStreamService          StartBackgroundJobTool       Runner            DB/EventHub
│ POST /stream          │                          │                        │                  │
│──────────────────────►│                          │                        │                  │
│                       │ tool_call(start_background_job)                   │                  │
│                       │─────────────────────────►│                        │                  │
│                       │                          │ TryAcquire(user)       │                  │
│                       │                          │ Create(Pending) ───────┼─────────────────►│
│                       │                          │ TryEnqueue(jobId) ─────┼─────────────────►│
│                       │                          │ return {jobId}         │                  │
│                       │ tool_result              │                        │                  │
│◄──────────────────────│ job_started              │                        │                  │
│                       │                          │                        │ RunJob → Running │
│ GET /api/jobs/{id}/stream ───────────────────────────────────────────────►│ ReportProgress   │
│◄────────────────────── job_progress / job_completed ─────────────────────│                  │
```

---

## § 6. Инструменты для LLM

### § 6.1. Состав (6 новых tool)

| Tool | Назначение | Approval | Read-only |
| :--- | :--- | :---: | :---: |
| `start_background_job` | Запустить Orchestrator/Vision/Smoke в фоне | ✅ | ❌ |
| `get_job_status` | Статус/прогресс/результат своей задачи | — | ✅ |
| `cancel_job` | Отменить свою задачу | — | ❌ |
| `add_todo` | Добавить пункт плана в чат | — | ❌ |
| `update_todo_status` | Сменить статус пункта | — | ❌ |
| `list_todos` | Список пунктов чата | — | ✅ |

**Итого Chat видит 25 инструментов** (было 19): +6. `ToolRegistry` — 72 (было 66).
**RULES § 4.44:** все 6 имён обязательно добавить в `allowedNames` в
`ChatStreamService.StreamAsync` (рядом с `RagToolNames` / `JobToolNames`).
Без этого LLM их не увидит.

### § 6.2. `start_background_job`

```json
{
  "type": "Orchestrator",
  "parameters": {
    "task": "Собери отчёт по трём источникам…",
    "parallel": true
  }
}
```

| Параметр | Тип | Обяз. | Описание |
| :--- | :--- | :---: | :--- |
| `type` | string | ✅ | `Orchestrator` / `Vision` / `Smoke` (case-insensitive) |
| `parameters` | object | ✅ | Схема зависит от типа, ≤ 64 KB |

Схемы `parameters`:

```jsonc
// Orchestrator
{ "task": "string 1..8000", "parallel": true }

// Vision
{ "task": "string 1..4000", "url": "https://…", "maxSteps": 30 }

// Smoke
{
  "sessionId": "abc123",
  "steps": [
    { "action": "goto", "url": "https://localhost:5001" },
    { "action": "assert_text", "selector": "h1", "contains": "Добро пожаловать" }
  ]
}
```

Поведение:

1. Проверить `context.UserId`; `context.ChatId` опционален.
2. Нормализовать `type`; неизвестный → `Fail` со списком.
3. Валидировать `parameters` (для Smoke — переиспользовать
   `BrowserSmokeRunner.TryParse` из KI-256).
4. Проверить `BackgroundJobs:Enabled`; `false` → `Fail("Фоновые задачи отключены")`.
5. `IJobConcurrencyLimiter.TryAcquire(userId)`; если `false` → `Fail`
   «Уже выполняется 3 фоновые задачи».
6. `IBackgroundJobService.CreateAsync(... Pending ...)`.
7. `IBackgroundJobQueue.TryEnqueue(job.Id)`; если `false` → `Fail`,
   `FailAsync(job.Id, "Очередь переполнена")`, `Release`.
8. Вернуть `{ jobId, type, status: "Pending", createdAt }` **сразу**.

> **Release при любой ошибке.** `TryAcquire` и создание/постановка job
> оборачиваются в `try/catch`: если `CreateAsync`, `TryEnqueue` или
> `job_started` падают — `Release(userId)` вызывается ровно один раз,
> иначе слот «утечёт» и пользователь навсегда упрётся в лимит 3.

После успешного `TryEnqueue` tool пишет в Chat SSE:

```csharp
context.EventWriter?.TryWrite(ChatStreamEvent.JobStarted(new ChatJobStartedDto
{
    JobId = job.Id,
    Type = job.Type,
    ChatId = job.ChatId,
    Status = job.Status,
    CreatedAt = job.CreatedAt
}));
```

`RequiresApprovalByDefault = true`: approval-модалка показывает тип и
параметры (для `orchestrate_task.task` — длинный текст, KI-249 уже умеет
рендерить). После approve задача выполняется без повторных approval
внутри (см. § 9.6).

### § 6.3. `get_job_status`

```json
{ "jobId": 42 }
```

Возвращает:

```json
{
  "jobId": 42,
  "type": "Orchestrator",
  "status": "Running",
  "progress": 60,
  "progressMessage": "Подзадача 3/5: web_agent",
  "createdAt": "2026-10-19T20:00:00Z",
  "startedAt": "2026-10-19T20:00:01Z",
  "completedAt": null,
  "result": null,
  "error": null,
  "resultTruncated": false
}
```

- Только свой job (`UserId`).
- `ResultJson` больше 64 KB → `resultPreview` (первые 16 KB) +
  `resultTruncated=true`; полный результат — в UI/REST.
- `RequiresApprovalByDefault=false`.

### § 6.4. `cancel_job`

```json
{ "jobId": 42 }
```

- Только свой job; идемпотентно.
- `Pending` → сразу `Cancelled` (runner пропустит).
- `Running` → `CancelRequestedAt=UtcNow`, `IJobCancellationRegistry.TryCancel`;
  статус меняет runner, когда tool вернётся/бросит `OperationCanceledException`.
- `Completed/Failed/Cancelled` → вернуть текущий статус без ошибки.
- `RequiresApprovalByDefault=false` (отмена своей задачи безопасна).

### § 6.5. Todo-tools

| Tool | Параметры | Поведение |
| :--- | :--- | :--- |
| `add_todo` | `title` (1..200), `description?` (≤2000) | `SortOrder = max+1`; лимит 20 на чат; возвращает item + `total` |
| `update_todo_status` | `todoId`, `status` (`Pending`/`InProgress`/`Completed`) | Проверка чата/пользователя; `CompletedAt` ставится/снимается; возвращает item |
| `list_todos` | `status?` | Список по `SortOrder`; по умолчанию все |

- Все три — `RequiresApprovalByDefault=false`: они меняют только план
  текущего чата и обратимы.
- `add_todo` без `ChatId` → `Fail` «доступно только в чате».
- Дубликаты заголовков не запрещены (LLM может переформулировать), но
  `add_todo` возвращает `warning`, если такой title уже есть.

### § 6.6. `DefaultSystemPrompt` (дополнения)

Добавить в конец `DefaultSystemPrompt` (стабильный префикс не трогать —
KV-cache):

```text
ФОНОВЫЕ ЗАДАЧИ:
- Для долгих операций (Orchestrator, Vision, Browser Smoke) используй
  start_background_job, а НЕ прямой вызов orchestrate_task/vision_agent.
- После запуска ответь: «Запустил задачу в фоне (jobId=N). Можете
  продолжать общение; я сообщу о завершении.»
- Не вызывай get_job_status в том же ответе — только когда пользователь
  спросит или после job_completed.
- Отменить задачу — cancel_job(jobId), только свою.

ПЛАН (TODO):
- Если задача требует 3+ шагов — сначала list_todos, затем add_todo на
  каждый шаг (3–7 пунктов), первый отметь InProgress.
- После выполнения шага — update_todo_status(Completed).
- Не создавай todo для простых вопросов и коротких ответов.
```

---

## § 7. Интеграция с Chat (SSE + REST + UI)

### § 7.1. Новые SSE-события

| Event | Где | Когда | Payload |
| :--- | :--- | :--- | :--- |
| `job_started` | Chat SSE (`/api/chat/stream`) | сразу после `start_background_job` | `jobId`, `type`, `chatId`, `status`, `createdAt` |
| `job_progress` | Job SSE (`/api/jobs/{id}/stream`) | при каждом обновлении (throttle 1 c) | `jobId`, `type`, `status`, `progress`, `message`, `updatedAt` |
| `job_completed` | Job SSE | терминальный статус | `jobId`, `type`, `status`, `progress`, `resultPreview?`, `error?`, `durationMs`, `completedAt` |

Примеры:

```text
event: job_started
data: {"jobId":42,"type":"Orchestrator","chatId":7,"status":"Pending","createdAt":"2026-10-19T20:00:00Z"}
```

```text
event: job_progress
data: {"jobId":42,"type":"Orchestrator","status":"Running","progress":60,"message":"Подзадача 3/5: web_agent","updatedAt":"2026-10-19T20:03:10Z"}
```

```text
event: job_completed
data: {"jobId":42,"type":"Orchestrator","status":"Completed","progress":100,"resultPreview":"{...}","error":null,"durationMs":420000,"completedAt":"2026-10-19T20:07:00Z"}
```

**Почему `job_progress`/`job_completed` не идут в Chat SSE:** Chat SSE
закрывается, когда LLM закончила ответ (`done`); фоновый job продолжает
жить. Отправлять события в закрытый поток невозможно. Поэтому:
- `job_started` — в Chat SSE (tool уже вернулся, стрим ещё открыт);
- `job_progress`/`job_completed` — в отдельный job-SSE, который UI
  открывает по `jobId` и держит независимо от чата.

`ChatStreamEvent` расширяется фабрикой `JobStarted(ChatJobStartedDto)`;
`BrowserSessionControlTool`/`start_background_job` пишет её через
`context.EventWriter.TryWrite(...)`.

### § 7.2. REST API

| Метод | URL | Назначение |
| :--- | :--- | :--- |
| `GET` | `/api/jobs?chatId=&activeOnly=true` | Список задач пользователя / чата (F5, панель) |
| `GET` | `/api/jobs/{id}` | Полный статус (включая `resultJson`) |
| `GET` | `/api/jobs/{id}/stream` | SSE прогресса/завершения |
| `POST` | `/api/jobs/{id}/cancel` | Отмена (то же, что tool) |
| `GET` | `/api/chats/{chatId}/todos` | Список todo |
| `POST` | `/api/chats/{chatId}/todos` | Создать (fallback/ручное) |
| `PATCH` | `/api/chats/{chatId}/todos/{todoId}` | Статус/текст |
| `DELETE` | `/api/chats/{chatId}/todos/{todoId}` | Удалить (опционально) |

Все — `[Authorize]`, ownership через `UserId`/`Chat.UserId`. SSE-эндпоинт:
заголовки как у `ChatStreamController` (`text/event-stream`, `no-cache`,
`X-Accel-Buffering: no`), heartbeat `: ping` каждые 15 с, закрытие после
`job_completed`.

### § 7.3. UI: панель активных задач

Расположение — между `#chat-search-bar` и `#chat-messages`:

```
┌─ Chat header ─────────────────────────────────────────────┐
│  [Поиск] [Удалить]                                        │
├─ #chat-jobs-panel (collapsible) ──────────────────────────┤
│  ⚙ Orchestrator · «Собери отчёт…»   ███████░░ 60%  [×]    │
│  👁 Vision · «Найди статью…»        ██░░░░░░ 15%  [×]    │
├─ #chat-todo-panel ────────────────────────────────────────┤
│  План 2/5  ▾   ☑ Собрать источники                        │
│                ◐ Проверить факты                          │
│                ☐ Свести отчёт                             │
├─ #chat-messages ──────────────────────────────────────────┤
```

Элементы:

- **Job card:** иконка типа, короткое название (из `parameters.task`),
  `progressbar` (`role="progressbar"`, `aria-valuenow`), статус-бейдж,
  кнопка `×` (cancel), кнопка «Результат» для терминальных.
- **Todo panel:** прогресс `N/M`, чек-лист `☐/◐/☑`, сворачивание,
  подсветка `InProgress`.
- **Состояния:** `Pending` (⏳), `Running` (спиннер + %),
  `Completed` (✅), `Failed` (❌ + error), `Cancelled` (⛔).
- **F5:** `GET /api/jobs?chatId=&activeOnly=true` → отрисовать карточки →
  для активных открыть `EventSource(/api/jobs/{id}/stream)`.
- **Завершение:** toast + обновление карточки; результат не вставляется
  в чат автоматически (пользователь нажимает «Результат» или просит LLM
  `get_job_status`).
- **i18n:** `JobsPanelTitle`, `JobStatusPending/Running/...`,
  `JobCancel`, `JobResult`, `TodoPanelTitle`, `TodoProgress` — RU + EN.
- **A11y:** `aria-live="polite"` на панели, `aria-busy` на карточке,
  клавиатурный фокус на cancel/result.

### § 7.4. Поведение LLM

- `start_background_job` → короткий ответ «запустил в фоне» + `jobId`;
  не ждать результат в этом же turn.
- `get_job_status` — по запросу пользователя («как там задача?») или
  после `job_completed` (если UI/система передала событие в контекст).
- Результат фоновой задачи **не** вставляется в system prompt
  автоматически (риск раздувания контекста); LLM получает его только
  через `get_job_status`.
- Stop в чате **не** отменяет job; для отмены — `cancel_job`/кнопка.

---

## § 8. Todo Tracking

### § 8.1. Модель

- Один чат — 0…20 todo; порядок `SortOrder`; статусы
  `Pending/InProgress/Completed`.
- `ChatTodo` привязан к `ChatId` (каскад); доступ — только владелец чата.
- Todo **не** являются сообщениями и не попадают в историю LLM
  автоматически; LLM видит их через `list_todos` (и UI — через REST).

### § 8.2. API и tools

- REST — для UI (F5, ручное редактирование).
- Tools — для LLM (создание/обновление/чтение).
- `add_todo` возвращает `{ todo, total, warning? }`; `list_todos` —
  `{ items[], total, completed }`.
- `update_todo_status` проверяет transition:
  `Pending → InProgress → Completed`; обратный переход разрешён только
  `Completed → InProgress` (reopen) и логируется.

### § 8.3. UI

- Панель над лентой сообщений; collapsed state в `localStorage`
  (`chat.todoPanelCollapsed`).
- Счётчик `completed/total`; прогресс-бар; чек-лист.
- Клик по пункту — cycle status (опционально; LLM всё равно обновляет).
- При `tool_result` от todo-tool — перерисовать панель; при F5 —
  `GET /api/chats/{chatId}/todos`.

### § 8.4. Пример сценария

```
Пользователь: «Сравни REST и gRPC для IIChatTools и сделай отчёт»
LLM:
  list_todos() → []
  add_todo("Собрать факты по REST")
  add_todo("Собрать факты по gRPC")
  add_todo("Сравнить по критериям")
  add_todo("Свести отчёт")
  update_todo_status(1, InProgress)
  start_background_job(Orchestrator, { task: "…" })
  → «Запустил в фоне (jobId=42). План: 4 шага.»
```

---

## § 9. Ограничения и безопасность

### § 9.1. Лимиты (конфиг)

```jsonc
"BackgroundJobs": {
  "Enabled": true,
  "MaxActivePerUser": 3,
  "MaxConcurrentJobs": 2,
  "QueueCapacity": 100,
  "MaxJobsPerDayPerUser": 50,
  "MaxResultBytes": 262144,
  "MaxParametersBytes": 65536,
  "RetentionHours": 24,
  "CleanupIntervalMinutes": 60,
  "MaxRuntimeSeconds": {
    "Orchestrator": 1200,
    "Vision": 900,
    "Smoke": 120
  }
}
```

- При превышении `MaxJobsPerDayPerUser` — `start_background_job` возвращает
  `Fail` с `retryAfter`.
- `MaxRuntimeSeconds` — жёсткий `CancelAfter` в runner; при срабатывании
  статус `Failed` (`Error="Превышен лимит времени (N с)"`) или `Cancelled`
  (если пользователь отменил).
- `Vision` дополнительно — **1 активный Vision job на пользователя**
  (desktop focus/overlay), даже если общий лимит 3.

### § 9.2. Ownership и IDOR

- Все чтения/отмены — `WHERE UserId = @userId`.
- `ChatId` проверяется на принадлежность пользователю при создании.
- `get_job_status`/`cancel_job` не раскрывают чужие jobId (404/`Fail`).
- `ChatTodo` — через `Chat.UserId`; нельзя добавить todo в чужой чат.

### § 9.3. Валидация параметров

- `type` — только 3 значения.
- `Orchestrator.task` ≤ 8000; `Vision.task` ≤ 4000; `Smoke.steps` ≤ 50
  (переиспользовать `BrowserSmokeRunner.TryParse`).
- `Smoke.sessionId` — существующая сессия пользователя; TTL 15 минут
  (если истекла — job `Failed` с понятной ошибкой).
- `ParametersJson` ≤ 64 KB; `ResultJson` ≤ 256 KB после санитайза.

### § 9.4. Большие результаты

- `ResultJson` — `nvarchar(max)`/`TEXT`; но **не** хранить мегабайты:
  - ≤ 256 KB — в БД;
  - больше — в workspace `background-jobs/{jobId}/result.json`,
    в БД — `{ "artifactPath": "...", "summary": "...", "truncated": true }`;
  - base64/скриншоты вырезаются санитайзером (как KI-176).
- `get_job_status` для LLM отдаёт ≤ 64 KB (`resultPreview`).

### § 9.5. SQLite и конкурентные записи

- Прогресс пишется **не чаще 1 раза в секунду** и только при изменении
  процента (throttle).
- Для Sqlite — `ExecuteUpdateAsync` (без загрузки entity), короткие
  транзакции; при `SQLITE_BUSY` — 3 retry с backoff 50/150/400 мс.
- Не держать `DbContext` на всё время job (scope per job, но обновления —
  короткими вызовами).
- `InMemory` провайдер: `ExecuteUpdateAsync` не поддерживается (RULES § 4.29) —
  fallback `FindAsync` + `SaveChangesAsync` в `IBackgroundJobService`.

### § 9.6. Approval и безопасность фонового выполнения

- `start_background_job` требует approval; в модалке видны `type` и
  параметры.
- Внутри runner инструменты вызываются через `IToolRegistry.ExecuteAsync`
  **без** повторного approval. Это осознанно: пользователь одобрил всю
  задачу. Риск: вложенный `file_system_agent`/`git_agent` может выполнить
  mutating-действие без отдельной модалки. Для v2.1.0:
  - audit `job.start` с `type`/`chatId` (без секретов);
  - ограничить типы (только 3) и параметры;
  - в UI предупредить «Выполнится без дополнительных подтверждений».
- Кандидат на отдельный KI-260 (security review).

### § 9.7. F5, Stop, закрытие браузера

| Событие | Поведение |
| :--- | :--- |
| F5 / закрытие вкладки | Job **продолжает** работать; UI при загрузке восстанавливает панель и подписывается на SSE |
| Stop в чате | Обрывает только Chat SSE; job продолжает; отмена — `cancel_job`/кнопка |
| `cancel_job` | `CancelRequestedAt`, CTS.Cancel; Pending → Cancelled сразу; Running → Cancelled после ответа tool |
| Graceful shutdown | Runner отменяет CTS, ждёт ≤ 15 с, помечает Running `Failed` («server shutdown»); Pending остаются в БД и восстанавливаются при старте |
| Жёсткое падение | `Running` остаются; Recovery при старте → Failed |

### § 9.8. Retention и память

- БД: 24 ч для терминальных job; todo — с чатом.
- Память: `_cancellations` и `_eventHub` очищаются в `finally`/`Complete`;
  очередь bounded; подписчики SSE — bounded-каналы.
- `ChatTodo` ограничен 20 на чат; `BackgroundJob` — 50/день на пользователя.
- Метрики/алерты: active jobs, queue length, failed total, avg duration.

---

## § 10. План реализации (фазы)

| Фаза | Содержание | Оценка |
| :--- | :--- | ---: |
| **Ф1. Entities + EF + миграции** | `BackgroundJob`, `ChatTodo`, константы, `DbSet`, `OnModelCreating`, SqlServer-миграция, обновление dev-Sqlite (EnsureCreated/delete), unit-тесты модели | **5 ч** |
| **Ф2. Services + Runner** | `IBackgroundJobService`, `IBackgroundJobQueue`, `BackgroundJobRunner`, `IJobEventHub`, `IJobCancellationRegistry`, `IJobConcurrencyLimiter`, recovery, retention, `BackgroundJobsOptions`, DI, тесты (очередь/лимиты/отмена/восстановление) | **16 ч** |
| **Ф3. Tools + prompt** | 6 tool'ов, `JobToolNames` в `allowedNames`, `DefaultSystemPrompt`, `IJobProgressSink` в `ToolExecutionContext`, прогресс в Orchestrator/Vision/Smoke, тесты валидации/ownership/approval | **10 ч** |
| **Ф4. SSE + REST + UI jobs** | `ChatStreamEvent.JobStarted`, `BackgroundJobEventDto`, `/api/jobs/*`, job-SSE, панель jobs в `chat.js`/`chat.css`/`Index.cshtml`, i18n, F5-восстановление, интеграционные тесты | **12 ч** |
| **Ф5. Todo UI + E2E** | Todo REST + панель, ручное редактирование, E2E-сценарии, smoke, документация (README/CHANGELOG/ARCHITECTURE), метрики | **9 ч** |

**Итого: ~52 ч** (без буфера). С буфером 15 % — **~60 ч**.
Фазы можно сдавать по отдельности; Ф4 зависит от Ф2/Ф3, Ф5 — от Ф3.

### DoD по фазам

- **Ф1:** `dotnet ef database update` на SqlServer; fresh Sqlite создаёт таблицы; 0 warnings.
- **Ф2:** 20+ unit-тестов (queue full, per-user limit, cancel pending/running, recovery, retention, event hub); graceful shutdown.
- **Ф3:** все 6 tools в `allowedNames`; `start_background_job` для трёх типов проходит; `get_job_status` не отдаёт чужой job.
- **Ф4:** браузер: запуск → панель → прогресс → результат; F5 не теряет job; Stop не убивает job.
- **Ф5:** todo-панель, 3 tool, prompt; E2E «сложная задача с планом»; docs.

---

## § 11. Риски и открытые вопросы

| # | Риск | Митигация |
| :---: | :--- | :--- |
| 1 | `ResultJson` больших размеров (Orchestrator-артефакт) | cap 256 KB + artifact в workspace |
| 2 | SQLite `database is locked` при частом прогрессе | throttle 1 c, `ExecuteUpdateAsync`, retry, WAL/busy_timeout (KI-085) |
| 3 | Отмена tool, игнорирующего `CancellationToken` (Puppeteer/LLM) | grace-таймаут, статус `Cancelling`, KI-258 |
| 4 | Рестарт теряет Pending/Running | recovery Pending, Running→Failed; durable queue — KI-257 |
| 5 | Утечка памяти в EventHub | bounded-каналы, `Complete` + TTL 60 c |
| 6 | Approval bypass внутри job | approval на старте + audit + KI-260 |
| 7 | Vision занимает desktop; два job конфликтуют | 1 Vision job на пользователя |
| 8 | Удаление чата во время job | `ChatId=SetNull`, job продолжает; UI показывает «без чата» |
| 9 | `ORDER` — reserved word | `SortOrder` |
| 10 | Background job не должен раздувать контекст LLM | результат только через `get_job_status`, cap 64 KB |
| 11 | `IToolRegistry` scoped в HostedService | scope per job, `Func<IToolRegistry>` ADR-002 |
| 12 | Миграции Sqlite | `EnsureCreated`/delete dev DB; `Migrations/Sqlite` — KI-070 |
| 13 | UI-сложность (панели, SSE reconnect) | отдельная Ф4/Ф5, feature-flag `BackgroundJobs:UiEnabled` |
| 14 | Долгий `OrchestrateTaskTool` сам по себе | лимит 1200 с, cancel, progress |
| 15 | Нет durable outbox | честно задокументировать; KI-257 |

**Открытые вопросы (решить до Ф2):**

1. Нужен ли `Progress` в `ToolExecutionContext` или достаточно `IJobProgressSink`? (рекомендация — sink).
2. Разрешать ли `start_background_job` вне чата (`ChatId=null`)? (рекомендация — да, но UI-панель только в чате).
3. Отменять ли job при удалении чата? (рекомендация — нет, SetNull).
4. Нужен ли `/jobs` (все задачи пользователя) в v2.1.0 или достаточно панели чата?
5. Хранить ли `ResultJson` сжатым (GZip) для больших результатов?

---

## § 12. Предлагаемые KI (текстом)

> Реестр `KNOWN_ISSUES.md` в этом документе **не менялся** (design-only).
> Номера — следующие свободные после KI-256; при регистрации уточнить.

### KI-257 — Durable queue для Background Jobs

- **Приоритет:** 🟡 Medium | **Статус:** Planned | **Запланировано:** v2.1.x
- **Суть:** очередь `Channel<int>` живёт в памяти. При рестарте Pending
  восстанавливаются из БД, Running помечаются Failed; нет гарантии
  «задача выполнится ровно один раз».
- **Решение:** outbox-таблица / polling БД / Hangfire; idempotency key;
  recovery вместо «Failed».

### KI-258 — Cooperative cancellation для browser/vision

- **Приоритет:** 🟡 Medium | **Статус:** Planned | **Запланировано:** v2.1.x
- **Суть:** PuppeteerSharp и Vision-цикл не всегда проверяют
  `CancellationToken` между шагами; `cancel_job` может ждать до конца
  текущего шага (десятки секунд).
- **Решение:** проверять `ct` перед каждым шагом, `WaitAsync(ct)`,
  grace-статус `Cancelling`, документация.

### KI-259 — Большие `ResultJson` и SQLite-write contention

- **Приоритет:** 🟡 Medium | **Статус:** Planned | **Запланировано:** v2.1.x
- **Суть:** Orchestrator-артефакт может быть > 1 MB; хранение в
  `nvarchar(max)`/`TEXT` + частые progress-updates дают нагрузку/локи.
- **Решение:** artifact-store в workspace, cap 256 KB, сжатие, throttle,
  `ExecuteUpdateAsync` + retry.

### KI-260 — Approval bypass внутри фоновых job

- **Приоритет:** 🟠 High | **Статус:** Planned | **Запланировано:** до v2.1.0
- **Суть:** `start_background_job` одобряется один раз, но внутри
  `orchestrate_task`/`vision_agent` могут выполняться mutating-инструменты
  (`file_system_agent`, `git_agent`, `send_email`) без отдельных approval.
- **Решение:** security review; per-type whitelist safe tools; audit;
  предупреждение в UI; опционально — «approval на каждый mutating шаг».

---

## § 13. Ссылки

- [RULES.md](../RULES.md) — § 2.5 (design docs), § 4.44 (allowedNames), § 4.25/4.29 (EF/Sqlite).
- [ARCHITECTURE.md](../ARCHITECTURE.md) — слои, DI, ADR-002 (`Func<IToolRegistry>`).
- [DESIGN v2.0 Orchestrator-Worker](../v2.0/DESIGN_ORCHESTRATOR_WORKER.md) — источник прогресса/SSE.
- [DESIGN v1.12 Vision Agent](../v1.12/DESIGN_VISION_AGENT.md) — `MaxTaskSeconds`, overlay.
- [KI-256](../reports/v2.0.3/SMOKE_RUNNER.md) — `run_smoke`, переиспользуется для Smoke-job.
- [KNOWN_ISSUES.md](../../KNOWN_ISSUES.md) — KI-085 (SQLite lock), KI-070 (Sqlite migrations), KI-176 (sanitize base64).
