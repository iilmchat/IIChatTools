# TODO_DESIGN_SPRINTS — спринты программы улучшений v2.3

**Дата:** 2026-11-22
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md`
**Правила:** `docs/development/RULES.md` v1.4.36
**Всего:** 6 этапов / 18 спринтов (D01–D18), по 2-4 задачи на спринт.

Каждый спринт: build 0 warnings/0 errors, `dotnet test` зелёный, отчёт
по RULES § 2.18 в `docs/development/reports/v2.3/Dxx_<name>.md`,
коммит со ссылками на KI, пуш в `main`.

---

## Этап A — Фундамент надёжности

| Спринт | Задачи | KI |
| :--- | :--- | :--- |
| D01 | Метрики-декоратор, реальные .Inc(), счётчики токенов | KI-044, KI-241 |
| D02 | Unique index `(ChatId, SortOrder)` + retry; транзакция AppSettings | KI-262, KI-274 |
| D03 | Outbox-таблица, idempotency key, resilience helpers | KI-265 (часть 1) |

## Этап B — Провайдеры

| Спринт | Задачи | KI |
| :--- | :--- | :--- |
| D04 | `IEmbeddingProvider`: LmStudio + OpenAI-compatible | KI-227 (доработка) |
| D05 | `IVectorStore`: Sqlite → Qdrant adapter | KI-243 |
| D06 | `ILLMProvider`: формат `<provider>:<model>`, обратная совместимость | KI-226 |
| D07 | `IDatabaseProvider`: PostgreSQL, SqlServer snapshot | KI-242, KI-099, KI-091 |

## Этап C — Безопасность и аудит

| Спринт | Задачи | KI |
| :--- | :--- | :--- |
| D08 | Per-user mail: сущность, IDataProtector, UI /profile | KI-108 |
| D09 | PII-masker до отправки во внешние LLM/VL | KI-138 |
| D10 | Audit decorator для всех ITool, correlationId | KI-169, KI-266 |

## Этап D — Vision-платформа

| Спринт | Задачи | KI |
| :--- | :--- | :--- |
| D11 | Расширение IVisionBackend: drag_select + read_clipboard | KI-164, KI-165 |
| D12 | Taskbar icons + Avalonia overlay | KI-166, KI-143 |
| D13 | Внешние VL провайдеры, Vision prompt v3 | KI-139 |

## Этап E — Качество и документы

| Спринт | Задачи | KI |
| :--- | :--- | :--- |
| D14 | Генератор сводки KI, ARCHITECTURE.md | KI-254, KI-275, KI-147 |
| D15 | Чеклист приёмки v2.3, UX-бандл, Feedback JSONL | KI-253, KI-237, KI-240 |
| D16 | PATCH/DELETE fallback, split тестов, модалка редактора | KI-047, KI-074, KI-082 |

## Этап F — Верификация и релиз

| Спринт | Задачи | KI |
| :--- | :--- | :--- |
| D17 | Контрактные/нагрузочные тесты, smoke v2.3 | — |
| D18 | Релиз v2.3.0, RELEASES.md, CHANGELOG | — |

---

## Статус

| Этап | Спринты | Состояние |
| :--- | :--- | :--- |
| A | D01–D03 | ⬜ Не начат |
| B | D04–D07 | ⬜ Не начат |
| C | D08–D10 | ⬜ Не начат |
| D | D11–D13 | ⬜ Не начат |
| E | D14–D16 | ⬜ Не начат |
| F | D17–D18 | ⬜ Не начат |

Отчёты завершённых KI-спринтов (S01–S20) —
в `docs/development/reports/v2.2/`.
