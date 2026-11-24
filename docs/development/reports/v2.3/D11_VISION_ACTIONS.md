# D11 — Vision-платформа: drag-select и clipboard

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.5 / § 4D
**Задачи:** KI-164, KI-165
**HEAD до спринта:** `e229571` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Interfaces/IVisionBackend.cs` | ~ | Default-методы DragSelectAsync / ReadClipboardAsync |
| `IIChatTools.Services/Implementation/VisionAgent/LocalHarnessVisionBackend.cs` | ~ | Реализации через SendInput / Win32 clipboard |
| `IIChatTools.Services/Implementation/VisionAgent/ClipboardReader.cs` | + | P/Invoke CF_UNICODETEXT |
| `IIChatTools.Services/DTO/VisionAgent/VisionActionDto.cs` | ~ | X2/Y2 для drag_select |
| `IIChatTools.Services/Implementation/VisionAgent/VisionAgentService.cs` | ~ | Cases drag_select / read_clipboard |
| `docs/KNOWN_ISSUES.md` | ~ | KI-164/165 → Partially Fixed; сводка Planned 10 / Partially 5 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: Vision backend actions |
| `docs/development/reports/v2.3/D11_VISION_ACTIONS.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Отдельных unit-тестов нет: Win32 SendInput/clipboard требуют живого desktop.
Существующие VisionAgentServiceTests (fake backend) проходят — default-методы
интерфейса не ломают fakes.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1350 passed / 1358 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 11/18 |

---

## 4. Smoke

1. Vision-задача «выдели текст мышью от (100,100) до (400,120)» →
   `drag_select` выполняется через SendInput, текст выделен.
2. `read_clipboard` — в логах длина скопированного текста (> 0).

---

## 5. Что не так

1. **KI-165 часть 2.** Текст из clipboard пока не возвращается в Planner —
   требуется поле результата шага в `VisionStepDto` и промпт-поддержка.
2. `drag_select` координаты X2/Y2 должны приходить от Planner — промпт
   `VisionSystemPrompt` пока не упоминает новое действие (будет в D13).
3. Clipboard P/Invoke — только Windows; на других ОС default-метод вернёт null.

---

## 6. Новые KI

Новых KI нет. KI-164/165 → Partially Fixed.

---

## 7. Обоснование

1. **Default interface methods.** Новые методы не сломали ни одну из
   реализаций (RULES § 4.46) — вызов идёт через `IVisionBackend`.
2. **SendInput переиспользован.** Drag собран из уже существующих
   `MakeMouseInput` + `SendInputBatch` — минимальный риск.
3. **Часть 2 не имитируется.** Честнее оставить read_clipboard частичным,
   чем выдавать текст «через log» как полноценный результат.
