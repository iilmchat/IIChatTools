# D01 — Фундамент надёжности: метрики

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.3 / § 4A
**Задачи:** KI-044, KI-241
**HEAD до спринта:** `3e068c8` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/AuditService.cs` | ~ | KI-044: `AuditEntriesTotal.WithLabels(status).Inc()` + Error при сбое |
| `IIChatTools.Services/Implementation/LmStudioClient.cs` | ~ | KI-044: `LmStudioRequestsTotal` (Success/Timeout/Error) в CompleteAsync и ChatStreamAsync |
| `IIChatTools.Services/Implementation/Debate/AgentDebateSessionService.cs` | ~ | KI-241: оценка токенов при usage=0, totals сессии |
| `IIChatTools.Tests/UnitTests/Debate/AgentDebateSessionServiceTests.cs` | + | KI-241: тест оценки токенов |
| `docs/KNOWN_ISSUES.md` | ~ | KI-044/241 → Fixed; сводка Fixed 205 / Documented 22 |
| `docs/TODO_KI.md` | ~ | 2 записи обновлены |
| `CHANGELOG.md` | + | `[Unreleased] → Fixed`: D01 |
| `docs/development/reports/v2.3/D01_METRICS.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `AddRoundAsync_ZeroTokens_EstimatesFromText` | KI-241: при usage=0 токены оцениваются (40 симв. → 10, 80 → 20) и попадают в totals |

Метрики Prometheus проверяются вручную через `/metrics` (§ 4).

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1327 passed / 1335 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 1/18 |

---

## 4. Smoke

1. Совершить несколько tool-вызовов и записать действия →
   `curl /metrics` показывает ненулевой `iichattools_audit_entries_total{status="Success"}`.
2. Вызвать LM Studio → `iichattools_lmstudio_requests_total{status="Success"} > 0`;
   при таймауте — label `Timeout`, при ошибке — `Error`; Stop (внешняя отмена)
   не увеличивает счётчик ошибок.
3. Завершить Debate-сессию с воркером без usage → `AgentDebateRound.TokensIn/Out > 0`.

---

## 5. Что не так

1. Оценка токенов (chars/4) приблизительна; точные usage от провайдера
   имеют приоритет. Для tiktoken-точности нужен `ITokenCounter` (будущий спринт).
2. SSE-стрим считается успешным и при завершении без явного `done` —
   это осознанный компромисс (иначе счётчик занижался бы).
3. Метрики не имеют label `model` — детализация в логах.

---

## 6. Новые KI

Новых KI нет. Закрыто: KI-044, KI-241 → Fixed (v2.3.0).

---

## 7. Обоснование

1. **Инкремент в месте события.** Метрика пишется там, где происходит событие
   (AuditService, LmStudioClient), а не в фоновом опросе — это гарантирует
   точность и простоту.
2. **Внешняя отмена ≠ ошибка.** По RULES § 4.48 `OperationCanceledException`
   при `cancellationToken.IsCancellationRequested` не считается провалом.
3. **Оценка вместо нуля.** Для Debate-воркеров ноль токенов хуже приближения:
   статистика сессии становится осмысленной без изменения контракта воркеров.
