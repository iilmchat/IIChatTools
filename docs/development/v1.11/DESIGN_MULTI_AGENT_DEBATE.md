# DESIGN — Мультиагентные паттерны (Actor-Critic / Debate)

**Версия:** v1.11.0 (Draft)
**Дата:** 2026-10-01
**Статус:** Draft
**Связанные документы:** [RULES.md](../RULES.md) · [DESIGN v1.4 (Multi-Agent)](../v1.4/DESIGN.md) · [DESIGN v1.8 External-LLM](../v1.8/DESIGN_EXTERNAL_LLM.md) · [ARCHITECTURE.md](../ARCHITECTURE.md) · KI-126

---

## § 0. Резюме

Добавляем **автономное взаимодействие суб-агентов** с ролями
«Исполнитель» (Actor) и «Критик» (Critic). Цель — повысить качество
финального артефакта (кода, плана) за счёт итеративной критики,
Human-in-the-loop, эскалации на внешние модели при неуверенности.

**Скоуп v1.11.0 (Фаза 1):** Actor-Critic для `code_agent`.
**Фаза 2 (v1.11.x):** Debate для `planner_agent` (Pro / Contra / Judge).
**Фаза 3 (v2.0):** Orchestrator-Worker, Blackboard.

---

## § 1. Контекст и мотивация

### § 1.1. Проблема

Сейчас Chat-LLM вызывает суб-агентов **последовательно, без внутренней
критики**. Пример из smoke-теста:

- Задача: «Напиши функцию на Python, которая проверяет, является ли
  строка палиндромом».
- `code_agent` возвращает: `def is_palindrome(s): return s == s[::-1]`.
- Проблема: не учитывает регистр, пробелы, знаки препинания
  (`"A man, a plan"` — не пройдёт).
- **Ошибку ловит пользователь**, а не система.

### § 1.2. Идея

Добавить **второй контур ревью** между агентом и Chat-LLM: критик
проверяет артефакт, при замечаниях — агент переделывает. При неуверенности
критика — эскалация на внешнюю модель (DeepSeek / Claude / Gemini через
`external_llm_agent`, KI-109).

### § 1.3. Применяемые паттерны (5 базовых)

| Паттерн | Суть | Роли |
|:---|:---|:---|
| **Actor-Critic** | Один пишет — другой проверяет. Итерации до max-rounds или Approved. | Actor, Critic |
| **Debate** | Два агента защищают противоположные позиции. | Pro, Contra, Judge |
| **Reflection** | Один агент пишет → критикует сам себя → переписывает. | Actor (совмещённый) |
| **Orchestrator-Worker** | Manager декомпозирует задачу на параллельные подзадачи. | Orchestrator, Workers |
| **Blackboard** | Агенты пишут/читают в общее структурированное пространство. | Все (через shared state) |

**В v1.11.0 — только Actor-Critic.** Остальные — roadmap.

---

## § 2. Архитектура Actor-Critic

### § 2.1. Диаграмма потока

```
   Chat LLM
      │
      │ tool_call: code_agent_with_review({ task, withReview: true })
      ▼
   ┌───────────────────────────────────────────┐
   │  AgentDebateSessionService                │
   │  (state machine, max 3 rounds)            │
   └───────────────────────────────────────────┘
      │
      ├─► [Round 1] ── code_agent (actor) ──► черновик кода
      │       │
      │       ▼
      │   code_reviewer_agent (critic) ──► { Approved | Rejected | Uncertain }
      │       │
      │       ├─ Approved ──► финал
      │       ├─ Rejected ──► Round 2 (actor исправляет)
      │       └─ Uncertain ──► ask_external_llm (DeepSeek) — эскалация
      │
      ├─► [Round 2] ── code_agent (actor, учитывает feedback)
      │       │
      │       ▼
      │   code_reviewer_agent ──► verdict
      │       │
      │       └─ Approved ──► финал, иначе Round 3
      │
      └─► [Round 3] ── финальный, принудительно Approved
              │
              ▼
        SSE debate_completed → UI
```

### § 2.2. Роли

| Роль | Существующий агент? | SystemPrompt (краткая суть) |
|:---|:---:|:---|
| **Actor** | `code_agent` (есть) | «Напиши/исправь код по задаче. Если есть feedback — учти все замечания.» |
| **Critic** | `code_reviewer_agent` (**новый**) | «Ты — опытный код-ревьюер. Найди РЕАЛЬНЫЕ проблемы (edge cases, безопасность, ошибки). Не придирайся к стилю. Верни JSON {verdict, issues, summary}.» |
| **Escalation** | `external_llm_agent` (есть) | «Второе мнение: оцени код-артефакт и verdict критика.» |

### § 2.3. Промпт критика (полный)

```
Ты — опытный код-ревьюер с 10+ годами опыта. Твоя задача — найти
РЕАЛЬНЫЕ проблемы в коде, которые приведут к ошибкам, уязвимостям или
неприемлемому UX.

ПРАВИЛА:
1. Проверяй: edge cases (пустой ввод, граничные значения, unicode),
   обработку ошибок (исключения, таймауты), безопасность (SQL injection,
   path traversal, XSS), производительность (N+1, лишние аллокации).
2. НЕ придирайся к стилю именования, форматированию, комментариям —
   это не дело ревью.
3. Если код корректен по всем пунктам — verdict = "Approved".
4. Если есть замечания — verdict = "Rejected" + список issues.
5. Если НЕ УВЕРЕН (задача требует domain knowledge, которой у тебя нет) —
   verdict = "Uncertain" + конкретный вопрос для эскалации.

ФОРМАТ ОТВЕТА (строго JSON):
{
  "verdict": "Approved" | "Rejected" | "Uncertain",
  "issues": [
    { "severity": "Critical" | "Major" | "Minor",
      "location": "строка/функция",
      "description": "что не так",
      "suggestion": "как исправить" }
  ],
  "summary": "1-2 предложения"
}
```

---

## § 3. Управление циклом

### § 3.1. Условия завершения

| Условие | Приоритет |
|:---|:---:|
| Critic вернул `Approved` | 1 |
| Достигнут `MaxRounds` (default 3) | 2 |
| Превышен `TokenBudget` (default 50k токенов) | 3 |
| Пользователь отменил (Stop / Cancel) | 4 |
| Все внешние провайдеры недоступны (при эскалации) | 5 |

### § 3.2. Human-in-the-loop

Используем существующий `ChatApprovalCoordinator` (KI-054):

- **Опция A (default):** Human-in-the-loop **между раундами** — критик
  вернул `Rejected`, показывается модалка «Продолжить исправление?»
  (approve → Round 2; reject → финал с текущим черновиком).
- **Опция B:** Human-in-the-loop **только после max-rounds** — все раунды
  автоматические, финал показывается с вопросом «Принять / Ещё раунд?».

Выбор через настройку `SubAgents:code_agent_with_review:HumanApproval = "BetweenRounds" | "AtEnd" | "Never"`.

### § 3.3. Эскалация на внешнюю LLM

Триггер: `critic.verdict == "Uncertain"` И `AllowEscalation == true`.

Поток:
1. Формируется prompt для `ask_external_llm`:
   - task (исходная задача);
   - код actor'а;
   - issues критика;
   - конкретный вопрос критика.
2. `ask_external_llm(provider="deepseek", prompt=...)` (по умолчанию).
3. Ответ внешней LLM подаётся **второму раунду критика** как дополнительный контекст.
4. Verdict второго раунда — финальный.

Стоимость учитывается в `ExternalLlmBudgetTracker` (per-user $5/день).

---

## § 4. Модель данных

### § 4.1. Новые сущности

```csharp
/// <summary>
/// Сессия автономного взаимодействия агентов (Actor-Critic / Debate).
/// Привязана к чату и инициирована пользователем.
/// </summary>
public class AgentDebateSession : BaseEntity
{
    public int ChatId { get; set; }
    public Chat Chat { get; set; }

    public int InitiatedByUserId { get; set; }

    /// Исходная задача (текст от пользователя / Chat LLM).
    public string Task { get; set; }

    /// Тип сессии: ActorCritic | Debate | Reflection.
    public string PatternType { get; set; }

    /// Статус: Pending | InProgress | Completed | Failed | Cancelled.
    public string Status { get; set; }

    /// Финальный вердикт: Approved | Rejected | NeedsHuman | MaxRoundsReached.
    public string FinalVerdict { get; set; }

    /// Финальный артефакт (код / план) в JSON.
    public string FinalArtifactJson { get; set; }

    /// Снимок конфига (MaxRounds, модель, флаги) — для воспроизводимости.
    public string ConfigSnapshotJson { get; set; }

    public decimal TotalCostUsd { get; set; }
    public int TotalTokensIn { get; set; }
    public int TotalTokensOut { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<AgentDebateRound> Rounds { get; set; }
}

/// <summary>
/// Один раунд внутри сессии: actor-вывод + critic-вердикт.
/// </summary>
public class AgentDebateRound : BaseEntity
{
    public int SessionId { get; set; }
    public AgentDebateSession Session { get; set; }

    /// Порядковый номер раунда (1..N).
    public int RoundNumber { get; set; }

    /// Actor-вывод (код / текст).
    public string ActorOutput { get; set; }

    /// Verdict критика: Approved | Rejected | Uncertain.
    public string CriticVerdict { get; set; }

    /// Feedback критика (JSON со списком issues).
    public string CriticFeedbackJson { get; set; }

    /// Модель, использованная для actor.
    public string ActorModel { get; set; }

    /// Модель, использованная для critic.
    public string CriticModel { get; set; }

    /// Был ли раунд с эскалацией на внешнюю LLM.
    public bool WasEscalated { get; set; }

    /// Провайдер эскалации (deepseek / openai / ...), если WasEscalated.
    public string EscalationProvider { get; set; }

    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public decimal CostUsd { get; set; }
    public int DurationMs { get; set; }
}
```

### § 4.2. Миграция

- `AddAgentDebateSessions` (SqlServer).
- FK: `AgentDebateSession.ChatId` → `Chats(Id)` (Cascade).
- FK: `AgentDebateRound.SessionId` → `AgentDebateSessions(Id)` (Cascade).
- Индексы: `(ChatId, StartedAt)`, `(InitiatedByUserId, Status)`.

**⚠️ RULES § 3.15:** перед `dotnet ef migrations add` — установить
`Database:Provider = "SqlServer"` в `appsettings.Development.json`.

**⚠️ Sqlite (dev):** удалить `Data/iichattools-dev.db` (RULES § 4.25).

---

## § 5. Интеграция с Chat

### § 5.1. Новый инструмент `code_agent_with_review`

Top-level `ITool` в Chat (не наследник `AgentToolBase` — по образцу
`database_agent`, KI-097 § 4.1).

**Обязательно** добавить в `allowedNames` в `ChatStreamService` (RULES § 4.44) —
иначе LLM его не увидит.

### § 5.2. SSE-события

Расширение `ChatStreamEvent` (по образцу `tool_approval_required`, KI-054):

| Event | Payload |
|:---|:---|
| `debate_started` | `{ sessionId, task, actorAgent, criticAgent, maxRounds }` |
| `debate_round` | `{ sessionId, roundNumber, actorOutput, criticVerdict, criticFeedback, wasEscalated }` |
| `debate_escalated` | `{ sessionId, reason, externalProvider, costUsd }` |
| `debate_completed` | `{ sessionId, verdict, totalRounds, finalArtifact, totalCostUsd }` |

### § 5.3. Когда вызывать?

**Триггер по умолчанию:** Chat-LLM сам решает, вызывать `code_agent_with_review`
или обычный `code_agent`. В `Description` инструмента:

```
Использовать для СЛОЖНЫХ задач кодинга (алгоритмы, парсеры, обработка
данных, security-критичный код). НЕ использовать для простых операций
(rename, add import, тривиальные функции) — там достаточно code_agent.
```

**Опция:** настройка `SubAgents:code_agent_with_review:AlwaysReview = true`
— тогда Chat-LLM предпочтёт review-вариант при любом коде.

---

## § 6. Конфигурация

Секция `SubAgents:code_agent_with_review` в `appsettings.json`:

```jsonc
"code_agent_with_review": {
  "Enabled": true,
  "DisplayName": "Кодинг с ревью (Actor-Critic)",
  "Description": "Решение сложных задач кодинга с итеративным ревью.",
  "Model": "qwen/qwen3-4b-2507",           // actor
  "CriticModel": "qwen/qwen3-4b-2507",     // critic (default — same)
  "MaxRounds": 3,
  "TokenBudget": 50000,
  "HumanApproval": "BetweenRounds",         // BetweenRounds | AtEnd | Never
  "AllowEscalation": true,
  "EscalationProvider": "deepseek",         // из ExternalLlm:Providers
  "RequiresApproval": true,
  "AllowedTools": ["run_python", "run_javascript", "execute_command"],
  "SystemPrompt": "Ты — опытный разработчик. ..."
}
```

Аналогично для `SubAgents:code_reviewer_agent`:

```jsonc
"code_reviewer_agent": {
  "Enabled": true,
  "DisplayName": "Код-ревьюер",
  "Model": "qwen/qwen3-4b-2507",
  "MaxSteps": 3,
  "RequiresApproval": false,
  "AllowedTools": [],                        // критик не вызывает инструменты
  "SystemPrompt": "Ты — опытный код-ревьюер с 10+ годами опыта. ..."
}
```

---

## § 7. UI

### § 7.1. Варианты отображения

**Селектор** в toolbar чата (рядом с селектором модели или в настройках):

- **Диалог (Вариант 1):** раунды как отдельные пузыри. Actor и Critic
  визуально различаются (иконка 🤖 vs 🧐).

```
┌─────────────────────────────────────────────┐
│ 🤖 Actor (code_agent) — Round 1             │
│ ┌─────────────────────────────────────────┐ │
│ │ def parse_csv(text): ...                │ │
│ └─────────────────────────────────────────┘ │
├─────────────────────────────────────────────┤
│ 🧐 Critic (code_reviewer_agent) — Round 1   │
│ ⚠️ 3 issues (Major)                         │
│ • Не обрабатывает кавычки                   │
│ • CRLF внутри поля                          │
│ • Нет валидации на пустой ввод              │
├─────────────────────────────────────────────┤
│ 🤖 Actor — Round 2 (revised)                │
│ ┌─────────────────────────────────────────┐ │
│ │ import csv; def parse_csv(text): ...    │ │
│ └─────────────────────────────────────────┘ │
├─────────────────────────────────────────────┤
│ 🧐 Critic — Round 2                         │
│ ✅ Approved                                 │
└─────────────────────────────────────────────┘
```

- **Сворачиваемый (Вариант 4):** только финал + раскрывающийся блок.

```
┌─────────────────────────────────────────────┐
│ ✅ Финальный парсер CSV готов               │
│ ┌─────────────────────────────────────────┐ │
│ │ import csv; def parse_csv(text): ...    │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ После 2 раундов review (Total: $0.003):     │
│ • Round 1: 3 issues (Major)                 │
│ • Round 2: Approved                         │
│                                             │
│ ▼ Показать детали (12 сообщений)            │
└─────────────────────────────────────────────┘
```

### § 7.2. Селектор UI

- Хранится в `localStorage["chat.debateView"]` (`"dialog"` | `"collapsed"`).
- Иконка переключателя в toolbar чата.
- Настройка per-user (не per-chat) — по образцу `chat.sidebarCollapsed` (KI-079).

### § 7.3. Локализация

Новые ключи `.resx` (RU + EN):

- `DebateStarted`, `DebateRound`, `DebateEscalated`, `DebateCompleted`;
- `DebateActor`, `DebateCritic`, `DebateJudge`;
- `DebateVerdictApproved`, `DebateVerdictRejected`, `DebateVerdictUncertain`;
- `DebateViewDialog`, `DebateViewCollapsed`, `DebateViewSwitch`;
- `DebateIssuesCount`, `DebateCostTotal`, `DebateShowDetails`;

**Локализация JS** — через `data-*`-атрибуты (RULES § 4.17).

---

## § 8. API endpoints

| Метод | URL | Назначение |
|:---|:---|:---|
| `POST` | `/api/chat/{chatId}/debate` | Запуск Actor-Critic для задачи (прямой API) |
| `GET` | `/api/debate/{sessionId}` | Статус + список раундов |
| `GET` | `/api/debate/{sessionId}/rounds` | Только раунды (пагинация) |
| `POST` | `/api/debate/{sessionId}/cancel` | Остановить сессию |
| `POST` | `/api/debate/{sessionId}/inject` | Human feedback между раундами |

---

## § 9. Фазы работ

### § 9.1. Фаза 1 — Actor-Critic для `code_agent` (v1.11.0, ~5-6 ч)

| Шаг | Что | Оценка |
|:---|:---|:---:|
| 1A | Entities + миграция `AddAgentDebateSessions` | 40 мин |
| 1B | `IAgentDebateSessionService` + state machine | 60 мин |
| 1C | `code_reviewer_agent` (system prompt, регистрация, тесты) | 40 мин |
| 1D | `code_agent_with_review` (top-level ITool, allowedNames) | 60 мин |
| 1E | SSE-события + `ChatStreamEvent` расширение | 40 мин |
| 1F | Эскалация на `ask_external_llm` (verdict Uncertain) | 30 мин |
| 1G | UI: селектор диалог / сворачиваемый + рендер раундов | 90 мин |
| 1H | Локализация (.resx RU + EN), CHANGELOG | 30 мин |
| 1I | Тесты: unit (state machine) + integration (3 сценария) | 60 мин |
| 1J | Smoke + релиз v1.11.0 | 30 мин |

### § 9.2. Фаза 2 — Debate для `planner_agent` (v1.11.x, ~5-6 ч)

Три роли: `architect_pro_agent` (защищает REST), `architect_contra_agent`
(защищает gRPC), `architect_judge_agent` (синтез). Расширение модели данных
(роль в раунде).

### § 9.3. Фаза 3 — Orchestrator-Worker + Blackboard (v2.0, ~3-4 ч)

- `Orchestrator` — декомпозиция задачи.
- `Blackboard` — shared state (JSON-документ в БД).
- Workers — параллельные подзадачи.

---

## § 10. Риски и ограничения

| Риск | Митигация |
|:---|:---|
| **Галлюцинации критика** (та же модель = те же слепые зоны) | Эскалация на внешнюю LLM при Uncertain; опционально гетерогенная модель с самого начала |
| **Mode collapse** (агенты сходятся к шаблону) | Max 3 раунда; гетерогенные модели на критике |
| **Token cost ×10** | TokenBudget (50k) + max-rounds (3); логирование per-round |
| **Деградация после 2-3 раундов** | MaxRounds = 3; останавливаемся на последнем вердикте |
| **UI-шум** (много раундов → длинная лента) | Default = «сворачиваемый» вид; селектор на «диалог» |
| **Задержка ответа** (3 раунда × 30-60 с) | SSE покажет прогресс; для простого кода — обычный `code_agent` |

---

## § 11. Тестирование

### § 11.1. Unit

- `AgentDebateSessionService` — 6+ тестов:
  - Round 1 Approved → 1 раунд, финал.
  - Round 1 Rejected → Round 2 Approved → 2 раунда.
  - Round 1-3 Rejected → max-rounds reached.
  - Round 1 Uncertain → эскалация → Round 2 Approved.
  - Cancel в середине → статус Cancelled.
  - TokenBudget exceeded → Failed с частичным результатом.

### § 11.2. Integration

- Actor-Critic loop с mock LM Studio + mock external LLM.
- Проверка SSE-событий (debate_started / round / completed).
- Проверка cost-tracking (per-round + per-session).

### § 11.3. Smoke (ручной)

- «Напиши функцию-палиндром» → 2 раунда, финал Approved.
- «Напиши парсер CSV» → 2-3 раунда, финал.
- «Проверь код на безопасность» (мок-уязвимость) → Critic rejects → Round 2 → Approved.
- Проверка UI: селектор, диалог, сворачиваемый.

---

## § 12. Открытые вопросы

1. **Где хранить `ActorModel` / `CriticModel`** — в раунде или в сессии?
   **Решение:** в раунде (может меняться при эскалации).
2. **Учитывается ли cost эскалации в `ExternalLlmBudgetTracker`?**
   **Решение:** да (per-user $5/день).
3. **Retention для завершённых сессий?** По аналогии с `ChatRetention`
   (30 дней) — в v1.11.x.
4. **Concurrency:** максимум одновременных сессий на пользователя.
   **Решение:** 3 (по образцу `MaxBrowserSessionsPerUser`).
5. **Отдельный KI или часть KI-126?** Часть KI-126 (Фаза 1 v1.11.0).
6. **Гетерогенная модель по умолчанию?** Пока нет (тот же qwen3-4b);
   опционально — DeepSeek через эскалацию.

---

## § 13. Ссылки

- [RULES.md](../RULES.md) — правила разработки (v1.4.26+).
- [ARCHITECTURE.md](../ARCHITECTURE.md) — обзор архитектуры.
- [DESIGN v1.4](../v1.4/DESIGN.md) — Multi-Agent (база).
- [DESIGN v1.8 External-LLM](../v1.8/DESIGN_EXTERNAL_LLM.md) — эскалация.
- [KI-126](../KNOWN_ISSUES.md#ki-126) — запись в реестре.

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.10.1**