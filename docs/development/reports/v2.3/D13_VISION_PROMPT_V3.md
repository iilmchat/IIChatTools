# D13 — Vision-платформа: Prompt v3 и внешние VL

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 4D
**Задачи:** KI-139 (Documented), prompt v3
**HEAD до спринта:** `f2e82ab` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/VisionAgent/VisionSystemPrompt.cs` | ~ | v3: 14 actions, x2/y2, titleQuery |
| `IIChatTools.Services/Implementation/VisionAgent/VisionActionParser.cs` | ~ | Whitelist +3 действия |
| `IIChatTools.Services/Implementation/VisionAgent/VisionActionValidator.cs` | ~ | Whitelist +3 действия |
| `IIChatTools.Services/Implementation/VisionAgent/VisionAgentService.cs` | ~ | Case focus_window |
| `IIChatTools.Services/DTO/VisionAgent/VisionActionDto.cs` | ~ | TitleQuery |
| `docs/KNOWN_ISSUES.md` | ~ | KI-139 → Documented; сводка Planned 8 / Documented 22 |
| `CHANGELOG.md` | + | `[Unreleased] → Changed`: prompt v3 |
| `docs/development/reports/v2.3/D13_VISION_PROMPT_V3.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Новых тестов нет (промпт — строка, действия покрыты D11/D12).
Существующие VisionActionParser/VisionActionValidator тесты проходят.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1350 passed / 1358 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 13/18 |

---

## 4. Smoke

Planner LLM может вернуть `{"action":"focus_window","titleQuery":"Chrome"}` —
действие проходит валидацию и переводит фокус на окно Chrome.

---

## 5. Что не так

1. KI-139 (внешние VL Claude/OpenAI CUA) остаётся Documented — нет ключей
   и сети в offline-окружении; реализация — отдельный адаптер.
2. read_clipboard всё ещё не передаёт текст в Planner (часть 2 KI-165).
3. Промпт v3 вырос — проверка токенов (KI-178) остаётся актуальной.

---

## 6. Новые KI

Новых KI нет. KI-139 → Documented.

---

## 7. Обоснование

1. **Единый whitelist.** Parser, Validator и Prompt используют один и тот же
   список из 14 actions — рассинхрон невозможен.
2. **Additive-расширение.** Старые 11 actions работают как раньше.
3. **Внешние VL не имитируются.** Без реальных API-ключей адаптер был бы
   нерабочим — честно Documented.
