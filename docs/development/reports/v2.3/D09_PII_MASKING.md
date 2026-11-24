# D09 — Безопасность: маскирование PII

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.4 / § 4C
**Задачи:** KI-138
**HEAD до спринта:** `4a30421` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Utils/PiiMasker.cs` | + | Маскирование email/phone/card/СНИЛС |
| `IIChatTools.Services/Implementation/ExternalLlm/ExternalLlmClient.cs` | ~ | `MaskMessagesIfEnabled` в SendAsync/StreamAsync |
| `IIChatTools.API/appsettings.json` | + | `ExternalLlm:PiiMaskingEnabled = true` |
| `IIChatTools.Tests/UnitTests/PiiMaskerTests.cs` | + | 4 теста |
| `docs/KNOWN_ISSUES.md` | ~ | KI-138 → Fixed; сводка Fixed 209 / Planned 12 |
| `docs/TODO_KI.md` | ~ | 1 чекбокс закрыт |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: PiiMasker |
| `docs/development/reports/v2.3/D09_PII_MASKING.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `Mask_Email_Replaced` | email → [EMAIL] |
| `Mask_RussianPhone_Replaced` | +7 916 123-45-67 → [PHONE] |
| `Mask_CardAndSnils_Replaced` | карта → [CARD], СНИЛС → [SNILS] |
| `Mask_NoPii_Unchanged` | текст без PII не меняется |

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1348 passed / 1356 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 9/18 |

---

## 4. Smoke

Отправить во внешний LLM запрос с email/телефоном в тексте → в логах
провайдера (при доступе) и в payload нет исходных PII, только плейсхолдеры.

---

## 5. Что не так

1. Маскирование применяется только к ExternalLlmClient; LM Studio (локальный)
   не маскирует — данные не уходят наружу.
2. Реgex телефона покрывает RU-форматы; зарубежные форматы (+1, +44) не
   маскируются.
3. Флаг `PiiMaskingEnabled` можно выключить — ответственность администратора.

---

## 6. Новые KI

Новых KI нет. KI-138 → Fixed (v2.3.0).

---

## 7. Обоснование

1. **Маскирование на границе.** PII заменяется непосредственно перед
   сериализацией payload — минимальный риск утечки в логи/сеть.
2. **Флаг по умолчанию включён.** Безопасное поведение по умолчанию
   (security by design), отключение — осознанное решение администратора.
3. **Индекser конфига.** RULES § 4.56 — надёжно с fake-конфигурациями тестов.
