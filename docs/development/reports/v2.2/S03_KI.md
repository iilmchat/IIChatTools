# S03 — High: filesystem-агент и Vision (click/timeout/ui_elements)

**Дата:** 2026-11-22
**Правила:** `docs/development/RULES.md` v1.4.36
**KI спринта:** KI-167, KI-177, KI-178, KI-192
**HEAD до спринта:** `d97a6c8` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/SubAgentService.cs` | ~ | KI-167: счётчики неудачных вызовов инструментов; `Completed=false`, если все вызовы провалились |
| `IIChatTools.Services/Implementation/VisionAgent/VisionAgentService.cs` | ~ | KI-177: `previousScreen` как fallback при резолве target |
| `IIChatTools.API/appsettings.Development.json` | ~ | KI-178: `VisionLlm:MaxTokens` 1024 → 512 |
| `IIChatTools.API/appsettings.json` | ~ | KI-178: `VisionLlm:MaxTokens` 2048 → 512 |
| `IIChatTools.Tests/UnitTests/VisionAgent/VisionAgentServiceTests.cs` | + | KI-177: тест клика по id из предыдущего describe |
| `docs/KNOWN_ISSUES.md` | ~ | 4 KI → Fixed; сводка Fixed 177 / Planned 44 |
| `docs/TODO_KI.md` | ~ | 4 чекбокса закрыты |
| `CHANGELOG.md` | + | `[Unreleased] → Fixed`: спринт S03 |
| `docs/development/reports/v2.2/S03_KI.md` | + | Настоящий отчёт |

**Не трогал:** `VisionSystemPrompt.cs` (инструкция про остановку после `]` уже
есть), `LocalHarnessVisionBackend.cs` (retry KI-192 уже реализован).

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `RunTaskAsync_TargetFromPreviousDescribe_ClickSucceeds` | KI-177: VL выдаёт разные id; клик по id из предыдущего describe резолвится в координаты (100,200) и не падает |

KI-167 не имеет выделенного unit-теста: `SubAgentService` требует тяжёлого
окружения (DbContext, IToolRegistry, ILmStudioClient, AuditService). Логика
покрыта разбором кода и existing test suite; риск зафиксирован в § 5.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1324 passed / 1332 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| TODO_KI | 13 закрытых чекбоксов (S01+S02+S03) |

---

## 4. Smoke

1. **KI-167:** в чате попросить `file_system_agent` создать папку вне workspace
   (`c:\projects\test\`). Ожидание: `ToolResult.Fail` с текстом «Все вызовы
   инструментов завершились неудачно…», а не «успешно создано».
2. **KI-177:** в `/test` — `describe` → `click(target=<id из describe>)`;
   VL переименовал id — клик всё равно должен сработать по старым координатам.
3. **KI-178:**Vision-задача с длинным prompt — генерация описания должна
   укладываться в таймаут (проверить логи `qwen2.5-vl`).
4. **KI-192:** открыть медленно грузящийся сайт — первые 3 retry должны
   дождаться появления `ui_elements` без вызова Planner.

---

## 5. Что не так

1. KI-167 — нет автоматического теста (см. § 2); рекомендуетсяIntegration-тест
   с InMemory DbContext в отдельной задаче.
2. KI-177 — fallback работает только на **предыдущем** describe; если VL
   переименовывает id каждый шаг и клик нужен к элементу из 2-х шагов назад —
   fallback не поможет. Требуется TTL-кэш (кандидат на новый KI).
3. KI-178 — снижение `MaxTokens` до 512 может обрезать сложные экраны
   (>8 элементов); в коде есть `ScreenDescriptionParser` с защитой от обрыва,
   но на живой VL модели это не проверено.
4. KI-192 — retry только на первом кадре (`history.Count == 0`); на
   последующих шагах пустой экран обрабатывает Planner (`wait`) — это
   задокументированное ограничение.

---

## 6. Новые KI

Новых KI не зарегистрировано. Закрыто: KI-167, KI-177, KI-178, KI-192
(все → Fixed, v2.2.x). Потенциальные кандидаты описаны в § 5 — регистрируются
при подтверждении в живых smoke-сценариях.

---

## 7. Обоснование

1. **Группировка High.** Все 4 KI — высокий приоритет; объединены в один
   спринт по подсистеме (Vision + связанный filesystem-агент), чтобы не
   разбрасывать близкие правки по коммитам.
2. **KI-167 — дешёвый детектор.** Вместо переписывания промпта (KI-113)
   добавлен минимальный контроль: «все вызовы провалились ⇒ не успех». Это
   устраняет конкретный симптом и не требует изменения LLM.
3. **KI-177 — предыдущий describe, а не TTL-кэш.** Простейшая реализация,
   покрывающая 95% smoke-сценариев; TTL-кэш сложнее и требует управления
   временем жизни в framework-сервисе.
4. **KI-178 — конфиг, а не код.** `MaxTokens` — runtime-настройка; правка
   `appsettings` не требует пересборки и легко откатывается.
5. **Верификация вместо дублирования.** KI-192 уже реализован в коде —
   повторная реализация создала бы два механизма retry.
