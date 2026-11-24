# D16 — Качество: POST-fallback для PATCH/DELETE

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 4E
**Задачи:** KI-047 (KI-074/082 отложены)
**HEAD до спринта:** `6ad714d` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.API/Controllers/ChatController.cs` | + | POST /api/chats/{id}/update и /delete |
| `docs/KNOWN_ISSUES.md` | ~ | KI-047 → Fixed; KI-074/082 → заметки; сводка Fixed 213 / Deferred 6 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: POST fallback |
| `docs/development/reports/v2.3/D16_API_FALLBACK.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Новых тестов нет: эндпоинты делегируют уже покрытым методам
`UpdateChatAsync` / `DeleteChatAsync`. Полный прогон — § 3.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1350 passed / 1358 total, 0 failed, 8 skipped |
| generate-ki-summary -Check | OK |
| Design-спринты | 16/18 |

---

## 4. Smoke

Через прокси, блокирующий PATCH/DELETE: `POST /api/chats/{id}/update`
с тем же `UpdateChatRequest` меняет название; `POST /api/chats/{id}/delete`
удаляет чат. Ответы идентичны основным эндпоинтам.

---

## 5. Что не так

1. Frontend не определяет доступность PATCH/DELETE автоматически — fallback
   доступен только по прямому вызову (feature detection — отдельная задача).
2. KI-074 (split тестов) и KI-082 (модалка редактора) остаются Deferred —
   структурные изменения, не влияющие на стабильность релиза.
3. Новые POST-роуты не задокументированы в USER_GUIDE — только в CHANGELOG.

---

## 6. Новые KI

Новых KI нет. KI-047 → Fixed.

---

## 7. Обоснование

1. **Делегирование, а не копирование.** Теневые эндпоинты вызывают те же
   методы контроллера — нет риска расхождения бизнес-логики.
2. **Additive-контракт.** Основные PATCH/DELETE остаются без изменений.
3. **Deferred честно.** KI-074/082 требуют структурных правок — не стали
   «фиксироваться ради галочки» в конце Design-программы.
