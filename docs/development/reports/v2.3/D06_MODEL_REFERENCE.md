# D06 — Провайдеры: ссылка на модель <provider>:<model>

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.1 / § 4B
**Задачи:** KI-226 (доработка п. 4)
**HEAD до спринта:** `7c6c459` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Utils/ModelReference.cs` | + | Парсер/формат `<provider>:<model>` |
| `IIChatTools.API/Controllers/ModelsController.cs` | ~ | Поля `refs` и `providers[].reference` |
| `IIChatTools.Tests/UnitTests/ModelReferenceTests.cs` | + | 5 тестов парсера |
| `IIChatTools.Tests/UnitTests/ModelsControllerTests.cs` | ~ | Проверка refs/reference |
| `docs/KNOWN_ISSUES.md` | ~ | Заметка D06 в KI-226 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: ModelReference |
| `docs/development/reports/v2.3/D06_MODEL_REFERENCE.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `Parse_WithPrefix_ReturnsProviderAndModel` | Разбор `routerai:qwen/...` |
| `Parse_LegacyNoPrefix_DefaultsToLmStudio` | Старое значение → lmstudio |
| `Parse_Empty_DefaultsToLocalModel` (3 кейса) | null/пусто → local-model |
| `GetModelsAsync_IncludesExternalProviders` (обновлён) | `reference = deepseek:deepseek-chat`, `refs[0] = lmstudio:<default>` |

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1336 passed / 1344 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 6/18 |

---

## 4. Smoke

`GET /api/models` — в ответе помимо `data` и `providers` есть `refs`
(например, `lmstudio:qwen/qwen3-4b-2507`) и `providers[].reference`
(`deepseek:deepseek-chat`). Старые чаты с `Chat.Model = "qwen/qwen3-4b"`
продолжают работать (трактуются как lmstudio).

---

## 5. Что не так

1. `Chat.Model` в БД пока хранится в старом формате — новое поле `refs`
   пока только для UI; миграция значений — после D07 (смена провайдера БД).
2. Per-chat переключение провайдера (KI-226 п. 5) не реализовано —
   требуется UI + контракт запроса.
3. Парсер не проверяет известные имена провайдеров — любая строка до `:` принимается.

---

## 6. Новые KI

Новых KI нет. KI-226 уже Fixed, добавлена заметка о доработке.

---

## 7. Обоснование

1. **Обратная совместимость.** Отсутствие префикса трактуется как lmstudio —
   старые чаты и конфиги не ломаются.
2. **Additive-контракт.** Новые поля `refs`/`reference` не влияют на
   существующих клиентов `/api/models`.
3. **Простой парсер.** Проверка известных провайдеров будет в D07 вместе с
   единым `ILLMProvider`-реестром.
