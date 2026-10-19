# DESIGN — Orchestrator-Worker + Blackboard (KI-126, Фаза 3)

**Версия:** v1.0 (Draft)
**Дата:** 2026-10-12
**Статус:** Draft — ждёт согласования перед реализацией.
**Релиз-цель:** v2.0.0 (Major — финальная часть мультиагентной трилогии).
**Связанные документы:** [RULES.md](../RULES.md) · [DESIGN_MULTI_AGENT_DEBATE.md](../v1.11/DESIGN_MULTI_AGENT_DEBATE.md) · [ARCHITECTURE.md](../ARCHITECTURE.md) · KI-126 · KI-244 (Reflection — отдельно).

---

## § 0. Резюме

Добавляем **третий (и последний) мультиагентный паттерн** в IIChatTools:
**Orchestrator-Worker** с разделяемым состоянием (**Blackboard**).

Отличие от уже реализованных паттернов:

| Паттерн | Что делает | Реализовано |
|:---|:---|:---|
| **Actor-Critic** (Ф1, v1.11.0) | Actor пишет → Critic проверяет | ✅ `code_agent_with_review` |
| **Debate** (Ф2, v1.14.0) | Pro защищает → Contra критикует → Judge выносит вердикт | ✅ `planner_with_debate` |
| **Orchestrator-Worker** (Ф3, v2.0.0) | Orchestrator **декомпозирует** задачу → Workers **выполняют параллельно/последовательно** → агрегация через **Blackboard** | 🚧 этот документ |

**Ключевая идея Ф3:** сложную задачу («исследуй X в 3 источниках», «сравни A/B/C», «собери отчёт из N частей») Orchestrator **разбивает на подзадачи** и распределяет между существующими агентами (`code_agent`, `web_agent`, `file_system_agent`, ...). Результаты каждой подзадачи пишутся в общий **Blackboard** (shared state) и агрегируются в финальный артефакт.

**Рефлексия** (один агент критикует сам себя) — **не входит** в Ф3, выделена в отдельный **KI-244** (концептуально проще, чем Orchestrator-Worker).

---

## § 1. Контекст и мотивация

### § 1.1. Проблема

Сейчас Chat-LLM вызывает агентов **по одному, последовательно, без общей памяти** между вызовами:

- Задача «Сравни REST и gRPC для IIChatTools» → Chat вызывает `planner_with_debate` (Ф2) → получает сбалансированный вердикт. ✅
- Но задача «Собери отчёт: (1) как работает X в источнике A, (2) как работает X в источнике B, (3) как работает X в источнике C, (4) сведи результаты» — Chat вызовет `web_agent` 3 раза подряд (для A, B, C), потом `code_agent` (для сведения). Но:
  - **нет явной декомпозиции** — Chat сам решает, что нужно 3 источника;
  - **нет параллелизма** — 3 web_agent'а идут последовательно, хотя могли бы параллельно;
  - **нет общего state** — 4-й вызов (агрегация) не видит результаты 1-3 напрямую; Chat «пробрасывает» их через контекст чата (лишние токены, потеря структуры);
  - **нет прогресс-UI** — пользователь видит 4 отдельных tool-блока, а не единую картину.

### § 1.2. Идея

Ввести **четвёртый top-level инструмент** `orchestrate_task` (по образцу `code_agent_with_review` / `planner_with_debate`), который:

1. **Декомпозирует** задачу на 2-5 подзадач через внутреннего агента-планировщика.
2. **Распределяет** подзадачи между существующими агентами (`code_agent`, `web_agent`, `file_system_agent`, `database_agent`, `browser_agent`, `external_llm_agent`, ...).
3. **Выполняет** их последовательно или параллельно (решает Orchestrator через флаг `parallel`).
4. **Пишет** результаты каждой подзадачи в **Blackboard** (in-memory + persistence в БД).
5. **Агрегирует** финальный артефакт (сводка / отчёт / сравнение).
6. **Стримит** SSE-события для UI (прогресс-бар `N/M`, список подзадач с иконками статуса, раскрывающийся блок с деталями).

### § 1.3. Применение

**Для чего использовать:**
- **Multi-source research:** «Исследуй X в источниках A, B, C».
- **Multi-aspect analysis:** «Сравни подходы A / B / C по критериям X, Y, Z».
- **Multi-part report:** «Собери отчёт из 4 частей: intro, methodology, results, conclusion».
- **Cross-tool workflows:** «Найди данные через web_agent, обработай через code_agent, сохрани через file_system_agent».

**НЕ для чего использовать:**
- Простые одношаговые задачи («посчитай 2+2», «прочитай файл X»).
- Задачи с одной подзадачей (там достаточно прямого вызова агента).
- Диалоговые вопросы (там достаточно Chat LLM напрямую).
- Задачи с известной жёсткой последовательностью (там достаточно Chat LLM вызвать 2-3 агента подряд).

---

## § 2. Архитектура Orchestrator-Worker

### § 2.1. Диаграмма потока

```
   Chat LLM
      │
      │ tool_call: orchestrate_task({ task: "...", parallel: false })
      ▼
   ┌──────────────────────────────────────────────┐
   │  AgentDebateSessionService                   │
   │  (PatternType = "Orchestrator")              │
   └──────────────────────────────────────────────┘
      │
      ├─► [Фаза 1] orchestrator_planner_agent (декомпозиция)
      │       │
      │       └─► JSON: { parallel, subtasks: [ {id, agent, task}, ... ] }
      │
      ├─► [Фаза 2] Выполнение подзадач
      │       │
      │       ├── Если parallel=false — последовательно:
      │       │       subtask[1] → Blackboard.Put(1, result)
      │       │       subtask[2] → Blackboard.Put(2, result)
      │       │       ...
      │       │
      │       └── Если parallel=true — Task.WhenAll с семафором:
      │               ┌── subtask[1] ─┐
      │               ├── subtask[2] ─┼─► Blackboard.Put(*, *)
      │               └── subtask[3] ─┘
      │
      ├─► [Human-in-the-loop] (опционально, между подзадачами
      │                        при HumanApproval = "BetweenSubtasks")
      │
      ├─► [Фаза 3] Агрегация (Chat LLM формирует финальный артефакт
      │                        на основе Blackboard snapshot)
      │
      └─► SSE debate_completed с PatternType="Orchestrator"
              │
              ▼
        UI: список подзадач + прогресс-бар + финальный артефакт
```

### § 2.2. Роли

| Роль | Существующий агент? | Назначение |
|:---|:---:|:---|
| **Orchestrator** | новый top-level `ITool` `orchestrate_task` | Координация, Blackboard, SSE |
| **Planner (декомпозитор)** | новый **внутренний** агент `orchestrator_planner_agent` (`Internal: true`) | Разбить задачу на 2-5 подзадач; вернуть JSON |
| **Workers** | существующие агенты | `code_agent`, `web_agent`, `file_system_agent`, `database_agent`, `browser_agent`, `external_llm_agent`, `mail_agent`, `vision_agent` |
| **Aggregator** | Chat LLM (сам) | Финальная сводка по Blackboard snapshot |

### § 2.3. Открытый вопрос: `planner_agent` или новый `orchestrator_planner_agent`?

**Вариант A (рекомендуется):** новый внутренний агент `orchestrator_planner_agent`.

Аргументы:
- Консистентно с Ф2 (там создали 3 агента `planner_pro_agent` / `planner_contra_agent` / `planner_judge_agent`).
- Не ломает существующий `planner_agent` (у него `AllowedTools = [save_memory, get_system_info]`, `MaxSteps = 5` — не подходит для декомпозиции).
- Свой SystemPrompt (JSON `{ parallel, subtasks[] }`), свои настройки (`MaxSteps = 3`, `AllowedTools = ["__no_tools__"]`).
- Помечается `Internal: true` — не виден в Chat-tools (RULES § 4.44).

**Вариант B:** переиспользовать `planner_agent` с расширенным SystemPrompt.

Аргументы:
- Меньше файлов.
- Минус: `planner_agent` используется и в других сценариях (сохранение памяти, системная информация) — смешение ответственности.

**Решение:** Вариант A. Новый агент `orchestrator_planner_agent` (`Internal: true`).

### § 2.4. Whitelist воркеров

Orchestrator может вызывать **только** агентов из whitelist:

```jsonc
"SubAgents:orchestrate_task:AllowedWorkerAgents": [
  "code_agent",
  "web_agent",
  "file_system_agent",
  "database_agent",
  "browser_agent",
  "external_llm_agent"
]
```

Если Orchestrator вернёт `agent` не из whitelist — подзадача помечается `status = "rejected"` с причиной, а не выполняется.

**Admin UI для whitelist** — отдельный **KI-245** (v2.0.x). Пока — только `appsettings.json` (по образцу `SubAgents:orchestrate_task:*`).

### § 2.5. Защита от рекурсии (Max depth = 2)

Уровни:
1. **Уровень 1** — Orchestrator (`orchestrate_task`).
2. **Уровень 2** — Worker (`code_agent`, `web_agent`, ...).
3. **Уровень 3** — Worker внутри Worker'а — **ЗАПРЕЩЕНО** (уже защищено в `SubAgentService`: вызов `*_agent` изнутри агента запрещён, RULES § 4.51).

Дополнительно: Orchestrator **не может вызвать** `orchestrate_task` рекурсивно. Проверка `agent == "orchestrate_task"` → reject.

---

## § 3. Управление циклом

### § 3.1. Условия завершения

| Условие | Приоритет |
|:---|:---:|
| Все подзадачи завершены (success / fail / rejected) | 1 |
| Orchestrator вернул пустой список подзадач | 2 |
| Превышен `MaxSubtasks` (default 5) | 3 |
| Превышен `TokenBudget` (default 50k) | 4 |
| Пользователь отменил (Stop в чате) | 5 |
| Агрегатор (Chat LLM) вернул финальный артефакт | 6 |

### § 3.2. Human-in-the-loop

По образцу Ф1/Ф2 — через `IAgentDebateCoordinator` (Singleton, TCS).

**Опции** (`SubAgents:orchestrate_task:HumanApproval`):

- **`"BetweenSubtasks"`** — после каждой подзадачи (только при `parallel=false`). Для `parallel=true` — после **всей** группы.
- **`"AtEnd"`** — только после всех подзадач, перед агрегацией.
- **`"Never"`** (default в prod) — автоматически, без остановок.

Endpoint: `POST /api/chat/debate/{sessionId}/inject` (уже есть из Ф1).

### § 3.3. Fallback на другого воркера (опционально, v2.0.x)

Если Worker упал (fail), Orchestrator **может** попробовать другого агента с той же задачей.

Пример: `web_agent` вернул fail (сайт недоступен) → fallback на `browser_agent`.

**Решение для MVP:** не реализуем. Помечаем подзадачу `status = "failed"`, продолжаем. В § 14 — открытый вопрос для v2.0.x.

### § 3.4. Ограничение параллелизма

Даже при `parallel=true` — не более **N** одновременных Worker'ов (default 3).

Причины:
- **LM Studio:** параллельные вызовы конкурируют за VRAM; 4B-модель может выдать неполный ответ.
- **External:** circuit breaker / budget tracker могут «сломаться» при 5+ параллельных fail'ах.
- **Браузер:** `MaxBrowserSessionsPerUser = 3` — если 5 parallel browser_agent'ов, они конкурируют за сессии.

Реализация: `SemaphoreSlim(N)` вокруг `Task.WhenAll`.

---

## § 4. Blackboard (shared state)

### § 4.1. Зачем

Общий стейт между Orchestrator'ом и Workers. Позволяет:
- Workers **читать** результаты других Workers (для зависимых подзадач).
- Orchestrator **агрегировать** финальный артефакт.
- UI **показывать** прогресс без перезапросов к LLM.

### § 4.2. Модель (гибрид)

**Runtime (in-memory):**
```csharp
ConcurrentDictionary<int, JObject> _blackboards; // sessionId → Blackboard
```

TTL — 30 минут (по образцу `AgentDebateCoordinator`, cleanup по KI-043).

**Persistence (БД):**
- При каждом шаге пишем JSON в `AgentDebateRound.ActorOutput`.
- Формат JSON:
```jsonc
{
  "pattern": "Orchestrator",
  "sessionId": 42,
  "parallel": false,
  "subtasks": [
    {
      "id": "1",
      "agent": "web_agent",
      "task": "Найди X в источнике A",
      "status": "completed",
      "result": "...",
      "error": null,
      "durationMs": 12345,
      "tokensIn": 300,
      "tokensOut": 1200
    },
    ...
  ]
}
```

### § 4.3. API Blackboard

Новый Singleton-сервис `IAgentBlackboard`:

```csharp
public interface IAgentBlackboard
{
    JObject GetOrCreate(int sessionId);
    void Set(int sessionId, string key, JToken value);
    JToken Get(int sessionId, string key);
    JObject Snapshot(int sessionId);
    void Clear(int sessionId);
}
```

**Injection:** `Singleton` — потому что Blackboard шарится между Orchestrator'ом (Scoped) и Worker'ами (Scoped), а сам не зависит от `AppDbContext`. Persistence делается отдельно в `OrchestrateTaskTool` через `IAgentDebateSessionService`.

### § 4.4. Что кладётся в Blackboard

По одной записи на подзадачу:

| Ключ | Значение |
|:---|:---|
| `subtask.{id}.status` | `"pending"` / `"running"` / `"completed"` / `"failed"` / `"rejected"` |
| `subtask.{id}.agent` | Имя агента-исполнителя |
| `subtask.{id}.task` | Текст подзадачи |
| `subtask.{id}.result` | `finalAnswer` от Worker'а |
| `subtask.{id}.error` | Сообщение об ошибке (при `failed`) |
| `subtask.{id}.durationMs` | Длительность |
| `meta.parallel` | `bool` — было ли параллельное выполнение |
| `meta.startedAt` | ISO timestamp |

---

## § 5. Модель данных

### § 5.1. Переиспользуем существующие сущности

**Новых сущностей НЕ создаём.** Используем `AgentDebateSession` + `AgentDebateRound` (v1.11.0, KI-126 Шаг 1A).

- `AgentDebateSession.PatternType = "Orchestrator"`.
- `AgentDebateSession.ConfigSnapshotJson` — расширяем полями `MaxSubtasks`, `Parallel` (см. Шаг 3A).
- `AgentDebateRound.ActorOutput` — содержит JSON Blackboard snapshot (см. § 4.2).
- Остальные поля (`ProOutput`, `ContraOutput`, `JudgeVerdict`, `JudgeFeedbackJson`, `CriticVerdict`, `CriticFeedbackJson`) — `null`.

**Миграция НЕ требуется.** Всё уже есть в БД.

### § 5.2. Расширение DTO

`AgentDebateConfigSnapshot` (`DTO/Debate/`) — добавляем поля:

```csharp
public string PatternType { get; set; }             // уже есть: "ActorCritic" | "Debate" | "Orchestrator"
public int MaxSubtasks { get; set; }                // NEW: для Orchestrator (default 5)
public bool Parallel { get; set; }                  // NEW: флаг от планировщика
public List<string> AllowedWorkerAgents { get; set; } // NEW: whitelist (снимок на момент старта)
```

`AgentDebateRoundDto` (`DTO/Debate/`) — добавляем опциональные поля:

```csharp
public string SubtaskId { get; set; }               // NEW: "1", "2", ...
public string SubtaskAgent { get; set; }            // NEW: "web_agent"
public string SubtaskTask { get; set; }             // NEW: текст подзадачи
public string SubtaskStatus { get; set; }           // NEW: pending/running/completed/failed/rejected
public string SubtaskResult { get; set; }           // NEW: финальный ответ Worker'а
public string SubtaskError { get; set; }            // NEW: сообщение об ошибке
```

`AgentDebateStatusDto` — добавляем:

```csharp
public string OrchestratorAgent { get; set; }       // NEW: "orchestrator_planner_agent"
public int TotalSubtasks { get; set; }              // NEW
public int CompletedSubtasks { get; set; }          // NEW
public int FailedSubtasks { get; set; }             // NEW
```

---

## § 6. Интеграция с Chat (SSE)

### § 6.1. Новый top-level `ITool` `orchestrate_task`

Обязательно добавить в `allowedNames` в `ChatStreamService.StreamAsync` (RULES § 4.44).

### § 6.2. Новые SSE-события `orchestrator_*`

Не переиспользуем `debate_*` — семантика разная (подзадачи vs раунды).
Добавляем 3 новых события (по образцу `debate_*` из Ф1):

| SSE-событие | Что содержит |
|:---|:---|
| `orchestrator_started` | `{ sessionId, task, parallel, maxSubtasks, allowedWorkerAgents, humanApproval }` |
| `orchestrator_round` | `{ sessionId, subtaskId, agent, task, status, result, error, durationMs, tokensIn, tokensOut }` |
| `orchestrator_completed` | `{ sessionId, totalSubtasks, completedSubtasks, failedSubtasks, finalArtifact, totalDurationMs }` |

**Реализация:**

- `ChatStreamEvent` — 3 новых factory-метода: `OrchestratorStarted` /
  `OrchestratorRound` / `OrchestratorCompleted`.
- 3 новых DTO в `DTO/Chat/`: `ChatOrchestratorStartedDto` /
  `ChatOrchestratorRoundDto` / `ChatOrchestratorCompletedDto`.
- UI (`chat.js`): `handleSseEvent` +3 case'а →
  `handleOrchestratorStarted` / `handleOrchestratorRound` /
  `handleOrchestratorCompleted`.
- Рендер — новая функция `renderOrchestratorContainer(sessionId)`.
  Переиспользует CSS-классы `.chat-debate-block` (фон, рамка, шапка).
- `AgentDebateSession.PatternType = "Orchestrator"` — для persistence
  (не про SSE). Остаётся в DTO `AgentDebateStatusDto` для F5-восстановления.

**Почему не переиспользуем `debate_*`:** семантически `debate_round` ≠
`orchestrator_round` (подзадача vs раунд). У Debate есть
`debate_escalated`, у Orchestrator будет `orchestrator_subtask_retry`
(v2.0.x) — разные смыслы. Смешение ведёт к nullable-полям в общих DTO.

**Оценка дополнительных затрат vs переиспользование:** +1.5-2 часа.

## § 7. Конфигурация

Секция `SubAgents:orchestrate_task` в `appsettings.json`:

```jsonc
"orchestrate_task": {
  "Enabled": true,
  "DisplayName": "Оркестрация задач (Orchestrator-Worker)",
  "Description": "Top-level оркестратор для сложных multi-step задач с декомпозицией...",
  "MaxSubtasks": 5,
  "MaxParallel": 3,
  "TokenBudget": 50000,
  "HumanApproval": "Never",
  "AllowedWorkerAgents": [
    "code_agent",
    "web_agent",
    "file_system_agent",
    "database_agent",
    "browser_agent",
    "external_llm_agent"
  ],
  "RequiresApproval": true
}
```

Внутренний агент-планировщик — отдельная секция `SubAgents:orchestrator_planner_agent`:

```jsonc
"orchestrator_planner_agent": {
  "Enabled": true,
  "Internal": true,
  "DisplayName": "Orchestrator Planner (декомпозитор)",
  "Description": "Разбивает задачу на подзадачи. Используется внутри orchestrate_task.",
  "Model": "qwen/qwen3-4b-2507",
  "MaxSteps": 3,
  "RequiresApproval": false,
  "SystemPrompt": "Ты — планировщик-декомпозитор...",
  "AllowedTools": ["__no_tools__"]
}
```

**TokenBudget / MaxSubtasks / HumanApproval** — настраиваются (по запросу user'а).

---

## § 8. UI

### § 8.1. Что показывать

**Единый блок `.chat-debate-block`** с `patternType = "Orchestrator"`.

**Иконка:** 🎭 (маска режиссёра).

**Составные части:**

1. **Header** — `🎭 Оркестрация задач` + задача (truncate) + `Max N подзадач` + `N/M выполнено`.
2. **Прогресс-бар** — `N/M` (CSS `width: N/M * 100%`).
3. **Список подзадач** — по одной строке на подзадачу:
   - Иконка статуса: ⏳ pending / 🔄 running / ✅ completed / ❌ failed / ⛔ rejected.
   - Имя агента (badge).
   - Задача (truncate).
   - Раскрывающийся блок с `result` / `error`.
4. **Финальный артефакт** — `<pre>` (как в Ф2).

### § 8.2. Ветвление рендера

В `chat.js` — `renderDebateContainerInner(sessionId)` уже ветвится по `patternType`. Добавляем case `"Orchestrator"`:

```js
if (isOrchestrator) {
    return renderOrchestratorBlock(entry);
}
```

`renderOrchestratorBlock` — новая функция (30-50 строк).

### § 8.3. Live + F5

- **Live:** SSE `debate_round` (по одной на подзадачу) → перерендер.
- **F5:** через `GET /api/chats/{id}` → `DebateSessions[].Rounds[].ActorOutput` → парсим JSON.

---

## § 9. Параллелизм и External-провайдеры

### § 9.1. Флаг `parallel`

Orchestrator (через `orchestrator_planner_agent`) возвращает JSON:

```jsonc
{
  "parallel": true,
  "reasoning": "Все 3 источника независимы, можно параллельно",
  "subtasks": [
    { "id": "1", "agent": "web_agent", "task": "Найди X в источнике A" },
    { "id": "2", "agent": "web_agent", "task": "Найди X в источнике B" },
    { "id": "3", "agent": "web_agent", "task": "Найди X в источнике C" }
  ]
}
```

**Default:** `false` (последовательно). Если planner не вернул поле или вернул невалидное — `false`.

### § 9.2. Реализация параллелизма

```csharp
if (parallel && subtasks.Count > 1) {
    var semaphore = new SemaphoreSlim(maxParallel);
    var tasks = subtasks.Select(async st => {
        await semaphore.WaitAsync(ct);
        try {
            return await ExecuteSubtaskAsync(st, ...);
        } finally {
            semaphore.Release();
        }
    });
    await Task.WhenAll(tasks);
}
```

**Ограничение:** `maxParallel` (default 3) из `SubAgents:orchestrate_task:MaxParallel`.

### § 9.3. External-провайдеры: pre-checks

**Перед запуском каждого Worker'а** (при `parallel=true`), если Worker — `external_llm_agent`:

1. **Circuit breaker check:** `IExternalLlmCircuitBreaker.IsOpen(provider)`. Если open — Worker не запускается, `subtask.status = "rejected"`.
2. **Budget check:** `IExternalLlmBudgetTracker.CanSpend(userId, estimatedCost)`. Если нет — `subtask.status = "rejected"`.
3. **Rate limit:** `ask_external_llm` уже сам ретраит и падает, но при 3 параллельных fail'ах circuit breaker быстро откроется. Поэтому pre-check обязателен.

**Если хотя бы один из параллельных Workers — External** — Orchestrator **снижает** `maxParallel` до **1** (последовательное выполнение External-вызовов). Причина: параллельные вызовы конкурируют за бюджет и «добивают» circuit breaker.

Это применяется **только если Worker = `external_llm_agent`**. Для LM Studio-воркеров — без изменений.

### § 9.4. LM Studio: VRAM

LM Studio на 4B-модели не любит **параллельные** вызовы — модель может отвечать неполно.

**Решение:** `MaxParallel = 3` — эмпирически выяснено, что 3 параллельных вызова в LM Studio работают стабильно. При 5+ — начинаются проблемы.

**Если нужно больше параллелизма:** переключиться на внешнего провайдера (`ChatLlm:Provider = "external:routerai"`).

---

## § 10. Безопасность

| # | Уровень | Что защищает |
|---|---|---|
| 1 | **Whitelist воркеров** | Orchestrator не может вызвать `orchestrate_task` или `*_agent` вне whitelist |
| 2 | **Max depth = 2** | Защита от рекурсии (уже в `SubAgentService`) |
| 3 | **MaxSubtasks = 5** | Защита от бесконечного цикла |
| 4 | **TokenBudget = 50k** | Защита от «сжигания» контекста |
| 5 | **Approval** (`RequiresApproval = true`) | Пользователь видит задачу перед стартом |
| 6 | **External pre-checks** | Circuit breaker + budget + rate limit (§ 9.3) |
| 7 | **Audit** | Каждый `orchestrate_task` — в `AuditLogs` (без текста подзадач, только метаданные) |

---

## § 11. Фазы работ (план)

| Шаг | Что | Оценка |
|:---|:---|:---:|
| **3A** | Расширение DTO: `AgentDebateConfigSnapshot` + `AgentDebateRoundDto` + `AgentDebateStatusDto` + SSE-DTO | 40 мин |
| **3B** | Внутренний агент `orchestrator_planner_agent` + регистрация в DI + appsettings | 30 мин |
| **3C** | `IAgentBlackboard` + `InMemoryAgentBlackboard` (Singleton) | 40 мин |
| **3D** | `OrchestrateTaskTool` (top-level `ITool`) — скелет, декомпозиция, последовательное выполнение | 90 мин |
| **3E** | Параллельное выполнение (`Task.WhenAll` + семафор + External pre-checks) | 60 мин |
| **3F** | SSE-события + persistence в `AgentDebateRound` + HITL | 60 мин |
| **3G** | DI + `allowedNames` (18 → 19) + правило 16 в `DefaultSystemPrompt` | 30 мин |
| **3H** | UI: рендер Orchestrator-блока (list + progress + details) | 90 мин |
| **3I** | Локализация `.resx` (RU + EN) | 30 мин |
| **3J** | Unit-тесты (`OrchestrateTaskToolTests` + `InMemoryAgentBlackboardTests`) | 90 мин |
| **3K** | Smoke + CHANGELOG + KNOWN_ISSUES + релиз v2.0.0 | 60 мин |

**Итого:** ~10-11 часов (реально, с тестами и UI).

---

## § 12. Риски и ограничения

| Риск | Митигация |
|:---|:---|
| **4B-модель плохо декомпозирует** (возвращает 1 подзадачу или мусорный JSON) | Lenient-парсер + fallback: если 1 подзадача — выполняем её, если 0 — Fail с сообщением |
| **Параллельные LM Studio-вызовы ломают ответ** | `MaxParallel = 3` + семафор |
| **External budget «сжигается» параллельно** | Pre-checks + `maxParallel = 1` для External |
| **UI-шум** (много подзадач → длинная лента) | Раскрывающийся `<details>` (как в Ф2 collapsed) |
| **Задержка** (5 последовательных подзадач × 30 сек = 2.5 мин) | Parallel + прогресс-бар в UI |
| **TokenBudget** (10 подзадач × 20k = 200k) | `MaxSubtasks = 5` + TokenBudget 50k + Fail при превышении |
| **User отменил → Blackboard остаётся** | Cleanup в `finally` + TTL 30 мин |

---

## § 13. Тестирование

### § 13.1. Unit

**`InMemoryAgentBlackboardTests`:**
- `GetOrCreate_NewSession_CreatesEmpty`
- `Set_Get_RoundTrip`
- `Snapshot_ReturnsDeepClone`
- `Clear_RemovesSession`
- `ConcurrentSet_NoRace`

**`OrchestrateTaskToolTests`** (по образцу `PlannerWithDebateToolTests`):
- Структура (Name / RequiresApproval / Parameters).
- Валидация (EmptyTask / TaskTooLong).
- Happy path: 3 подзадачи последовательно → все completed → финальный артефакт.
- Happy path: 3 подзадачи параллельно (`parallel=true`) → все completed.
- Rejected: subtask вне whitelist → статус `rejected`.
- Failed: subtask упала → статус `failed`, продолжаем остальные.
- MaxSubtasks: 10 подзадач → обрезаем до 5.
- TokenBudget: превышен → стопаем, partial result.
- External pre-checks: circuit breaker open → `rejected`.
- External pre-checks: budget exceeded → `rejected`.
- HITL: `BetweenSubtasks` → вызов `IAgentDebateCoordinator.WaitForFeedbackAsync`.
- SSE: 3 события `debate_round` + `debate_completed` с `patternType = "Orchestrator"`.
- Persistence: `AgentDebateRound.ActorOutput` содержит JSON Blackboard.
- Декомпозитор вернул 1 подзадачу → выполняем её.
- Декомпозитор вернул 0 подзадач → Fail.
- Декомпозитор вернул мусор → Fail с понятной ошибкой.

### § 13.2. Integration

- `OrchestrateTaskTool` + fake `IToolRegistry` (2-3 воркера) + fake `IAgentDebateSessionService`.
- Проверка сквозного пути: декомпозиция → выполнение → Blackboard → persistence.

### § 13.3. Smoke (ручной)

- «Исследуй X в 3 источниках (A, B, C)» → параллельно 3 web_agent → сводка.
- «Сравни A / B / C по критериям X, Y» → последовательно 3 code_agent → сравнение.
- «Собери отчёт: intro, methodology, results, conclusion» → 4 подзадачи → финальный отчёт.
- Отмена (Stop) посреди выполнения → сессия Cancelled, Blackboard очищен.
- F5 посреди выполнения → сессия восстанавливается из БД.

---

## § 14. Открытые вопросы — решения приняты

| # | Вопрос | Решение |
|:--|:---|:---|
| 1 | Агент-декомпозитор | **Вариант A** — новый `orchestrator_planner_agent` (`Internal: true`). |
| 2 | HumanApproval по умолчанию | **`Never` в prod**, **`BetweenSubtasks` в dev**. |
| 3 | MaxParallel для External | **1** (последовательное выполнение External-вызовов). |
| 4 | Fallback на другого воркера | **Отложить в v2.0.x**, зафиксировано в **KI-246**. |
| 5 | Admin UI для whitelist воркеров | **Отдельный KI-245** (v2.0.x). |
| 6 | Reflection | **Отдельный KI-244** (v2.1+). |
| 7 | SSE-события | **Новые `orchestrator_*`** (не переиспользуем `debate_*`). |

---

## § 15. Ссылки

- [DESIGN_MULTI_AGENT_DEBATE.md](../v1.11/DESIGN_MULTI_AGENT_DEBATE.md) — Ф1/Ф2 (Actor-Critic + Debate).
- [DESIGN v1.8 External-LLM](../v1.8/DESIGN_EXTERNAL_LLM.md) — circuit breaker + budget tracker.
- [RULES.md](../RULES.md) — § 4.44, § 4.51, § 4.52, § 4.53.
- [ARCHITECTURE.md](../ARCHITECTURE.md) — ADR-002, ADR-016.
- [KI-126](../../KNOWN_ISSUES.md#ki-126) — родительский KI (Ф1 → Ф2 → Ф3).
- KI-244 (Reflection — planned).
- KI-245 (Admin UI whitelist — planned, v2.0.x).

---

**© 2026 RuChating (iilmchat) · IIChatTools v2.0.0 (Draft)**