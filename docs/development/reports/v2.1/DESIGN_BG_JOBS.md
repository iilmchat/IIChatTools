# DESIGN_BG_JOBS — Background Jobs + Todo Tracking (v2.1.0)

**Дата:** 2026-10-19
**HEAD:** `4c8b2df` (v2.0.3)
**Правила:** `docs/development/RULES.md` v1.4.35
**Статус:** design-only, код C# не менялся, коммитов нет

---

## 1. Файлы (+/-)

| Файл | Операция | Назначение |
| :--- | :--- | :--- |
| `docs/development/v2.1/DESIGN_BACKGROUND_JOBS.md` | создан | Полный DESIGN: entities, services, tools, SSE/UI, todo, фазы, риски |
| `docs/development/reports/v2.1/DESIGN_BG_JOBS.md` | создан | Этот отчёт (7 разделов) |

**Код C# не изменялся.** `chat.js`, `Startup.cs`, `AppDbContext.cs`,
`ChatStreamService.cs`, `OrchestrateTaskTool.cs`, `VisionAgentTool.cs` —
только прочитаны для анализа.

`git status --short`:

```text
?? docs/development/v2.1/
?? docs/development/reports/v2.1/
```

---

## 2. Архитектура

### 2.1. Entities

| Сущность | Ключевые поля | Связи |
| :--- | :--- | :--- |
| `BackgroundJob` | `UserId`, `ChatId?`, `Type`, `Status`, `Progress` (0–100), `ProgressMessage`, `ParametersJson`, `ResultJson`, `Error`, `StartedAt?`, `CompletedAt?`, `CancelRequestedAt?` | `User` — Cascade; `Chat` — **SetNull** |
| `ChatTodo` | `ChatId`, `Title`, `Description?`, `Status`, `SortOrder`, `CompletedAt?` | `Chat` — Cascade |

- `ParametersJson` добавлен к постановке: без него нельзя восстановить
  `Pending` после рестарта и показать параметры в UI.
- `Order` из постановки в БД называется **`SortOrder`** (`ORDER` — reserved word SQL);
  в API/JSON остаётся `order`.
- Статусы — строки (`Pending/Running/Completed/Failed/Cancelled`,
  `Pending/InProgress/Completed`), как в `AgentDebateSession`.

### 2.2. Services

| Компонент | Lifetime | Ответственность |
| :--- | :--- | :--- |
| `IBackgroundJobService` | Scoped | CRUD, progress, complete/fail/cancel, cleanup; ownership |
| `IBackgroundJobQueue` | Singleton | bounded `Channel<int>` (100), `TryEnqueue` |
| `BackgroundJobRunner` | HostedService | `Parallel.ForEachAsync` (2 параллельно), scope per job, type timeout |
| `IJobEventHub` | Singleton | подписчики SSE, bounded-каналы, `job_progress`/`job_completed` |
| `IJobCancellationRegistry` | Singleton | `CancellationTokenSource` на job |
| `IJobConcurrencyLimiter` | Singleton | 3 активных job на пользователя |
| `BackgroundJobRetentionService` | HostedService | удаление терминальных job старше 24 ч |
| `BackgroundJobRecoveryService` | HostedService | `Running → Failed`, `Pending → re-enqueue` |

**Ключевая идея:** БД — source of truth, `Channel` — только доставка.
Runner создаёт **отдельный DI-scope** на задачу и вызывает существующий
`IToolRegistry.ExecuteAsync` (`orchestrate_task` / `vision_agent` /
`browser_session_control`), а не дублирует их логику.

### 2.3. Tools (+6, Chat 19 → 25, ToolRegistry 66 → 72)

| Tool | Approval | Тип |
| :--- | :---: | :--- |
| `start_background_job(type, parameters)` | ✅ | job |
| `get_job_status(jobId)` | — | job |
| `cancel_job(jobId)` | — | job |
| `add_todo(title, description?)` | — | todo |
| `update_todo_status(todoId, status)` | — | todo |
| `list_todos(status?)` | — | todo |

- `type` — строгий whitelist: `Orchestrator` / `Vision` / `Smoke`.
- `Smoke` переиспользует `BrowserSmokeRunner.TryParse` из KI-256.
- Все 6 имён — в `allowedNames` (`ChatStreamService`, RULES § 4.44).
- `DefaultSystemPrompt` получает правила: долгие задачи — через
  `start_background_job`; задачи 3+ шагов — через todo-план.

---

## 3. SSE и UI

### 3.1. Новые SSE-события

| Event | Канал | Когда | Данные |
| :--- | :--- | :--- | :--- |
| `job_started` | Chat SSE (`/api/chat/stream`) | сразу после `start_background_job` | `jobId`, `type`, `chatId`, `status`, `createdAt` |
| `job_progress` | Job SSE (`/api/jobs/{id}/stream`) | throttle 1 с | `jobId`, `type`, `status`, `progress`, `message`, `updatedAt` |
| `job_completed` | Job SSE | терминальный статус | `jobId`, `type`, `status`, `progress`, `resultPreview`, `error`, `durationMs`, `completedAt` |

**Почему два канала:** Chat SSE закрывается после `done` ответа LLM;
фоновый job продолжает жить. `job_progress`/`job_completed` идут в
отдельный job-SSE, который UI открывает по `jobId` и держит при F5.

### 3.2. REST

| Метод | URL | Назначение |
| :--- | :--- | :--- |
| `GET` | `/api/jobs?chatId=&activeOnly=true` | Восстановление панели после F5 |
| `GET` | `/api/jobs/{id}` | Полный статус + `resultJson` |
| `GET` | `/api/jobs/{id}/stream` | SSE прогресса/завершения |
| `POST` | `/api/jobs/{id}/cancel` | Отмена |
| `GET/POST` | `/api/chats/{chatId}/todos` | Список / создание |
| `PATCH/DELETE` | `/api/chats/{chatId}/todos/{id}` | Статус/удаление |

### 3.3. UI

- **`#chat-jobs-panel`** — между header и лентой сообщений: иконка типа,
  название задачи, `progressbar`, статус-бейдж, `×` (cancel), «Результат».
- **`#chat-todo-panel`** — над лентой: `План 2/5`, чек-лист `☐/◐/☑`,
  сворачивание, `localStorage`.
- **F5:** `GET /api/jobs?chatId=&activeOnly=true` → карточки → `EventSource`
  для активных; todo — `GET /api/chats/{id}/todos`.
- **Завершение:** toast + обновление карточки; результат не вставляется
  в чат автоматически — по кнопке или через `get_job_status`.
- **Stop в чате не отменяет job**; отмена — `cancel_job`/кнопка.
- i18n: `JobsPanelTitle`, `JobStatus*`, `JobCancel`, `JobResult`,
  `TodoPanelTitle`, `TodoProgress` (RU + EN).

---

## 4. План фаз

| Фаза | Содержание | Оценка |
| :--- | :--- | ---: |
| **Ф1. Entities + EF + migrations** | `BackgroundJob`, `ChatTodo`, константы, `DbSet`, `OnModelCreating`, SqlServer-миграция, dev-Sqlite | **5 ч** |
| **Ф2. Services + Runner** | service, queue, runner, event hub, cancellation registry, limiter, recovery, retention, options, DI, тесты | **16 ч** |
| **Ф3. Tools + prompt** | 6 tools, `allowedNames`, `DefaultSystemPrompt`, `IJobProgressSink`, progress в Orchestrator/Vision/Smoke, тесты | **10 ч** |
| **Ф4. SSE + REST + UI jobs** | `job_started`, DTO, `/api/jobs/*`, job-SSE, панель jobs, i18n, F5, интеграционные тесты | **12 ч** |
| **Ф5. Todo UI + E2E** | todo REST + панель, E2E, smoke, README/CHANGELOG/ARCHITECTURE, метрики | **9 ч** |

**Итого:** ~**52 ч** без буфера; с буфером 15 % — **~60 ч**.
Зависимости: Ф4 ← Ф2/Ф3; Ф5 ← Ф3.

**DoD по фазам:** Ф1 — миграция применяется, fresh Sqlite создаёт таблицы;
Ф2 — 20+ тестов очереди/лимитов/отмены/восстановления; Ф3 — все 6 tools
в `allowedNames`, три типа job проходят; Ф4 — браузерный сценарий
«запуск → панель → прогресс → результат» и F5; Ф5 — todo-панель и E2E
«сложная задача с планом».

---

## 5. Что не так (риски)

| # | Риск | Митигация |
| :---: | :--- | :--- |
| 1 | `ResultJson` больших размеров (Orchestrator-артефакт) | cap 256 KB; > — в workspace `background-jobs/{jobId}/result.json` |
| 2 | SQLite `database is locked` при progress-записях | throttle 1 с, `ExecuteUpdateAsync`, retry, WAL/busy_timeout (KI-085) |
| 3 | Отмена tool, игнорирующего `CancellationToken` | grace-статус `Cancelling`, лимит времени, KI-258 |
| 4 | Рестарт теряет очередь | recovery: `Running → Failed`, `Pending → re-enqueue`; durable queue — KI-257 |
| 5 | Утечка памяти в `EventHub` | bounded-каналы, `Complete` + TTL 60 с, `Unregister` в `finally` |
| 6 | Approval bypass внутри job | approval на старте + audit + предупреждение в UI; KI-260 |
| 7 | Vision занимает desktop | 1 активный Vision job на пользователя |
| 8 | Удаление чата во время job | `ChatId = SetNull`; job продолжает, UI показывает «без чата» |
| 9 | `ORDER` — reserved word | `SortOrder` в БД, `order` в API |
| 10 | Sqlite-миграции отсутствуют | fresh DB / delete dev DB; `Migrations/Sqlite` — KI-070 |
| 11 | Background job раздувает контекст LLM | результат только через `get_job_status`, preview ≤ 64 KB |
| 12 | UI-сложность (панели, SSE reconnect) | отдельные Ф4/Ф5, feature-flag `BackgroundJobs:UiEnabled` |

**Ограничения дизайна:** оценки фаз — экспертные, не измеренные; нет
durable queue; нет мульти-нодовости; `MaxTaskSeconds` Vision = 900 с —
из текущего конфига, Orchestrator 10–15 мин — по наблюдениям.

---

## 6. Новые KI (текстом; реестр не менялся)

| KI | Приоритет | Суть |
| :--- | :---: | :--- |
| **KI-257** | 🟡 Medium | **Durable queue:** `Channel<int>` теряется при рестарте; нужен outbox/polling/Hangfire + idempotency |
| **KI-258** | 🟡 Medium | **Cooperative cancellation:** PuppeteerSharp/Vision не проверяют `ct` между шагами; `cancel_job` ждёт до конца шага |
| **KI-259** | 🟡 Medium | **Большие `ResultJson` + SQLite write contention:** artifact-store, cap, сжатие, throttle, retry |
| **KI-260** | 🟠 High | **Approval bypass:** `start_background_job` одобряется один раз, вложенные mutating-tools выполняются без отдельных approval — security review |

Номера — следующие свободные после KI-256; при регистрации уточнить.
В `docs/KNOWN_ISSUES.md` в этом шаге ничего не добавлялось (design-only).

---

## 7. Обоснование решений

1. **`Channel<int>` + `BackgroundService`, а не `ConcurrentQueue`.** Async
   backpressure, bounded capacity, `TryWrite` для быстрого отказа при
   переполнении, без внешних зависимостей. `ConcurrentQueue` требует polling,
   `BlockingCollection` блокирует поток.
2. **БД — source of truth, очередь — доставка.** Статус/прогресс/результат
   переживают F5 и рестарт; очередь можно потерять (recovery). Это проще,
   чем делать очередь источником истины.
3. **Отдельный `BackgroundJobRunner`, а не `Task.Run` в tool.** `Task.Run`
   не переживает scope/DI, не ограничивает параллелизм, не даёт graceful
   shutdown. HostedService даёт единый пул и управляемый stop.
4. **`IJobProgressSink` в `ToolExecutionContext`, а не парсинг SSE.**
   Единый контракт для Orchestrator/Vision/Smoke; не зависит от формата
   `ChatStreamEvent`. Мост из SSE — только fallback для legacy.
5. **Отдельный job-SSE.** Chat SSE закрывается вместе с ответом LLM; нельзя
   отправить `job_progress` в закрытый поток. Job-SSE живёт независимо.
6. **`ChatId = SetNull`, `User = Cascade`.** Удаление чата не должно убивать
   уже запущенную работу/аудит; удаление пользователя — очищает всё.
7. **TTL 24 ч + bounded queue + limiter 3/2.** Ограничивает БД, память и
   нагрузку; 24 ч достаточно, чтобы пользователь вернулся к результату,
   и мало для накопления мусора.
8. **`SortOrder` вместо `Order`.** `ORDER` — reserved word; избегаем проблем
   с raw SQL/CLI и будущими провайдерами.
9. **Строковые статусы, как в `AgentDebateSession`.** Не ломает существующий
   стиль, JSON стабилен, добавление статуса не требует миграции enum.
10. **6 узких tools, а не один `run_anything`.** RULES § 4.44 и безопасность:
    только 3 типа job и 3 todo-операции; нет произвольного запуска `ITool`.
11. **Todo — отдельная сущность, не сообщения.** План не засоряет историю
    чата и контекст LLM; LLM читает его через `list_todos`, UI — через REST.
12. **Без кода в этом шаге.** Сначала согласование архитектуры (RULES § 2.5),
    затем Ф1–Ф5; иначе риск переписать миграции/контракты после ревью.
