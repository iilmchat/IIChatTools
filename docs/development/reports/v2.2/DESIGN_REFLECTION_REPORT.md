# REPORT — DESIGN_REFLECTION.md (v2.2.0, KI-244)

**Дата:** 2026-10-20
**Версия:** v2.2.0 (Draft), отчёт по design-only задаче
**HEAD:** `0d10e74` (v2.1.1) + незакоммиченные документы
**Правила:** `docs/development/RULES.md` v1.4.35
**Статус:** создано 2 Markdown-файла, **кода не писалось**, **коммитов нет**
**Design-документ:** [`docs/development/v2.2/DESIGN_REFLECTION.md`](../v2.2/DESIGN_REFLECTION.md)

---

## 1. Файлы (+/-)

### Создано

| Файл | Строк | Назначение |
| :--- | ---: | :--- |
| `docs/development/v2.2/DESIGN_REFLECTION.md` | ~1000 | Архитектурный DESIGN паттерна Reflection (15 разделов, § 0–§ 15) |
| `docs/development/reports/v2.2/DESIGN_REFLECTION_REPORT.md` | ~180 | Настоящий отчёт (7 разделов) |

### Изменено

**Ничего.** C#-код, конфиги, `.resx`, миграции, `CHANGELOG.md`,
`KNOWN_ISSUES.md` — **не трогались** (RULES § 2.4: регистрация KI — при старте
реализации; DESIGN-only, KI-244 остаётся `Planned` до релиза v2.2.0).

`git status --short`:

```text
?? docs/development/reports/v2.2/
?? docs/development/v2.2/
```

**Коммитов нет.** Новых C#-файлов/классов не создано (жёсткое требование
постановки: только Markdown).

Проверка перед сдачей: в DESIGN-документе **нет** блоков ` ```csharp `,
объявлений классов/методов C#; только Markdown-таблицы, JSON/JSONC, ASCII-схема
и language-neutral псевдокод.

---

## 2. Архитектура Reflection

**Идея:** один агент `reflector_agent` в одном цикле — **черновик → самокритика
→ переписывание** — до `Approved` / `MaxRounds` / `Uncertain` / отмены.

### Сущности БД: переиспользуются полностью, **миграция НЕ требуется**

| Reflection | Существующая колонка |
| :--- | :--- |
| тип паттерна | `AgentDebateSession.PatternType = "Reflection"` (свободная `nvarchar(50)`, уже упомянута в комментарии) |
| draft раунда | `AgentDebateRound.ActorOutput` |
| вердикт самокритики | `AgentDebateRound.CriticVerdict` |
| JSON критики (`issues[]`, `summary`) | `AgentDebateRound.CriticFeedbackJson` |
| финальный артефакт | `AgentDebateSession.FinalArtifactJson` = `{ finalArtifact, verdict, totalRounds }` |
| модели/токены/cost/длительность | `ActorModel` / `CriticModel` / `TokensIn` / `TokensOut` / `CostUsd` / `DurationMs` |
| снимок конфига | `ConfigSnapshotJson` (+ аддитивное `SelfCritiquePromptHash`) |

`ProOutput` / `ContraOutput` / `Judge*` / `Subtask*` остаются `null` —
прецедент Orchestrator-Worker (`DESIGN_ORCHESTRATOR_WORKER.md § 5.1`).

### Агент `reflector_agent` (один агент, два промпта)

- Конфиг-дескриптор `SubAgents:reflector_agent` (`Internal: true`):
  `Model`, `MaxSteps`, `SystemPrompt` (draft) + **новый ключ**
  `SelfCritiquePrompt` (critique).
- **ADR-001:** `SystemPromptOverride` в `SubAgentTaskRequest` — полная замена
  промпта (`SubAgentService.cs:380-396`), но через `IToolRegistry` его передать
  нельзя: `AgentToolBase` жёстко ставит промпт дескриптора
  (`AgentToolBase.cs:177`). Поэтому `reflect_task` вызывает
  `Func<ISubAgentService>` **напрямую** (без обёртки-`ITool`), передавая нужный
  промпт на каждый вызов. Рассмотрены и отклонены: два Internal-агента
  (противоречит «один агент», ×2 конфиг), правка `AgentToolBase`
  (prompt-override-протокол отложен — KI-266).
- `AllowedTools = ["__no_tools__"]` — **sentinel, не пустой список**
  (пустой/`null` подставляет 11 дефолтных инструментов,
  `SubAgentService.cs:121-128`); плюс defensive-проверка `UsedTools.Count == 0`.
- Прямой вызов обязателен и по DI-причине: `SubAgentService` сам зависит от
  `IToolRegistry` (`SubAgentService.cs:52-57`) — иначе цикл (RULES § 4.51).

### Инструмент `reflect_task` (top-level `ITool`)

- Параметры: `task` (required, ≤8000), `context` (≤20000),
  `maxRounds` (1..5, default 2; `max_rounds` принимается как алиас).
- Цикл: draft → self-critique (строгий JSON `{verdict, issues[], summary}`) →
  `Rejected` ⇒ rewrite в следующем раунде по `issues`; `Approved`/`Uncertain` ⇒ стоп.
- Persistence: `StartAsync` (`PatternType="Reflection"`) /
  `MarkInProgressAsync` / `AddRoundAsync` / `CompleteAsync` — существующий
  `IAgentDebateSessionService`; при отмене — `CancelAsync(...None)`.
- `ToolResult.Ok` для `Approved`/`Uncertain`/`MaxRoundsReached` (артефакт
  пригоден), `Fail` — только `Failed`/отмена; `rounds[]` в `data` **не**
  возвращается (KI-132), история — SSE/F5.
- `RequiresApprovalByDefault = true`; добавляется в `allowedNames` и
  `isMultiAgentTool` (RULES § 4.44 + F5-метаданные).

---

## 3. SSE и UI

### Новые SSE-события (3)

| Событие | Payload (ключевое) |
| :--- | :--- |
| `reflect_started` | `sessionId, patternType="Reflection", task, agent="reflector_agent", maxRounds, draftModel, selfCritiqueModel, humanApproval="Never"` |
| `reflect_round` | `sessionId, patternType, roundNumber, draft, verdict, issues[], critiqueSummary, durationMs, tokensIn:0, tokensOut:0` |
| `reflect_completed` | `sessionId, patternType, verdict, totalRounds, finalArtifact, totalDurationMs` |

Почему не `debate_*`: другая семантика раунда (draft+critique одного агента),
прецедент `orchestrator_*`; цена ~30-40 мин (3 DTO + 3 фабрики).
Транспорт **не меняется** — `EventWriter`/`execute-with-streaming` форвардит
события любого tool'а автоматически (`ChatStreamService.cs:1561-1622`).

### UI (тот же `.chat-debate-block`, `patternType = "Reflection"`)

- **Заголовок:** 🪞 + «Reflection (самокритика)», задача, `Max N раундов`, `N/M`.
- **Карточка раунда:** «📝 Черновик» (`<pre>` — как actor output) +
  «🧐 Самокритика» (verdict-badge + `issues` через существующий
  `renderCriticFeedback`).
- **Финал:** `<pre>` + вердикт; collapsed/dialog — по глобальной
  `chat.debateView` (отдельной настройки у Reflection нет).
- **CSS:** новых классов не требуется — переиспользуются
  `.chat-debate-block/-round/-actor/-critic/-verdict-*/-issue-*`.
- **F5:** `MetadataJson {debateSessionId}` (через `isMultiAgentTool`),
  `prefillDebatesFromChatDetail` → ветка `isReflection`,
  `debateTools` Set += `reflect_task`, `BuildStatusDto` → `ReflectionAgent`.
- **Локализация:** +3-4 ключа `ChatDebateTitleReflection` /
  `ChatDebateReflectionDraft` / `ChatDebateSelfCritique` (RU+EN, PascalCase),
  `data-label-*` в `Views/Chat/Index.cshtml` (RULES § 4.16/4.17).
- **XSS:** draft/critique — вывод модели; только `escapeHtml` (как
  `chat.js:3669/3682`).

---

## 4. План фаз

| Фаза | Содержание | Оценка |
| :--- | :--- | ---: |
| **Ф1. Контракты и конфиг** | `PatternReflection` const; `AgentDebateStatusDto.ReflectionAgent`; `SelfCritiquePromptHash`; оба `appsettings` (`reflect_task`, `reflector_agent`, промпты); 3 SSE-DTO + 3 фабрики | 80 мин |
| **Ф2. Агент и промпты** | Прямой `ISubAgentService` + `SubAgentTaskRequest` (два `SystemPromptOverride`); модель из `ISubAgentRegistry`; lenient-парсер самокритики; тексты draft/critique/rewrite | 105 мин |
| **Ф3. Tool + wiring** | `ReflectTaskTool` (валидация, цикл, persistence, cancel, verdict, data); `BuildStatusDto` → `isReflection`; DI + `allowedNames` + `isMultiAgentTool` + правило 19 `DefaultSystemPrompt`; эмиссия SSE | 170 мин |
| **Ф4. UI + F5** | `handleSseEvent` +3 case → `state.debates`; ветка `isReflection` в рендере (draft/critique/collapsed); F5-prefill + `debateTools`; resx RU+EN + `data-label-*` | 140 мин |
| **Ф5. Тесты, smoke, релиз** | `ReflectTaskToolTests` (~25 фактов); доп. факты `AgentDebateSessionServiceTests`; ручной smoke; CHANGELOG/KNOWN_ISSUES/ARCHITECTURE + релиз v2.2.0 | 180 мин |

**Итого: 675 мин ≈ 11 ч 15 мин** (с буфером 15% — ≈ 12,5 ч).
KI-244 оценивал «~5-6 ч» — это только цикл; с UI (~2-2,5 ч), тестами (~2 ч) и
F5-ветками реально ближе к v2.0 (~10-11 ч). **Миграция не входит в план —
не нужна.**

---

## 5. Что не так (риски и ограничения)

### Зацикливание — как избегаем

1. `MaxRounds` default **2**, hard-limit **5** (`Math.Clamp`).
2. Ранние выходы: `Approved` и `Uncertain` завершают цикл сразу.
3. **Duplicate-draft guard:** SHA-256 нормализованного черновика; если rewrite
   не изменил текст → стоп с `Uncertain` («самокритика зациклилась»).
4. `Rejected` **обязан** содержать `issues[]`; `Rejected` без issues →
   трактуется как `Uncertain` (некорректный контракт — не крутимся).
5. Приблизительный бюджет символов (`TokenBudget*4`) + cancellation + лимит
   длины артефакта.

### Ключевые ограничения (важнее «красоты» паттерна)

- **Слабая 4B-модель критикует саму себя:** те же слепые зоны + self-approval
  bias. Митигация: строгая рубрика, «представь, что писал другой человек»,
  опция гетерогенной `SelfCritiqueModel`, метрика approval-rate (KI-267).
- **Нет точного token/cost-учёта** (наследие KI-241): `SubAgentTaskResult` без
  usage → `TokensIn/Out=0`, `CostUsd=0`; бюджет — приблизительный.
- **Отмена «проглатывается»:** `SubAgentService` ловит `Exception` вокруг
  LM-вызова (`:149-160`) → OCE выглядит как ошибка модели; поэтому после
  каждого вызова проверяем `ct.IsCancellationRequested` **первым**.
- **Общий лимит сессий 3** для всех паттернов → обязательна гарантированная
  финализация в `finally` (`CompleteAsync/CancelAsync` с `CancellationToken.None`),
  иначе зависшая Reflection-сессия блокирует пользователя.
- **Дублирование данных:** прямой `ISubAgentService` пишет `AgentStates` на
  каждый вызов с полным `TaskDescription` (draft) — без FK/retention (KI-269).
- **Truncation:** `LmStudio:MaxTokens=4096`; SubAgentService считает любой
  text-only ответ завершённым → длинный черновик может быть молча обрезан и
  «Approved»; нужен cap + warning.
- **Безопасность:** approval обязателен; no-tools sentinel; prompt-injection
  ограничен разделителями; XSS — только `escapeHtml`.
- **Админка:** виртуальный `reflector_agent` не пишет `agent.*` stats, critique-
  промпт не редактируется в Admin UI (KI-266).
- **Вне scope v2.2.0:** HITL между раундами, эскалация на внешнюю LLM
  (KI-267), Reflection как Background Job (KI-268).
- **F5-ловушки** (иначе блок «тихо» не восстановится): `isMultiAgentTool`,
  `debateTools` Set, `BuildStatusDto`, `getVerdictLabel` без `Failed`,
  запрет HITL-feedback-ветки для Reflection.

---

## 6. Новые KI (текстом)

Номера — следующие свободные после KI-265; в реестр `KNOWN_ISSUES.md`
**сознательно не писали** (design-only; RULES § 2.4 — при старте реализации).
Подробные формулировки — `DESIGN_REFLECTION.md § 14`.

| KI | Приоритет | Статус/план | Суть |
| :--- | :--- | :--- | :--- |
| **KI-266** | 🟢 Low | Planned (v2.2.x) | Reflection: прямой вызов `ISubAgentService` минует `AgentToolBase` → нет `agent.reflector_agent` в AuditLogs/`/api/admin/agents/stats`; `SelfCritiquePrompt` не редактируется в Admin UI. Решение: prompt-override-протокол для `Internal`-агентов (+обёртка) или ручной audit-лог; расширить Admin UI. |
| **KI-267** | 🟡 Medium | Planned (v2.2.x) | Reflection: гетерогенная модель самокритики / эскалация. Default — та же 4B (self-approval bias); coder-30B/4B смесь упирается в VRAM/свопы LM Studio; `Uncertain` можно эскалировать на внешнюю LLM (по образцу Actor-Critic). |
| **KI-268** | 🟡 Medium | Planned (v2.2.x) | Reflection как тип Background Job: `BackgroundJobRunner`/`start_background_job` знают только Orchestrator/Vision/Smoke; длинные Reflection-сессии нельзя выполнять в фоне. Добавить тип + валидацию + SSE/UI. |
| **KI-269** | 🟢 Low | Planned (v2.2.x) | Reflection: дублирование черновиков в `AgentStates` без retention. Прямой `ISubAgentService` пишет `AgentStates.TaskDescription = request.Task` на каждый вызов (`SubAgentService.cs:93-104`); у `AgentStates` нет ChatId-FK/cleanup → черновик хранится дважды и переживает удаление чата. Решение: `PersistState=false` для Reflection или retention. |

Также наследуются (не новые): **KI-241** (`TokensIn/Out=0`, нет usage в
`SubAgentTaskResult`), **KI-132** (не раздувать `tool_result` раундами),
**KI-264** (RULES § 2.18 не существует; `Migrations/Sqlite/` отсутствует).

---

## 7. Обоснование решений

### 7.1. Почему Reflection нужен, если уже есть Actor-Critic

1. **Actor-Critic в текущей реализации — только про код.**
   `CodeAgentWithReviewTool` жёстко зашит на `code_agent` + `code_reviewer_agent`
   (`:64-65`); для текстов/документов/планов/объяснений паттерн недоступен.
   Reflection — универсальная обёртка «черновик + самокритика» для **любого**
   артефакта (код, текст, ТЗ, инструкция).
2. **Разный экономический профиль.** Actor-Critic — это два разных «мозга»
   (и потенциальная эскалация): дороже по конфигу и вниманию пользователя.
   Reflection — один агент и два промпта: минимум сущностей, один конфиг,
   простой UI; подходит для «дёшево улучшить и проверить», а не для
   «независимого ревью».
3. **Четвёртый паттерн закрывает матрицу.** Actor-Critic (код), Debate
   (архитектурные решения), Orchestrator (декомпозиция), Reflection
   (самопроверка единичного артефакта) — Chat LLM получает понятный выбор
   по типу задачи (decision matrix в § 1.3), а не «ещё один похожий инструмент».
4. **Переиспользование инфраструктуры.** Сессии, раунды, SSE-транспорт, UI-блок,
   F5, concurrency-лимит и coordinator уже есть → Reflection стоит ~11 ч, а не
   «с нуля». Это и делает его осмысленным дополнением, а не дублем: **новых
   сущностей/миграций нет**.
5. **Честные границы.** Если нужен независимый критик, гетерогенная модель или
   эскалация — это Actor-Critic (или KI-267); Reflection не пытается их
   заменить, правило 19 в `DefaultSystemPrompt` разводит триггеры.

### 7.2. Ключевые архитектурные решения (ADR)

| Решение | Почему так |
| :--- | :--- |
| **Существующие сущности, без миграции** | колонки `ActorOutput`/`CriticVerdict`/`CriticFeedbackJson`/`FinalArtifactJson` семантически покрывают Reflection; прецедент Orchestrator; меньше риска (нет SqlServer/Sqlite migration drift) |
| **ADR-001: один агент через прямой `Func<ISubAgentService>` + `SystemPromptOverride`** | только так можно дать одному агенту два промпта; `AgentToolBase` пинит промпт дескриптора; прямой inject `ISubAgentService` дал бы DI-цикл (`SubAgentService→IToolRegistry`) |
| **Новые SSE `reflect_*`, но общий `state.debates`** | KI-244 требует новые события (семантика раунда другая), но тот же UI-блок; отдельный кэш потребовал бы дублировать F5/prefill/рендер без выгоды (ADR-003) |
| **`MaxRounds=2` (не 3)** | дешёвая самокритика: 2 раунда = максимум 4 вызова; `Approved` обычно на 1-м; конфигурируемо, hard-limit 5 |
| **`Ok` для `Uncertain`/`MaxRoundsReached`** | частичный артефакт пригоден; `Fail` только для ошибки/отмены — как в трёх существующих паттернах |
| **`AllowedTools=["__no_tools__"]`** | пустой список включает 11 дефолтных инструментов (факт `SubAgentService.cs:121-128`); sentinel + `UsedTools.Count==0` защищают от побочных эффектов |
| **Промпты в конфиге (appsettings), модель — из реестра** | draft/critique-промпты — часть конфигурации паттерна (рестарт); модель/MaxSteps — через существующий Admin UI registry (runtime, в пределах процесса) |
| **HITL/эскалация/Background Job — вне scope** | не нужны для MVP паттерна; вынесены в KI-267/KI-268, инфраструктура готова |
| **Design-only, без кода** | требование постановки; реализация — отдельными фазами после согласования DESIGN |

### 7.3. Соответствие RULES

- § 2.5 — документ в `docs/development/v2.2/`, house-style § 0–§ 15.
- § 2.10 — вывод в чат в 4 бэктиках (отчёт содержит блоки кода).
- § 2.4 — KI-266…269 зафиксированы текстом в DESIGN/report (регистрация в
  `KNOWN_ISSUES.md` — при старте реализации, чтобы не менять реестр design-only задачей).
- § 4.44/§ 4.51/§ 4.52/§ 4.53/§ 4.16/§ 4.17 — учтены в плане фаз и чек-листах.
- Ссылки на RULES § 2.18 **нет** (в v1.4.35 такого пункта не существует — KI-264).
