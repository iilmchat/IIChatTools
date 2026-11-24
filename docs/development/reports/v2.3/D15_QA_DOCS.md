# D15 — Качество и документы: чеклист и Feedback export

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 4E
**Задачи:** KI-240, KI-253, KI-237
**HEAD до спринта:** `94dbe13` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.API/Controllers/AdminFeedbackController.cs` | + | Admin-only JSONL экспорт feedback |
| `docs/qa/MANUAL_TESTING_GUIDE.md` | ~ | § 6.1 smoke v2.3 (9 сценариев), история v2.3.0 |
| `docs/KNOWN_ISSUES.md` | ~ | KI-240 → Fixed, KI-237 → Partially Fixed; сводка Fixed 212 / Planned 5 / Partially 7 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: feedback export |
| `docs/development/reports/v2.3/D15_QA_DOCS.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Новых unit-тестов нет (endpoint требует AppDbContext + авторизацию Admin).
Существующий набор тестов прогнан полностью — § 3.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1350 passed / 1358 total, 0 failed, 8 skipped |
| generate-ki-summary -Check | OK |
| Design-спринты | 15/18 |

---

## 4. Smoke

1. Поставить 👍 на пару assistant-сообщений → `GET /api/admin/feedback/export`
   под Admin возвращает NDJSON с этими оценками.
2. Открыть `docs/qa/MANUAL_TESTING_GUIDE.md` — § 6.1 содержит 9 сценариев v2.3.

---

## 5. Что не так

1. KI-237 (UX backlog) только частично закрыт — добавлен чеклист, но сами
   UX-правки (бандл) не реализованы.
2. Экспорт не фильтрует по дате/чату — при большом объёме вернёт всё сразу.
3. Содержимое сообщений попадает в JSONL (PII) — контур Admin-only, но
   при выгрузке датасета нужна дополнительная маскировка (кандидат на KI).

---

## 6. Новые KI

Новых KI нет. KI-240 → Fixed, KI-237 → Partially Fixed, KI-253 → note.

---

## 7. Обоснование

1. **Изолированный контроллер.** AdminFeedbackController не меняет
   AdminController — никаких изменений в его конструкторе и тестах.
2. **NDJSON, а не CSV.** Строка = объект — удобно для датасетов LLM.
3. **Чеклист как документация.** § 6.1 фиксирует smoke-покрытие всех
   изменений D01–D15 без переписывания базовых сценариев v1.x.
