# SMOKE_SCENARIOS — реестр пользовательских сценариев

**Версия:** 1.0
**Дата:** 2026-10-05
**Статус:** Living document — обновляется после каждого smoke-прогона.
**Связанные документы:** [DESIGN_VISION_AGENT.md](../v1.12/DESIGN_VISION_AGENT.md),
[KNOWN_ISSUES.md](../../KNOWN_ISSUES.md),
[PROMPT_V4.md](../PROMPT_V4.md).

---

## Зачем этот файл

Собрать в одном месте **пользовательские DoD-сценарии** для smoke-прогонов
IIChatTools. Каждый сценарий — конкретная задача, которую пользователь
ожидает выполнить через Chat. Сценарий считается **пройденным**, когда
результат воспроизводится на чистой системе.

**Формат:** таблица «# | Сценарий | Статус | Блокеры | KI».

Статусы:
- 🟢 **Passed** — работает на текущей версии.
- 🟡 **Partial** — часть шагов работает, часть — блокеры.
- 🔴 **Failed** — не работает.
- ⚪ **Not started** — ещё не прогонялся.
- 🕒 **Deferred** — отложен до закрытия зависимостей.

---

## Активные сценарии

| # | Сценарий | Статус | Блокеры | KI |
|---|---|---|---|---|
| **1** | **DeepSeek API** — открыть сайт, создать чат, задать вопрос, выделить и скопировать ответ | ⚪ Not started | — | — |
| **2** | **Начало дня** — меню Windows, запустить VS Code, Total Commander, Task Manager | ⚪ Not started | taskbar-icon detection | KI-166 |
| **3** | **Запуск скрипта** — создать `c:\projects\test\script.bat` с `echo "Hello World"` | 🟡 Partial (fix ready) | path-traversal + tool-selection + hallucination — **fix в v1.13.x, ждёт ре-smoke** | KI-114, KI-167, KI-168 |
| **4** | **Отчёт о проделанной работе** — notepad → сохранить в `c:\projects\test\` | ⚪ Not started | path-traversal (та же, что #3) | KI-167 |
| **5** | **Котик** — нарисовать HTML/CSS котика, открыть в браузере, отредактировать по замечаниям | 🕒 Deferred | path-traversal, `file://` в whitelist, anti-loop | KI-160, KI-167, KI-168 |

---

## Детали сценариев

### #1 — DeepSeek API

**DoD:**
1. Открыть браузер.
2. Перейти на `https://www.deepseek.com/chat`.
3. Открыть новый чат (кнопка «New chat» с иконкой крестик в кружке).
4. Ввести запрос «Сколько попугаев в одном питоне».
5. Отправить запрос (справа внизу синяя иконка со стрелочкой вверх).
6. Выделить ответ.
7. Скопировать выделенный текст в буфер обмена.

**Ожидаемые инструменты:** `browser_session_open` / `browser_open_page` / `browser_session_control` (type, click, screenshot). Возможно — `vision_agent(describe)`.

**Потенциальные блокеры:**
- Шаги 6–7 (drag-select + read clipboard) — **KI-164, KI-165** (Planned). Для MVP достаточно `browser_get_content` + HTML-парсинг ответа.

**Что проверяем:**
- Правильный выбор инструмента (browser_* vs vision_agent).
- Динамические селекторы DeepSeek (React SPA).
- Ожидание стабилизации страницы после отправки.

### #2 — Начало дня

**DoD:**
1. Открыть меню Windows.
2. Найти и запустить Visual Studio Code.
3. Запустить Total Commander из панели задач (иконка-дискетка).
4. Открыть Диспетчер задач (`Ctrl+Shift+Esc`).

**Ожидаемые инструменты:** `vision_agent` (все шаги) + `press_key` / `hotkey` / `type`.

**Потенциальные блокеры:**
- Taskbar-icon detection (**KI-166**) — иконки ~16–32 px, после downscale 1024×640 становятся ~10 px. VL-модель может не найти.
- Решение: anchor-clicks (`Win+1`, `Win+2`) или crop+upscale taskbar-региона.

**Что проверяем:**
- Vision Agent работает с desktop (не только browser).
- Tool selection: `press_key("Win")` vs `hotkey(["Win"])`.
- Стабильность focus после каждой итерации.

### #3 — Запуск скрипта

**DoD:**
1. Запустить `cmd`.
2. Перейти в `c:\projects\test\`, если такого пути нет — создать.
3. Создать `script.bat` с содержимым `echo "Hello World"`.

**Статус:** 🔴 Failed (2026-10-05).

**Что произошло:**
- LLM → `file_system_agent`.
- Внутри: `list_directory` → `make_directory` → `save_file`.
- Агент вернул «успешно создано».
- **Реальность:** файла нет.

**Блокеры:**
- **KI-167** — галлюцинация успеха (рецидив KI-113 + усилитель KI-114).
- **KI-168** — LLM выбирает `file_system_agent` для путей вне workspace. Правильный инструмент — `execute_command` или `code_agent`.
- `c:\projects\test\` — вне workspace, `PathHelper` отклоняет (RULES § 1.9).

**Fix-план (выполнено 2026-10-05):**
- ✅ **KI-114:** `AgentToolBase` → `Fail` при `Completed=false`.
- ✅ **KI-167:** усилен SystemPrompt `file_system_agent` (dev + prod) —
  правила 4/5/6 (жёсткий запрет внешних путей, не врать про успех,
  перечислять частичный результат). Description уточнено.
- ✅ **KI-168:** правило 8 в `DefaultSystemPrompt` + ПРИМЕР 3.
- ⚠️ **Ключевое открытие:** `ExecuteCommandTool.AllowedCommands` НЕ
  содержит `cmd` / `powershell`. Поэтому правило «внешние пути →
  `execute_command`» не работает; правильный инструмент — `code_agent`
  (Python / JS). Это отражено в правиле 8.
- **Рекомендация для ре-smoke:**
  - **Вариант A (чистый tool-selection):** переписать DoD #3 с путём
    внутри workspace — `%USERPROFILE%\IIChatToolsWorkspace\test\`.
    Тогда `file_system_agent` сработает, smoke чистый.
  - **Вариант B (оригинальный DoD с `c:\projects\test\`):** проверяет
    правило 8 — LLM должна выбрать `code_agent`, а не `file_system_agent`.

### #4 — Отчёт о проделанной работе

**DoD:**
1. Запустить notepad из панели задач.
2. Записать «Отчёт о проделанной работе» (3 предложения).
3. Сохранить в `c:\projects\test\` под именем
   «Отчёт о проделанной работе от {{дата и время}}.txt».

**Ожидаемые инструменты:** `vision_agent` (notepad) + `execute_command` / `save_file` (в workspace).

**Потенциальные блокеры:**
- Path `c:\projects\test\` (та же проблема, что #3 → KI-167).
- Имя с русскими + датой — Unicode через `KEYEVENTF_UNICODE` работает, но `{{дата}}` требует интерполяции на стороне tool (не VL).
- Save dialog Windows: «Сохранить» через `Ctrl+S` + `Enter`.

**Решение:** писать в workspace, а не в `c:\projects\test\`.

### #5 — Котик (HTML/CSS + анализ + loop)

**DoD:**
1. Перейти в `C:\projects\test\`, создать если нет.
2. Нарисовать котика через HTML/CSS, сохранить в `cat.html`.
3. Открыть `cat.html` в браузере.
4. Проанализировать котика, сформировать замечания.
5. Отредактировать `cat.html` с учётом замечаний.
6. Вернуться к шагу 3.

**Статус:** 🕒 Deferred (2026-10-05).

**Почему отложен:**
- **Path `C:\projects\test\`** — та же проблема, что #3 (KI-167/168).
- **`file://` в браузере** — whitelist доменов не покрывает файловые пути.
  `browser_open_page("file:///C:/...")` — поведение не проверено.
- **Loop 3→6** — anti-loop KI-160 срабатывает после 2 итераций
  одного действия → цикл прервётся на 3-й итерации.

**Условие запуска:** после закрытия KI-160, KI-167, KI-168 + проверки `file://` в browser-инструментах.

---

## Порядок прохождения (roadmap)

1. **Fix KI-167/168** (path + tool-selection) — **сейчас**.
2. **Fix KI-114** (`AgentToolBase` → `Fail` при `Completed=false`) — **сейчас**.
3. **Smoke #1 (DeepSeek)** — после фиксов.
4. **Smoke #3 (Запуск скрипта) с путём в workspace** — быстрый win.
5. **KI-164/165** (drag + read clipboard) — если #1 потребует буквального шага 6–7.
6. **Smoke #2 (Начало дня)** — после KI-166 (taskbar).
7. **Smoke #4 (Отчёт)** — после переписывания DoD с workspace-путём.
8. **Smoke #5 (Котик)** — после KI-160 refactor + закрытия path-блокеров.

---

## Как запускать (стандарт)

**Pre-conditions:**
1. `dotnet run` из `IIChatTools.API`.
2. LM Studio: модель `qwen/qwen3-4b-2507`, Context Length ≥ 16384, загружена.
3. Чистая БД (или убедиться, что chatId — новый).
4. Открыт `/chat`, создан новый чат.

**Как смотрим:**
- DevTools → Network → POST `/api/chat/stream` → Response (SSE): какие `tool_call` / `tool_result`.
- DevTools → Console: `[chat] SSE ...`, `Approval required: ...`.
- Терминал `dotnet run`: `ToolRegistry → Выполнение инструмента X`.
- После: `sqlite3 IIChatTools.API\Data\iichattools-dev.db "SELECT ToolName, Status FROM AuditLogs WHERE UserId=1 ORDER BY Id DESC LIMIT 10;"`.

**Критерии:**
- LLM выбрала правильный инструмент (не vision_agent для file-system задач).
- Уложилась в N шагов (сценарий-специфично).
- Результат **реально существует** на диске (не только в ответе LLM).

**Провал → KI:**
- Галлюцинация → KI (рецидив KI-113 / KI-114).
- Неправильный tool selection → KI (аналог KI-118/120/127/168).
- Anti-loop / не завершилось → KI (аналог KI-160).

---

## История

| Дата | Сценарий | Результат | Примечание |
|---|---|---|---|
| 2026-10-05 | #3 Запуск скрипта | 🔴 Failed | KI-167, KI-168, KI-169 |
| 2026-10-05 | #5 Котик | 🕒 Deferred | 3 блокера (path, file://, anti-loop) |
| 2026-10-05 | #1, #2, #4 | ⚪ Not started | — |

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.13.1**