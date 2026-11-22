# S09 — Orchestrator Admin UI

**Дата:** 2026-11-22
**Правила:** `docs/development/RULES.md` v1.4.36
**KI спринта:** KI-273 (KI-246/247/248/274 перенесены — см. § 5)
**HEAD до спринта:** `541d14e` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/Orchestrator/AdminOrchestratorService.cs` | ~ | KI-273: ISubAgentRegistry + предупреждения о неизвестных/отключённых агентах |
| `IIChatTools.Tests/UnitTests/Orchestrator/AdminOrchestratorServiceTests.cs` | ~ | Обновлён конструктор (Mock<ISubAgentRegistry>) |
| `docs/KNOWN_ISSUES.md` | ~ | KI-273 → Fixed; сводка Fixed 195 / Planned 26 |
| `docs/TODO_KI.md` | ~ | 1 чекбокс закрыт |
| `CHANGELOG.md` | + | `[Unreleased] → Fixed`: KI-273 |
| `docs/development/reports/v2.2/S09_KI.md` | + | Настоящий отчёт |

---

## 2. Покрытие

`AdminOrchestratorServiceTests` (24 теста) прогнаны после изменения
конструктора. Новых тестов нет — валидация whitelist просто логируется,
не меняет результат сохранения.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1326 passed / 1334 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| TODO_KI | 31 закрытый чекбокс (S01–S09) |

---

## 4. Smoke

Сохранить в `/admin → Оркестратор` whitelist с опечаткой (`web-agent`)
и с отключённым агентом → в логах два предупреждения с упоминанием KI-273;
настройки при этом сохраняются (free-form остаётся).

---

## 5. Что не так

1. KI-246 (fallback воркера), KI-247 (pre-checks), KI-248 (HITL) и
   KI-274 (транзакция persist) перенесены — каждый требует отдельного
   изменения в OrchestrateTaskTool / IAppSettingsService.
2. KI-273 — только предупреждение в логах; UI пока не показывает список
   доступных агентов (datalist) — это часть исходной постановки.
3. Проверка registry выполняется при каждом сохранении настроек —
   накладные расходы минимальны (десятки агентов).

---

## 6. Новые KI

Новых KI нет. KI-246/247/248/274 остаются Planned.

---

## 7. Обоснование

1. **Предупреждение, а не блокировка.** Whitelist остаётся free-form
   (по ТЗ KI-245); отказ от сохранения сломал бы обратную совместимость
   и планы администратора.
2. **Инъекция реестра.** ISubAgentRegistry уже Singleton в DI — новый
   аргумент конструктора не требует регистрации; обновлён только один
   тестовый вызов (RULES § 4.34).
3. **Перенос остальных.** Durable/transaction/HITL-изменения — архитектурные,
   их честнее оставить открытыми, чем делать мнимые фиксы.
