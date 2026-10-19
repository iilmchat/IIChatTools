# PHASE4_JOBS_UI — Background Jobs: REST + SSE + UI (v2.1.0)

**Дата:** 2026-10-20
**HEAD:** `6b6780a` + незакоммиченные Фазы 1–4
**Правила:** `docs/development/RULES.md` v1.4.35
**Статус:** изменения в рабочем дереве, **не закоммичены**

---

## 1. Файлы (+/-)

### Создано (Фаза 4)

| Файл | Назначение |
| :--- | :--- |
| `IIChatTools.API/Controllers/BackgroundJobsController.cs` | REST list/detail/cancel + SSE `job_progress`/`job_completed` |
| `IIChatTools.Services/DTO/BackgroundJobs/BackgroundJobDto.cs` | DTO для списка/деталей (title из `parameters.task`) |

### Изменено (Фаза 4)

| Файл | Что |
| :--- | :--- |
| `IIChatTools.Services/DTO/Chat/ChatStreamDtos.cs` | `ChatJobStartedDto` + `ChatStreamEvent.JobStarted` |
| `IIChatTools.Services/Implementation/ChatStreamService.cs` | Точечно: `job_started` перед `tool_result`; helper `TryBuildJobStarted` |
| `IIChatTools.Services/Interfaces/IBackgroundJobService.cs` | `ListAsync(userId, chatId)` |
| `IIChatTools.Services/Implementation/BackgroundJobs/BackgroundJobService.cs` | `GetAsync` → `AsNoTracking`; `ListAsync` |
| `IIChatTools.Services/Implementation/BackgroundJobs/BackgroundJobRunner.cs` | `WorkspaceRoot` из `IWorkspaceResolver`; не перетирать `Cancelled`; мост `orchestrator_*` → progress |
| `IIChatTools.Services/Implementation/Tools/BackgroundJobs/StartBackgroundJobTool.cs` | В результат добавлен `type` |
| `IIChatTools.API/Startup.cs` | Удалены дублирующиеся регистрации job-tools |
| `IIChatTools.API/Views/Chat/Index.cshtml` | `#chat-jobs-panel` + `data-label-*` |
| `IIChatTools.API/wwwroot/css/chat.css` | `.chat-job-card`, прогресс-бар, статусные классы |
| `IIChatTools.API/wwwroot/js/modules/chat.js` | `job_started`, EventSource, F5-загрузка, cancel, cleanup |
| `SharedResources.resx` / `.ru.resx` | 6 ключей RU/EN |
| `docs/KNOWN_ISSUES.md` | KI-259, KI-260, KI-261; сводка 240 |
| `CHANGELOG.md` | `[Unreleased]` Added/Fixed |

**Фазы 1–3** (entities, DbContext, migrations, services, tools) уже лежали
в рабочем дереве до этого шага — они не переписывались, кроме точечных
правок выше.

**Не трогал:** `ChatStreamService` целиком (только 2 вставки), `chat.js`
не переписывал (добавлен один блок функций), Todo UI (Фаза 5).

---

## 2. Backend

### 2.1. REST (`BackgroundJobsController`)

`[Authorize] [ApiController] [Route("api/jobs")]`.

| Метод | Поведение |
| :--- | :--- |
| `GET /api/jobs?chatId=` | `ListAsync(userId, chatId)` → `BackgroundJobDto[]` (≤ 50, новые сверху) |
| `GET /api/jobs/{id}` | Детали + `result`; чужой job → 404 |
| `POST /api/jobs/{id}/cancel` | `UpdateStatusAsync("Cancelled")`; терминальный — идемпотентный 200 |
| `GET /api/jobs/{id}/stream` | SSE: polling БД каждые **1.5 с** |

SSE:

- заголовки `text/event-stream`, `Cache-Control: no-cache`,
  `X-Accel-Buffering: no`;
- пока `Pending`/`Running` → `event: job_progress` с `progress`/`message`
  (только при изменении; иначе `: ping` для keep-alive);
- терминальный статус → `event: job_completed` + `break`;
- `HttpContext.RequestAborted` + linked CTS останавливают цикл;
- 404 до записи заголовков, если job чужой/не найден.

`GetAsync` переведён на `AsNoTracking()`: иначе `FindAsync` вернул бы
закэшированный tracked-инстанс, и SSE показывал бы «застывший» прогресс.

### 2.2. `job_started` в ChatStreamService

Точечно, без переписывания:

1. `ChatStreamDtos.cs`: `ChatJobStartedDto { JobId, Type, Status }`,
   фабрика `ChatStreamEvent.JobStarted(dto)`.
2. В цикле tool-вызовов, после сохранения tool-сообщения и **до**
   `yield return ChatStreamEvent.ToolResult(...)`:

```csharp
if (toolResult.Success
    && string.Equals(functionName, StartBackgroundJobToolName, StringComparison.Ordinal)
    && TryBuildJobStarted(toolResult.Data, args, out var jobStartedDto))
{
    yield return ChatStreamEvent.JobStarted(jobStartedDto);
}
```

`TryBuildJobStarted` читает `jobId`/`type`/`status` из `ToolResult.Data`
(`JObject.FromObject` для анонимного объекта), с fallback `type` из
аргументов. `StartBackgroundJobTool` теперь возвращает `type` в Data.

### 2.3. Progress bridge (Runner)

Runner создаёт `Channel<ChatStreamEvent>` и передаёт writer в
`ToolExecutionContext.EventWriter`. Отдельная задача `PumpProgressAsync`
читает события в **отдельном DI-scope** (чтобы не делить `AppDbContext`
с выполняющимся tool'ом) и обновляет `BackgroundJob.Progress`:

| SSE-событие tool'а | Progress | Message |
| :--- | ---: | :--- |
| `orchestrator_started` | 5 | «Декомпозиция…» |
| `orchestrator_round` | 10 + 15×N (cap 90) | `agent: status` |
| `orchestrator_completed` | 95 | «Агрегация…» |

`UpdateProgressIfActiveAsync` не перетирает терминальные статусы. Для
Vision/Smoke таких событий нет — карточка показывает 10% → 100%.
Перед финальным `Completed/Failed` runner завершает канал и ждёт pump.

### 2.4. Cancel

Контроллер и `CancelJobTool` вызывают `UpdateStatusAsync(..., "Cancelled")`.
Runner после возврата tool перечитывает job и, если статус `Cancelled`,
**не сохраняет** поздний `Completed`/`Failed`. Это не останавливает сам
tool (см. KI-259), но статус в UI больше не «откатывается».

---

## 3. Frontend

### HTML (`Index.cshtml`)

Под `#chat-messages` добавлен:

```html
<div id="chat-jobs-panel" class="chat-jobs-panel d-none" aria-live="polite"
     data-label-title="..." data-label-status-pending="..."
     data-label-status-running="..." data-label-status-completed="..."
     data-label-status-failed="..." data-label-status-cancelled="..."
     data-label-cancel="...">
  <div class="chat-jobs-header"><span class="chat-jobs-title"></span></div>
  <div id="chat-jobs-list" class="chat-jobs-list"></div>
</div>
```

Все строки — через `data-*` (RULES § 4.17), значения из `.resx` (RULES § 1.14).

### CSS

`.chat-jobs-panel` (flex-элемент между лентой и input), `.chat-job-card`,
`.chat-job-progress`/`-bar`, `.chat-job-cancel`, статусные акценты
`.job-status-running/completed/failed/cancelled`.

### JS (`chat.js`)

Добавлен один блок функций (старые не менялись):

- `state.jobs` — `jobId → { id, type, title, status, progress, message }`;
- `state.jobStreams` — `jobId → EventSource`;
- `handleSseEvent('job_started')` → карточка + `openJobStream(jobId)`;
- `openJobStream` → `new EventSource('/api/jobs/{id}/stream')`:
  - `job_progress` → `applyJobProgress`;
  - `job_completed` → `applyJobCompleted` + `closeJobStream`;
  - `onerror` → закрыть только при `readyState === CLOSED` (иначе браузер
    сам переподключается);
- `loadJobsForChat(chatId)` — F5/смена чата: `GET /api/jobs?chatId=`,
  рендер, подписка только на `Pending`/`Running`;
- `cancelJob(jobId)` — `POST /api/jobs/{id}/cancel`;
- `resetJobsPanel`/`closeAllJobStreams` — смена чата, empty state,
  `beforeunload`; утечек EventSource нет;
- `bindJobsPanelEvents` — делегирование `[data-job-cancel]`.

Stop чата **не** закрывает job-SSE и не отменяет job (как в дизайне).

---

## 4. Smoke (Build/Test)

```text
dotnet build IIChatTools.sln -c Debug
Сборка успешно завершена. Предупреждений: 0, Ошибок: 0.
```

```text
dotnet test IIChatTools.Tests/IIChatTools.Tests.csproj -c Debug --no-build
Пройден! : не пройдено 0, пройдено 1201, пропущено 8, всего 1209, длительность 2–3 s.
```

Дополнительно:

```text
node --check IIChatTools.API/wwwroot/js/modules/chat.js
exit=0
```

Ручной браузерный smoke не выполнялся (нет запущенного экземпляра
приложения с LM Studio в этом шаге); SSE/UI проверены статически
и сборкой.

---

## 5. Что не так

1. **Отмена не останавливает выполнение** (KI-259). Controller только
   меняет статус; `BackgroundJobRunner` передаёт `stoppingToken` хоста,
   а не per-job CTS. Orchestrator/Vision/Smoke продолжают работу.
2. **`BackgroundJobQueue` с `FullMode=DropWrite`** (KI-260): при
   переполнении `TryWrite` может вернуть `true`, молча выбросив jobId;
   задача навсегда остаётся `Pending`.
3. **`MaxJobsPerUser` не enforced** (KI-261): опция есть, но
   `StartBackgroundJobTool` её не читает; лимит 3 не работает.
4. **SSE-опрос per-client**: 1 запрос к БД / 1.5 с на каждую открытую
   задачу. Для 100 клиентов — ~66 запросов/с; в v2.1.x нужен
   `Channel`/pub-sub (архитектурный долг, не блокер).
   Для Vision/Smoke progress не гранулярный (10% → 100%), потому что
   их сервисы не пишут `orchestrator_*`-события.
5. **EventSource vs RULES § 4.18.** Задача требует `EventSource`;
   он не поддерживает `AbortController`, отмена — `EventSource.close()`.
   Для fetch-SSE чата правило соблюдается (`AbortController` уже есть).
6. **EventSource не умеет заголовки.** Работает только cookie-auth;
   если UI перейдёт на JWT-only, понадобится query-token или прокси.
   CORS не проблема: same-origin.
7. **Нет тестов контроллера/SSE.** Покрытие — сборка + `node --check`;
   `WebApplicationFactory` в проекте пока не используется.
8. **`BackgroundJobDto.Title`** берёт `parameters.task`; для Smoke/Vision
   без `task` в карточке будет тип. Приемлемо для v2.1.0.
9. **`job_started` только в Chat SSE.** Вызовы через `/api/tools/execute`
   не получат событие, но задача работает; UI увидит её после F5.
10. **Фазы 1–3 не закоммичены** — `git status` содержит и их файлы;
    в отчёте разделены «Фаза 4» и «ранее».
11. **`BackgroundJobRunner` не имеет per-job timeout/recovery** —
    это Фаза 2-долг; текущая Фаза 4 его не расширяет.
12. **`CancelRequestedAt` не заполняется** при отмене — поле есть,
    но контроллер его не ставит; UI ориентируется на `Status`.
13. **`ProgressMessage` формируется в runner по-русски** («Инициализация…»,
    «Выполнение…», «Готово»). В EN-локали карточка покажет русский текст;
    это наследие Фазы 3, локализация прогресса — отдельная задача.
14. **README-метрики не обновлены после Фаз 1–3**: там всё ещё
    «66 инструментов / Chat 19», фактически с 3 job-tools — 69 / 22
    (Todo-tools появятся в Фазе 5). Это документация, не Фаза 4.

---

## 6. Новые KI

Добавлены в `docs/KNOWN_ISSUES.md` (сводка обновлена: **240** уникальных):

| KI | Приоритет | Статус | Суть |
| :--- | :---: | :--- | :--- |
| **KI-259** | 🟠 High | Planned (v2.1.0) | Отмена не останавливает выполнение: нет per-job CTS; нужен `IJobCancellationRegistry` + linked token |
| **KI-260** | 🟠 High | Planned (v2.1.0) | `BackgroundJobQueue` `DropWrite` теряет jobId при переполнении; `Pending` навсегда |
| **KI-261** | 🟡 Medium | Planned (v2.1.0) | `MaxJobsPerUser=3` не enforced в `StartBackgroundJobTool` |

Также в KI-252/дизайне уже были KI-257 (durable queue), KI-258
(cooperative cancellation) — эти три дополняют их практическими
находками Фазы 4.

---

## 7. Обоснование решений

1. **Polling в SSE, а не `IJobEventHub`.** В Фазе 1–3 нет pub/sub-хаба;
   опрос `IBackgroundJobService.GetAsync` раз в 1.5 с — минимальный
   контракт, который работает между scope'ами runner/controller без
   изменения ChatStreamService. Для прогресса добавлен только мост
   `orchestrator_*` → `BackgroundJob.Progress`. Полный `Channel`/hub —
   оптимизация v2.1.x (см. § 5.4), не блокер.
2. **`AsNoTracking` для `GetAsync`.** Без него SSE-цикл в одном
   `DbContext` получал бы tracked-инстанс из `FindAsync` и не видел
   обновлений runner'а. Это точечный фикс, не изменение API.
3. **`EventSource` на клиенте.** Нативный SSE, авто-reconnect,
   cookie-auth same-origin, минимум кода. `AbortController` неприменим
   к `EventSource`; закрытие — `close()`. Для fetch-SSE чата
   `AbortController` остаётся.
4. **Два канала событий.** Chat SSE закрывается после `done`; фон живёт
   дальше. `job_started` идёт в Chat SSE (UI успевает создать карточку),
   `job_progress`/`job_completed` — в отдельный job-SSE.
5. **`job_started` перед `tool_result`.** Так карточка появляется до
   отрисовки tool-блока; порядок не ломает существующий `tool_result`.
6. **`data-*` + `.resx`.** RULES § 4.17/§ 1.14: JS не содержит
   UI-строк; `LocalizationSyncTests` проходит.
7. **Отмена через `UpdateStatusAsync`.** Соответствует постановке и
   текущему `CancelJobTool`. Guard в runner не даёт позднему
   `Completed`/`Failed` перетереть `Cancelled`; полноценная остановка —
   KI-259.
8. **Progress-мост через `EventWriter`.** Не меняет Orchestrator:
   он уже пишет `orchestrator_*` в `ToolExecutionContext.EventWriter`.
   Runner лишь переводит их в `BackgroundJob.Progress`; отдельный scope
   защищает DbContext от параллельного использования.
9. **Панель под `#chat-messages`.** По требованию задачи; CSS
   `flex: 0 0 auto` не ломает скролл ленты.
10. **Минимальные правки `ChatStreamService`.** Вставка ~8 строк +
    helper; файл не переписывался (RULES/постановка).
11. **Фаза 4 без Todo UI.** Todo REST/UI — Фаза 5; сейчас не смешиваем,
    чтобы не расширять поверхность ревью.
