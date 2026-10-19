# KI259_CANCELLATION — реальная отмена фоновых задач (v2.1.0)

**Дата:** 2026-10-20
**HEAD:** `7910f6c` + незакоммиченные изменения
**Правила:** `docs/development/RULES.md` v1.4.35
**Статус:** изменения в рабочем дереве, **не закоммичены**

---

## 1. Файлы (+/-)

### Создано

| Файл | Назначение |
| :--- | :--- |
| `IIChatTools.Services/Interfaces/IJobCancellationRegistry.cs` | Контракт реестра CTS для job |
| `IIChatTools.Services/Implementation/BackgroundJobs/JobCancellationRegistry.cs` | Singleton на `ConcurrentDictionary<int, CancellationTokenSource>` |
| `IIChatTools.Tests/UnitTests/BackgroundJobs/JobCancellationRegistryTests.cs` | 4 unit-теста реестра |

### Изменено

| Файл | Что |
| :--- | :--- |
| `IIChatTools.Services/Implementation/BackgroundJobs/BackgroundJobRunner.cs` | Инжект реестра; `GetOrCreate` + `CreateLinkedTokenSource`; `effectiveToken` в tool и progress-pump; `Remove` в `finally`; повторная проверка статуса перед стартом |
| `IIChatTools.Services/Implementation/Tools/BackgroundJobs/CancelJobTool.cs` | Инжект реестра; `Cancel(jobId)` после `UpdateStatusAsync("Cancelled")` |
| `IIChatTools.API/Controllers/BackgroundJobsController.cs` | Инжект реестра; `Cancel(id)` в `Cancel` |
| `IIChatTools.API/Startup.cs` | `AddSingleton<IJobCancellationRegistry, JobCancellationRegistry>()` |
| `docs/KNOWN_ISSUES.md` | KI-259 → Fixed; KI-260/261 отмечены Fixed; сводка 240 |
| `CHANGELOG.md` | `[Unreleased] → Fixed`: KI-259 |

**Не трогал:** `ChatStreamService`, frontend, `BackgroundJobQueue` (KI-260 уже
исправлен в рабочем дереве), модель БД.

---

## 2. Архитектура отмены

### 2.1. `IJobCancellationRegistry` (Singleton)

```csharp
public interface IJobCancellationRegistry
{
    CancellationTokenSource GetOrCreate(int jobId);
    void Cancel(int jobId);
    void Remove(int jobId);
}
```

Реализация:

```csharp
private readonly ConcurrentDictionary<int, CancellationTokenSource> _registry = new();

public CancellationTokenSource GetOrCreate(int jobId)
    => _registry.GetOrAdd(jobId, _ => new CancellationTokenSource());

public void Cancel(int jobId)
{
    if (_registry.TryGetValue(jobId, out var cts))
    {
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* runner уже завершил job */ }
    }
}

public void Remove(int jobId)
{
    if (_registry.TryRemove(jobId, out var cts))
        cts.Dispose();
}
```

- `ConcurrentDictionary` — lock-free, безопасен для вызовов из
  scoped-tool'а, scoped-контроллера и singleton-runner'а.
- `GetOrAdd` гарантирует **один** CTS на jobId даже при гонке.
- `Remove` делает `TryRemove` + `Dispose`; повторный `Remove` — no-op.
- `Cancel` после `Remove` не бросает: `TryGetValue` не найдёт, а гонка
  с `Dispose` покрыта `catch (ObjectDisposedException)`.

### 2.2. Связывание токенов

В `BackgroundJobRunner.ProcessJobAsync`:

```csharp
var jobCts = _cancellationRegistry.GetOrCreate(jobId);
CancellationTokenSource linkedCts = null;

try
{
    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        ct,              // stoppingToken хоста (graceful shutdown)
        jobCts.Token);   // отмена конкретной задачи
    var effectiveToken = linkedCts.Token;

    var context = new ToolExecutionContext
    {
        CancellationToken = effectiveToken,
        // ...
    };
}
finally
{
    linkedCts?.Dispose();
    _cancellationRegistry.Remove(jobId);
}
```

`CreateLinkedTokenSource` — ровно требование RULES § 4.18: отмена
срабатывает, если **либо** останавливается хост, **либо** пользователь
отменяет job. `effectiveToken` уходит в `ToolExecutionContext` (его читают
Orchestrator/Vision/Smoke) и в progress-pump (чтобы он не висел после
отмены).

### 2.3. Жизненный цикл

```
Pending ──(runner GetOrCreate)──► Running
   │                                  │
   │ Cancel (нет CTS)                 │ Cancel → registry.Cancel(jobId)
   ▼                                  ▼
Cancelled                        effectiveToken.IsCancellationRequested
   │                                  │
   │ runner видит Cancelled           │ tool бросает OCE / завершает шаг
   └──────────────► Cancelled ◄───────┘
                         │
                         ▼
                 finally: linkedCts.Dispose()
                          registry.Remove(jobId)
```

- **Pending:** CTS ещё нет. `Cancel` меняет статус; runner перед стартом
  повторно читает `Status` и не запускает tool. Гонка «cancel между
  проверкой и `GetOrCreate`» закрыта повторной проверкой **после**
  создания CTS.
- **Running:** CTS есть; `Cancel` отменяет токен, tool получает OCE
  (или завершает текущий шаг и видит `ct` на следующем).
- **Terminal:** `Remove` в `finally`; `Cancel` после этого — no-op.

---

## 3. Интеграция

### Runner

- Конструктор: `IJobCancellationRegistry cancellationRegistry`.
- `ProcessJobAsync`: `GetOrCreate` → linked CTS → `effectiveToken`.
- `ToolExecutionContext.CancellationToken = effectiveToken`.
- `PumpProgressAsync(..., effectiveToken)` — прогресс останавливается
  вместе с job.
- `finally`: `linkedCts.Dispose()`, `_cancellationRegistry.Remove(jobId)`.
- Перед `UpdateStatusAsync("Running")` — повторная проверка статуса:
  если `Cancelled`, tool не запускается.

### `CancelJobTool`

```csharp
await _jobService.UpdateStatusAsync(job.Id, "Cancelled", job.Progress, ...);
_cancellationRegistry.Cancel(job.Id);
```

Сначала фиксируем статус в БД (UI/SSE увидят), затем будим tool.
Для Pending `Cancel` — no-op, но runner всё равно увидит статус.

### `BackgroundJobsController.Cancel`

Аналогично: `UpdateStatusAsync("Cancelled")` → `_cancellationRegistry.Cancel(id)`.
Терминальные задачи не трогаем (идемпотентный 200).

### DI

```csharp
services.Configure<BackgroundJobOptions>(...);
services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
services.AddSingleton<IJobCancellationRegistry, JobCancellationRegistry>();
services.AddScoped<IBackgroundJobService, BackgroundJobService>();
services.AddHostedService<BackgroundJobRunner>();
```

### Защита от «воскрешения» статуса

`BackgroundJobService.UpdateStatusAsync` не перетирает терминальный статус
другим статусом (фаза 4, terminal-guard). Даже если progress-pump или
tool попытаются выставить `Running`/`Failed` после `Cancelled`, статус
останется `Cancelled`.

---

## 4. Smoke (Build/Test)

```text
dotnet build IIChatTools.sln -c Debug
Сборка успешно завершена. Предупреждений: 0, Ошибок: 0.
```

```text
dotnet test IIChatTools.Tests/IIChatTools.Tests.csproj -c Debug --no-build
Пройден! : не пройдено 0, пройдено 1205, пропущено 8, всего 1213, длительность 2 s.
```

Новые тесты (`JobCancellationRegistryTests`):

| Тест | Проверка |
| :--- | :--- |
| `GetOrCreate_SameJob_ReturnsSameInstance` | Один CTS на jobId |
| `Cancel_CancelsJobToken` | `Cancel` отменяет токен |
| `Remove_AllowsNewTokenAfterwards` | После `Remove` новый CTS не отменён |
| `CancelAndRemove_UnknownJob_DoNotThrow` | No-op для неизвестного jobId |

Итог: +4 теста к 1209 → **1213**; 0 failed.

---

## 5. Что не так

1. **Кооперативность.** Отмена работает только если tool проверяет
   `CancellationToken`. Orchestrator проверяет на шагах, Vision — между
   шагами; если внешний вызов (LLM/Puppeteer) завис внутри шага, job
   завершится только после его возврата. Это KI-258, не KI-259.
2. **Pending + гонка.** CTS для Pending ещё нет; `Cancel` меняет статус,
   runner проверяет его перед стартом. Между повторной проверкой и
   фактическим вызовом tool отмена уже отменяет токен (CTS создан), так
   что окно закрыто, но теоретически tool может не успеть проверить `ct`
   до первого внешнего вызова.
3. **`CancelRequestedAt` по-прежнему не заполняется.** Поле есть, но
   `CancelJobTool`/контроллер его не ставят; UI ориентируется на `Status`.
4. **`Remove`/`Dispose` и гонка с `Cancel`.** `Cancel` ловит
   `ObjectDisposedException`; если `Cancel` вызван после удаления — no-op.
   Это сознательно, но означает, что поздняя отмена уже завершённой
   задачи не имеет эффекта (ожидаемо).
5. **Нет интеграционного теста реальной остановки Orchestrator/Vision.**
   Покрыт только реестр; для E2E нужен живой tool/Chromium, которого нет
   в unit-тестах. Ручной smoke остаётся обязательным.
6. **Singleton-реестр живёт в памяти.** При падении процесса CTS
   теряются (вместе с job); при рестарте Running-задачи помечаются Failed
   (Фаза 2). Это не утечка, но и не durable cancellation.
7. **`Cancel` сначала пишет БД, потом отменяет токен.** Микроокно, в
   котором статус уже `Cancelled`, а tool ещё работает; UI увидит
   `Cancelled`, SSE закроется. Runner не перетрёт статус (terminal-guard).
8. **`BackgroundJobRunner` по-прежнему использует `Task.Run`**, а не
   `Parallel.ForEachAsync`; `Remove` в `finally` покрывает нормальные
   исключения, но `Environment.FailFast`/kill процесс не покрыть.
9. **KI-260/261 отмечены Fixed в реестре** по контексту задачи; их код
   (`Reader.Count`, `CountActiveByUserAsync`) уже в рабочем дереве.
10. **`StartBackgroundJobTool` проверка лимита — count-then-create**:
    два параллельных start могут оба увидеть `activeCount=2` и создать
    две задачи. Для v2.1.0 допустимо, но строгий лимит требует
    транзакции/лимитера.

---

## 6. Новые KI

**Новых KI не заводил:** KI-259 закрыт; KI-258 (cooperative cancellation)
и KI-260/261 (queue/limit) уже описывают оставшиеся ограничения.

Наблюдения без отдельного KI:
- `CancelRequestedAt` не заполняется (п. 5.3);
- count-then-create гонка лимита (п. 5.10);
- интеграционный тест отмены — в рамках будущего E2E.

---

## 7. Обоснование решений

1. **Почему Singleton.** Реестр должен быть общим для трёх lifetimes:
   `BackgroundJobRunner` (HostedService/Singleton), `CancelJobTool`
   (Scoped) и `BackgroundJobsController` (Scoped). Scoped-реестр не увидел
   бы CTS, созданный runner'ом. Singleton + `ConcurrentDictionary` —
   единственный корректный вариант.
2. **Почему `ConcurrentDictionary`, а не `Dictionary` + lock.** Операции
   `GetOrAdd`/`TryRemove` атомарны; блокировка на каждом `Cancel`/`Remove`
   не нужна, а `GetOrAdd` защищает от двух CTS на один jobId.
3. **Почему `CreateLinkedTokenSource`.** Нужно два источника отмены:
   `stoppingToken` хоста (graceful shutdown) и `jobCts.Token`
   (пользовательская отмена). Linked source даёт единый `effectiveToken`
   для tool'а и progress-pump; RULES § 4.18.
4. **Почему `Remove` в `finally`.** `GetOrCreate` вызывается до `try`;
   если tool/парсинг/БД бросит исключение, `finally` всё равно удалит CTS.
   Без этого `ConcurrentDictionary` рос бы на каждый job (утечка).
5. **Почему `Cancel` после `UpdateStatusAsync`.** Сначала фиксируем
   намерение в БД (UI/SSE, аудит), затем будим tool. Если tool успеет
   завершиться между этими строками, terminal-guard не даст перетереть
   `Cancelled`.
6. **Почему повторная проверка статуса перед стартом.** Для Pending CTS
   ещё нет; `Cancel` может прийти между первым `GetAsync` и `GetOrCreate`.
   Повторное чтение после создания CTS закрывает это окно (а `Cancel`
   после создания отменит уже существующий токен).
7. **Почему `catch (ObjectDisposedException)` в `Cancel`.** `Remove`
   диспозит CTS; параллельный `Cancel` мог получить ссылку до `TryRemove`.
   Вместо падения — no-op: отменять уже нечего.
8. **Почему не трогал `ChatStreamService`/UI.** KI-259 — backend-only;
   UI уже показывает `Cancelled` из существующего SSE/REST.
