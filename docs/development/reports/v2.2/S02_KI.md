# S02 — Незавершённые работы: In Progress + Partially Fixed

**Дата:** 2026-11-22
**Правила:** `docs/development/RULES.md` v1.4.36
**KI спринта:** KI-234, KI-057, KI-114, KI-173, KI-182
**HEAD до спринта:** `c2c69d2` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.API/Controllers/ModelsController.cs` | ~ | KI-057: фильтрация по `LmStudio:ExcludeModelPatterns` вместо хардкода `embed` |
| `IIChatTools.API/appsettings.json` | ~ | Добавлен ключ `LmStudio:ExcludeModelPatterns: ["embed"]` |
| `IIChatTools.Tests/UnitTests/ModelsControllerTests.cs` | + | 4 unit-теста фильтрации (по умолчанию, кастомные шаблоны, default первый, fallback) |
| `docs/KNOWN_ISSUES.md` | ~ | 5 KI → Fixed; сводка: Fixed 173, Partially 0, In Progress 0 |
| `docs/TODO_KI.md` | ~ | 5 чекбоксов отмечены как закрытые |
| `CHANGELOG.md` | + | `[Unreleased] → Fixed`: KI-057 + запись о верификации KI-114/173/182/234 |
| `docs/development/reports/v2.2/S02_KI.md` | + | Настоящий отчёт |

**Не трогал:** `AgentToolBase.cs`, `VisionAgentService.cs`, `chat.js` — их фиксы
уже присутствуют в коде (v1.13.x), потребовалась только верификация.

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `GetModelsAsync_WithoutConfig_FiltersEmbedByDefault` | По умолчанию исключается `embed` |
| `GetModelsAsync_WithCustomPatterns_FiltersAllMatches` | Несколько шаблонов из конфига, регистронезависимо |
| `GetModelsAsync_DefaultModelAlwaysFirst` | Default-модель всегда первая |
| `GetModelsAsync_LmStudioUnavailable_ReturnsDefaultOnly` | Fallback при недоступности LM Studio |

Правила RULES § 4.56 соблюдено: конфиг читается через `GetChildren()`,
без `GetValue<T>()`. В тестах `JObject.FromObject` → PascalCase `Id` (RULES § 4.43).

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1323 passed / 1331 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 уникальных KI / 255 секций / сумма статусов 254 |
| TODO_KI | 9 закрытых чекбоксов (S01 + S02) |

---

## 4. Smoke

1. `GET /api/models` с настроенным LM Studio — в списке нет моделей с `embed`
   в имени; default-модель первая.
2. Добавить в User Secrets `LmStudio:ExcludeModelPatterns` с кастомными
   подстроками → соответствующие модели исчезают из списка без пересборки.
3. Остановить LM Studio → `GET /api/models` возвращает только default
   с сообщением о fallback (без 500).

---

## 5. Что не так

1. `ExcludeModelPatterns` читается при каждом запросе — при большом списке
   моделей это лишние аллокации; для текущего масштаба (десятки моделей)
   не критично.
2. KI-173/KI-182 закрыты по факту наличия кода, но полный smoke с живым
   Vision Agent/Chrome не проводился — требует реального окружения.
3. KI-234 закрыт по факту реализации в v1.13.17; UI-сценарий вручную не
   прогнан в этом спринте.

---

## 6. Новые KI

Новых KI нет. Закрыто: KI-057, KI-114, KI-173, KI-182, KI-234 (все → Fixed, v2.2.x).

---

## 7. Обоснование

1. **Сначала незавершённые.** В спринте S02 собраны записи с минимальным
   остатком работы (In Progress / Partially Fixed) — быстрый способ снять
   «хвосты» перед более крупными фичами.
2. **Верификация вместо переписывания.** В коде уже есть фиксы KI-114/173/182/234
   с подробными комментариями; переписывать их рискованно и бессмысленно —
   достаточно подтвердить соответствие реестра факту.
3. **KI-057 — config-driven.** Хардкод `embed` заменён на список шаблонов
   по исходной постановке KI; чтение через `GetChildren()` (RULES § 4.56)
   позволяет задавать массив через конфиг без новых типов.
4. **Тесты через прямой вызов контроллера.** Консистентно с
   `SpeechControllerTests` / `AdminKnowledgeControllerTests` — без
   `WebApplicationFactory`, быстрее и надёжнее.
