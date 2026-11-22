# S05 — Mail Agent: контекст и честность ответов

**Дата:** 2026-11-22
**Правила:** `docs/development/RULES.md` v1.4.36
**KI спринта:** KI-111, KI-113 (KI-108 перенесён — см. § 5)
**HEAD до спринта:** `c29917a` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/DTO/ToolExecutionContext.cs` | + | KI-111: поле `RecentChatContext` |
| `IIChatTools.Services/Implementation/ChatStreamService.cs` | ~ | KI-111: `BuildRecentChatContext(messages)` + заполнение в обоих `execContext` |
| `IIChatTools.Services/Implementation/Tools/SubAgent/AgentToolBase.cs` | ~ | KI-111: подмешивание снимка в `SubAgentTaskRequest.Context` |
| `IIChatTools.API/appsettings.json` | ~ | KI-113: правило 7 в `SubAgents:mail_agent:SystemPrompt` |
| `IIChatTools.API/appsettings.Development.json` | ~ | KI-113: то же |
| `IIChatTools.Tests/IntegrationTests/AgentToolBaseTests.cs` | + | KI-111: тест передачи снимка истории |
| `docs/KNOWN_ISSUES.md` | ~ | 2 KI → Fixed; сводка Fixed 183 / Planned 38 |
| `docs/TODO_KI.md` | ~ | 2 чекбокса закрыты |
| `CHANGELOG.md` | + | `[Unreleased] → Fixed`: спринт S05 |
| `docs/development/reports/v2.2/S05_KI.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `ExecuteAsync_RecentChatContext_AppendedToSubAgentRequest` | KI-111: снимок истории из `ToolExecutionContext` попадает в `SubAgentTaskRequest.Context` |

KI-113 — конфиг-правка промпта; автоматического теста нет (требуется живая
LLM), проверено по содержимому строки в обоих `appsettings*.json`.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1326 passed / 1334 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| TODO_KI | 19 закрытых чекбоксов (S01–S05) |

---

## 4. Smoke

1. В чате: «Прочитай письмо про Алису» → затем «Прочитай его полностью».
   Ожидание: агент видит UID из предыдущего сообщения и не делает повторный
   `list_emails` (проверить в логах/AuditLogs).
2. `mail_agent` с исчерпанным MaxSteps: финальный ответ не содержит «успешно»,
   а явно говорит «Не удалось: … Нужны дополнительные шаги».

---

## 5. Что не так

1. **KI-108 перенесён.** Per-user почта требует новой сущности `UserMailAccount`,
   миграции БД, шифрования через `IDataProtector` и UI в `/profile` — это
   выходит за рамки одного спринта. Запись остаётся Planned; нужен отдельный спринт.
2. Снимок истории ограничен 3 сообщениями × 500 символов — для длинных
   почтовых тредов может не хватить UID из более ранних сообщений.
3. `BuildRecentChatContext` берёт `content` только из строковых сообщений;
   `tool_calls`/`tool` сообщения пропускаются — это осознанно (контекст
   должен быть компактным).

---

## 6. Новые KI

Новых KI нет. KI-108 остаётся Planned (перенос в следующий спринт).

---

## 7. Обоснование

1. **Расширение DTO, а не конструкторов.** Добавление поля
   `RecentChatContext` в `ToolExecutionContext` не ломает ни один из ~10
   конструкторов наследников `AgentToolBase` (RULES § 4.34/§ 4.53).
2. **Компактный снимок.** 3 сообщения × 500 символов — компромисс между
   пользой для агента и расходом контекста LM Studio (4B модель).
3. **Промпт + код.** KI-113 закрыт на уровне промпта (явный запрет), а
   KI-114/KI-167 — на уровне кода (Fail при провале); вместе это убирает
   «эффект галлюцинации успеха» с двух сторон.
4. **Перенос KI-108.** Честнее оставить запись открытой, чем заявлять
   мнимый фикс без миграции и UI.
