# S08 — Orchestrator / Reflection / Background Jobs

**Дата:** 2026-11-22
**Правила:** `docs/development/RULES.md` v1.4.36
**KI спринта:** KI-266, KI-267, KI-269 (KI-265 перенесён — см. § 5)
**HEAD до спринта:** `9dfb9c9` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/Tools/Debate/ReflectTaskTool.cs` | ~ | KI-266: IAuditService; KI-267: SelfCritiqueModel; KI-269: PersistState=false |
| `IIChatTools.Services/DTO/SubAgent/SubAgentTaskRequest.cs` | + | KI-269: свойство `PersistState` (default true) |
| `IIChatTools.Services/Implementation/SubAgentService.cs` | ~ | KI-269: условная persists в AgentStates |
| `IIChatTools.Tests/UnitTests/Debate/ReflectTaskToolTests.cs` | ~ | Новый аргумент конструктора (NoopAuditService) |
| `docs/KNOWN_ISSUES.md` | ~ | 3 KI → Fixed; сводка Fixed 194 / Planned 27 |
| `docs/TODO_KI.md` | ~ | 3 чекбокса закрыты |
| `CHANGELOG.md` | + | `[Unreleased] → Fixed`: спринт S08 |
| `docs/development/reports/v2.2/S08_KI.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Существующие `ReflectTaskToolTests` (25+ тестов) прогнаны после изменения
конструктора — новых тестов не потребовалось (логика audit не влияет на
результат инструмента). Полный прогон прилагается в § 3.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1326 passed / 1334 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| TODO_KI | 30 закрытых чекбоксов (S01–S08) |

---

## 4. Smoke

1. Вызвать `reflect_task` в чате → в `/admin → Аудит` появляется запись
   `agent.reflector_agent` с verdict и моделью (KI-266).
2. Задать `SubAgents:reflect_task:SelfCritiqueModel = gemma-4-12b` →
   SSE `reflect_started.selfCritiqueModel` показывает другую модель (KI-267).
3. После Reflection — в `AgentStates` нет дублей черновика (KI-269).

---

## 5. Что не так

1. **KI-265 (durable queue) перенесён.** Реализация outbox/polling/Hangfire
   с idempotency — отдельный крупный спринт; запись остаётся Planned.
2. KI-266 — audit пишется только при успешном завершении; при ошибке/отмене
   записи нет (можно добавить Status=Error/Cancelled в следующем спринте).
3. KI-267 — конфиг читается через индексер; смена модели без перезапуска
   не применяется (приемлемо для текущего цикла).
4. KI-269 — `PersistState=false` отключает запись в `AgentStates`, но
   `AgentDebateRound` по-прежнему хранит черновик (это нужно для F5).

---

## 6. Новые KI

Новых KI нет. KI-265 остаётся Planned (перенос в следующий спринт).

---

## 7. Обоснование

1. **Минимальные правки в DI.** Добавление `IAuditService` в конструктор
   `ReflectTaskTool` не требует регистрации (уже в DI) — обновлён только
   один вызов в тестах (RULES § 4.34/§ 4.53).
2. **SelfCritiqueModel через конфиг.** Позволяет включить гетерогенную
   самокритику без пересборки и без риска VRAM (по умолчанию та же модель).
3. **PersistState на уровне запроса.** Флаг в `SubAgentTaskRequest` с
   default `true` сохраняет поведение всех остальных вызовов; только
   Reflection явно отключает сохранение.
4. **Перенос KI-265.** Durable queue — архитектурное изменение; честнее
   оставить открытым, чем делать мнимую реализацию.
