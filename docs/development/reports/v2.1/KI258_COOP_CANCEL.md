# KI-258 — Cooperative Cancellation (`orchestrate_task` / `vision_agent` / `run_smoke`)

**Дата:** 2026-10-20
**Версия:** v2.1.1 (в работе)
**HEAD:** `4fe40aa` (v2.1.0) + незакоммиченные изменения
**Правила:** `docs/development/RULES.md` v1.4.35
**Статус:** изменения в рабочем дереве, **не закоммичены** (жёсткое требование постановки)
**Контекст:** Фазы 1–5 Background Jobs закрыты, KI-259 (передача `CancellationToken` в
`ToolExecutionContext`) закрыт в v2.1.0. Здесь — «вторая половина» KI-259: сами tool'ы
должны **проверять** токен между шагами и останавливаться по «Отмене».

> **Нумерация.** В `DESIGN_BACKGROUND_JOBS § 12` эта задача значилась как «KI-258»
> (предлагаемый номер), но в реестре `KI-258` уже занят записью «run_smoke: assert_text
> регистрозависим». Поэтому работа зарегистрирована как **KI-263**; коллизия номеров
> зафиксирована в **KI-264**. По тексту отчёта сохранён исходный ID постановки — KI-258.

---

## 1. Файлы (+/-)

### Изменено (production)

| Файл | +/- | Что изменено |
| :--- | ---: | :--- |
| `IIChatTools.Services/Implementation/Tools/Debate/OrchestrateTaskTool.cs` | +243 / −19 | Кооперативная отмена: pre-check до старта, OCE-ветка планировщика, проверки в последовательном и параллельном циклах, `catch (OperationCanceledException)` в `ExecuteSubtaskAsync`, статус `cancelled`, `CancelAsync` сессии, `Fail("Операция отменена.")`, поля `cancelled`/`cancelledSubtasks`, иконка `⏹` в артефакте |
| `IIChatTools.Services/Implementation/VisionAgent/VisionAgentService.cs` | +38 / −5 | `currentStep` + проверка отмены в начале каждой итерации, логирование отмены (внешняя / STOP overlay), унификация `result.Error = "Операция отменена."`; аудит per-step I/O — все вызовы уже получают `effectiveCts.Token` |
| `IIChatTools.Services/Implementation/Browser/BrowserSmokeRunner.cs` | +42 / −10 | Проверка `ct` перед шагом, `Task.WaitAsync(ct)` для Puppeteer-операций, различение «отмена пользователя» / «общий таймаут 60 с», лог с номером шага |
| `IIChatTools.Services/Implementation/BrowserSessionManager.cs` | +8 / −0 | `run_smoke`: при отмене — `ToolResult.Fail("Операция отменена.")` (а не «шаг N упал») |
| `IIChatTools.Services/Implementation/BackgroundJobs/BackgroundJobRunner.cs` | +11 / −0 | Если tool вернул Fail после отмены per-job CTS — job помечается `Cancelled`, а не `Failed` |

### Изменено (тесты)

| Файл | +/- | Что добавлено |
| :--- | ---: | :--- |
| `IIChatTools.Tests/UnitTests/Debate/OrchestrateTaskToolTests.cs` | +187 / −1 | Хуки `FakeToolRegistry.OnExecute` / `ThrowCancellationFor`, мок `CancelAsync`; 3 теста отмены |
| `IIChatTools.Tests/UnitTests/VisionAgent/VisionAgentServiceTests.cs` | +89 / −1 | Тест отмены между шагами + test-local `CancellingPlannerLlmClient`; `CreateService` принимает `IPlannerLlmClient` |

### Изменено (документация)

| Файл | +/- | Что |
| :--- | ---: | :--- |
| `docs/KNOWN_ISSUES.md` | +87 / −9 | KI-263 (Fixed), KI-264 (Documented), KI-265 (Planned); актуализация KI-259, KI-189; сводка статусов |
| `CHANGELOG.md` | +13 / −2 | `[Unreleased] → Fixed`: KI-258/KI-263; `Planned` → KI-265 |
| `docs/development/v2.1/DESIGN_BACKGROUND_JOBS.md` | +6 / −0 | Примечание в § 12 о фактических номерах KI-263/KI-265 |
| `docs/development/reports/v2.1/KI258_COOP_CANCEL.md` | new | Настоящий отчёт |

**Не трогал:** `VisionAgentTool.cs` (уже корректно ловит `OperationCanceledException`
и возвращает `ToolResult.Fail("Операция отменена.")`), `BrowserSessionControlTool.cs`
(команда `run_smoke` не менялась), `IAgentDebateSessionService` (сигнатуры не менялись),
`Startup.cs`, UI/JS, `Directory.Build.props` (версия не бампалась — релиза нет).

`git status --short` (до создания этого файла):

```text
 M CHANGELOG.md
 M IIChatTools.Services/Implementation/BackgroundJobs/BackgroundJobRunner.cs
 M IIChatTools.Services/Implementation/Browser/BrowserSmokeRunner.cs
 M IIChatTools.Services/Implementation/BrowserSessionManager.cs
 M IIChatTools.Services/Implementation/Tools/Debate/OrchestrateTaskTool.cs
 M IIChatTools.Services/Implementation/VisionAgent/VisionAgentService.cs
 M IIChatTools.Tests/UnitTests/Debate/OrchestrateTaskToolTests.cs
 M IIChatTools.Tests/UnitTests/VisionAgent/VisionAgentServiceTests.cs
 M docs/KNOWN_ISSUES.md
 M docs/development/v2.1/DESIGN_BACKGROUND_JOBS.md
```

**Коммитов нет.**

---

## 2. Orchestrator (`OrchestrateTaskTool.cs`)

Главный цикл — **не `Task.WhenAll` на верхнем уровне, а две ветки**:
последовательный `foreach` по `activeSubtasks` (если `parallel=false` / есть
`external_llm_agent`) и параллельная `Task.WhenAll` + `SemaphoreSlim(maxParallel)`.
Проверки добавлены во все точки, где может начаться новая работа.

### 2.1. До старта

```csharp
// ---------- 1.5. KI-258: кооперативная отмена ----------
if (context.CancellationToken.IsCancellationRequested)
{
    _logger.LogInformation("Задача отменена пользователем. Шаг 0.");
    return ToolResult.Fail("Операция отменена.");
}
```

Сессия `AgentDebateSession` не создаётся, планировщик не вызывается — отмена
«до старта» не оставляет мусора в БД.

### 2.2. Отмена во время декомпозиции

`CallPlannerAsync` ловит OCE **отдельным фильтрованным catch** и возвращает
`PlannerCallResult { Cancelled = true }` (ошибка декомпозитора — отдельный случай):

```csharp
catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
{
    _logger.LogInformation(
        "OrchestrateTaskTool: декомпозиция отменена пользователем.");
    return new PlannerCallResult
    {
        Success = false,
        Cancelled = true,
        Message = OperationCancelledMessage
    };
}
```

### 2.3. Последовательный цикл

```csharp
foreach (var st in activeSubtasks)
{
    if (context.CancellationToken.IsCancellationRequested)
    {
        _logger.LogInformation(
            "Задача отменена пользователем. Шаг {Step}.", seqIndex + 1);
        MarkRemainingAsCancelled(subtasks);
        break;
    }

    await ExecuteSubtaskAsync(
        context, st, sessionId, subtasks.Count, ++seqIndex);
}
```

`MarkRemainingAsCancelled` переводит все `pending`/`running` подзадачи в
`status="cancelled"` с `error="Операция отменена пользователем."`.
Завершённые/`failed`/`rejected` не трогаются.

### 2.4. Параллельная ветка (`Task.WhenAll` + `SemaphoreSlim`)

```csharp
var tasks = activeSubtasks.Select(async (st, idx) =>
{
    if (context.CancellationToken.IsCancellationRequested)
    {
        MarkSubtaskAsCancelled(st);
        _logger.LogInformation(
            "Задача отменена пользователем. Шаг {Step}.", idx + 1);
        return;
    }

    try
    {
        await semaphore.WaitAsync(context.CancellationToken);
    }
    catch (OperationCanceledException)
    {
        // Слот НЕ захвачен — Release не нужен.
        MarkSubtaskAsCancelled(st);
        _logger.LogInformation(
            "Задача отменена пользователем. Шаг {Step}.", idx + 1);
        return;
    }

    try { await ExecuteSubtaskAsync(context, st, sessionId, subtasks.Count, idx + 1); }
    finally { semaphore.Release(); }
});

try
{
    await Task.WhenAll(tasks);
}
catch (OperationCanceledException)
{
    MarkRemainingAsCancelled(subtasks);
    _logger.LogInformation(
        "OrchestrateTaskTool: параллельное выполнение прервано " +
        "отменой пользователя.");
}
```

Токен передаётся внутрь worker'а через `ToolExecutionContext` (поле
`CancellationToken` уже заполнено `BackgroundJobRunner`-ом / `ChatStreamService`),
поэтому `Task.Run` не нужен: `IToolRegistry.ExecuteAsync` получает контекст с
токеном. OCE **не покидает** `ExecuteAsync`.

### 2.5. `ExecuteSubtaskAsync`

Три уровня защиты:
1. pre-check в начале метода (последний рубеж) → `cancelled`, `return`;
2. `catch (OperationCanceledException)` **до** `catch (Exception)` →
   `status="cancelled"`, `error="Операция отменена пользователем."` (не `failed`);
3. если worker **сам вернул** `ToolResult.Fail("Операция отменена.")` вместо
   исключения и токен уже отменён → тоже `cancelled`, а не `failed`.

Длительность, Blackboard, `AddRoundAsync` и SSE `orchestrator_round` выполняются
как раньше (запись `AddRoundAsync` с уже отменённым токеном обёрнута в
существующий `try/catch` и не роняет инструмент).

### 2.6. Финализация

```csharp
var wasCancelled = context.CancellationToken.IsCancellationRequested
    || subtasks.Any(s => s.Status == "cancelled");

if (wasCancelled) MarkRemainingAsCancelled(subtasks);

// ... completed / failed / cancelledSubtasks, finalArtifact, STOPWATCH ...

if (wasCancelled)
{
    await SafeCancelAsync(sessionId.Value, context.UserId);   // НЕ CompleteAsync
}
else
{
    var verdict = failed == 0 ? "Approved"
        : (completed > 0 ? "PartiallyApproved" : "Failed");
    await SafeCompleteAsync(sessionId.Value, verdict, artifactJson, context.CancellationToken);
}

// SSE orchestrator_completed эмитится всегда (UI закрывает блок),
// Blackboard.Clear — всегда.
if (wasCancelled)
    return ToolResult.Fail("Операция отменена.", data);
```

`SafeCancelAsync` вызывает `_sessionService.CancelAsync(sessionId, userId,
CancellationToken.None)` с `try/catch` — запись отмены должна пройти **после**
отмены пользовательского токена. `CompleteAsync` для отмены не используется:
его реализация **всегда** ставит `Status="Completed"` и лишь пишет
`FinalVerdict` строкой.

`ToolResult.Fail(..., data)` сохраняет частичный прогресс: `sessionId`,
`subtasks[]` (включая `cancelled`), `cancelled`/`cancelledSubtasks`,
`finalArtifact`. Это аддитивные поля — существующие имена не менялись.

---

## 3. VisionAgent (`VisionAgentService.cs`)

### 3.1. Проверка между шагами

Главный цикл — `for (int step = 1; step <= maxSteps; step++)`. Введён
`currentStep`, видимый в `catch`-блоках (локальная переменная `step` видна только
в теле цикла, а `catch` — снаружи `try`):

```csharp
var currentStep = 0;

for (int step = 1; step <= maxSteps; step++)
{
    currentStep = step;

    if (effectiveCts.Token.IsCancellationRequested)
    {
        if (cancellationToken.IsCancellationRequested
            || overlayStopToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Задача отменена пользователем. Шаг {Step}. VisionAgent[{TaskId}]",
                currentStep, taskId);
        }

        effectiveCts.Token.ThrowIfCancellationRequested();
    }

    // ... screenshot → describe → plan → validate → act ...
}
```

Фраза «отменена пользователем» пишется **только** для внешней отмены/STOP overlay;
внутренний timeout (`MaxTaskSeconds`) сохраняет прежнее сообщение и логируется
своим `LogWarning` — иначе пользователь видел бы «отмену пользователем» там, где
он ничего не нажимал.

### 3.2. Precedence catch-блоков (не менялась)

| Токен | Catch | Поведение |
| :--- | :--- | :--- |
| `overlayStopToken` (STOP) | 1-й фильтрованный | `result.Error = "Операция отменена."`, возврат (не throw) |
| `timeoutCts` (MaxTaskSeconds) | 2-й фильтрованный | `result.Error = "Превышен лимит времени задачи..."`, возврат |
| внешний `cancellationToken` | общий `catch (OperationCanceledException)` | лог шага + `throw` → `VisionAgentTool` → `Fail("Операция отменена.")` |

### 3.3. Аудит per-step I/O (все получают `effectiveCts.Token`)

| Вызов | Токен |
| :--- | :--- |
| `_backend.ScreenshotAsync` (+ retry) | ✅ `effectiveCts.Token` |
| `_screenshotStore.SaveAsync(..., step, png, token)` | ✅ |
| `_visionLlm.DescribeAsync(png, token)` (+ retry) | ✅ |
| `_plannerLlm.PlanNextAsync(..., token)` | ✅ |
| `EnrichWithOcrAsync(screen, token)` → `ScreenshotFullResolutionAsync(ct)`, `RecognizeWithLayoutAsync(..., ct)` | ✅ |
| `ExecuteActionAsync(..., token)` → `ResolveCoordinatesAsync`, `domProvider.ResolveAsync`, backend `Click/Type/PressKey/...`, `WaitAsync` | ✅ |
| `Task.Delay(retryDelayMs, token)`, `Task.Delay(ActionDelayMs, token)` | ✅ |
| `_backend.OpenAsync(url, token)` (pre-loop) | ✅ |

**Правок не потребовалось** — KI-259 и предыдущие фазы уже протянули токен.
Новое здесь — явная проверка/логирование в начале шага и унификация сообщения.

### 3.4. Результат

`VisionAgentTool.ExecuteAsync` уже содержит `catch (OperationCanceledException) →
ToolResult.Fail("Операция отменена.")`. Для overlay-STOP (возврат без throw)
`result.Error = "Операция отменена."` → `HandleRunTaskAsync` возвращает
`ToolResult.Fail(message)` — единый текст для всех видов пользовательской отмены.

---

## 4. SmokeRunner (`BrowserSmokeRunner.cs`)

### 4.1. Проверка между шагами

Цикл `for (var i = 0; i < plan.Steps.Count; i++)` уже проверял
`cancellationToken.ThrowIfCancellationRequested()`; добавлено:
- проверка `ct` в начале `ExecuteStepAsync` (defense in depth);
- лог и различение причины в catch.

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    if (context.CancellationToken.IsCancellationRequested)
    {
        _logger.LogInformation(
            "Задача отменена пользователем. Шаг {Step}.", currentStep);

        return Failure(
            currentStep, started.ElapsedMilliseconds, screenshots, plan.Steps.Count,
            $"Операция отменена. Шаг {currentStep}.");
    }

    return Failure(
        currentStep, started.ElapsedMilliseconds, screenshots, plan.Steps.Count,
        $"Превышен общий таймаут smoke-сценария ({OverallTimeoutSeconds} с) на шаге {currentStep}.");
}
```

`BrowserSessionManager` создаёт `smokeCts = linked(context.CancellationToken)` +
`CancelAfter(60 с)`, поэтому **оба** случая приходят одним токеном; внешнюю
отмену отличаем по `context.CancellationToken`.

### 4.2. PuppeteerSharp 7.1.0 без `CancellationToken`

У используемых методов `Page`/`Keyboard` нет overload'ов с `CancellationToken`,
поэтому ожидание обёрнуто в `Task.WaitAsync(cancellationToken)`:

| Операция | Обёртка |
| :--- | :--- |
| `page.GoToAsync` | `.WaitAsync(ct)` |
| `page.ClickAsync` | `.WaitAsync(ct)` |
| `page.TypeAsync` | `.WaitAsync(ct)` |
| `page.Keyboard.PressAsync` | `.WaitAsync(ct)` |
| `page.WaitForSelectorAsync` | `.WaitAsync(ct)` (плюс `Timeout = effectiveTimeoutMs`) |
| `page.EvaluateFunctionAsync` (`click_by_text`, `assert_text`, `assert_selector`) | `.WaitAsync(ct)` |
| `page.ScreenshotDataAsync` | `.WaitAsync(ct)` |
| `File.WriteAllBytesAsync` | `ct` (нативно) |
| `Task.Delay` (action `wait`) | `ct` (нативно) |

Перед каждым шагом `page.DefaultTimeout = effectiveTimeout`, поэтому
«брошенная» Puppeteer-операция ограничена сверху и не висит вечно. `page.CloseAsync`
по `ct.Register` **не** используется: сессия браузера общая для чата, закрытие
сломало бы её и для следующих команд.

### 4.3. Возврат ToolResult

`BrowserSessionManager`, case `run_smoke`:

```csharp
if (context.CancellationToken.IsCancellationRequested)
{
    return ToolResult.Fail("Операция отменена.", payload);
}

return smokeResult.Passed
    ? ToolResult.Ok(payload)
    : ToolResult.Fail(smokeResult.Error, payload);
```

`BackgroundJobRunner` при `jobCts.Token.IsCancellationRequested` и
`result.Success == false` ставит job `Cancelled`, а не `Failed` — иначе
кооперативная отмена «маскировалась» бы под ошибку.

---

## 5. Smoke (Build/Test)

### 5.1. Сборка

```text
> dotnet build IIChatTools.sln -v minimal --nologo
Сборка успешно завершена.
    Предупреждений: 0
    Ошибок: 0
```

**0 warnings / 0 errors** (RULES § 3.6).

### 5.2. Тесты

```text
> dotnet test IIChatTools.sln --no-build -v minimal --nologo
Пройден! : не пройдено 0, пройдено 1220, пропущено 8, всего 1228,
           длительность 2 s. - IIChatTools.Tests.dll (net10.0)
```

- **0 failed**, 1220 passed, 8 skipped — все 8 skip **предсуществующие
  `[Skip]`**: 6 × `ExternalLlmIntegrationTests.*_RealRequest` (нужны реальные
  API-ключи) и 2 × `PuppeteerSharpCdpSessionTests.*_RealChrome` (нужен реальный
  Chromium). Новые тесты не пропущены.

### 5.3. Новые тесты (4)

Точечный прогон:

```text
Пройден OrchestrateTaskToolTests.ExecuteAsync_CancelledBeforeStart_ReturnsFail
Пройден OrchestrateTaskToolTests.ExecuteAsync_CancellationBetweenSubtasks_StopsAndMarksCancelled
Пройден OrchestrateTaskToolTests.ExecuteAsync_WorkerThrowsOperationCanceled_MarksCancelledNotFailed
Пройден VisionAgentServiceTests.RunTaskAsync_CancelledBetweenSteps_StopsLoop
```

| Тест | Проверяет |
| :--- | :--- |
| `ExecuteAsync_CancelledBeforeStart_ReturnsFail` | отменённый токен до старта → Fail, планировщик/сессия не тронуты |
| `ExecuteAsync_CancellationBetweenSubtasks_StopsAndMarksCancelled` | отмена после 1-й подзадачи → 2-я/3-я не запускаются, статусы `cancelled`, `CancelAsync` 1 раз, `CompleteAsync` 0 раз |
| `ExecuteAsync_WorkerThrowsOperationCanceled_MarksCancelledNotFailed` | worker бросил OCE → `cancelled` (не `failed`) |
| `RunTaskAsync_CancelledBetweenSteps_StopsLoop` | отмена на 2-м шаге Vision → OCE, 3-го скриншота/клика нет |

Smoke-сценарий (`BrowserSmokeRunner.RunAsync`) юнит-тестами не покрыт и раньше:
нужен реальный `Page`/Chromium (см. комментарий в `BrowserSmokeTests.cs`, KI-257).
Проверка — сборка + существующие 29 тестов парсинга/валидации (входят в 1220).

---

## 6. Новые KI

Номера взяты после KI-262 (свободные). Подробные записи — в `docs/KNOWN_ISSUES.md`.

| KI | Приоритет | Статус | Суть |
| :--- | :--- | :--- | :--- |
| **KI-263** | 🟠 High | ✅ **Fixed (v2.1.1)** | Cooperative cancellation: Orchestrator/Vision/Smoke проверяют токен между шагами, отмена → `cancelled`/`Cancelled`, а не `failed`/`Failed`. В DESIGN § 12 значилось как «KI-258». Ограничение зафиксировано в записи: отмена **кооперативная** — между шагами/подзадачами; внутри одного длинного вызова (HTTP LLM у worker'а, одна Puppeteer-операция) остановка по завершении/таймауту. |
| **KI-264** | 🟢 Low | Documented | Документация: предлагаемые номера KI-257…260 из `DESIGN_BACKGROUND_JOBS § 12` конфликтуют с реестром (KI-257 cast, KI-258 `assert_text`, KI-259 cancel jobs, KI-260 `DropWrite`); `CHANGELOG [Unreleased]` и KI-259 ссылались на «KI-258» в двух смыслах. Плюс: постановка ссылается на RULES § 2.18 «Стандарт отчётов», которого в `RULES.md` v1.4.35 нет (раздел заканчивается § 2.11). Ссылки актуализированы, KI-263/265 зарегистрированы. |
| **KI-265** | 🟡 Medium | Planned (v2.1.x) | Durable queue для Background Jobs (ранее в DESIGN § 12 числилось как KI-257 — номер занят): outbox/polling/Hangfire, idempotency, recovery вместо `Failed`. |

**Существующие записи не переименовывались** (реестр append-only): KI-257 и KI-258
остались за прежними задачами, чтобы не ломать внешние ссылки.

Прочие наблюдения аудита (без отдельного KI):
- в `BackgroundJobRunner` `catch (OperationCanceledException)` помечает job
  `Cancelled` и при graceful shutdown (DESIGN § 9.7 ожидает `Failed` «server
  shutdown») — поведение **предсуществующее**, не менялось, вынесено в
  ограничения KI-263;
- `VisionAgentTool.cs` уже был корректен (OCE → `Fail("Операция отменена.")`),
  правок не потребовал.

---

## 7. Обоснование решений

### 7.1. `ThrowIfCancellationRequested` vs `break`

- **VisionAgent / SmokeRunner — `ThrowIfCancellationRequested`.** Там уже есть
  разветвлённые `catch`-блоки, различающие внешнюю отмену / STOP overlay / timeout;
  исключение — единственный способ донести «причину» до этих блоков без дублирования
  логики в каждой точке. Проверка стоит в начале итерации, т.е. **новый шаг не
  начинается**.
- **Orchestrator — `break` + явная маркировка.** Нужно **доформировать** результат:
  частичный `finalArtifact`, статусы подзадач, `cancelledSubtasks`, SSE
  `orchestrator_completed`, очистку Blackboard и `CancelAsync` сессии. `throw`
  оборвал бы метод до финализации (или потребовал бы огромный `catch` вокруг всего
  тела). `break` позволяет пройти общий путь сборки данных и вернуть
  `ToolResult.Fail("Операция отменена.", data)` с частичным прогрессом — это важно
  и для UI (видно, что успело выполниться), и для LLM (не теряет промежуточный ответ).
- Внутри `ExecuteSubtaskAsync` `throw` от worker'а **перехватывается** отдельным
  `catch (OperationCanceledException)` — статус `cancelled` вместо `failed`;
  это гарантирует инвариант «failed = реальная ошибка, cancelled = отмена».

**RULES § 4.52** (`yield return` запрещён в `catch`): ни один из трёх файлов не
является итератором (`IAsyncEnumerable`/`yield` отсутствуют), поэтому правило не
применимо напрямую; тем не менее соблюдён его дух — побочные эффекты и возврат
результата вынесены **после** `catch`-блоков, а не внутрь них. `yield return` в
`catch` не добавлялся.

### 7.2. `CancelAsync`, а не `CompleteAsync("Cancelled")`

`AgentDebateSessionService.CompleteAsync` **всегда** ставит `Status="Completed"` и
только пишет `FinalVerdict` строкой. Отмена — это отдельный терминальный статус
`Cancelled`, поэтому вызывается `CancelAsync` (он разрешён из `Pending`/`InProgress`).
Токен — `CancellationToken.None`, иначе запись в БД упадёт сразу после отмены
пользовательского токена. Ошибка `CancelAsync` не роняет tool (`SafeCancelAsync`
с `try/catch` + `LogWarning`) — отмена не должна превращаться в исключение.

### 7.3. Почему `Task.WaitAsync(ct)` для Puppeteer, а не `ct.Register(page.CloseAsync)`

PuppeteerSharp **7.1.0** не принимает `CancellationToken` в `Page`/`Keyboard`
методах. Варианты:
1. `ct.Register(() => page.CloseAsync())` — **отклонён**: страница принадлежит
   общей браузерной сессии чата; закрытие по отмене сломает сессию для всех
   последующих команд и потребует переоткрытия (плюс гонки с текущим шагом).
2. `Task.WaitAsync(ct)` — **выбран**: перестаём ждать немедленно, следующий шаг
   не начинается; сама операция ограничена `page.DefaultTimeout` и не висит вечно.
   Это ровно рекомендация `DESIGN_BACKGROUND_JOBS § 12` («проверять `ct` перед
   каждым шагом, `WaitAsync(ct)`»).
3. Просто «проверять `ct` перед вызовом» — недостаточно: нельзя прервать уже
   начавшийся `wait_for_selector` (до 15 с), поэтому `WaitAsync` добавлен поверх.

### 7.4. Логирование: почему «Шаг {Step}» и почему один лог

- Формат `"Задача отменена пользователем. Шаг {Step}."` единый для трёх tool'ов —
  по логам сразу видно, **где** остановилась задача (номер подзадачи у
  Orchestrator'а, номер шага у Vision/Smoke), и что причина — пользователь, а не
  внутренний timeout/ошибка.
- Для VisionAgent фраза пишется только при внешней отмене/STOP, а не при
  `MaxTaskSeconds`, чтобы не подменять причину.
- В Orchestrator лог ставится **до** `break`/`MarkRemainingAsCancelled` — одна
  запись на факт отмены, без спама на каждой последующей подзадаче (цикл
  прерывается).

### 7.5. `BackgroundJobRunner`: почему защита нужна и там

Требование KI-259 — «job должен стать `Cancelled`». Инструменты теперь
**кооперативно** возвращают `ToolResult.Fail("Операция отменена.")` (не бросают
OCE), поэтому `catch (OperationCanceledException)` в runner'е может не сработать,
и job получил бы `Failed`. Добавлена проверка `jobCts.Token.IsCancellationRequested`
перед записью `Failed`: это закрывает случай «отмена пришла, пока tool работал»
даже если tool «проглотил» токен. Отмена Pending (статус `Cancelled` до старта)
по-прежнему обрабатывается существующей проверкой статуса.

### 7.6. Совместимость и RULES

- Все изменения **аддитивны**: существующие поля `data` не переименованы,
  сигнатуры интерфейсов не менялись (grep по реализациям не требовался,
  RULES § 4.34/4.53), `allowedNames`/DI не затрагивались (§ 4.44/4.51).
- XML-doc новых helper'ов и комментарии — на русском (§ 1.1/1.2); публичные
  контракты не расширялись.
- **0 warnings / 0 errors**, тесты зелёные; коммитов нет (жёсткое требование
  постановки), CHANGELOG/KNOWN_ISSUES обновлены в том же рабочем дереве (§ 2.1/4.37).
