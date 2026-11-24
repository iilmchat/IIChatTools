# D02 — Фундамент надёжности: гонки и транзакции

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.2 / § 4A
**Задачи:** KI-262, KI-274
**HEAD до спринта:** `a1653f4` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Data/AppDbContext.cs` | ~ | KI-262: уникальный индекс `(ChatId, SortOrder)` |
| `IIChatTools.Services/Implementation/BackgroundJobs/TodoService.cs` | ~ | KI-262: retry до 3 попыток при `DbUpdateException` |
| `IIChatTools.Services/Interfaces/IAppSettingsService.cs` | ~ | KI-274: `UpsertBatchAsync` (default fallback для fakes) |
| `IIChatTools.Services/Implementation/AppSettingsService.cs` | ~ | KI-274: атомарный batch в одном `SaveChangesAsync` |
| `IIChatTools.Services/Implementation/Orchestrator/AdminOrchestratorService.cs` | ~ | KI-274: 5 ключей одним batch-вызовом |
| `docs/KNOWN_ISSUES.md` | ~ | KI-262/274 → Fixed; сводка Fixed 207 / Planned 17 |
| `docs/TODO_KI.md` | ~ | 2 чекбокса закрыты |
| `CHANGELOG.md` | + | `[Unreleased] → Fixed`: D02 |
| `docs/development/reports/v2.3/D02_RELIABILITY.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Существующие тесты `AdminOrchestratorServiceTests` (проверка 5 ключей,
отсутствие дублей при повторном update) и `TodoServiceTests` (последовательные
SortOrder) прогнаны полностью — см. § 3.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1327 passed / 1335 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 2/18 |

---

## 4. Smoke

1. Два быстрых `add_todo` параллельно → SortOrder 0 и 1, без ошибки
   (при коллизии retry пересчитывает максимум).
2. Сохранить настройки Orchestrator в `/admin` → все 5 ключей в БД;
   при искусственном сбое на 3-м ключе — ни один не записан (атомарность).

---

## 5. Что не так

1. Уникальный индекс применён в модели EF Core; для **существующих** БД
   нужна миграция (Sqlite/SqlServer) — отдельная задача в D07.
2. `UpsertBatchAsync` в default interface реализации (для fakes) неатомарен —
   production `AppSettingsService` переопределяет метод.
3. Retry в TodoService не различает причину `DbUpdateException` — повтор
   возможен и при других ошибках БД (но не более 3 раз).

---

## 6. Новые KI

Новых KI нет. Закрыто: KI-262, KI-274 → Fixed (v2.3.0).

---

## 7. Обоснование

1. **Индекс + retry.** Один индекс гарантирует отсутствие дублей на уровне
   БД; retry делает гонку прозрачной для пользователя.
2. **Один `SaveChangesAsync` = одна транзакция.** EF Core автоматически
   оборачивает сохранение набора сущностей — не нужен явный `BeginTransaction`,
   что упрощает код и тесты.
3. **Default interface method.** Добавление метода не сломало 3 fake-реализации
   (RULES § 4.34/§ 4.46); вызов всегда идёт через интерфейс.
