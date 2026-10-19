# SMOKE_RUNNER — `browser_session_control:run_smoke` (KI-256)

**Дата:** 2026-10-19
**Версия:** v2.0.3 (в работе)
**HEAD:** `83e9b6f` (v2.0.2) + незакоммиченные изменения
**Правила:** `docs/development/RULES.md` v1.4.35
**Статус:** изменения в рабочем дереве, **не закоммичены**

---

## 1. Файлы (+/-)

### Создано

| Файл | Строк | Назначение |
| :--- | ---: | :--- |
| `IIChatTools.Services/DTO/Browser/BrowserSmokeDtos.cs` | 84 | DTO шага, плана и результата smoke-сценария |
| `IIChatTools.Services/Implementation/Browser/BrowserSmokeRunner.cs` | 554 | Парсер/валидатор `steps` + исполнитель шагов на `Page` |
| `IIChatTools.Tests/UnitTests/Browser/BrowserSmokeTests.cs` | 322 | 29 unit-тестов парсинга/валидации и метаданных инструмента |

### Изменено

| Файл | +/- | Что |
| :--- | :--- | :--- |
| `IIChatTools.Services/Implementation/BrowserSessionManager.cs` | +43 / −1 | Команда `run_smoke`, общий `CancellationTokenSource` на 60 с, вызов runner, payload `passed/failedStep/...` |
| `IIChatTools.Services/Implementation/Tools/Browser/BrowserSessionControlTool.cs` | +14 / −6 | Описание `run_smoke`, команда в списке, параметр `steps` (array) |
| `CHANGELOG.md` | +6 | `[Unreleased] → Added`: KI-256 (RULES § 2.1) |

**Не трогал:** `chat.js` и другие файлы фронтенда; `ToolRegistry`/`Startup.cs` (новый top-level `ITool` не добавлялся — RULES § 4.44); `IIChatTools.Services/Interfaces/IBrowserSessionManager.cs` (сигнатура не менялась).

`git status --short`:

```text
 M CHANGELOG.md
 M IIChatTools.Services/Implementation/BrowserSessionManager.cs
 M IIChatTools.Services/Implementation/Tools/Browser/BrowserSessionControlTool.cs
?? IIChatTools.Services/DTO/Browser/BrowserSmokeDtos.cs
?? IIChatTools.Services/Implementation/Browser/
?? IIChatTools.Tests/UnitTests/Browser/
?? docs/development/reports/v2.0.3/
```

**Новые KI в реестр не добавлял:** блокирующих багов не найдено (см. § 6).

---

## 2. Реализация

### Точка входа

`browser_session_control` → `BrowserSessionControlTool.ExecuteAsync` → проверка `sessionId`/`command` → `BrowserSessionManager.ExecuteCommandAsync` → `switch (command)` → **`case "run_smoke"`**.

Сессия должна быть уже открыта (`browser_session_open`); `run_smoke` работает в текущей вкладке и **не закрывает** её.

### Парсинг и валидация (до запуска браузера)

`BrowserSmokeRunner.TryParse(steps, out plan, out error, out failedStep)`:

- `steps` обязателен, должен быть JSON-массивом, не пустым, ≤ **50** шагов;
- `action` приводится к нижнему регистру;
- проверяются обязательные поля каждого действия;
- `goto`: только абсолютный `http(s)`-URL (как в существующем `goto`);
- CSS-селекторы: отклоняются XPath (`//…`, `text=`, `text()`, `:contains(`);
- `wait_for_selector.timeoutMs`: 1…60000, default **5000**;
- `wait.ms`: 1…60000, default **500**;
- при ошибке шага возвращается его номер (`failedStep`), при глобальной ошибке — `null`.

Ошибка парсинга → `ToolResult.Fail(parseError, data)` с `passed=false`, `failedStep`, `durationMs=0`, `screenshots=[]`, `error`.

### Цикл выполнения

`BrowserSmokeRunner.RunAsync(page, plan, context, defaultTimeoutMs, ct)`:

1. Запоминает `page.DefaultTimeout`, создаёт `Stopwatch`, список скриншотов.
2. Для каждого шага:
   - проверяет `ct` (общий таймаут 60 с);
   - вычисляет остаток общего бюджета: `60000 − elapsed`;
   - `effectiveTimeout = min(step.timeoutMs ?? defaultTimeoutMs, remaining)`;
   - ставит `page.DefaultTimeout = effectiveTimeout`;
   - пишет `LogInformation("Browser smoke: шаг N/M — action ...")`;
   - выполняет действие.
3. В `finally` восстанавливает `page.DefaultTimeout`.

### Действия

| Action | Реализация |
| :--- | :--- |
| `goto` | `page.GoToAsync(url)` |
| `click` | `page.ClickAsync(selector)` |
| `click_by_text` | `page.EvaluateFunctionAsync<JObject>` — точное совпадение → частичное → fallback по листовым узлам (как в существующей команде) |
| `type` | `page.TypeAsync(selector, text)` |
| `press_key` | `page.Keyboard.PressAsync(key)` |
| `wait_for_selector` | `page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Timeout = effectiveTimeout })` |
| `assert_text` | `page.EvaluateFunctionAsync<string>` → `innerText \|\| textContent`; проверка `Contains` (Ordinal) |
| `assert_selector` | `page.EvaluateFunctionAsync<bool>` → наличие + `getBoundingClientRect()` > 0 + `display/visibility/opacity` |
| `screenshot` | `ScreenshotDataAsync(PNG)` → `PathHelper.TryGetSafeFullPath` → запись в workspace; путь добавляется в `screenshots[]` |
| `wait` | `Task.Delay(ms, ct)` |

### Ошибки и возврат

- Исключения шагов **не пробрасываются**: ловятся в `RunAsync`, возвращается `BrowserSmokeResult { Passed=false, FailedStep=N, Error="Шаг N (action): ..." }`.
- Отмена/общий таймаут → `Passed=false`, `failedStep`, `error` с упоминанием 60 с.
- Успех → `passed=true`, `failedStep=null`, `durationMs`, `screenshots`, `error=null`.
- `ToolResult`: успех — `Ok(payload)`, ошибка — `Fail(error, payload)` (внешний `success=false` + `passed=false`).

---

## 3. Тесты

`IIChatTools.Tests/UnitTests/Browser/BrowserSmokeTests.cs` — **29 тестов**:

| Группа | Что проверяется |
| :--- | :--- |
| Полный сценарий | Все 7 шагов из постановки: actions, URL, selector, text, contains, path, ms |
| Case-insensitive | `GOTO` → `goto` |
| Пустой/отсутствующий/не-массив steps | Ошибки с понятным текстом |
| Неизвестный action | Ошибка + номер шага (`failedStep=2`) |
| Обязательные параметры | Theory на 10 действий (goto/click/click_by_text/type/press_key/wait_for_selector/assert_text/assert_selector/screenshot/wait) |
| URL | Отклоняются `/relative`, `ftp://`, `javascript:` |
| Селекторы | Отклоняются XPath/`text=`/`:contains(` |
| Defaults | `wait_for_selector` → 5000; success → `failedStep=null` |
| Лимит | 51 шаг → ошибка |
| Метаданные инструмента | `Description` содержит `run_smoke`; `steps` имеет Type=`array`; `command` перечисляет `run_smoke` |

Запуск PuppeteerSharp в unit-тестах не выполняется: нужен Chromium и реальная страница. Покрыта вся чистая логика, которая может ломаться без браузера.

---

## 4. Smoke KI

Команда из постановки:

```text
dotnet test IIChatTools.Tests/IIChatTools.Tests.csproj -c Debug --no-build --filter Browser
```

Результат:

```text
Пройден! : не пройдено 0, пройдено 29, пропущено 0, всего 29, длительность 134 ms.
```

Полный прогон (регрессия):

```text
Пройден! : не пройдено 0, пройдено 1201, пропущено 8, всего 1209, длительность 2 s.
```

Сборка решения: **0 warnings, 0 errors**.

---

## 5. Что не так

1. **Нет интеграционного теста с реальным Chromium.** `run_smoke` проверен только на парсинге/валидации; фактический `Page.GoToAsync/ClickAsync/EvaluateFunctionAsync` в тестах не запускается. Для полной проверки нужен ручной smoke на живом приложении (LM Studio + браузер).
2. **`BrowserSessionControlTool` кастует `IBrowserSessionManager` к конкретному `BrowserSessionManager`** (существующая архитектура). Из-за этого нельзя подставить Moq-менеджер и протестировать диспетчер `run_smoke` без Puppeteer. Пришлось ограничиться тестами парсера. Кандидат на рефакторинг (вынести `ExecuteCommandAsync` в интерфейс без каста) — вне KI-256.
3. **`assert_text` регистрозависим и чувствителен к пробелам** (`innerText`, `StringComparison.Ordinal`). Так задумано: спецификация говорит «содержит подстроку», а не «case-insensitive». Для UI-текста с переносами строк это может давать ложные FAIL — в сценариях лучше использовать короткую уникальную подстроку.
4. **`run_smoke` не добавлен в `browser_agent` SystemPrompt** (`appsettings.json`): там перечислены старые команды. LLM увидит `run_smoke` в описании tool-схемы, но системный промпт может смещать выбор. Правка конфига — вне «только C#».
5. **README-метрика тестов устарела:** в `README.md` указано `1180/1180`, фактически после KI-251/255/256 — `1201 passed + 8 skip = 1209`. Документация не входила в scope этой задачи.
6. **`CHANGELOG.md` изменён, хотя в правилах задачи сказано «только C#».** Это требование RULES § 2.1 (CHANGELOG обновляется вместе с кодом), поэтому оставил запись `[Unreleased] → Added`.
7. **`failedStep` для ошибок уровня «весь steps» (пустой массив, >50 шагов) равен `null`** — это не шаг, а ошибка сценария. Для ошибок внутри шага номер возвращается.
8. **`page.DefaultTimeout` — глобальное свойство страницы.** Runner меняет его на время шага и восстанавливает в `finally`; при параллельных вызовах в одной сессии возможна гонка. Сессии и так не предназначены для параллельных команд (одна вкладка), но это стоит помнить.
9. **`git status` показывает только файлы этой задачи** — предыдущие работы (v2.0.2) уже закоммичены. Изменения не коммитил.

---

## 6. Новые KI

**Новых KI не заводил:** блокирующих багов в `PuppeteerSharp 7.1.0` или в существующем browser-коде не обнаружено.

Наблюдения, которые не тянут на отдельный KI в рамках этой задачи:

- `BrowserSessionControlTool` → concrete cast (п. 5.2) — архитектурный долг, не баг;
- README `1180/1180` vs `1209` (п. 5.5) — документация, отдельная задача;
- `sync-known-issues.ps1` не парсит суффиксные ID (`KI-110a/b`) — известно из KI-252, к browser-smoke не относится.

Если нужно — оформлю `KI-257` на актуализацию README-метрик и `KI-258` на рефакторинг интерфейса browser-команд отдельными задачами.

---

## 7. Обоснование решений

1. **Отдельный `BrowserSmokeRunner`, а не ещё один `case` в менеджере.** Парсер/валидатор — чистая функция, её можно тестировать без Chromium; менеджер остаётся тонким диспетчером. Это дало 29 быстрых тестов вместо 0.
2. **Валидация до браузера.** Ошибки в JSON (неизвестный action, отсутствующий selector, кривой URL) отлавливаются до `GoToAsync`, чтобы не тратить 60-секундный бюджет и не оставлять страницу в промежуточном состоянии.
3. **`assert_text`/`assert_selector` через `EvaluateFunctionAsync`, а не `GetContentAsync`.** `GetContentAsync` отдаёт весь HTML (дорого по контексту/времени) и не даёт computed-видимость. `EvaluateFunction` точечно читает `innerText` и `getBoundingClientRect`/`getComputedStyle` — это и есть проверяемое условие.
4. **`assert_selector` не через `WaitForSelectorAsync(Visible=true)`.** Wait-вариант бросает общий timeout без деталей; собственный JS возвращает `false` и позволяет вернуть понятное «не найден или не виден».
5. **Общий таймаут 60 с + per-step timeout.** `CancellationTokenSource` покрывает паузы/отмену, а `page.DefaultTimeout = min(step, remaining)` не даёт одному `GoToAsync`/`ClickAsync` съесть весь бюджет. В `finally` таймаут восстанавливается.
6. **Ошибка → `ToolResult.Fail`, не исключение.** LLM получает структурированный `passed=false` + `failedStep`, а не стектрейс; частичные скриншоты сохраняются в `screenshots[]` для диагностики.
7. **Скриншоты — в workspace, без base64.** `screenshot` в `run_smoke` сохраняет файл (как `screenshot_to_file`), чтобы не раздувать контекст LLM; путь проверяется `PathHelper` (RULES § 1.9).
8. **Никакого нового top-level `ITool`.** Расширена существующая команда, `ToolRegistry`/`ChatStreamService.allowedNames` не менялись (RULES § 4.44).
9. **`click_by_text` переиспользует ту же JS-логику**, что и существующая команда (exact → contains → leaf fallback), чтобы поведение smoke и ручных команд совпадало.
10. **Фронтенд не трогал.** Весь функционал доступен через `/api/tools/execute` и агента; `chat.js` не знает о `run_smoke` и не должен.

---

**Git:** изменения не закоммичены, `git add -A` не использовался.
