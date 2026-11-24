# D03 — Фундамент надёжности: resilience и outbox (часть 1)

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.2 / § 4A
**Задачи:** KI-265 (часть 1)
**HEAD до спринта:** `03c53c7` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/Resilience/ResiliencePolicy.cs` | + | Timeout/retry helper с backoff + jitter |
| `IIChatTools.Tests/UnitTests/Resilience/ResiliencePolicyTests.cs` | + | 3 теста (retry, non-transient, отмена) |
| `docs/KNOWN_ISSUES.md` | ~ | KI-265 → Partially Fixed; сводка Planned 16 / Partially 1 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: ResiliencePolicy |
| `docs/development/reports/v2.3/D03_RESILIENCE.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `ExecuteWithRetryAsync_TransientFailure_RetriesAndSucceeds` | HttpRequestException → повтор → успех (2 вызова) |
| `ExecuteWithRetryAsync_NonTransientFailure_NoRetry` | InvalidOperationException → без повтора |
| `ExecuteWithRetryAsync_ExternalCancellation_ThrowsImmediately` | Отменённый токен → OperationCanceledException, 0 вызовов |

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1330 passed / 1338 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 3/18 |

---

## 4. Smoke

Проверка через unit-тесты (внешние сервисы не требуются). Применение
политики к конкретным HTTP-клиентам — в D04–D07 по мере перехода на адаптеры.

---

## 5. Что не так

1. **Outbox-очередь (часть 2) не реализована.** `BackgroundJobQueue`
   остаётся in-memory; восстановление `Pending` из БД работает, но гарантии
   «ровно один раз» нет. Перенесено в D07 (вместе с миграциями).
2. Политика пока не применена к реальным вызовам — только инфраструктура
   и тесты. Внедрение — в следующих спринтах адаптеров.
3. Transient-классификация ограничена HttpRequestException/Timeout/IOException;
   HTTP 5xx от LM Studio пока не считается transient (Status-коды не исключения).

---

## 6. Новые KI

Новых KI нет. KI-265 → Partially Fixed (часть 1).

---

## 7. Обоснование

1. **Инфраструктура до внедрения.** Единый helpers исключит дублирование
   timeout/retry-логики в каждом адаптере (D04–D07).
2. **Внешняя отмена отдельно.** По RULES § 4.48 distinguish timeout vs Stop —
   тест явно проверяет 0 вызовов при отменённом токене.
3. **Часть 2 после миграций.** Outbox требует новой таблицы — логичнее
   делать вместе с D07 (IDatabaseProvider + миграции).
