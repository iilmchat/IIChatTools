# D10 — Безопасность и аудит: correlationId и tool audit

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.3 / § 4C
**Задачи:** KI-169
**HEAD до спринта:** `ed3ca35` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Utils/CorrelationContext.cs` | + | AsyncLocal correlationId с Begin/Dispose |
| `IIChatTools.Services/Implementation/SubAgentService.cs` | ~ | IAuditService + запись `tool.<name>` с correlationId |
| `IIChatTools.Tests/UnitTests/CorrelationContextTests.cs` | + | 2 теста |
| `docs/KNOWN_ISSUES.md` | ~ | KI-169 → Fixed; сводка Fixed 210 / Documented 21 |
| `docs/TODO_KI.md` | ~ | 1 запись обновлена |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: CorrelationContext |
| `docs/development/reports/v2.3/D10_AUDIT_CORRELATION.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `Begin_SetsAndRestores` | Установка и восстановление предыдущего значения |
| `Begin_WithoutId_GeneratesNonEmpty` | Автогенерация correlationId |

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1350 passed / 1358 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 10/18 |

---

## 4. Smoke

Запустить `code_agent` в чате → в `/admin → Аудит` видны записи
`tool.read_file`, `tool.save_file` и т.д. с одинаковым correlationId
в ParametersJson (вместе с `agent.code_agent` из AgentToolBase).

---

## 5. Что не так

1. CorrelationId пока не пробрасывается из SSE-событий в UI (только логи/audit).
2. AuditLog не имеет отдельной колонки CorrelationId — значение хранится
   в ParametersJson (без миграции).
3. При сбое записи аудита tool-call не прерывается (по образцу AgentToolBase).

---

## 6. Новые KI

Новых KI нет. KI-169 → Fixed (v2.3.0).

---

## 7. Обоснование

1. **AsyncLocal вместо сигнатур.** CorrelationContext не меняет публичные
   контракты сервисов — добавление не затронуло ни одного вызова (RULES § 4.34).
2. **Аудит на границе tool.** SubAgentService — единственное место, где
   выполняются внутренние tool'ы; запись там гарантирует полноту.
3. **Без схемы БД.** CorrelationId в ParametersJson — компромисс, позволяющий
   реализовать сквозную трассировку без миграции в этом спринте.
