# DESIGN v1.4.0 — Multi-Agent (специализированные суб-агенты)

**Версия:** 1.4.0-draft
**Дата:** 2026-09-23
**Автор:** IIChatTools Team
**Статус:** Design (согласовано, ждём реализации)
**Связанные KI:** KI-052 (основной), KI-053 (multi-user approvals — v1.4.0.x), KI-049 (tiktoken — v1.4.0.x)

---

## § 1. Контекст

В v1.3.0 Chat UI видит **40 инструментов** одновременно. Одна модель (особенно 4B-7B)
плохо выбирает инструмент из такого количества — особенно при неоднозначных задачах.
В `SubAgent:DefaultAllowedTools` уже применён рабочий приём: список ограничен 10-12
инструментами, и это улучшает точность. Но всё ещё остаётся «шум»: LLM получает
инструменты из разных групп (файлы + Git + Web) в одном промпте.

**Цель v1.4.0:** разбить 40 инструментов на **6 специализированных суб-агентов**
по группам. Оркестратор (Chat) видит **7 верхнеуровневых инструментов** — 6 агентов
+ `consult_secondary_agent` (fallback). Внутри агента — свой system prompt, узкий
набор инструментов, опционально — отдельная модель.

---

## § 2. Проблема (из KI-052)

**Симптомы:**
- Модель `qwen/qwen3-4b-2507` иногда выбирает `git_status`, когда нужен `list_directory`.
- При 40 инструментах в промпте — большая нагрузка на контекст (экономим токены).
- Модели уровня 4B «теряются» при большом выборе.
- Нет способа задать разный system prompt для разных типов задач (файлы vs код vs веб).

**Плюсы решения:**
- Маленькие промпты → точнее выбор.
- Тонкая настройка модели под группу (code_agent → coder-модель).
- Возможность отключить группу (например, `github_agent` в offline-сети).

**Минусы (принимаем):**
- Сложность: два уровня агентов (Chat → SubAgent).
- Дороже по токенам (двойной проход: Chat + SubAgent).
- Возможны «зацикливания» при плохих промптах (защита — `MaxSteps`).

---

## § 3. Архитектура (диаграмма)

```
┌─────────────────────────────────────────────────────────────────┐
│                    Пользователь (Chat UI)                       │
│                    /chat                                        │
└──────────────────────────┬──────────────────────────────────────┘
                           │ SSE
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│              ChatStreamService (оркестратор)                    │
│  - видит 7 верхнеуровневых инструментов:                        │
│    file_system_agent, code_agent, web_agent, git_agent,         │
│    github_agent, planner_agent, consult_secondary_agent         │
│  - multi-turn loop (до 5 итераций)                              │
└──────────────────────────┬──────────────────────────────────────┘
                           │ tool_call(name="file_system_agent", task=...)
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│         FileSystemAgentTool : ITool                             │
│  - RequiresApprovalByDefault = true                             │
│  - Параметры: { task, context?, maxSteps? }                     │
│  - Внутри: SubAgentService.ExecuteTaskAsync(                    │
│       request.AllowedTools = [13 FS-инструментов],              │
│       request.SystemPromptOverride = "Ты — агент файловой...",  │
│       request.ModelOverride = "qwen/qwen3-4b-2507")             │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│         SubAgentService (существующий)                          │
│  - multi-turn loop (MaxSteps)                                   │
│  - LLM вызывает FS-инструменты (list_directory, read_file, ...) │
│  - возвращает SubAgentTaskResult                                │
└─────────────────────────────────────────────────────────────────┘
```

---

## § 4. Новые DTO

### § 4.1. `SubAgentDescriptor` — метаданные агента

```csharp
namespace IIChatTools.Services.DTO.SubAgent
{
    /// <summary>
    /// Описание специализированного суб-агента (для реестра и админки).
    /// </summary>
    public class SubAgentDescriptor
    {
        /// <summary>Техническое имя (snake_case), например "file_system_agent".</summary>
        public string Name { get; set; }

        /// <summary>Русское отображаемое имя, например "Агент файловой системы".</summary>
        public string DisplayName { get; set; }

        /// <summary>Краткое описание для UI/админки (1-2 предложения).</summary>
        public string Description { get; set; }

        /// <summary>Полный system prompt, уходящий модели внутри агента.</summary>
        public string SystemPrompt { get; set; }

        /// <summary>Список имён доступных инструментов внутри агента.</summary>
        public IReadOnlyList<string> AllowedTools { get; set; }

        /// <summary>Модель LM Studio (если null — fallback на LmStudio:Model).</summary>
        public string Model { get; set; }

        /// <summary>Максимум шагов внутри агента (1–30).</summary>
        public int MaxSteps { get; set; } = 10;

        /// <summary>
        /// Требует ли вызов агента подтверждения пользователя целиком
        /// (approval на входе в агента, не на каждый tool-call).
        /// </summary>
        public bool RequiresApprovalByDefault { get; set; }

        /// <summary>Отключён ли агент (не виден в списке tools Chat).</summary>
        public bool Disabled { get; set; }
    }
}
```

### § 4.2. Правки `SubAgentTaskRequest` (обратная совместимость)

Добавляем **два опциональных поля** (оба — `null` по умолчанию, ничего не ломается):

```csharp
/// <summary>
/// Системный промпт. Если null — используется SubAgent:SystemPrompt из конфига.
/// </summary>
public string SystemPromptOverride { get; set; }

/// <summary>
/// Модель LM Studio. Если null — LmStudio:Model (для consult_secondary_agent)
/// или SubAgents:X.Model (для специализированных).
/// </summary>
public string ModelOverride { get; set; }
```

---

## § 5. Реестр агентов

### § 5.1. `ISubAgentRegistry`

```csharp
namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Реестр специализированных суб-агентов.
    /// Загружается из конфигурации (SubAgents:*), позволяет получать дескрипторы
    /// и обновлять их в runtime (для админки).
    /// </summary>
    public interface ISubAgentRegistry
    {
        /// <summary>Все зарегистрированные агенты (включая отключённые).</summary>
        IReadOnlyList<SubAgentDescriptor> GetAll();

        /// <summary>Только включённые (Disabled = false) — для Chat tools.</summary>
        IReadOnlyList<SubAgentDescriptor> GetEnabled();

        /// <summary>Описание агента по имени (null, если не найден).</summary>
        SubAgentDescriptor Get(string name);

        /// <summary>Обновить дескриптор (для админки).</summary>
        void Update(SubAgentDescriptor descriptor);

        /// <summary>Сбросить дескриптор к значениям из appsettings.json.</summary>
        void Reset(string name);
    }
}
```

### § 5.2. `SubAgentRegistry` — реализация

- **Singleton** (как `ToolRegistry`).
- Инициализация: `IConfiguration.GetSection("SubAgents")` → словарь.
- Ключ секции в `appsettings.json` = `Name` агента (snake_case).
- Порядок сортировки `GetAll()` — по имени (детерминированно).
- Fallback: если секция `SubAgents` пуста → реестр пуст (Chat работает только с `consult_secondary_agent`).

**Приоритет загрузки (на будущее, для Фазы 6/админки):**
1. `AppSettings` из БД (высший).
2. `appsettings.json` (база).
3. Hardcoded fallback — если оба пусты.

---

## § 6. Оркестратор (Chat → named tools)

`ChatStreamService` не меняется в архитектуре. Меняется только **список tools**,
который уходит в LM Studio.

**Было:** `SubAgent:DefaultAllowedTools` (12 инструментов из 40).
**Станет:** `SubAgentRegistry.GetEnabled()` → 6 `*_agent` + `consult_secondary_agent`
(итого 7 верхнеуровневых инструментов).

**Generic-класс `AgentToolBase`** (базовый для всех агентов):

```csharp
namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Базовый класс для инструментов-обёрток вокруг специализированных суб-агентов.
    /// Наследники переопределяют Name, Description, Parameters и AgentName.
    /// </summary>
    public abstract class AgentToolBase : ITool
    {
        protected readonly ISubAgentService SubAgentService;
        protected readonly ISubAgentRegistry Registry;
        protected readonly ILogger Logger;

        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract IReadOnlyList<ToolParameterDescriptor> Parameters { get; }
        public virtual bool RequiresApprovalByDefault => true;

        /// <summary>Имя агента в реестре (file_system_agent, code_agent, ...).</summary>
        protected abstract string AgentName { get; }

        protected AgentToolBase(
            ISubAgentService subAgentService,
            ISubAgentRegistry registry,
            ILogger logger)
        {
            SubAgentService = subAgentService ?? throw new ArgumentNullException(nameof(subAgentService));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public virtual async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var task = arguments.GetString("task");
            if (string.IsNullOrWhiteSpace(task)) return ToolResult.Fail("Не указана задача");
            if (task.Length > 8000) return ToolResult.Fail("Задача превышает 8000 символов");

            var extraContext = arguments.GetString("context");
            var maxSteps = arguments.GetInt("maxSteps", 0);

            var descriptor = Registry.Get(AgentName);
            if (descriptor == null)
                return ToolResult.Fail($"Агент '{AgentName}' не найден в реестре");
            if (descriptor.Disabled)
                return ToolResult.Fail($"Агент '{AgentName}' отключён администратором");

            var request = new SubAgentTaskRequest
            {
                Task = task,
                Context = extraContext,
                MaxSteps = maxSteps > 0 ? maxSteps : descriptor.MaxSteps,
                AllowedTools = descriptor.AllowedTools,
                SystemPromptOverride = descriptor.SystemPrompt,
                ModelOverride = descriptor.Model
            };

            var result = await SubAgentService.ExecuteTaskAsync(context, request);
            return ToolResult.Ok(new
            {
                sessionId = result.SessionId,
                finalAnswer = result.FinalAnswer,
                completed = result.Completed,
                steps = result.Steps,
                durationMs = result.DurationMs,
                usedTools = result.UsedTools
            });
        }
    }
}
```

**6 наследников** (по одному на агента):
- `FileSystemAgentTool` → `file_system_agent`
- `CodeAgentTool` → `code_agent`
- `WebAgentTool` → `web_agent`
- `GitAgentTool` → `git_agent`
- `GitHubAgentTool` → `github_agent`
- `PlannerAgentTool` → `planner_agent`

Каждый — **1 класс + 1 строка регистрации** в `Startup.cs` (правило 1.15).

---

## § 7. Специализированные агенты

| # | `Name` | `DisplayName` (RU) | Инструментов | Модель по умолчанию | Approval |
|---|---|---|:---:|---|---|
| 1 | `file_system_agent` | Агент файловой системы | 13 | `qwen/qwen3-4b-2507` | ✅ |
| 2 | `code_agent` | Агент выполнения кода | 3 | `gemma-4-12b-coder...` | ✅ |
| 3 | `web_agent` | Веб-агент | 3 | `qwen/qwen3-4b-2507` | ❌ |
| 4 | `git_agent` | Git-агент | 7 | `qwen/qwen3-4b-2507` | ✅ |
| 5 | `github_agent` | GitHub-агент | 7 | `qwen/qwen3-4b-2507` | ✅ |
| 6 | `planner_agent` | Агент-планировщик | 2 (`save_memory`, `get_system_info`) | `gemma-4-12b...` | ❌ |
| — | `consult_secondary_agent` | Универсальный агент *(fallback)* | browser (4) + остальное | `LmStudio:Model` | ✅ |

**`consult_secondary_agent`** — остаётся как есть (существующий `ConsultSecondaryAgentTool`).
Ему достаются browser-инструменты + всё, что не укладывается в группы.

---

## § 8. Конфигурация (`appsettings.json`)

Ключи секции `SubAgents` — **технические имена агентов** (snake_case). Это `Name`.

```jsonc
"SubAgents": {
  "file_system_agent": {
    "Enabled": true,
    "DisplayName": "Агент файловой системы",
    "Description": "Работа с файлами: чтение, запись, поиск, метаданные.",
    "Model": "qwen/qwen3-4b-2507",
    "MaxSteps": 10,
    "RequiresApproval": true,
    "SystemPrompt": "Ты — агент файловой системы IIChatTools. Твоя задача — выполнять файловые операции в workspace пользователя. Действуй пошагово, проверяй результаты. Отвечай на русском языке. Максимум {{maxSteps}} шагов.",
    "AllowedTools": [
      "list_directory", "read_file", "save_file", "delete_path",
      "replace_text_in_file", "move_file", "copy_file",
      "find_files", "get_file_metadata", "fuzzy_find_local_files",
      "make_directory", "change_directory", "delete_files_by_pattern"
    ]
  },
  "code_agent": {
    "Enabled": true,
    "DisplayName": "Агент выполнения кода",
    "Description": "Запуск JavaScript, Python и shell-команд.",
    "Model": "gemma-4-12b-coder-fable5-composer2.5-v1",
    "MaxSteps": 10,
    "RequiresApproval": true,
    "AllowedTools": ["run_javascript", "run_python", "execute_command"]
  },
  "web_agent": {
    "Enabled": true,
    "DisplayName": "Веб-агент",
    "Description": "Поиск в интернете и Wikipedia, загрузка веб-страниц.",
    "Model": "qwen/qwen3-4b-2507",
    "MaxSteps": 10,
    "RequiresApproval": false,
    "AllowedTools": ["web_search", "wikipedia_search", "fetch_web_content"]
  },
  "git_agent": {
    "Enabled": true,
    "DisplayName": "Git-агент",
    "Description": "Операции с локальным Git-репозиторием.",
    "Model": "qwen/qwen3-4b-2507",
    "MaxSteps": 10,
    "RequiresApproval": true,
    "AllowedTools": ["git_status", "git_diff", "git_log", "git_add", "git_commit", "git_checkout", "git_push"]
  },
  "github_agent": {
    "Enabled": true,
    "DisplayName": "GitHub-агент",
    "Description": "Работа с GitHub через gh CLI: issues, PRs, комментарии.",
    "Model": "qwen/qwen3-4b-2507",
    "MaxSteps": 10,
    "RequiresApproval": true,
    "AllowedTools": ["gh_auth_status", "gh_create_issue", "gh_list_issues", "gh_view_comments", "gh_create_pr", "gh_list_prs", "gh_view_pr_diff"]
  },
  "planner_agent": {
    "Enabled": true,
    "DisplayName": "Агент-планировщик",
    "Description": "Долговременная память и системная информация.",
    "Model": "gemma-4-12b-coder-fable5-composer2.5-v1",
    "MaxSteps": 5,
    "RequiresApproval": false,
    "AllowedTools": ["save_memory", "get_system_info"]
  }
}
```

---

## § 9. Админка

### § 9.1. UI

**Новая вкладка «Агенты»** в `/admin` (после «Аудит»):

**Таблица агентов:**

| Агент | Русское имя | Модель | Инструментов | Approval | Вкл | Действия |
|---|---|---|---|---|---|---|
| file_system_agent | Агент файловой системы | qwen/qwen3-4b | 13 | ✅ | ✅ | ✏️ Изменить / ↻ Сбросить |

**Модалка редактирования** (переиспользуем `showModal` из `admin.js`):
- DisplayName (text)
- Description (textarea)
- Model (text)
- MaxSteps (number, 1-30)
- RequiresApproval (checkbox)
- SystemPrompt (textarea)
- AllowedTools (multi-select из `/api/tools`)

**Секция «Статистика» (read-only):**
- Всего запусков агента (`AgentStates`).
- Среднее время выполнения.
- % успешных (`Completed = true`).
- Последние 10 задач.

### § 9.2. API endpoints

| Метод | URL | Назначение |
|---|---|---|
| `GET` | `/api/admin/agents` | Список агентов + статистика |
| `PUT` | `/api/admin/agents/{name}` | Обновить дескриптор (→ `AppSettings`) |
| `POST` | `/api/admin/agents/{name}/reset` | Сбросить к `appsettings.json` |

Все — под `[Authorize(Policy = "AdminOnly")]`.

### § 9.3. Хранение переопределений

Таблица `AppSettings` (уже есть). Ключи:
- `SubAgents.file_system_agent.Model`
- `SubAgents.file_system_agent.SystemPrompt`
- `SubAgents.file_system_agent.AllowedTools` (JSON-массив)
- `SubAgents.file_system_agent.MaxSteps`
- `SubAgents.file_system_agent.RequiresApproval`
- `SubAgents.file_system_agent.Enabled`

`SubAgentRegistry` при старте: `appsettings.json`, потом накладывает `AppSettings` из БД.

---

## § 10. Локализация

**Новые ключи `.resx` (RU + EN, правило 1.14):**

| Ключ | RU | EN |
|---|---|---|
| `AdminTabAgents` | Агенты | Agents |
| `AgentColumnName` | Техническое имя | Technical name |
| `AgentColumnDisplayName` | Отображаемое имя | Display name |
| `AgentColumnModel` | Модель | Model |
| `AgentColumnTools` | Инструментов | Tools count |
| `AgentColumnApproval` | Approval | Approval |
| `AgentColumnEnabled` | Включён | Enabled |
| `AgentColumnActions` | Действия | Actions |
| `AgentEditTitle` | Редактировать агента | Edit agent |
| `AgentResetConfirm` | Сбросить агента к значениям по умолчанию? | Reset agent to defaults? |
| `AgentResetSuccess` | Агент сброшен | Agent reset |
| `AgentSaveSuccess` | Агент сохранён | Agent saved |
| `AgentStatsTotal` | Всего запусков | Total runs |
| `AgentStatsAvgTime` | Среднее время | Average time |
| `AgentStatsSuccessRate` | Успешных | Success rate |

**Русские `DisplayName`** у агентов — в `appsettings.json` (данные, не UI-строки).

---

## § 11. Approval в суб-агентах

**Решение:** approval **на уровне агента целиком**.

**Flow:**
1. Пользователь в чате → «LLM решает вызвать `file_system_agent(task='Создай файл X')`».
2. Chat видит `RequiresApprovalByDefault = true` → SSE `tool_approval_required`.
3. Пользователь → модалка: «Агент файловой системы хочет выполнить задачу: Создай файл X».
4. Approve → `FileSystemAgentTool.ExecuteAsync` → внутри `SubAgentService` выполняет **все** FS-инструменты без дальнейших approval.
5. Reject → `ToolResult.Fail("Пользователь отклонил")`.

**Обоснование:**
- **UX:** одна модалка вместо N (при «Создай 5 файлов» было бы 5 модалок подряд).
- **Безопасность:** пользователь видит задачу целиком и решает.
- **ChatGPT-style:** approve once per agent task.
- **Технически:** SubAgentService — batch, не SSE. Вложенный approve требует переделки на `IAsyncEnumerable` (~2 дня).

**Риски (осознанные):**
- LLM внутри агента может сделать лишнее (например, удалить файл).
- **Митигации:** System prompt агента («строго в рамках задачи»), ограничение `AllowedTools` в админке, `MaxSteps`, аудит всех шагов в `AgentStates`.
- **В v1.4.x:** опциональный «строгий режим» с approval на каждый mutating.

---

## § 12. Тесты

### § 12.1. Unit — `SubAgentRegistryTests` (4 теста)

- `GetAll_ReturnsConfiguredAgents`
- `Get_UnknownName_ReturnsNull`
- `GetEnabled_ExcludesDisabled`
- `Update_OverridesConfig` + `Reset_RestoresDefaults`

### § 12.2. Integration — `SpecializedSubAgentServiceTests` (1 тест)

- `ExecuteTaskAsync_FileSystemAgent_UsesAllowedToolsOnly` — подсовываем `FakeLmStudioClient`, у которого LLM вызывает 2 инструмента: разрешённый + неразрешённый. Проверяем, что неразрешённый не выполнен.

**Ожидаем:** 36 + 5 = **41/41**.

---

## § 13. Ограничения (осознанные)

| # | Ограничение | Причина |
|---|---|---|
| 1 | Результат агента отдаётся **одним куском** (не SSE-стрим) | MVP; стриминг из SubAgent → Chat требует переделки на `IAsyncEnumerable` (~2 дня). Все шаги — в `AgentStates` для аудита |
| 2 | Approval **на входе в агента**, не на каждом tool | UX: 1 модалка вместо N. Технически: вложенный approval требует переделки SubAgentService |
| 3 | Вложенные агенты запрещены (рекурсия) | Защита от бесконечного цикла (`ParentToolName`) |
| 4 | `ModelOverride` через перегрузку `ILmStudioClient.CompleteAsync(..., string model = null)` | Thread-safe, обратносовместимо, явно |
| 5 | Статистика — только по `AgentStates` (не по каждому tool-call) | Tool-call'ы не пишутся отдельно (идут через `ToolRegistry.ExecuteAsync`) |
| 6 | Мультитенантность агентов — вне roadmap v1.4.0 | Админка — глобальная для всех пользователей |

---

## § 14. План работ (фазы)

| Фаза | Что | Файлы | Оценка |
|:---:|---|---|:---:|
| **0** | DESIGN.md (этот документ) + согласование | `docs/development/v1.4/DESIGN.md` | ~2 ч ✅ |
| **1** | DTO + `ISubAgentRegistry` + `SubAgentRegistry` + unit-тесты | `Services/DTO/SubAgent/`, `Services/Interfaces/`, `Services/Implementation/Agents/` | ~4 ч |
| **2** | Перегрузка `ILmStudioClient.CompleteAsync(..., string model)` + `SystemPromptOverride`/`ModelOverride` в `SubAgentService` | `ILmStudioClient`, `LmStudioClient`, `SubAgentService` | ~3 ч |
| **3** | `AgentToolBase` + 6 наследников + регистрация в `Startup.cs` | `Tools/SubAgent/` | ~4 ч |
| **4** | `appsettings.json` + `appsettings.Development.json` (секция `SubAgents`) | Конфиги | ~2 ч |
| **5** | Chat: переключение на `SubAgentRegistry.GetEnabled()` | `ChatStreamService` | ~2 ч |
| **6** | Админка: вкладка «Агенты» + API endpoints + локализация | `admin.js`, `Admin.cshtml`, `.resx`, `AdminAgentsController` | ~6 ч |
| **7** | Тесты: 4 unit + 1 integration | `IIChatTools.Tests/` | ~3 ч |
| **8** | Документация (CHANGELOG, README, KNOWN_ISSUES, RULES § 7) | Docs | ~2 ч |
| **9** | Smoke + релиз v1.4.0 | — | ~2 ч |

**Итого:** ~30 ч (≈4 рабочих дня).

---

## § 15. Definition of Done (v1.4.0)

- [ ] `SubAgents:*` в `appsettings.json` (6 агентов + consult).
- [ ] `ISubAgentRegistry` + `SubAgentRegistry` (singleton, читает из конфига + `AppSettings`).
- [ ] `ILmStudioClient.CompleteAsync(..., string model = null)` — перегрузка.
- [ ] `SubAgentTaskRequest.SystemPromptOverride` + `ModelOverride`.
- [ ] `AgentToolBase` + 6 наследников + `consult_secondary_agent` (существует).
- [ ] Chat видит **7 инструментов** вместо 12.
- [ ] `/admin` → вкладка «Агенты»: список, редактирование, сброс, статистика.
- [ ] `AdminAgentsController` (`GET/PUT/POST /api/admin/agents/*`).
- [ ] 15+ ключей `.resx` (RU + EN), `LocalizationSyncTests` проходит.
- [ ] 41/41 тестов (36 + 5 новых).
- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] CHANGELOG `[1.4.0]` + `[Unreleased]` пустая.
- [ ] README обновлён (раздел «Multi-Agent»).
- [ ] Smoke: «Создай файл test.txt» → LLM выбирает `file_system_agent` → approve → файл создан.

---

## § 16. Ссылки

- KI-052 — специализированные суб-агенты (roadmap).
- KI-053 — multi-user approvals (v1.4.0.x).
- KI-049 — tokensIn/tokensOut через tiktoken (v1.4.0.x).
- RULES § 3.1 — маленькие шаги.
- RULES § 4.15 — имена папок не совпадают с типами.
- RULES § 1.15 — новый инструмент = 1 класс + 1 строка регистрации.
- `docs/development/v1.3/DESIGN.md` — эталон формата.
