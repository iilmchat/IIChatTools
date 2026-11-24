# D12 — Vision-платформа: taskbar и Avalonia

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 4D
**Задачи:** KI-166 (часть 1), KI-143 (отложен)
**HEAD до спринта:** `665ed12` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/VisionAgent/TaskbarWindowResolver.cs` | + | EnumWindows/GetWindowText — поиск по заголовку |
| `IIChatTools.Services/Implementation/VisionAgent/LocalHarnessVisionBackend.cs` | ~ | FocusWindowByTitleAsync (Win32 SetForegroundWindow) |
| `docs/KNOWN_ISSUES.md` | ~ | KI-166 → Partially Fixed; KI-143 → заметка; сводка Planned 9 / Partially 6 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: TaskbarWindowResolver |
| `docs/development/reports/v2.3/D12_TASKBAR.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Отдельных тестов нет (Win32 EnumWindows требует desktop). Существующие
Vision-тесты с fake backend проходят.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1350 passed / 1358 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 12/18 |

---

## 4. Smoke

Vision-задача «переключись на окно Chrome» → `FocusWindowByTitleAsync("Chrome")`
находит видимое окно и ставит его в фокус; в логах KI-166.

---

## 5. Что не так

1. KI-166 часть 2 — кроп иконки taskbar + OCR подписей не реализованы;
   поиск по заголовку покрывает базовый сценарий «Win+1 по имени».
2. KI-143 (Avalonia overlay) перенесён — новый UI-фреймворк требует
   отдельного проекта и сравнения с текущим WPF overlay.
3. `FindWindow` берёт первое совпадение — при нескольких окнах с одинаковым
   заголовком выбор недетерминирован.

---

## 6. Новые KI

Новых KI нет. KI-166 → Partially Fixed.

---

## 7. Обоснование

1. **Заголовок вместо иконки.** Win32 API даёт надёжный способ найти окно
   без OCR иконок taskbar — быстрее и точнее для MVP.
2. **Переиспользование Win32Interop.** SetForegroundWindow уже используется
   в KI-155 (refocus Chrome) — нет новых P/Invoke.
3. **Avalonia не в этом спринте.** Есть риск добавить второй UI-стек в решение
   без острой необходимости — отложено с фиксацией причины.
