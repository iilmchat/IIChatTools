# DESIGN v1.9 — Vision Agent

**Версия:** 2.1
**Дата:** 2026-10-03
**Статус:** Draft — ждёт согласования (Planned в v1.12.0)
**Связанные KI:** KI-131 (Vision Agent) · KI-137 (OCR fallback) ·
KI-138 (PII masking) · KI-139 (External VL)
**Целевой релиз:** v1.12.0 (local-harness + sandbox + remote-vnc, 3 модели)
**Связанные документы:** [RULES.md](../RULES.md), [ARCHITECTURE.md](../ARCHITECTURE.md), [DESIGN_DB_AGENT.md](../v1.7/DESIGN_DB_AGENT.md), [DESIGN_EXTERNAL_LLM.md](../v1.8/DESIGN_EXTERNAL_LLM.md)

---

## § 1. Контекст

### § 1.1. Текущее состояние (после v1.11.0)

- **Chat видит 15 инструментов:** 10 агентов (`file_system`, `code`,
  `code_reviewer`, `code_agent_with_review`, `web`, `git`, `github`,
  `planner`, `mail`, `external_llm`) + `consult_secondary_agent`
  + 3 RAG + `database_agent`.
- **Actor-Critic (v1.11.0, KI-126):** top-level `code_agent_with_review`
  + субагент-критик `code_reviewer_agent`; persistence в
  `AgentDebateSession` / `AgentDebateRound`; F5-восстановление (KI-129).
- **Browser-автоматизация** (v1.1.0): 4 raw-инструмента (`browser_navigate`,
  `browser_get_content`, `browser_screenshot`, `browser_close`) — только
  чтение, без управления.
- **LLM не умеет:**
  - Управлять UI через визуальные подсказки — только CSS-селекторы.
  - Работать с canvas / WebGL / shadow-DOM.
  - Обходить антибот-защиту (Cloudflare Turnstile, DataDome).
  - Выполнять многошаговые задачи: «купи билет РЖД до Камчатки», «заполни
    заявление на госуслугах», «настрой 1С».
  - Управлять десктопными приложениями (Outlook, Excel, 1С).

### § 1.2. Industry best practices (Computer Use pattern)

Проанализированы ведущие реализации (Anthropic, OpenAI, Microsoft, ByteDance,
xAI). Общий паттерн — **VLM-loop**:

```
┌──────────────────────────────────────────────────────────┐
│  1. Screenshot текущего состояния                        │
│  2. Multimodal LLM: «Что делать дальше для задачи X?»    │
│  3. LLM возвращает JSON action: { click, x, y }          │
│  4. Выполнить action                                     │
│  5. Goto 1 (пока LLM не скажет done / max steps)         │
└──────────────────────────────────────────────────────────┘
```

**Ключевой вывод:** vision-подход закрывает то, что не могут селекторы
(dynamic ID, canvas, shadow-DOM, антибот), но уступает селекторам в скорости
и стоимости. Поэтому Vision Agent — **дополнение**, не замена `browser_*`.

### § 1.3. Цели v1.9.0

| # | Цель | Метрика |
|---|---|---|
| 1 | LLM выполняет задачи через скриншоты + мышь + клавиатуру | Tool `vision_agent` |
| 2 | Три backend'а: локальный / Sandbox / удалённый | Переключение через config |
| 3 | Оркестрация трёх моделей: Chat → Planner → Vision | Все модели в config |
| 4 | Fallback на внешние VL (GPT-4o / Claude / Gemini) | Через `IExternalLlmClient` |
| 5 | On-screen indicator в локальном режиме | Обязателен (ESC — стоп) |
| 6 | 5 уровней безопасности | Whitelist + approval + audit |
| 7 | Готовность к Windows-приложениям (Outlook, Excel) | `LocalHarnessVisionBackend` |

### § 1.4. Что НЕ входит в v1.9.0

- **Linux desktop** (X11 / Wayland, `xdotool`, `grim`) — не планируется.
- **macOS desktop** — не планируется.
- **OCR без VL-модели** — не нужно, VL-модели закрывают задачу.
- **Обход капчи / антибот-детекции** — принципиально не входит.
- **Запись видео / real-time streaming** — только статичные PNG.
- **Fine-tuning VL-модели** — не входит.

---

## § 2. Проблема

### § 2.1. LLM не видит экран

**Пример задачи:** «Открой сайт РЖД, купи билет Москва → Петропавловск-Камчатский
на следующую пятницу, купе, нижняя полка».

Сейчас: LLM не может. `browser_get_content` возвращает HTML (10 МБ JS-кода
для SPA), селекторы часто динамические (`#__next > div > div:nth-child(3)`),
кнопки «Найти» рендерятся не сразу. А сайт РЖД вообще может блокировать
headless-браузеры.

### § 2.2. Прямой доступ к UI — антипаттерн

| Проблема | Последствие |
|---|---|
| Нет whitelist процессов | LLM управляет любым окном, включая Outlook с паролями |
| Нет approval | LLM кликает «Удалить аккаунт» без ведома пользователя |
| Нет лимита шагов | LLM зациклится на капче → 1000 запросов к VL-модели |
| Нет таймаута | Задача на 30 минут вместо 5 |
| Нет валидации действий | `Ctrl+Alt+Del`, `F12`, `Win+L` |
| Скриншоты содержат PII | Пароли, токены, личные сообщения в логах |
| Нет аудита | Нет следа, что делал LLM |
| Пользователь не видит | LLM управляет машиной «за спиной» — недопустимо |

### § 2.3. Ограничения существующих инструментов

| Инструмент | Что делает | Чего не хватает |
|---|---|---|
| `browser_navigate` | Открывает URL | Нет доменного whitelist |
| `browser_get_content` | Читает HTML/DOM | Не работает для canvas / SPA |
| `browser_screenshot` | Скриншот **без** управления | Нет click / type / scroll |
| `web_search` | Поиск через DuckDuckGo | Не подходит для интерактивных сайтов |

Vision Agent закрывает пробел: **«визуальное управление компьютером»**.

---

## § 3. Решение

### § 3.1. Единый агент с backend-абстракцией

Один top-level инструмент `vision_agent` (не `AgentToolBase`). Работает через
`IVisionBackend` — абстракция «поверхности». **Три реализации:**

| Backend | Когда использовать | Изоляция |
|---|---|---|
| `LocalHarnessVisionBackend` | Быстрый прототип, своя машина | Chrome fresh profile + whitelist процессов |
| `SandboxVisionBackend` | Локально, но безопасно | Windows Sandbox (одноразовая VM) |
| `VncMcpVisionBackend` | Удалённый Windows | Физически отдельная машина |

Переключение — через `VisionAgent:Backend:Mode` в `appsettings.json`.

### § 3.2. Оркестрация трёх моделей

**Ключевое решение:** не одна модель делает всё, а **три** — каждая на своём
уровне. Все модели — в конфиге, взаимозаменяемы.

```
┌─────────────────────────────────────────────────────────────────┐
│  Chat LLM (qwen3-4b)                                            │
│  Роль: диалог с пользователем, делегирование Vision Agent.      │
│  Config: LmStudio:Model (уже есть)                              │
│  «Купи билет РЖД Москва→Камчатка»                               │
└──────────────────────────┬──────────────────────────────────────┘
                           │ vision_agent(task=...)
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  Planner LLM (qwen3-coder-30b-a3b)                              │
│  Роль: планирование следующего действия.                        │
│  Config: VisionAgent:PlannerLlm:Model                           │
│  Вход: task + history + screen_description                      │
│  Выход: { action, target, text? }                               │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  Vision LLM (ministral-3-3b-instruct-2512)                      │
│  Роль: описание UI со скриншота.                                │
│  Config: VisionAgent:VisionLlm:Model                            │
│  Вход: PNG                                                      │
│  Выход: { description, ui_elements[] }                          │
└─────────────────────────────────────────────────────────────────┘
```

**Почему три, а не одна:**

- **4B не хватит** для планирования многошаговых задач (РЖД — 8-12 шагов).
- **3B Vision** — достаточно для описания UI, но не для решений.
- **30B-A3B (MoE)** — Agentic Browser-Use SOTA, 3.3B активных параметров
  на токен → работает на CPU обычной машины.

**Почему все три — в конфиге:**

- Можно заменить `qwen3-coder-30b-a3b` на `qwen3-max`, `claude-sonnet-4.5`,
  `gpt-4o` — без правки кода.
- Можно заменить `ministral-3-3b` на `llava-1.5-13b`, `pixtral-12b`,
  `gpt-4o-mini` — без правки кода.
- Можно использовать внешние провайдеры через `IExternalLlmClient` (v1.8.1).

### § 3.3. Loop: screenshot → describe → plan → act

```
1. backend.OpenAsync(url)             — открыть страницу (browser) / no-op (desktop)
2. screenshot = backend.ScreenshotAsync()   → PNG bytes
3. screen = VisionLlm.DescribeAsync(screenshot)
   → { description: "...", ui_elements: [...] }
4. action = PlannerLlm.PlanNextAsync(
        task, history, screen, plan
   )
   → { action: "click", target: "search_btn" }
5. backend.ExecuteAsync(action)              → OK / Fail
6. history.append({ step, action, result })
7. overlay.UpdateProgress(step, action)
8. repeat until done / max_steps / ESC / timeout
```

### § 3.4. Многоуровневая безопасность (Defense in Depth)

| # | Уровень | Что защищает |
|---|---|---|
| 1 | **Изоляция** | Sandbox / отдельный пользователь / удалённая машина |
| 2 | **Whitelist процессов** | LLM не переключается на Outlook / Explorer |
| 3 | **On-screen indicator** | Пользователь видит + ESC для остановки |
| 4 | **Approval + бюджет** | 1 клик на `run_task`, MaxSteps, MaxTaskSeconds |
| 5 | **Валидатор + audit** | Blocklist hotkey'ев, все действия — в AuditLogs |

---

## § 4. Архитектура

### § 4.1. Слои (IIChatTools.Services)

```
IIChatTools.Services/
  ├── DTO/VisionAgent/
  │   ├── VisionAgentOptions.cs               — bind appsettings:VisionAgent
  │   ├── VisionBackendOptions.cs             — Local / Sandbox / RemoteVnc
  │   ├── VisionLlmOptions.cs                 — Vision-модель
  │   ├── PlannerLlmOptions.cs                — Planner-модель
  │   ├── VisionActionDto.cs                  — { action, x, y, text, target, ... }
  │   ├── VisionStepDto.cs                    — { stepIndex, action, screen, durationMs }
  │   ├── VisionTaskRequest.cs                — { task, url?, maxSteps? }
  │   ├── VisionTaskResultDto.cs              — { success, steps[], summary }
  │   ├── UiElementDto.cs                     — { id, type, label, center, bounds }
  │   ├── ScreenDescriptionDto.cs             — { description, ui_elements[] }
  │   └── VisionActionResult.cs               — { success, error }
  │
  ├── Interfaces/
  │   ├── IVisionAgentService.cs              — оркестратор loop
  │   ├── IVisionBackend.cs                   — абстракция поверхности
  │   ├── IVisionLlmClient.cs                 — multimodal LLM (описание UI)
  │   ├── IPlannerLlmClient.cs                — planner LLM (решение)
  │   └── IVisionActionValidator.cs           — валидатор действий
  │
  ├── Implementation/VisionAgent/
  │   ├── VisionAgentService.cs               — Scoped, оркестратор loop
  │   ├── LocalHarnessVisionBackend.cs        — Scoped, SystemHarness.Windows
  │   ├── SandboxVisionBackend.cs             — Scoped, Windows Sandbox
  │   ├── VncMcpVisionBackend.cs              — Scoped, MCP-клиент
  │   ├── LmStudioVisionClient.cs             — Singleton, Ministral-3B
  │   ├── LmStudioPlannerClient.cs            — Singleton, qwen3-coder-30b
  │   ├── ExternalVisionClient.cs             — Singleton, GPT-4o / Claude
  │   ├── ExternalPlannerClient.cs            — Singleton, внешний planner
  │   ├── AutoVisionClient.cs                 — Singleton, fallback chain
  │   ├── AutoPlannerClient.cs                — Singleton, fallback chain
  │   ├── VisionActionValidator.cs            — Singleton
  │   ├── VisionAgentOptionsProvider.cs       — Singleton (baseline + runtime)
  │   ├── VisionOverlayLauncher.cs            — Singleton, запуск VisionOverlay.exe
  │   └── InMemoryVisionRateLimiter.cs        — Singleton, IDisposable
  │
  └── Implementation/Tools/VisionAgent/
      └── VisionAgentTool.cs                  — top-level ITool (не AgentToolBase!)

IIChatTools.VisionOverlay/                    — отдельный WPF-проект
  ├── VisionOverlayApp.xaml                   — always-on-top прозрачное окно
  ├── VisionOverlayApp.xaml.cs
  └── Program.cs                              — single-instance + IPC
```

**Почему `VisionAgentTool` — top-level `ITool`, а не `AgentToolBase`:**

- `AgentToolBase` работает через `SubAgentService`, который **не умеет**
  передавать изображения в LLM.
- Vision-агенту нужен **свой** loop со скриншотами и двумя моделями.
- Паттерн совпадает с двумя прецедентами:
  - `DatabaseAgentTool` (v1.7.0, KI-097) — прямой детерминированный
    оркестратор (4 action);
  - `CodeAgentWithReviewTool` (v1.11.0, KI-126) — top-level `ITool`,
    координирует actor+critic через `IToolRegistry`, эмитит SSE-события
    через `ToolExecutionContext.EventWriter`.
- **RULES § 4.44:** top-level `ITool` → обязательно добавить в
  `allowedNames` в `ChatStreamService.StreamAsync`.
- **RULES § 4.51:** если `VisionAgentTool` будет зависеть от `IToolRegistry`
  (например, для вызова `consult_secondary_agent`) — использовать
  `Func<IToolRegistry>` (ADR-002) для разрыва DI-цикла.

### § 4.2. DI-регистрация (Startup.cs)

```csharp
// Только если VisionAgent:Enabled = true
services.Configure<VisionAgentOptions>(Configuration.GetSection("VisionAgent"));
services.AddSingleton<VisionAgentOptionsProvider>();
services.AddSingleton<IVisionActionValidator, VisionActionValidator>();
services.AddSingleton<InMemoryVisionRateLimiter>();
services.AddSingleton<VisionOverlayLauncher>();

// Vision LLM — выбор через config
services.AddSingleton<LmStudioVisionClient>();
services.AddSingleton<ExternalVisionClient>();
services.AddSingleton<IVisionLlmClient>(sp =>
{
    var provider = Configuration["VisionAgent:VisionLlm:Provider"]; // lmstudio | external | auto
    return provider switch
    {
        "external" => sp.GetRequiredService<ExternalVisionClient>(),
        "auto"     => sp.GetRequiredService<AutoVisionClient>(),
        _          => sp.GetRequiredService<LmStudioVisionClient>()
    };
});

// Planner LLM — выбор через config
services.AddSingleton<LmStudioPlannerClient>();
services.AddSingleton<ExternalPlannerClient>();
services.AddSingleton<IPlannerLlmClient>(sp =>
{
    var provider = Configuration["VisionAgent:PlannerLlm:Provider"];
    return provider switch
    {
        "external" => sp.GetRequiredService<ExternalPlannerClient>(),
        "auto"     => sp.GetRequiredService<AutoPlannerClient>(),
        _          => sp.GetRequiredService<LmStudioPlannerClient>()
    };
});

// Backends — все регистрируются, выбор по config
services.AddScoped<LocalHarnessVisionBackend>();
services.AddScoped<SandboxVisionBackend>();
services.AddScoped<VncMcpVisionBackend>();
services.AddScoped<IVisionBackend>(sp =>
{
    var mode = Configuration["VisionAgent:Backend:Mode"];
    return mode switch
    {
        "sandbox"    => sp.GetRequiredService<SandboxVisionBackend>(),
        "remote-vnc" => sp.GetRequiredService<VncMcpVisionBackend>(),
        _            => sp.GetRequiredService<LocalHarnessVisionBackend>()
    };
});

services.AddScoped<IVisionAgentService, VisionAgentService>();
services.AddScoped<ITool, VisionAgentTool>();
```

Порядок: `RegisterVisionAgentTools(services, Configuration)` — новый private
метод в `Startup.cs`, после `RegisterSqlAgentTools`.

### § 4.3. Tool параметры `vision_agent`

| Параметр | Тип | Обязательный | Описание |
|---|---|---|---|
| `action` | string | ✅ | `run_task` / `describe` / `screenshot` / `click` / `type` / ... |
| `task` | string | для `run_task` | Описание задачи естественным языком |
| `url` | string | нет | Стартовый URL |
| `target` | string | нет | ID элемента из `ui_elements` (приоритет над x/y) |
| `x`, `y` | int | нет | Координаты (fallback, если нет target) |
| `text` | string | для `type` | Текст |
| `key` | string | для `press_key` | `Enter` / `Tab` / `Escape` / … |
| `keys` | string[] | для `hotkey` | `["Ctrl", "C"]` |
| `deltaY` | int | для `scroll` | +вниз / -вверх |
| `maxSteps` | int | нет | Override `MaxSteps` для этого `run_task` |

**Действия:**

| Action | Назначение | Approval |
|:---|:---|:---:|
| `describe` | Скриншот → `{ description, ui_elements[] }` | — |
| `screenshot` | Скриншот → path + base64 | — |
| `run_task` | Полный loop (screenshot → describe → plan → act) | ✅ |
| `click` | Клик по `target` или (x, y) | ✅ |
| `double_click` | Двойной клик | ✅ |
| `right_click` | Контекстное меню | ✅ |
| `move_mouse` | Наведение (для hover-меню) | — |
| `type` | Ввод текста | ✅ |
| `press_key` | Одиночная клавиша | ✅ |
| `hotkey` | Комбинация (`Ctrl+C`) | ✅ |
| `scroll` | Прокрутка | — |
| `wait` | Пауза | — |

**Per-action approval** — через `ITool.RequiresApprovalForCall` (v1.7.0, KI-101).

### § 4.4. JSON-схема `ui_elements`

Vision LLM (Ministral) возвращает структурированное описание:

```json
{
  "description": "Страница поиска РЖД. Поля: Откуда, Куда, Дата. Кнопка «Найти» неактивна.",
  "ui_elements": [
    {
      "id": "from_input",
      "type": "text_input",
      "label": "Откуда",
      "value": "",
      "bounds": { "x": 340, "y": 210, "w": 200, "h": 40 },
      "center": { "x": 440, "y": 230 }
    },
    {
      "id": "search_btn",
      "type": "button",
      "label": "Найти",
      "enabled": false,
      "center": { "x": 640, "y": 320 }
    }
  ]
}
```

Planner LLM получает это и решает: `click(target="from_input")`,
`type(target="from_input", text="Москва")`.

### § 4.5. System prompt для Planner LLM

```
Ты — Planner Agent IIChatTools. Твоя задача — управлять компьютером
через действия. Ты НЕ видишь экран напрямую — ты получаешь текстовое
описание UI от Vision-модели.

На вход ты получаешь:
  - task: задача пользователя
  - history: список выполненных шагов (action + result)
  - screen: { description, ui_elements[] } текущего состояния
  - plan: список подзадач (можешь перепланировать)

На выход — строго JSON:
{
  "action": "click" | "type" | "press_key" | "hotkey" | "scroll" | "wait" | "done" | "fail",
  "target": "from_input",       // ID из ui_elements
  "text": "Москва",             // для type
  "key": "Enter",               // для press_key
  "keys": ["Ctrl","C"],         // для hotkey
  "deltaY": 300,                // для scroll
  "reason": "Поле «Откуда»"
}

Если задача выполнена — { "action": "done", "reason": "…" }.
Если невозможно — { "action": "fail", "reason": "…" }.
```

### § 4.6. On-screen indicator (VisionOverlay.exe)

**Обязателен** для `local-harness` и `sandbox`. Отдельный процесс (WPF),
запускается `VisionOverlayLauncher` при старте `run_task`.

**Содержимое:**

- Строка: «🤖 Vision Agent: шаг N из M»
- Текущее действие: «Клик → Откуда»
- Кнопка **STOP** (или ESC) — немедленная остановка.
- Индикатор статуса: зелёный / жёлтый / красный.

**Свойства окна:**

- `Topmost="True"`, `AllowsTransparency="True"`.
- `IsHitTestVisible="False"` для всего, кроме кнопки STOP.
- Расположение — правый верхний угол, ширина 320px.
- IPC: `NamedPipeServerStream` (`iichattools-vision-overlay`) — основное
  приложение отправляет `UpdateProgress(step, action)`, overlay отправляет
  `StopRequested`.

---

## § 5. Конфигурация

### § 5.1. `appsettings.json` — полная секция

```jsonc
"VisionAgent": {
  "Enabled": true,
  "AdminUiEnabled": true,

  "Backend": {
    "Mode": "local-harness",                    // local-harness | sandbox | remote-vnc

    "Local": {
      "CaptureMode": "gdi",                     // gdi | windows-graphics-capture
      "AllowNonBrowserProcesses": false,        // если true — Outlook/Excel разрешены
      "AllowedProcesses": [ "chrome", "msedge", "firefox" ],
      "ChromeFreshProfile": true,               // --user-data-dir в %TEMP%\vision-profile
      "ShowOverlay": true                       // on-screen indicator (обязателен)
    },

    "Sandbox": {
      "ConfigPath": "VisionAgent/sandbox.wsb",  // путь к .wsb-конфигу
      "StartupTimeoutSeconds": 60,
      "VncPort": 5901,
      "VncPassword": "CHANGE_ME_VIA_USER_SECRETS",
      "AutoShutdownAfterTask": true,
      "MappingFolder": "%USERPROFILE%\\IIChatToolsVision" // shared folder для файлов
    },

    "RemoteVnc": {
      "McpEndpoint": "http://127.0.0.1:8765",   // vnc-mcp-server
      "McpApiKey": "CHANGE_ME_VIA_USER_SECRETS",
      "VncHost": "192.168.1.50",
      "VncPort": 5900,
      "VncPassword": "CHANGE_ME_VIA_USER_SECRETS",
      "ConnectionTimeoutSeconds": 15
    }
  },

  "VisionLlm": {
    "Provider": "lmstudio",                     // lmstudio | external | auto
    "Model": "ministral-3-3b-instruct-2512",
    "MaxImageWidth": 1024,
    "MaxImageHeight": 768,
    "ImageFormat": "png",                       // png | webp
    "MaxTokens": 1024,
    "Temperature": 0.1,
    "TimeoutSeconds": 30,
    "CacheEnabled": true,                       // кэш описаний по hash скриншота
    "FallbackChain": [ "lmstudio", "external:groq", "external:openai" ] // для auto
  },

  "PlannerLlm": {
    "Provider": "lmstudio",                     // lmstudio | external | auto
    "Model": "qwen3-coder-30b-a3b-instruct",
    "MaxTokens": 2048,
    "Temperature": 0.1,
    "TimeoutSeconds": 60,
    "MaxHistorySteps": 20,                      // хранить последние N шагов
    "FallbackChain": [ "lmstudio", "external:deepseek", "external:openai" ]
  },

  "Limits": {
    "MaxSteps": 30,
    "MaxTaskSeconds": 300,
    "MaxScreenshotBytes": 2097152,              // 2 MB
    "MaxTasksPerUserPer5Min": 5,
    "ActionDelayMs": 500,                       // пауза после действия
    "PageStabilityCheckMs": 500                 // ждать стабилизации страницы
  },

  "Whitelist": {
    "Domains": [
      "rzd.ru", "*.rzd.ru",
      "wikipedia.org", "*.wikipedia.org",
      "github.com", "*.github.com",
      "gosuslugi.ru", "*.gosuslugi.ru"
    ],
    "DeniedDomains": [
      "*.bank*", "*.sberbank.ru", "*.tinkoff.ru", "*.vtb.ru",
      "*.alfabank.ru", "*.gazprombank.ru"
    ],
    "AllowAnyDomain": false
  },

  "ActionValidation": {
    "BlockedKeys": [
      "F12", "Ctrl+Shift+I", "Ctrl+Shift+J", "Ctrl+U",
      "Alt+F4", "Ctrl+W", "Ctrl+Shift+W",
      "Ctrl+Shift+Delete", "Ctrl+Shift+N", "Ctrl+N"
    ],
    "BlockedHotkeys": [
      ["Ctrl","Alt","Delete"],
      ["Meta","L"], ["Meta","R"],               // Windows lock
      ["Alt","Tab"],                            // переключение окон
      ["Meta","D"], ["Meta","E"]                // показать рабочий стол / Explorer
    ],
    "MaxTextLength": 2000,
    "MaxScrollDelta": 2000
  },

  "Privacy": {
    "PersistScreenshots": false,                // НЕ сохранять в ChatMessage
    "SaveToWorkspace": true,                    // временно — workspace/screenshots/{taskId}/
    "WorkspaceRetentionHours": 1,
    "MaskUrlBar": true                          // обрезка top 40px (browser-режим)
  }
}
```

### § 5.2. `appsettings.Development.json`

```jsonc
"VisionAgent": {
  "Enabled": true,
  "Backend": {
    "Mode": "local-harness",
    "Local": {
      "CaptureMode": "gdi",
      "AllowedProcesses": [ "chrome", "msedge", "firefox" ],
      "ChromeFreshProfile": true,
      "ShowOverlay": true
    }
  },
  "VisionLlm": {
    "Provider": "lmstudio",
    "Model": "ministral-3-3b-instruct-2512",
    "MaxImageWidth": 1024,
    "MaxImageHeight": 768,
    "Temperature": 0.1
  },
  "PlannerLlm": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct",
    "Temperature": 0.1
  },
  "Limits": { "MaxSteps": 15, "MaxTaskSeconds": 120 },
  "Whitelist": {
    "Domains": [ "wikipedia.org", "*.wikipedia.org", "example.com", "localhost" ],
    "AllowAnyDomain": false
  },
  "Privacy": { "PersistScreenshots": false, "SaveToWorkspace": true, "MaskUrlBar": false }
}
```

Отличия dev: меньше viewport (быстрее), `MaxSteps = 15` (терпимее),
`MaskUrlBar = false` (проще отлаживать), узкий whitelist.

### § 5.3. User Secrets

VL-модели в LM Studio — **не требуют** ключей. Для внешних провайдеров —
те же ключи, что у `ExternalLlm` (v1.8.1). Отдельно — пароли VNC и MCP:

```powershell
cd <repo-root>/IIChatTools.API

# VNC (для remote-vnc)
dotnet user-secrets set "VisionAgent:Backend:RemoteVnc:VncPassword" "<vnc-password>"
dotnet user-secrets set "VisionAgent:Backend:RemoteVnc:McpApiKey" "<mcp-api-key>"

# VNC (для sandbox — TightVNC внутри Sandbox)
dotnet user-secrets set "VisionAgent:Backend:Sandbox:VncPassword" "<vnc-password>"

# External LLM (если используется fallback)
dotnet user-secrets set "ExternalLlm:OpenAI:ApiKey" "sk-proj-…"
dotnet user-secrets set "ExternalLlm:Groq:ApiKey"   "gsk_…"
dotnet user-secrets set "ExternalLlm:DeepSeek:ApiKey" "sk-…"
```

### § 5.4. Admin override через AppSettings

По аналогии с `SubAgents.*` (v1.4.0) и `SqlAgent.*` (v1.7.0):

| Ключ | Значение |
|---|---|
| `VisionAgent.enabled` | `"true"` / `"false"` |
| `VisionAgent.backendMode` | `"local-harness"` / `"sandbox"` / `"remote-vnc"` |
| `VisionAgent.visionLlmProvider` | `"lmstudio"` / `"external"` / `"auto"` |
| `VisionAgent.visionLlmModel` | `"ministral-3-3b-instruct-2512"` |
| `VisionAgent.plannerLlmProvider` | `"lmstudio"` / `"external"` / `"auto"` |
| `VisionAgent.plannerLlmModel` | `"qwen3-coder-30b-a3b-instruct"` |
| `VisionAgent.whitelistDomains` | `["rzd.ru", "wikipedia.org"]` |
| `VisionAgent.deniedDomains` | `["*.bank*"]` |
| `VisionAgent.maxSteps` | `"30"` |
| `VisionAgent.maxTaskSeconds` | `"300"` |

Загрузка при старте — `Program.LoadVisionAgentOverrides`. Runtime-применение —
через `VisionAgentOptionsProvider` (Singleton, `lock`).

---

## § 6. Безопасность

### § 6.1. Уровень 1 — Изоляция backend'а

| Backend | Изоляция | Кто видит, что делает LLM |
|---|---|---|
| `local-harness` | Chrome fresh profile + whitelist процессов | On-screen indicator **обязателен** |
| `sandbox` | Windows Sandbox (одноразовая VM) | On-screen indicator **обязателен** (на хосте) |
| `remote-vnc` | Физически отдельная машина | По желанию (оверлей внутри VNC) |

**Chrome fresh profile:** `--user-data-dir=%TEMP%\vision-profile-{taskId}` —
без сохранённых паролей, cookies, истории.

**Whitelist процессов (local-harness):** через FlaUI `GetForegroundWindow` →
`GetWindowThreadProcessId` → сравнение с `AllowedProcesses`. Если фокус ушёл
на `notepad.exe` — действие отменяется.

### § 6.2. Уровень 2 — Валидатор действий

`IVisionActionValidator.Validate(action)` проверяет:

| Правило | Действие при нарушении |
|---|---|
| `target` существует в `ui_elements` | Reject |
| `x ∈ [0, ViewportWidth]`, `y ∈ [0, ViewportHeight]` | Reject |
| `text.Length ≤ MaxTextLength` | Truncate + warning |
| `key` не в `BlockedKeys` (case-insensitive) | Reject |
| `hotkey` (набор) не в `BlockedHotkeys` | Reject |
| `deltaY ∈ [-MaxScrollDelta, MaxScrollDelta]` | Clamp |
| `action` ∈ whitelist | Reject |

**Формат результата:** `VisionActionResult { Success, Error, SanitizedAction }`.

### § 6.3. Уровень 3 — Approval + бюджет

**Approval** (через `ITool.RequiresApprovalForCall`):

- `run_task` — всегда. Пользователь видит: «Агент хочет управлять компьютером:
  задача X, backend Y».
- Mutation-actions (`click`, `type`, `press_key`, `hotkey`) вне `run_task` —
  всегда.
- Read-only (`screenshot`, `describe`, `move_mouse`, `scroll`, `wait`) — без
  approval.

**Бюджет** (per-user):

- `MaxSteps` (default 30) — жёсткий предел шагов.
- `MaxTaskSeconds` (default 300) — wall-clock timeout.
- `MaxTasksPerUserPer5Min` (default 5) — rate limit.

### § 6.4. Уровень 4 — On-screen indicator

**Обязателен** для `local-harness` и `sandbox`. Требования:

- Всегда видим во время выполнения задачи.
- Не перехватывает клики (кроме кнопки STOP).
- Показывает: шаг N из M, текущее действие, статус.
- **ESC** или кнопка STOP → немедленная отмена через
  `CancellationTokenSource.Cancel()`.
- Автоматическое закрытие в `finally` (даже при ошибке).

**Почему обязательно:** пользователь должен видеть, что LLM управляет его
машиной. Это не опция — это требование.

### § 6.5. Уровень 5 — Audit + privacy

**Audit** (в `AuditLogs`):

- Каждый `run_task` — 1 запись: `ToolName = "vision_agent.run_task"`,
  `ParametersJson = { backend, taskHash, taskPreview, url }` (без полного task),
  `ResultJson = { success, steps, durationMs, error }`.
- Каждое mutation-действие внутри loop — отдельная запись с `StepIndex` и
  `Reason`.
- Скриншоты в audit **НЕ** пишутся.

**Privacy:**

- `PersistScreenshots = false` (default) — скриншоты **НЕ** сохраняются в
  `ChatMessage.MetadataJson`. Только в workspace на время задачи
  (`workspace/screenshots/{taskId}/step-NNN.png`). Удаление через
  `VisionRetentionService` (TTL 1 ч).
- `MaskUrlBar = true` — обрезка top-40px в browser-режиме.
- **В логах `ILogger`** — только метаданные: `stepIndex`, `actionType`,
  `durationMs`, `hasError`. Никогда — `text`, `url` (полный), содержимое
  скриншота.

### § 6.6. Сводная таблица угроз и защит

| Угроза | Защита | Обходится? |
|---|---|---|
| LLM заходит на фишинговый сайт | Whitelist доменов | ❌ |
| LLM кликает «Удалить аккаунт» | Approval + on-screen indicator | ❌ |
| LLM вводит пароль в открытом виде | Approval + privacy | ❌ |
| LLM обходит антибот через F12 | BlockedKeys + BlockedHotkeys | ❌ |
| LLM зацикливается на капче | MaxSteps + MaxTaskSeconds | ❌ |
| LLM управляет Outlook с паролями | Whitelist процессов | ❌ |
| LLM выходит за пределы Sandbox | Sandbox — VM, изоляция | ❌ |
| Утечка PII через логи | Только метаданные | ❌ |
| Утечка PII через скриншот | MaskUrlBar + PersistScreenshots=false | ⚠️ Best-effort |
| Пользователь не видит управление | On-screen indicator (обязателен) | ❌ |

---

## § 7. План фаз (0–9)

**Итого:** ~62 ч (≈8 рабочих дней).

| Фаза | Что | Оценка | Зависимости |
|---|---|---|---|
| 0 | DESIGN (этот документ) | — | ✅ Done |
| 1 | Контракты + DTO + интерфейсы | 4 ч | Фаза 0 |
| 2 | `LocalHarnessVisionBackend` (SystemHarness) | 8 ч | Фаза 1 |
| 3 | `SandboxVisionBackend` (.wsb + noVNC) | 10 ч | Фаза 2 |
| 4 | `VncMcpVisionBackend` (MCP-клиент) | 8 ч | Фаза 1 |
| 5 | `IVisionLlmClient` + `IPlannerLlmClient` | 10 ч | Фаза 1 |
| 6 | `VisionAgentService` loop + `VisionOverlay.exe` | 8 ч | Фазы 2-5 |
| 7 | `VisionAgentTool` + Chat-интеграция | 4 ч | Фаза 6 |
| 8 | Тесты (unit + integration) | 6 ч | Фазы 1-7 |
| 9 | Документация + релиз v1.9.0 | 4 ч | Фазы 1-8 |

### § 7.1. Фаза 1 — Контракты + DTO (4 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 1.1 | `DTO/VisionAgent/VisionAgentOptions.cs` | — |
| 1.2 | `DTO/VisionAgent/VisionBackendOptions.cs` | — |
| 1.3 | `DTO/VisionAgent/VisionLlmOptions.cs` | — |
| 1.4 | `DTO/VisionAgent/PlannerLlmOptions.cs` | — |
| 1.5 | `DTO/VisionAgent/VisionActionDto.cs` | — |
| 1.6 | `DTO/VisionAgent/VisionStepDto.cs` | — |
| 1.7 | `DTO/VisionAgent/VisionTaskRequest.cs` | — |
| 1.8 | `DTO/VisionAgent/VisionTaskResultDto.cs` | — |
| 1.9 | `DTO/VisionAgent/UiElementDto.cs` | — |
| 1.10 | `DTO/VisionAgent/ScreenDescriptionDto.cs` | — |
| 1.11 | `DTO/VisionAgent/VisionActionResult.cs` | — |
| 1.12 | `Interfaces/IVisionAgentService.cs` | — |
| 1.13 | `Interfaces/IVisionBackend.cs` | — |
| 1.14 | `Interfaces/IVisionLlmClient.cs` | — |
| 1.15 | `Interfaces/IPlannerLlmClient.cs` | — |
| 1.16 | `Interfaces/IVisionActionValidator.cs` | — |

**DoD фазы:** `dotnet build` 0/0. Все DTO/интерфейсы компилируются, но нигде
не используются. Заведена **KI-124**.

### § 7.2. Фаза 2 — `LocalHarnessVisionBackend` (8 ч)

| Шаг | Что | Тесты |
|---|---|---|
| 2.1 | NuGet `SystemHarness.Core` + `SystemHarness.Windows` | — |
| 2.2 | `ScreenshotAsync()` — `Screen.CaptureAsync` | 2 |
| 2.3 | `ClickAsync(x, y)` / `DoubleClickAsync` / `RightClickAsync` | 3 |
| 2.4 | `TypeAsync(text)` — `Keyboard.TypeAsync` | 1 |
| 2.5 | `PressKeyAsync(key)` / `HotkeyAsync(keys)` | 2 |
| 2.6 | `ScrollAsync(deltaY)` | 1 |
| 2.7 | Whitelist процессов через FlaUI (`GetForegroundWindow`) | 2 |
| 2.8 | Downscale до `MaxImageWidth/Height` (ImageSharp) | 2 |
| 2.9 | Chrome fresh profile: `--user-data-dir` + relaunch | 1 |

**DoD фазы:** через backend можно открыть Chrome, снять скриншот, кликнуть
в (100, 200), ввести текст. Юнит-тесты — с mock `WindowsHarness`.

### § 7.3. Фаза 3 — `SandboxVisionBackend` (10 ч)

| Шаг | Что | Тесты |
|---|---|---|
| 3.1 | Генерация `.wsb`-конфига из шаблона (Chrome + TightVNC) | 2 |
| 3.2 | Запуск Windows Sandbox через `WindowsSandbox.exe config.wsb` | 1 |
| 3.3 | Ожидание старта VNC-сервера внутри Sandbox (timeout 60с) | 2 |
| 3.4 | Подключение к VNC (RFB) через `VncSharp` / `RemoteViewing` | 3 |
| 3.5 | `ScreenshotAsync` — через VNC framebuffer | 2 |
| 3.6 | `ClickAsync` / `TypeAsync` — через VNC input events | 2 |
| 3.7 | Auto-shutdown Sandbox после задачи | 1 |
| 3.8 | Shared folder для обмена файлами | 1 |

**DoD фазы:** Sandbox запускается, VNC подключается, задачи выполняются,
Sandbox закрывается.

### § 7.4. Фаза 4 — `VncMcpVisionBackend` (8 ч)

| Шаг | Что | Тесты |
|---|---|---|
| 4.1 | MCP-клиент (`ModelContextProtocol` .NET SDK или HTTP-мост) | 3 |
| 4.2 | `ScreenshotAsync` → `vnc_screenshot` | 1 |
| 4.3 | `ClickAsync` → `vnc_click` | 1 |
| 4.4 | `TypeAsync` → `vnc_type_text` | 1 |
| 4.5 | `PressKeyAsync` / `HotkeyAsync` → `vnc_keypress` | 1 |
| 4.6 | Health-check MCP-сервера при старте задачи | 1 |

**DoD фазы:** через MCP-клиент можно подключиться к `vnc-mcp-server`,
снять скриншот удалённой машины, кликнуть, ввести текст.

### § 7.5. Фаза 5 — `IVisionLlmClient` + `IPlannerLlmClient` (10 ч)

| Шаг | Что | Тесты |
|---|---|---|
| 5.1 | `LmStudioVisionClient` — POST `/v1/chat/completions` с `image_url` | 3 |
| 5.2 | Парсер `ScreenDescriptionDto` из JSON-ответа (fallback на текст) | 5 |
| 5.3 | `LmStudioPlannerClient` — POST `/v1/chat/completions` с system prompt | 3 |
| 5.4 | Парсер `VisionActionDto` из JSON-ответа | 5 |
| 5.5 | `ExternalVisionClient` — обёртка над `IExternalLlmClient` | 3 |
| 5.6 | `ExternalPlannerClient` — то же | 3 |
| 5.7 | `AutoVisionClient` / `AutoPlannerClient` — fallback chain | 3 |
| 5.8 | Кэш описаний скриншотов (по SHA-256 PNG) | 2 |
| 5.9 | System prompts (Vision + Planner) | — |

**DoD фазы:** клиенты отправляют PNG + prompt в LM Studio, получают
`ScreenDescriptionDto` / `VisionActionDto`. Fallback работает.

### § 7.6. Фаза 6 — `VisionAgentService` + loop + Overlay (8 ч)

| Шаг | Что | Тесты |
|---|---|---|
| 6.1 | `VisionActionValidator` — blocked keys, координаты, длина | 8 |
| 6.2 | `VisionAgentService.RunTaskAsync` — основной loop | 5 |
| 6.3 | Timeout через `CancellationTokenSource.CancelAfter` (RULES § 4.48) | 2 |
| 6.4 | `InMemoryVisionRateLimiter` — 5/5min per-user | 3 |
| 6.5 | Сохранение скриншотов в `workspace/screenshots/{taskId}/` | 2 |
| 6.6 | `VisionRetentionService` (TTL 1 ч) — BackgroundService | 2 |
| 6.7 | `IIChatTools.VisionOverlay` — WPF-проект + IPC | 4 |
| 6.8 | `VisionOverlayLauncher` — запуск / остановка overlay | 2 |

**DoD фазы:** `RunTaskAsync(task="…")` выполняет loop, возвращает
`VisionTaskResultDto`. Overlay виден во время задачи, ESC останавливает.

### § 7.7. Фаза 7 — `VisionAgentTool` + Chat (4 ч)

| Шаг | Что | Тесты |
|---|---|---|
| 7.1 | `VisionAgentTool` (top-level `ITool`) — switch по action | — |
| 7.2 | `RequiresApprovalForCall` — per-action (KI-101 паттерн) | 3 |
| 7.3 | `Startup.RegisterVisionAgentTools(services, configuration)` | — |
| 7.4 | `ChatStreamService` — `vision_agent` в `allowedNames` (RULES § 4.44) | — |
| 7.5 | Smoke через curl: describe / run_task | — |
| 7.6 | Smoke через Chat UI: «купи билет РЖД…» | — |

**DoD фазы:** Chat видит **14 инструментов** (было 13). LLM может вызвать
`vision_agent` и выполнить задачу.

### § 7.8. Фаза 8 — Тесты (6 ч)

| Шаг | Что | Кол-во |
|---|---|---|
| 8.1 | `VisionActionValidatorTests` | ~15 |
| 8.2 | `LocalHarnessVisionBackendTests` (mock `WindowsHarness`) | ~10 |
| 8.3 | `SandboxVisionBackendTests` (.wsb-генерация) | ~5 |
| 8.4 | `VncMcpVisionBackendTests` (mock MCP) | ~8 |
| 8.5 | `LmStudioVisionClientTests` + `LmStudioPlannerClientTests` | ~15 |
| 8.6 | `VisionAgentServiceTests` (fake backend + fake LLM) | ~12 |
| 8.7 | `VisionAgentToolTests` | ~8 |
| 8.8 | Integration: `run_task` на `example.com` | ~2 |

**Итого: +75 тестов** (658 → ~733).

### § 7.9. Фаза 9 — Документация + релиз v1.9.0 (4 ч)

| Шаг | Что |
|---|---|
| 9.1 | README — раздел «Vision Agent» (3 backend'а, примеры, ограничения) |
| 9.2 | RULES.md § 4 — новые правила (по итогам уроков) |
| 9.3 | KNOWN_ISSUES.md — KI-124 → Fixed |
| 9.4 | TESTING.md — smoke для всех 3 backend'ов |
| 9.5 | CHANGELOG.md — [1.9.0] |
| 9.6 | Directory.Build.props — `<Version>1.9.0</Version>` |
| 9.7 | Tag v1.9.0 + GitHub Release + Docker |

---

## § 8. Definition of Done (v1.9.0)

### § 8.1. Функциональные требования

- [ ] `VisionAgentTool` зарегистрирован в DI и виден Chat (**14 инструментов**).
- [ ] `local-harness` backend: скриншот + клик + ввод работают на Chrome.
- [ ] `sandbox` backend: Windows Sandbox запускается, VNC подключается, задачи
      выполняются, Sandbox закрывается после задачи.
- [ ] `remote-vnc` backend: MCP-клиент общается с `vnc-mcp-server`.
- [ ] Vision LLM (`ministral-3-3b`) возвращает `{ description, ui_elements[] }`.
- [ ] Planner LLM (`qwen3-coder-30b-a3b`) возвращает `{ action, target, text? }`.
- [ ] Все три модели — в конфиге, взаимозаменяемы без правки кода.
- [ ] Whitelist доменов блокирует не-разрешённые URL.
- [ ] Whitelist процессов блокирует не-browser окна (local-harness).
- [ ] Валидатор блокирует `F12`, `Ctrl+Shift+I`, `Ctrl+Alt+Del`, `Alt+Tab`.
- [ ] Approval срабатывает 1 раз на `run_task`, 1 раз на mutation-действие.
- [ ] `MaxSteps` / `MaxTaskSeconds` / rate limit работают.
- [ ] On-screen indicator виден во время задачи, ESC останавливает.
- [ ] Скриншоты сохраняются в `workspace/screenshots/{taskId}/`, удаляются
      через TTL 1 ч.
- [ ] `PersistScreenshots = false` — скриншоты **не** попадают в
      `ChatMessage.MetadataJson`.
- [ ] Admin UI `/admin → Vision Agent` позволяет менять whitelist / limits /
      provider в runtime.

### § 8.2. Нефункциональные

- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — ~604/604.
- [ ] CI + Docker Publish — зелёные.
- [ ] Покрытие `VisionActionValidator` — > 90%.
- [ ] Все секреты (VNC, MCP, API-ключи) — только в User Secrets.
- [ ] Все `run_task` — в `AuditLogs` (без task text, без скриншотов).
- [ ] Логирование — без PII.
- [ ] README + CHANGELOG + KNOWN_ISSUES + RULES + TESTING — обновлены.

### § 8.3. Smoke (10 сценариев)

| # | Сценарий | Ожидание |
|---|---|---|
| 1 | `vision_agent(action=describe)` на wikipedia.org | `{ description, ui_elements[] }` |
| 2 | `run_task(task="найди статью про Москву", url="https://wikipedia.org")` | success, 5-10 шагов |
| 3 | `run_task(url="https://evil.com")` (не в whitelist) | Fail: «Домен не в whitelist» |
| 4 | Валидатор блокирует F12 в prompt | Reject |
| 5 | Chat UI: «Зайди на example.com и скажи, что там» | LLM → `vision_agent` → ответ |
| 6 | Approval reject → loop не стартует | Fail: «Пользователь отклонил» |
| 7 | `MaxSteps=5` на большой задаче | Fail: «Исчерпан лимит шагов» |
| 8 | On-screen indicator: ESC во время задачи | Loop останавливается немедленно |
| 9 | `local-harness`: фокус ушёл на notepad.exe | Действие отменяется |
| 10 | `sandbox`: Sandbox закрылся после задачи | Процесс `WindowsSandbox.exe` не в списке |

### § 8.4. Документация

- [ ] README — раздел «Vision Agent» (3 backend'а, требования, ограничения).
- [ ] CHANGELOG — [1.9.0].
- [ ] KNOWN_ISSUES — KI-124 → Fixed.
- [ ] TESTING.md — smoke для 3 backend'ов.
- [ ] RULES.md — обновлён, если есть новые уроки.

---

## § 9. Ссылки

### § 9.1. KI

- **KI-131** — Vision Agent (этот документ). Planned, v1.12.0.
- **KI-137** (Planned, v1.12.x) — Vision Agent: OCR-fallback для мелкого текста.
- **KI-138** (Planned, v1.12.x) — Vision Agent: маскирование PII на скриншотах
  (детекция credit card / email через VL-модель).
- **KI-139** (Planned, v1.12.x) — Vision Agent: поддержка внешних VL
  (Claude Computer Use / OpenAI CUA).

### § 9.2. Правила (RULES.md)

- § 1.14 — локализация (RU + EN).
- § 4.16 — camelCase ключи `.resx`.
- § 4.17 — JS-локализация через `data-*`.
- § 4.44 — новый top-level `ITool` → `allowedNames` в Chat.
- § 4.46 — default interface method не виден через конкретный тип.
- § 4.48 — `HttpClient.Timeout` нельзя менять после первого `SendAsync`.
- § 5.x — User Secrets, без PII в логах.

### § 9.3. Внешние источники

**Computer Use reference:**
- **Anthropic:** [Computer Use demo](https://github.com/anthropics/anthropic-quickstarts/tree/main/computer-use-demo).
- **OpenAI:** [Operator / CUA](https://openai.com/index/introducing-operator/).
- **Microsoft:** [UFO](https://github.com/microsoft/UFO) — Windows UIAutomation.
- **ByteDance:** [UI-TARS](https://github.com/bytedance/UI-TARS) — end-to-end UI agent.

**Backends:**
- **SystemHarness:** `SystemHarness.Core` + `SystemHarness.Windows` (NuGet 0.28.x)
  — screen capture, mouse, keyboard, UI Automation, Office.
- **Deskhand:** .NET 9 + FlaUI (UIA3) + MCP-сервер.
- **Zaya.Screenshot:** Windows Graphics Capture API + Direct3D 11.
- **vnc-mcp-server:** `volkan-m/vnc-mcp-server` (TypeScript, Windows-friendly),
  `signal-slot/mcp-vnc` (C++/Qt, .exe).
- **Windows Sandbox:** встроен в Windows 10/11 Pro/Enterprise.

**VL-модели:**
- **Ministral-3-3B-Instruct-2512:** Mistral AI, 3.4B language + 0.4B vision,
  FP8, 256K context.
- **qwen3-coder-30b-a3b-instruct:** MoE, 3.3B активных, Agentic Browser-Use SOTA.

### § 9.4. Внутренние документы

- `docs/development/RULES.md` — правила разработки (v1.4.26+).
- `docs/development/ARCHITECTURE.md` — архитектура проекта.
- `docs/development/v1.7/DESIGN_DB_AGENT.md` — эталон для top-level `ITool`
  и per-action approval (KI-101).
- `docs/development/v1.8/DESIGN_EXTERNAL_LLM.md` — эталон для fallback-провайдера.
- `docs/KNOWN_ISSUES.md` — реестр проблем.

### § 9.5. Приложения

**Приложение A — пример loop'а `run_task` (РЖД):**

```
Task:  «Купи билет РЖД Москва → Петропавловск-Камчатский, купе, нижняя полка»

Step 1: screenshot → Vision LLM
        → { description: "Форма поиска РЖД. Поля: Откуда, Куда, Дата.",
            ui_elements: [ {id:"from", center:{440,230}}, {id:"to", center:{440,290}} ] }
        Planner: { action: "click", target: "from", reason: "Поле «Откуда»" }

Step 2: click(from) → пауза 500ms → screenshot → Vision LLM
        → { description: "Фокус в поле «Откуда». Курсор мигает." }
        Planner: { action: "type", target: "from", text: "Москва" }

Step 3: type("Москва") → пауза 500ms → screenshot → Vision LLM
        → { description: "Выпадающий список: Москва (DME), Москва (VKO)...",
            ui_elements: [ {id:"dme", center:{440,350}} ] }
        Planner: { action: "click", target: "dme" }

... (аналогично для «Куда», «Дата»)

Step N: Vision LLM → "Страница с поездами. Купе, нижняя полка — есть."
        Planner: { action: "done", reason: "Билеты найдены, купе доступно" }

Result: { success: true, steps: 12, durationMs: 45230, summary: "..." }
```

**Приложение B — пример ответа `/api/tools/execute`:**

```json
{
  "success": true,
  "data": {
    "task": "Купи билет РЖД Москва→Камчатка, купе",
    "success": true,
    "backend": "sandbox",
    "steps": [
      { "stepIndex": 1, "action": "click", "target": "from", "llmReason": "Поле Откуда", "durationMs": 812 },
      { "stepIndex": 2, "action": "type", "target": "from", "text": "Москва", "durationMs": 645 }
    ],
    "summary": "Найдены билеты, купе доступно",
    "finalScreenshotPath": "workspace/screenshots/vt_8f2a/step-012.png"
  },
  "message": "Задача выполнена за 12 шагов (45.2 с)."
}
```

**Приложение C — формат UI-модалки approval для `run_task`:**

Модалка подтверждения (уже реализована в v1.3.0) показывает:

- Инструмент: `vision_agent`
- Действие: `run_task`
- Задача: «Купи билет РЖД Москва→Камчатка, купе, нижняя полка»
- Backend: `sandbox` (Windows Sandbox)
- Стартовый URL: `https://rzd.ru`
- Лимит: 30 шагов / 5 минут
- Vision LLM: `ministral-3-3b-instruct-2512` (LM Studio)
- Planner LLM: `qwen3-coder-30b-a3b-instruct` (LM Studio)
- **On-screen indicator:** включён (ESC для остановки)

Кнопки: Approve / Reject.

**Приложение D — пример `.wsb`-конфига (Sandbox):**

```xml
<Configuration>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>%USERPROFILE%\IIChatToolsVision</HostFolder>
      <SandboxFolder>C:\Vision</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell -Command "Start-Process chrome.exe -ArgumentList '--user-data-dir=C:\Vision\chrome-profile','https://rzd.ru' -Wait"</Command>
  </LogonCommand>
</Configuration>
```

**Приложение E — формат `ScreenDescriptionDto` (Vision LLM):**

```json
{
  "description": "Страница поиска РЖД. Поля: Откуда, Куда, Дата. Кнопка «Найти» неактивна.",
  "ui_elements": [
    {
      "id": "from_input",
      "type": "text_input",
      "label": "Откуда",
      "value": "",
      "bounds": { "x": 340, "y": 210, "w": 200, "h": 40 },
      "center": { "x": 440, "y": 230 }
    },
    {
      "id": "search_btn",
      "type": "button",
      "label": "Найти",
      "enabled": false,
      "center": { "x": 640, "y": 320 }
    }
  ]
}
```

**© 2026 RuChating (iilmchat) · IIChatTools v1.12.0**
