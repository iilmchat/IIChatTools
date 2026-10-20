# REPORT — Phase 5: Tests + Release (v2.2.0, KI-244 Reflection)

**Дата:** 2026-10-20
**Версия:** v2.2.0 (release-prep)
**HEAD:** `fb51a94` (Фазы 1–4) + незакоммиченные изменения Фазы 5
**Правила:** `docs/development/RULES.md` v1.4.35
**Статус:** **не закоммичено** (жёсткое правило постановки). Git tag `v2.2.0` и
GitHub Release **не созданы** — см. § 5.
**Design:** [`docs/development/v2.2/DESIGN_REFLECTION.md`](../v2.2/DESIGN_REFLECTION.md) (Implemented, v1.1)

---

## 1. Файлы (+/-)

### Создано

| Файл | Строк | Назначение |
| :--- | ---: | :--- |
| `IIChatTools.Tests/UnitTests/Debate/ReflectTaskToolTests.cs` | 663 | **25 `[Fact]`** unit-тестов `ReflectTaskTool` (fake-сервисы, без Moq) |
| `docs/development/reports/v2.2/PHASE5_TESTS.md` | ~190 | Настоящий отчёт (7 разделов) |

### Изменено

| Файл | +/- | Что |
| :--- | :--- | :--- |
| `IIChatTools.Services/Implementation/Tools/Debate/ReflectTaskTool.cs` | +13 / −1 | **2 test-driven фикса:** (1) все раунды `Rejected` → `MaxRoundsReached`; (2) `FinalArtifactJson` — JSON `{finalArtifact}` (KI-271) |
| `CHANGELOG.md` | +18 / −0 | `[Unreleased] → Added`: Reflection (KI-244); `Fixed`: `MaxRoundsReached` |
| `docs/KNOWN_ISSUES.md` | +66 / −18 | KI-244 → **Fixed (v2.2.0)** с реализацией; новые **KI-270** (Planned), **KI-271** (Fixed); сводка 250 KI |
| `README.md` | +9 / −6 | Chat-tools **25 → 26**, ToolRegistry **72 → 73** (+ строка `reflect_task` в таблице и примечании, breakdown `66 → 73`, DI `46 → 47`) |
| `docs/development/v2.2/DESIGN_REFLECTION.md` | +7 / −3 | Статус `Draft → Implemented (v2.2.0)`, версия `v1.0 → v1.1`, HEAD, заметка о KI-270/271 |
| `docs/development/ARCHITECTURE.md` | +12 / −2 | Версия `2.0.0 → 2.2.0`; § 1 — Reflection в списке + «мультиагентная серия (4 паттерна)»; § 6.2 — заметка о `reflect_task` |
| `Directory.Build.props` | +3 / −3 | `<Version>2.2.0</Version>`, `<Copyright>… v2.2.0</Copyright>` |

`git status --short`:

```text
 M CHANGELOG.md
 M Directory.Build.props
 M IIChatTools.Services/Implementation/Tools/Debate/ReflectTaskTool.cs
 M README.md
 M docs/KNOWN_ISSUES.md
 M docs/development/ARCHITECTURE.md
 M docs/development/v2.2/DESIGN_REFLECTION.md
?? IIChatTools.Tests/UnitTests/Debate/ReflectTaskToolTests.cs
?? docs/development/reports/v2.2/PHASE5_TESTS.md
```

**Коммитов, тегов и релизов нет.**

---

## 2. Покрытие (25 тестов)

### Структура инструмента (3)

1. `Name_ReturnsReflectTask`
2. `RequiresApprovalByDefault_IsTrue`
3. `Parameters_ContainsTaskAndMaxRounds`

### Валидация (7)

4. `ExecuteAsync_EmptyTask_ReturnsFail` — пустой/пробельный `task`; planner/agent не вызываются
5. `ExecuteAsync_TaskTooLong_ReturnsFail` — 8001 символ, сообщение содержит «8000»
6. `ExecuteAsync_AgentMissing_ReturnsFail` — `reflector_agent` не зарегистрирован
7. `ExecuteAsync_AgentDisabled_ReturnsFail` — `Disabled = true`
8. `ExecuteAsync_SelfCritiquePromptMissing_ReturnsFail` — нет `SelfCritiquePrompt`
9. `ExecuteAsync_MaxRoundsZero_ClampsToOne` — `maxRounds=0 → 1` (в config snapshot)
10. `ExecuteAsync_MaxRoundsTen_ClampsToFive_AndAliasWorks` — алиас `max_rounds=10 → 5`

### Happy path (3)

11. `ExecuteAsync_ApprovedFirstRound_ReturnsOk` — 1 раунд, `CompleteAsync("Approved")`, 2 LLM-вызова
12. `ExecuteAsync_RejectedThenApproved_ReturnsApproved` — Rejected → rewrite → Approved; 4 вызова; 2 раунда
13. `ExecuteAsync_AllRejected_ReturnsMaxRoundsReached` — `CompleteAsync("MaxRoundsReached")`, `ToolResult.Ok`

### Edge cases (5)

14. `ExecuteAsync_CritiqueUncertain_StopsWithUncertain` — без rewrite
15. `ExecuteAsync_RejectedWithoutIssues_TreatedAsUncertain` — раунд сохранён с `Rejected`, итог `Uncertain`
16. `ExecuteAsync_DuplicateDraft_StopsWithUncertain` — rewrite не изменил текст; второй раунд не пишется
17. `ExecuteAsync_InitialDraftNull_ReturnsFail` — null от агента → `CancelAsync`, без `CompleteAsync`
18. `ExecuteAsync_CritiqueNull_StopsWithUncertain` — 0 раундов сохранено, финал = draft

### Persistence (1)

19. `ExecuteAsync_AddRoundAsync_PersistsRoundFields` — `RoundNumber` / `ActorOutput` (draft) / `CriticVerdict` / `CriticFeedbackJson` (issues), 2 раунда

### Отмена (2)

20. `ExecuteAsync_PreCancelledToken_ReturnsFail` — `Fail("Операция отменена.")`, сессия не создаётся
21. `ExecuteAsync_CancelledAfterDraft_CancelsSessionAndFails` — `CancelAsync` вызван, `CompleteAsync` — нет

### SSE (3)

22. `ExecuteAsync_ReflectStarted_EmittedOnce` — `reflect_started` ×1, DTO: `patternType="Reflection"`, `agent=reflector_agent`
23. `ExecuteAsync_ReflectRound_EmittedPerRound` — `reflect_round` ×N (2), draft/verdict/issues
24. `ExecuteAsync_ReflectCompleted_EmittedOnce` — `reflect_completed` ×1, verdict/totalRounds/finalArtifact

### Контекст (1)

25. `ExecuteAsync_NoChatId_PassesZeroToStartAsync` — `ChatId=null → StartAsync(chatId: 0)`, `PatternType="Reflection"`

> Итого **25 `[Fact]`**: структура 3 + валидация 7 + happy path 3 + edge 5 +
> persistence 1 + отмена 2 + SSE 3 + контекст 1 = **25**.

---

## 3. Результаты

### Сборка

```text
> dotnet build IIChatTools.sln -v minimal --nologo
Сборка успешно завершена.
    Предупреждений: 0
    Ошибок: 0
```

**0 warnings / 0 errors** (в т.ч. xUnit-анализаторы: `xUnit2029`/`xUnit2031`
исправлены).

### Полный прогон тестов

```text
> dotnet test IIChatTools.sln --no-build -v minimal --nologo
Пройден! : не пройдено 0, пройдено 1245, пропущено 8, всего 1253,
           длительность 2 s. - IIChatTools.Tests.dll (net10.0)
```

- **1245 passed / 0 failed / 8 skipped / 1253 total** — ровно ожидаемый
  результат (1220 базовых + 25 новых; 8 skip — предсуществующие `[Skip]`:
  реальные внешние LLM и реальный Chromium).
- Точечный прогон новых тестов:
  ```text
  --filter "FullyQualifiedName~ReflectTaskToolTests"
  Пройден! : не пройдено 0, пройдено 25, пропущено 0, всего 25
  ```

### Версия

`Directory.Build.props`: `<Version>2.2.0</Version>`,
`© 2026 RuChating (iilmchat) · IIChatTools v2.2.0`.

---

## 4. Smoke (ручной сценарий)

Автоматически **не выполнялся**: нужны LM Studio (модель `qwen/qwen3-4b-2507`),
запущенное приложение и браузер/UI. Чек-лист для приёмки v2.2.0:

**Подготовка:** `dotnet run --project IIChatTools.API`; LM Studio на
`localhost:8034`; в чате модель Chat. Убедиться, что в `appsettings` есть
`SubAgents:reflect_task` и `SubAgents:reflector_agent` (`Internal: true`,
`SystemPrompt` + `SelfCritiquePrompt`).

1. **Reflection выбирается:** запрос «Напиши ТЗ на фичу X (цель, требования,
   edge cases, критерии приёмки)» → Chat вызывает `reflect_task` (approval-модалка
   появляется; `RequiresApprovalByDefault=true`).
2. **Approve → SSE-блок:** в чате появляется `.chat-debate-block` с заголовком
   «🪞 Reflection (самокритика)»; `reflect_started` → карточка раунда
   «📝 Черновик» + «🧐 Самокритика» (verdict + issues); итог — `reflect_completed`.
3. **1 раунд Approved:** если черновик хорош — `Approved` за 1 раунд, финальный
   артефакт отображается, totalRounds=1.
4. **Rejected → rewrite:** на задаче с типичной ошибкой (например, «функция
   палиндрома без учёта регистра/пробелов») критика возвращает `Rejected` +
   issues → следующий раунд с переписанным черновиком → `Approved`.
5. **Лимит раундов:** при упорных `Rejected` (или провокационном промпте) —
   `MaxRoundsReached` (default 2), финал = последний draft, `ToolResult.Ok`.
6. **Отмена:** Stop/Cancel во время 2-го раунда → `ToolResult.Fail("Операция
   отменена.")`, сессия `Cancelled`, UI не «висит».
7. **F5:** обновить страницу посреди и после выполнения → блок Reflection
   восстанавливается (раунды, вердикты, финальный артефакт); убедиться, что
   `MetadataJson` содержит `debateSessionId`.
8. **Переключатель dialog/collapsed:** глобальная настройка `chat.debateView`
   применяется и к Reflection-блоку.
9. **Простая задача** («2+2») → Chat **не** вызывает Reflection (правило
   `DefaultSystemPrompt`, анти-триггеры).
10. **Отключение агента:** `SubAgents:reflector_agent:Enabled=false` → отработать
    после рестарта; вызов `reflect_task` должен вернуть Fail
    «не зарегистрирован или отключён» (через реальный registry).

---

## 5. Что не так

1. **Git tag + GitHub Release НЕ сделаны.** Жёсткое правило #1 постановки —
   «НЕ КОММИТИТЬ». Тег `v2.2.0` на текущий HEAD (`fb51a94`) указал бы на
   состояние **без** Фазы 5 (без тестов/версии 2.2.0), а GitHub Release требует
   push. Поэтому тег/релиз отложены до коммита. Команды для владельца:
   ```text
   git add -A
   git commit -m "chore(release): v2.2.0 — Reflection (KI-244) + tests/docs"
   git tag -a v2.2.0 -m "v2.2.0 — Reflection (KI-244)"
   git push origin main --tags
   gh release create v2.2.0 --title "v2.2.0 — Reflection" --notes-file CHANGELOG.md
   ```
   Версия в `Directory.Build.props` уже `2.2.0`, но **репозиторий формально
   остаётся на v2.1.1-коммите** — это осознанный release-prep, не выпуск.
2. **KI-270 (Medium):** `SubAgents:reflect_task:MaxRounds` / `TokenBudget`
   **игнорируются** — в `ReflectTaskTool` default `maxRounds=2` и
   `TokenBudget=50000` захардкожены (`ReflectTaskTool.cs:61-64`, `:87`);
   `IConfiguration` не инжектится. Зарегистрировано в KI-270 (Planned).
3. **KI-271 (Fixed v2.2.0):** `FinalArtifactJson` хранил сырой текст, а не
   `{finalArtifact: ...}`; F5 спасал fallback `parseFinalArtifact`, но контракт
   расходился с DESIGN § 4.3. Исправлено в Фазе 5 (`JsonConvert.SerializeObject`).
4. **KI-269 (Planned):** прямой вызов `ISubAgentService` по-прежнему пишет
   `AgentStates.TaskDescription` (полный draft) без FK/retention — дублирование
   данных. Не блокер релиза.
5. **KI-266/267/268 (Planned):** виртуальный `reflector_agent` не пишет
   `agent.*` stats и не редактируется в Admin UI; гетерогенная модель/эскалация
   не реализованы; Reflection недоступен как Background Job.
6. **Расхождение DESIGN ↔ реализация (не критично):** в tool нет параметра
   `context` (только `task` + `maxRounds`); нет HITL между раундами;
   `TokensIn/Out=0`, `CostUsd=0` (наследие KI-241); `TokenBudget` не
   enforced (только `MaxRounds`). Зафиксировано в KI-270/267/241.
7. **Косметика кода:** в `ReflectTaskTool.cs` остались комментарии-маркеры
   `// <--- Добавлено`, `// <--- ДОБАВИТЬ` (следы реализации). На сборку/тесты
   не влияют; рекомендуется убрать при коммите (в Фазе 5 не трогал, чтобы не
   расширять scope).
8. **README-числа были противоречивы** (66 / 72 / 69 / 25 в разных местах).
   В Фазе 5 нормализовано: **73** ToolRegistry, **26** Chat-visible, DI **47**;
   breakdown пересчитан. При следующих фичах — обновлять все 4 точки
   (feature-list, заголовок таблицы, итог таблицы, дерево ToolRegistry).
9. **Финальный прогон:** см. § 3 (1245/1253) — целевые цифры постановки
   совпали точно.

---

## 6. Новые KI

Добавлены в `docs/KNOWN_ISSUES.md` (RULES § 2.4). Номера свободны после KI-269.

| KI | Приоритет | Статус | Суть |
| :--- | :--- | :--- | :--- |
| **KI-270** | 🟡 Medium | Planned (v2.2.x) | Reflection: настройки `SubAgents:reflect_task` (`MaxRounds`, `TokenBudget`, `RequiresApproval`) игнорируются — значения захардкожены (2 / 50000). Решение: inject `IConfiguration`, default из конфига + clamp, TokenBudget в snapshot, тест «конфиг переопределяет default». |
| **KI-271** | 🟢 Low | ✅ **Fixed (v2.2.0)** | Reflection: `FinalArtifactJson` хранил сырой текст вместо JSON `{finalArtifact}` (F5 работал только через fallback). Исправлено: сериализация перед `CompleteAsync`. |

Остальные KI Reflection (из DESIGN/Фаз 1–4) остаются в силе:
**KI-266** (audit/Admin UI виртуального агента), **KI-267** (гетерогенная
модель/эскалация), **KI-268** (Reflection как Background Job), **KI-269**
(дублирование черновиков в `AgentStates`). Наследуются **KI-241** (tokens=0) и
**KI-132** (без `rounds[]` в `ToolResult.Data`).

Сводка реестра: **250 уникальных KI** (Fixed 160, Planned 51, Documented 25,
Deferred 8, Partially Fixed 4, In Progress 1, Won't Fix 1); секций `### KI-` — 251.

---

## 7. Обоснование

### 7.1. Почему эти тесты

- **Контракт, а не LM Studio.** Reflection — это цикл из 2–4 вызовов агента;
  тестировать его с реальной моделью нельзя (нестабильно/дорого). Тесты
  фиксируют **поведение инструмента**: порядок фаз, условия выхода, вердикты,
  persistence-поля, отмену и SSE — то, что ломается при рефакторинге.
- **Покрыты все обязательные кейсы постановки:** валидация (5 сценариев),
  happy-path 1/2 раунда, `MaxRoundsReached`, `Uncertain`, `Rejected` без issues,
  duplicate-guard, null-ответы, persistence, отмена (до и после draft),
  SSE-события (started ×1 / round ×N / completed ×1), отсутствие `ChatId`.
- **Тесты сверяют `ToolResult.Ok` vs `Fail`** осознанно: `Ok` для
  `Approved`/`Uncertain`/`MaxRoundsReached` (артефакт пригоден), `Fail` — только
  для ошибок/отмены — как в трёх существующих паттернах (важно для Chat LLM:
  `success=false` заставляет её перепланировать/извиняться).
- **Ровно 25 тестов** — целевой прирост 1220 → 1245 (совпало с ожиданием
  постановки); каждый тест проверяет отдельное поведение, без дублирования.

### 7.2. Почему такие Fake'и

- **`FakeSubAgentService`** (очередь ответов `draft/critique/rewrite`) — даёт
  полный контроль над сценарием и позволяет проверить **какой именно
  `SystemPromptOverride`** ушёл в каждый вызов (draft vs critique vs rewrite).
  С Moq это было бы громоздко (`SetupSequence` + захват аргументов).
- **`FakeAgentDebateSessionService`** — запоминает `StartAsync`/`AddRoundAsync`/
  `CompleteAsync`/`CancelAsync` в списках: тесты проверяют не «вызвано ли», а
  **с какими данными** (PatternType, MaxRounds, RoundNumber, ActorOutput,
  CriticVerdict, CriticFeedbackJson, вердикт финала) — это и есть persistence-контракт.
- **`FakeSubAgentRegistry`** — один дескриптор, легко переключить
  `null`/`Disabled`/`SelfCritiquePrompt=null` без конфигурации DI.
- **`FakeEventWriter`** (наследник `ChannelWriter<ChatStreamEvent>`) — вместо
  реального `Channel` позволяет читать события **синхронно** сразу после
  `ExecuteAsync`, без `ReadAllAsync`/таймаутов; реализованы
  `TryWrite`/`WriteAsync`/`WaitToWriteAsync`/`TryComplete`.
- **Без Moq** — по требованию постановки (fake-классы) и потому что все
  зависимости — узкие интерфейсы; fake'и проще читать и они детерминированы.
  В соседних тестах Moq — исторически; смешивать не стали.
- **Никаких `Task.Delay`/`Thread.Sleep`** — все сценарии детерминированы,
  прогон 25 тестов ~85 мс.

### 7.3. Почему 2 test-driven фикса в production-коде

Тесты выявили расхождения реализации с постановкой/DESIGN:
1. **`MaxRoundsReached`:** без фикса все-Rjected сессии завершались вердиктом
   `Rejected` — пользователь/UI не отличали «критика против» от «лимит
   исчерпан»; постановка требует `CompleteAsync("MaxRoundsReached")`.
   Минимальная правка (3 строки) + покрыта тестом.
2. **`FinalArtifactJson` = JSON:** DESIGN § 4.3 и все три существующих паттерна
   пишут `{finalArtifact}`; F5-парсер UI ожидает JSON (сейчас спасал fallback).
   Исправлено `JsonConvert.SerializeObject`; `data`/SSE по-прежнему отдают
   готовый текст.

Обе правки — в границах Phase 5 (tests + release polish), не меняют публичный
контракт `ITool`/DTO и покрыты тестами (KI-271 Fixed).

### 7.4. Почему тег/релиз не созданы

Жёсткое правило постановки **«НЕ КОММИТИТЬ»** приоритетнее пункта «Git tag +
GitHub Release»: тег на незакоммиченное состояние технически невозможен без
изменения HEAD, а тег на текущий HEAD (`fb51a94`) выпустил бы v2.2.0 **без**
тестов/версии/документации Фазы 5. Поэтому релиз переведён в **release-prep**:
версия 2.2.0, CHANGELOG/KNOWN_ISSUES/README/ARCHITECTURE/DESIGN обновлены,
сборка и 1245 тестов зелёные; финальные `git commit`/`tag`/`push`/`gh release`
выполняет владелец (§ 5, команды готовы).
