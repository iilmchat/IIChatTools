# IIChatTools v1.4.1
[![CI](https://github.com/iilmchat/IIChatTools/actions/workflows/ci.yml/badge.svg)](https://github.com/iilmchat/IIChatTools/actions/workflows/ci.yml)
[![Docker Publish](https://github.com/iilmchat/IIChatTools/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/iilmchat/IIChatTools/actions/workflows/docker-publish.yml)

**Платформа инструментального моста между локальной LLM (LM Studio) и средой разработчика.**

© 2026 RuChating (iilmchat) · IIChatTools v1.4.1

---

## Что это

IIChatTools — серверное приложение на **.NET 10 LTS**, предоставляющее LLM
широкий набор безопасных инструментов: работа с файловой системой, выполнение
кода, веб-поиск, браузерная автоматизация, Git/GitHub, делегирование суб-агентам.

Проект вдохновлён [Beledarian's LM Studio Tools](https://lmstudio.ai/beledarian/beledarians-lm-studio-tools)
и реализует аналогичную функциональность в экосистеме .NET.

## Ключевые возможности

- **💬 Chat UI** — полноценный чат с LLM (как ChatGPT): sidebar с историей диалогов, стриминг SSE, переименование/удаление чатов, автоскролл, копирование, approvals прямо из чата.
- **🤖 Multi-Agent (v1.4.0)** — Chat общается с **7 верхнеуровневыми инструментами** (6 специализированных агентов + `consult_secondary_agent`). Каждый агент — со своим system prompt, моделью и белым списком инструментов. Управление через админку `/admin → Агенты`.
- **40 инструментов** для LLM (файловая система, код, веб, Git/GitHub, браузер, суб-агенты, утилиты).
- **Tool calling в чате** — LLM сама вызывает инструменты в multi-turn loop (до 5 итераций).
- **Approvals в чате** — mutating-инструменты требуют подтверждения через модалку (drag-and-drop, countdown, approve/reject).
- **Суб-агенты** — делегирование многошаговых задач с авто-отладкой.
- **Веб-админка** с полным CRUD (пользователи, настройки, белый список, аудит).
- **Мультипользовательность** с ролями Admin/User, изоляцией workspace.
- **Локализация** RU/EN (интерфейс + сообщения).
- **Гибридная БД**: SqlServer / Sqlite / InMemory (выбор через `Database:Provider`).
- **Аудит** всех действий: БД + опциональный JSONL-файл (`logs/audit/`).
- **Безопасность**: `PathHelper`, `ArgumentList`, whitelist команд, лимиты размеров, TTL-сессии.
- **Offline-развёртывание**: сборка без доступа к интернету через `LocalPackages/`.
- **🚧 RAG / Knowledge Base (v1.5.0, in progress)**: семантический поиск по документам проекта, приложенным файлам и истории чатов. Дизайн — [docs/development/v1.5/DESIGN.md](docs/development/v1.5/DESIGN.md).
- **Логотип (KI-081):** фирменный знак IIChatTools (шестиугольник с переплетением) — в navbar, на главной (hero), на страницах входа/регистрации и в empty state чата. Favicon — SVG + PNG (16/32) + apple-touch-icon. Файлы: `wwwroot/images/logo-icon.svg`, `logo-full.svg`, `site.webmanifest`.

---

## Требования

| Компонент | Версия |
| :--- | :--- |
| .NET SDK | **10.0.401+** ([скачать](https://dotnet.microsoft.com/download/dotnet/10.0)) |
| MSSQL Server (опционально) | 2016+ (Express/Developer/Standard) |
| SQLite | встроен (по умолчанию) |
| LM Studio | 0.3.0+ (с OpenAI-совместимым API) |
| Node.js (опционально) | 18+ |
| Python 3 (опционально) | 3.x |
| Git CLI (опционально) | любая актуальная |
| GitHub CLI `gh` (опционально) | любая актуальная |
| Chromium для PuppeteerSharp | скачивается автоматически при первом запуске (~200 МБ) |

> **Примечание**: SQLite используется по умолчанию — это позволяет запустить
> приложение без установки SQL Server. Провайдер выбирается в `appsettings.json`
> через `Database:Provider` (`SqlServer` / `Sqlite` / `InMemory`).

---

## Установка

### Вариант A: стандартная установка (с интернетом)

```bash
git clone https://github.com/iilmchat/IIChatTools.git
cd IIChatTools
dotnet restore
```

### Вариант B: offline-установка (без интернета)

Для машин без доступа в интернет используется локальный источник пакетов
`LocalPackages/`. Структура:

```
IIChatTools/
├── NuGet.Config              # источник: только LocalPackages
├── LocalPackages/            # .nupkg файлы (не коммитится)
└── ...
```

Порядок:
1. Скопируйте папку `LocalPackages/` из архива/шары в корень репозитория.
2. Убедитесь, что `NuGet.Config` в корне содержит:
   ```xml
   <packageSources>
     <clear />
     <add key="LocalPackages" value="LocalPackages" />
   </packageSources>
   ```
3. Выполните:
   ```bash
   dotnet restore
   dotnet build IIChatTools.sln
   ```

**Наполнение `LocalPackages`** (для разработчиков с интернетом):

Локальный источник `LocalPackages/` наполняется из глобального NuGet-кэша
через три шага:

```bash
# 1. Создать NuGet.Config.online (временный, не коммитится)
pwsh -ExecutionPolicy Bypass -File scripts/setup/enable-online-restore.ps1

# 2. Скачать все пакеты в глобальный кэш (~/.nuget/packages)
dotnet restore IIChatTools.sln --configfile NuGet.Config.online --force

# 3. Скопировать все .nupkg из глобального кэша в LocalPackages/
pwsh -ExecutionPolicy Bypass -File scripts/setup/fill-local-packages.ps1
```

Для чистой пересборки `LocalPackages/` — с резервной копией:
```bash
pwsh -ExecutionPolicy Bypass -File scripts/setup/fill-local-packages.ps1 -Clean
```

Предварительный просмотр без копирования:
```bash
pwsh -ExecutionPolicy Bypass -File scripts/setup/fill-local-packages.ps1 -DryRun
```

---

## Настройка

Отредактируйте `IIChatTools.API/appsettings.json`:

```jsonc
{

  // Гибридная БД: выберите провайдер и укажите соответствующую строку
  "Database": {
    "Provider": "Sqlite",                                 // SqlServer | Sqlite | InMemory
    "SqlServerConnectionString": "Server=localhost;Database=IIChatTools;Trusted_Connection=True;MultipleActiveResultSets=true",
    "SqliteConnectionString": "Data Source=Data/iichattools.db",
    "InMemoryDatabaseName": "IIChatTools"
  },

  // Аудит: БД и/или JSONL-файл (ротация по дням)
  "Audit": {
    "ToDatabase": true,
    "ToFile": true,
    "LogDirectory": "logs/audit",
    "FileRetentionDays": 30
  },

  // JWT (для API-клиентов; в UI используется cookie)
  "Jwt": {
    "Issuer": "IIChatTools",
    "Audience": "IIChatToolsClients",
    "Key": "CHANGE_ME_VIA_USER_SECRETS",                  // ← через dotnet user-secrets
    "ExpirationMinutes": 480
  },

  // Workspace (корень файловой песочницы) и лимиты
  "Workspace": {
    "RootPath": "C:\\IIChatToolsWorkspace",
    "MaxFileSizeBytes": 10485760,
    "MaxCommandOutputBytes": 1048576
  },

  // Безопасность: флаги функций, таймауты, браузер
  "Security": {
    "EnableCodeExecution": true,
    "EnableBrowserAutomation": true,
    "EnableShellCommands": true,
    "ApprovalExpirationMinutes": 5,
    "GitTimeoutSeconds": 60,
    "GhTimeoutSeconds": 90,
    "BrowserHeadless": true,
    "MaxBrowserSessionsPerUser": 3,
    "BrowserSessionTtlMinutes": 15,
    "BrowserPageTimeoutSeconds": 30
  },

  // Инструменты: требовать ли подтверждение и белый список
  "Tools": {
    "RequireApprovalByDefault": true,
    "Whitelist": []
  },

  // LM Studio (OpenAI-совместимый API)
  "LmStudio": {
    "BaseUrl": "http://localhost:8034",
    "Model": "local-model",
    "Temperature": 0.3,
    "MaxTokens": 4096,
    "RequestTimeoutSeconds": 300
  },

  // Суб-агенты
  "SubAgent": {
    "DefaultMaxSteps": 10,
    "MaxStepsLimit": 30,
    "SystemPrompt": "Ты — автономный вторичный агент IIChatTools. …",
    "ReviewerSystemPrompt": "Ты — критичный рецензент. …"
  },

  // Браузерная автоматизация (PuppeteerSharp)
  "Browser": {
    "Headless": true,
    "UserAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    "ExecutablePath": "",                                 // пусто → автоопределение Edge/Chrome
    "ProxyServer": "CHANGE_ME_VIA_USER_SECRETS",
    "ProxyUsername": "CHANGE_ME_VIA_USER_SECRETS",
    "ProxyPassword": "CHANGE_ME_VIA_USER_SECRETS",
    "IgnoreCertificateErrors": true                       // для MITM-прокси
  },

  "AllowedHosts": "*"
}
```

> **Секреты** (`Jwt:Key`, `Browser:ProxyServer`, `Browser:ProxyUsername`,
> `Browser:ProxyPassword`, прод-строка подключения к БД) рекомендуется хранить
> в **User Secrets** (`dotnet user-secrets`) или переменных окружения,
> а не в `appsettings.json`.
>
> Инициализация User Secrets:
> ```bash
> cd IIChatTools.API
> dotnet user-secrets set "Jwt:Key" "<ваш-секрет-не-менее-32-символов>"
> dotnet user-secrets set "Browser:ProxyServer" "http://proxy.company.local:8080"
> dotnet user-secrets set "Browser:ProxyUsername" "<login>"
> dotnet user-secrets set "Browser:ProxyPassword" "<password>"
> ```

### Профиль разработки

Файл `appsettings.Development.json` содержит упрощённые значения и
**не должен попадать в прод**. Отличия от базового:
- `Database:SqliteConnectionString` указывает на `Data/iichattools-dev.db`;
- `Workspace:RootPath` — локальный путь разработчика (`G:\AI\IIChatTools\Workspace`);
- `Logging:LogLevel:Default = "Debug"`;
- `Jwt:Key` — тестовый, **не для прода**.

Локальные пути и прокси-credentials **не коммитятся**. Храните их в User Secrets:
```bash
cd IIChatTools.API

# Workspace — локальная песочница для файловых инструментов
dotnet user-secrets set "Workspace:RootPath" "G:\AI\IIChatTools\Workspace"

# Прокси (если используется)
dotnet user-secrets set "Browser:ProxyServer"   "http://proxy.local:8080"
dotnet user-secrets set "Browser:ProxyUsername" "<login>"
dotnet user-secrets set "Browser:ProxyPassword" "<password>"
```

Проверка: `dotnet user-secrets list`.

В `appsettings.Development.json` остаются **нейтральные placeholder**:
- `Workspace:RootPath` — `%USERPROFILE%\IIChatToolsWorkspace` (раскрывается через `Environment.ExpandEnvironmentVariables`)
- `Browser:ProxyServer` / `ProxyUsername` / `ProxyPassword` — `CHANGE_ME_VIA_USER_SECRETS`

---

## Миграции и запуск

**SqlServer** (требует применения миграций):
```bash
dotnet ef database update --project IIChatTools.Data --startup-project IIChatTools.API
```

**Заменить на:**

```markdown
**Sqlite / InMemory** (схема создаётся автоматически):
```bash
dotnet run --project IIChatTools.API
```
Важно для Sqlite: EnsureCreatedAsync не мигрирует существующую схему. Если БД создана на старой версии (без новых таблиц/колонок) — приложение упадёт с SQLite Error 1: no such table: <Table>. Решение: удалить Data/iichattools-dev.db (и bin/Debug/net10.0/Data/*.db, если есть) → перезапустить. Схема пересоздастся с текущей моделью. Данные будут потеряны. Для прод-Sqlite — миграции в Migrations/Sqlite/ (план на v1.3.x, KI-070).

При первом запуске автоматически:
- применяются миграции (SqlServer) или `EnsureCreated` (Sqlite/InMemory);
- создаются роли `Admin` и `User`;
- синхронизируются настройки из `appsettings.json` в БД.

**Первый зарегистрированный пользователь** получает роль **Admin**. Все последующие — **User**.

Приложение будет доступно на:
- `https://localhost:5001` (Razor UI)
- `http://localhost:5000`

---

## API (основные endpoints)

| Метод | URL | Назначение |
| :--- | :--- | :--- |
| `POST` | `/auth/register` | Регистрация |
| `POST` | `/auth/login` | Вход (cookie) |
| `POST` | `/auth/token` | Получение JWT |
| `GET` | `/api/tools` | Список инструментов |
| `POST` | `/api/tools/execute` | Вызов инструмента |
| `GET` | `/api/tools/execution/{id}` | Статус вызова (polling) |
| `GET` | `/api/approvals/pending` | Ожидающие подтверждения |
| `POST` | `/api/approvals/{id}/approve` | Подтвердить |
| `POST` | `/api/approvals/{id}/reject` | Отклонить |
| `GET` | `/api/status/snapshot` | Снимок состояния |
| `*` | `/api/admin/*` | CRUD админки (только Admin) |
| `GET` | `/api/chats` | Список чатов пользователя (с MessageCount) |
| `GET` | `/api/chats/{id}?limit=50` | Чат с историей сообщений |
| `POST` | `/api/chats` | Создать чат |
| `PATCH` | `/api/chats/{id}` | Обновить title / model / systemPrompt |
| `DELETE` | `/api/chats/{id}` | Удалить чат |
| `POST` | `/api/chat/stream` | SSE-стриминг ответа LLM (tool calling) |
| `POST` | `/api/chat/approvals/{callId}/approve` | Подтвердить вызов инструмента в чате |
| `POST` | `/api/chat/approvals/{callId}/reject` | Отклонить вызов инструмента в чате |
| `GET` | `/api/models` | Список моделей LM Studio (без embedding) |
| `GET` | `/api/admin/agents` | Список специализированных суб-агентов (v1.4.0) |
| `PUT` | `/api/admin/agents/{name}` | Обновить дескриптор агента (v1.4.0) |
| `POST` | `/api/admin/agents/{name}/reset` | Сбросить агента к appsettings.json (v1.4.0) |
| `POST` | `/api/chat/regenerate` | Перегенерировать последний ответ ассистента |
| `POST` | `/api/chat/messages/{id}/edit` | Редактировать user-сообщение (удаляет всё после) |
| `POST` | `/api/chats/{id}/generate-title` | AI-генерация названия из первого сообщения |

---

## Инструменты (40)

| Группа | Кол-во | Требуют подтверждения |
| :--- | :---: | :---: |
| Файловая система | 13 | 7 |
| Выполнение кода | 3 | 3 |
| Веб | 3 | 0 |
| Git | 7 | 4 |
| GitHub | 7 | 2 |
| Браузер (PuppeteerSharp) | 4 | 1 |
| Суб-агенты | 1 | 1 |
| Утилиты | 2 | 0 |
| **Итого (в реестре)** | **40** | **18** |
| **+ Агенты (v1.4.0)** | **+6** | **(по агенту)** |
| **Итого (ToolRegistry)** | **46** | — |

> **Примечание:** Chat теперь видит **7 инструментов** (6 агентов + `consult_secondary_agent`),
> а не 12 из `SubAgent:DefaultAllowedTools`. Все 40 «сырых» инструментов доступны
> **внутри** агентов.

---

## Chat UI

Полноценный чат-интерфейс (`/chat`) по аналогии с ChatGPT/DeepSeek:

**Возможности:**
- 📋 **Sidebar** — список чатов с относительными датами (`только что`, `5 мин назад`, `вчера`, `22.09`) и счётчиком сообщений.
- 🔍 **Поиск по чатам** — server-side фильтр (KI-068) + ⌘K-модалка (`Ctrl+K`, KI-078B) с превью совпадений.
- 🔎 **Внутричатовый поиск** (KI-078A) — `Ctrl+F` в ленте активного чата: подсветка `<mark>`, навигация ↑/↓, авто-скролл.
- ➕ **Создание / переименование (✏️) / удаление (🗑)** чатов прямо в sidebar.
- ↔️ **Свернуть/развернуть sidebar** (KI-079) — кнопка внутри панели + хоткей `Ctrl+B`, состояние в `localStorage`.
- 🔢 **Авто-нумерация** новых чатов: «Новый чат», «Новый чат 2», «Новый чат 3», …
- 🤖 **AI-генерация названия** из первого сообщения (ChatGPT-style, после первого ответа).
- 🎛 **Селектор модели** в header — переключение модели чата (`PATCH /api/chats/{id}`).
- 🌊 **SSE-стриминг** ответа LLM — потоковая отрисовка с мигающим курсором.
- 📝 **Markdown-рендеринг** ответов (`marked` + `DOMPurify`) + **подсветка синтаксиса** (`highlight.js`).
- 💻 **Code blocks** — шапка с языком + кнопки Copy / Download.
- 🛠 **Tool calling** — LLM автоматически вызывает инструменты (до 5 итераций, SSE `tool_call` / `tool_result`).
- ✅ **Approvals** — mutating-инструменты требуют подтверждения:
  - Модалка с именем инструмента, JSON-параметрами, countdown (5 минут).
  - Drag-and-drop за заголовок.
  - Approve/Reject; закрытие крестиком = Reject.
- ✏️ **Edit user-сообщения** → автоматическая регенерация ответа.
- 🔄 **Regenerate / Retry** — перегенерировать ответ ассистента или повторить после Stop.
- ⏹ **Stop** — прерывание стрима (`AbortController`), частичный ответ отбрасывается.
- 📋 **Copy** на каждом сообщении.
- 🔢 **Статистика в meta-строке** (KI-049a/b, KI-084a/b) — `123 / 45 токенов · 7.4 tok/s · 9.1 с` под ответом ассистента. Токены — tiktoken (±5–10% для Qwen); tok/s и длительность — Stopwatch.
- 📜 **Persistентная история** — все диалоги в БД + **retention** (авто-удаление старых).
- ⬇️ **ChatGPT-style скроллинг** — кнопка «↓ Вниз», автоскролл отключается при ручной прокрутке вверх.
- 💾 **Enter** — отправка, **Shift+Enter** — новая строка, автоувеличение textarea.
- 🎨 **DeepSeek-style поле ввода** (KI-080) — закруглённое поле на всю ширину, круглые SVG-кнопки Send/Stop внутри.
- 🌐 **Локализация RU/EN**.

**Точки входа:**
- UI: `/chat`
- API: `/api/chats`, `/api/chat/stream`, `/api/chat/regenerate`, `/api/chat/messages/{id}/edit`, `/api/chat/approvals/*`, `/api/chats/{id}/generate-title`, `/api/models`

**Скриншоты** — в [docs/development/v1.3/DESIGN.md](docs/development/v1.3/DESIGN.md).

---

## Multi-Agent (v1.4.0)

Chat работает через **7 верхнеуровневых инструментов** — 6 специализированных
суб-агентов + универсальный fallback:

| Агент | Инструментов | Модель (по умолчанию) | Approval |
|:---|:---:|:---:|:---:|
| `file_system_agent` | 13 | `qwen/qwen3-4b-2507` | ✅ |
| `code_agent` | 3 | `gemma-4-12b-coder...` | ✅ |
| `web_agent` | 3 | `qwen/qwen3-4b-2507` | ❌ |
| `git_agent` | 7 | `qwen/qwen3-4b-2507` | ✅ |
| `github_agent` | 7 | `qwen/qwen3-4b-2507` | ✅ |
| `planner_agent` | 2 | `gemma-4-12b-coder...` | ❌ |
| `consult_secondary_agent` | *(browser + fallback)* | `LmStudio:Model` | ✅ |

**Зачем:** одна модель (особенно 4B) плохо выбирает инструмент из 40.
Внутри агента — узкий набор (2-13 инструментов) + свой system prompt →
точнее выбор, меньше токенов.

**Как работает:**
1. Пользователь пишет задачу.
2. Chat (LLM) выбирает агента → SSE `tool_call`.
3. Модалка approval: «Агент X хочет выполнить задачу: ...» → approve.
4. Внутри агента — multi-turn loop (до `MaxSteps`, обычно 10) с **белым списком** инструментов.
5. Финальный ответ агента → Chat формулирует ответ пользователю.

**Retention чатов (KI-067):** per-user override глобального срока хранения.
- `/profile` — своя страница: срок хранения (дней) + флаг «Не удалять».
- `/admin` → вкладка «Пользователи» → ⚙ в строке → модалка retention.
- Ключи: `Chat.RetentionDays` (int), `Chat.DoNotDelete` (bool) в таблице `UserSettings`.
- Приоритет: `DoNotDelete = true` перебивает `RetentionDays`.

**Управление:** `/admin` → вкладка **«Агенты»** — список, редактирование (DisplayName,
Model, MaxSteps, SystemPrompt, AllowedTools, RequiresApproval, Disabled), сброс к defaults.
**Статистика (KI-076):** карточки над списком — TotalRuns, AvgTime, SuccessRate, LastRun
по каждому агенту (источник — `AuditLogs`, `GET /api/admin/agents/stats`).
Изменения сохраняются в БД (`AppSettings`, ключ `SubAgents.{name}`) и восстанавливаются
при старте приложения.

**API:**
- `GET  /api/admin/agents` — список агентов.
- `PUT  /api/admin/agents/{name}` — обновить дескриптор.
- `POST /api/admin/agents/{name}/reset` — сбросить к appsettings.json.

**Конфигурация:** секция `SubAgents` в `appsettings.json` (6 агентов × настройки).
Описания и промпты — в [docs/development/v1.4/DESIGN.md](docs/development/v1.4/DESIGN.md).

---

## Rate Limiting

Per-user и per-IP лимиты запросов. Настраивается в `appsettings.json` (секция `RateLimiting`).

| Политика | Endpoint | По умолчанию |
|----------|----------|--------------|
| `tools-execute` | `POST /api/tools/execute` | 30 req/min |
| `auth` | `/auth/login`, `/auth/register` | 5 req/min |
| `per-user` | Остальные API | 100 req/min |
| — | `/health/*` | без лимита |

При превышении — `429 Too Many Requests` с JSON-ответом `{ success: false, message, retryAfterSeconds }`.

Отключить: `RateLimiting:Enabled = false` в конфигурации.

> **Реализация**: собственный `RateLimitingMiddleware` на базе `System.Threading.RateLimiting` (KI-042 — `Microsoft.AspNetCore.RateLimiting` недоступен в SDK 10.0.401).

---

## Metrics

Prometheus-метрики доступны по `/metrics` (публичный, без авторизации).

Запрос:

    curl https://localhost:5001/metrics

**Стандартные** (prometheus-net):

| Метрика | Тип | Labels |
|---------|-----|--------|
| http_requests_received_total | counter | method, code, controller, action, endpoint |
| http_request_duration_seconds | histogram | method, code, controller, action, endpoint |
| http_requests_in_progress | gauge | method |
| dotnet_collection_count_total | counter | generation |
| process_* | — | CPU, memory, virtual memory, working set |

**Кастомные IIChatTools**:

| Метрика | Тип | Labels | Описание |
|---------|-----|--------|----------|
| iichattools_tool_executions_total | counter | tool_name, status | Success / Error / Rejected / Expired |
| iichattools_tool_execution_duration_seconds | histogram | tool_name | Длительность вызова инструмента |
| iichattools_pending_approvals | gauge | — | Ожидающие подтверждения (обновляется раз в 30 сек) |
| iichattools_active_users | gauge | — | Активные пользователи (обновляется раз в 30 сек) |
| iichattools_audit_entries_total | counter | status | Записи аудита |
| iichattools_lmstudio_requests_total | counter | status | Запросы к LM Studio |
| iichattools_audit_cleanup_total | counter | target | Удалённые записи/файлы аудита (retention) |
| iichattools_chat_cleanup_total | counter | reason | Удалённые чаты по retention (`reason="retention"`) |

**Prometheus scrape config**:

    scrape_configs:
      - job_name: 'iichattools'
        metrics_path: '/metrics'
        static_configs:
          - targets: ['iichattools:8080']

---

## Безопасность

- **Пути** всегда проверяются через `PathHelper.TryGetSafeFullPath` (запрет выхода из workspace).
- **Команды CLI** — через `ProcessStartInfo.ArgumentList` (без конкатенации строк) и белый список.
- **Размеры данных** — конфигурируемые лимиты (файл, вывод команды, тело запроса).
- **Таймауты** — для всех внешних операций.
- **Сессии браузера** — TTL, изоляция по пользователю, лимит одновременных сессий.
- **Суб-агенты** — запрет рекурсии, лимит шагов, персистентность.
- **Подтверждения** — все критичные действия требуют явного согласия пользователя.
- **Прокси** — поддержка корпоративных HTTP-прокси с Basic Auth (через User Secrets / env).

---

## Архитектура

```
LM Studio (LLM) :8034
      ↓ HTTP (OpenAI-совместимый API)
IIChatTools.API  (net10.0)
  ├── Controllers: Home, Auth, Tools, Approvals, Admin, AdminAgents, Status, Chat, ChatStream, ChatView, Models
  ├── Razor Views (RU/EN через IStringLocalizer<SharedResources>)
  ├── ES-модули: api, ui, status, approvals, admin, admin-agents, test, chat
  ├── Program.cs: ConfigureDefaultProxy + миграции по провайдеру + LoadSubAgentOverrides
  └── Startup.cs: DI + 46 инструментов (40 raw + 6 агентов) + Chat services
      ↓ DI
IIChatTools.Services  (net10.0)
  ├── ToolRegistry (46 инструментов: 40 raw + 6 агентов)
  ├── Agents: SubAgentRegistry (Singleton, v1.4.0)
  ├── Chat: ChatService, ChatStreamService, ChatApprovalCoordinator (Singleton)
  ├── LmStudioClient (SSE-стриминг + tool calling + ModelOverride)
  ├── Cross-cutting services (аудит, подтверждения, workspace, браузер)
  ├── Tools/ (8 групп)
  └── Tools/SubAgent/ (AgentToolBase + 6 наследников: FileSystem, Code, Web, Git, GitHub, Planner)
      ↓ EF Core 10
IIChatTools.Data  (net10.0)
  ├── ApplicationUser + Identity (Admin, User)
  ├── Chat + ChatMessage (v1.3)
  └── AuditLog, AppSetting, PendingAction, AgentState, MemoryEntry
      ↓ (опционально)
logs/audit/*.jsonl (JSONL, ротация)
```

**Решение о слоях**: `AppVersionHolder` разрывает зависимость `Services` → `API`;
`Func<ISubAgentService>` разрывает DI-цикл `SubAgent` ↔ `ToolRegistry`.

---

## CI/CD

- **CI** (`ci.yml`) — build + test на каждый push в `main` / `feature/**` и на PR. Загружает coverage и test-results как артефакты.
- **Docker Publish** (`docker-publish.yml`) — сборка образа при push в `main` и при создании тега `v*`. Образ публикуется в **GitHub Container Registry** (`ghcr.io/iilmchat/iichattools`).

### Образы в ghcr.io

```bash
# Последняя версия из main
docker pull ghcr.io/iilmchat/iichattools:latest

# Конкретный релиз
docker pull ghcr.io/iilmchat/iichattools:v1.4.1
docker pull ghcr.io/iilmchat/iichattools:1.4.1
docker pull ghcr.io/iilmchat/iichattools:1.4
docker pull ghcr.io/iilmchat/iichattools:1

Развёртывание на любом Linux-сервере с Docker:
docker run -d \
  --name iichattools \
  -p 8080:8080 \
  -e Jwt__Key="<ваш-секрет-≥32-символа>" \
  -v iichattools-data:/app/Data \
  -v iichattools-logs:/app/logs \
  -v iichattools-workspace:/app/Workspace \
  ghcr.io/iilmchat/iichattools:latest

---

## Сборка и тесты

```bash
dotnet build IIChatTools.sln -c Release
dotnet test IIChatTools.sln -c Release
```

**Статус**: 81/81 тестов проходят (unit + integration).

---

## Разработка

### Полезные сниппеты

**Проверить токены сообщений через DevTools Console** (для активного чата):

```javascript
const chatId = new URL(window.location.href).searchParams.get('chatId');
const d = await fetch(`/api/chats/${chatId}`).then(x => x.json());
console.table(d.data.messages.map(m => ({
    id: m.id,
    role: m.role,
    tokensIn: m.tokensIn,
    tokensOut: m.tokensOut,
    preview: (m.content || '').substring(0, 40)
})));
```

**Проверить токены через sqlite3 CLI** (dev-БД):

```powershell
sqlite3 IIChatTools.API\Data\iichattools-dev.db "SELECT Id, Role, TokensIn, TokensOut, SUBSTR(Content, 1, 40) FROM ChatMessages ORDER BY Id DESC LIMIT 5;"
```

**Открыть БД в DB Browser** — только в режиме **Read Only**
(иначе KI-085: `database is locked`, запись блокируется на 30 с).

### Проверка retention

`Chat:Retention:Enabled = false` в `appsettings.Development.json` — намеренно (RULES § 4.31).
Для проверки:

1. Установить `Chat:Retention:Enabled = true`, `CleanupIntervalHours = 1`.
2. Перезапустить приложение.
3. Подождать **2 минуты** (первый прогон через `Task.Delay`) + интервал.
4. В логах: `ChatRetentionService запущен: интервал=1ч, срок=Nд`.
5. Проверить, что старые чаты удалены (в БД или через `/api/chats`).

### CI/CD

- **CI** — `dotnet build` + `dotnet test` на каждый push в `main`.
- **Docker Publish** — образ в `ghcr.io` на `main` и на теги `v*`.
- **Локально** перед push:
  ```powershell
  Get-Process IIChatTools.API -ErrorAction SilentlyContinue | Stop-Process -Force
  dotnet build IIChatTools.sln
  dotnet test IIChatTools.sln --no-build
  ```
  (RULES § 3.14 — остановить приложение, иначе MSB3027/MSB3021.)

---

## Развёртывание

1. Установите **.NET 10 Runtime** на целевой сервер:
   - Windows: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
   - Linux: `apt install dotnet-runtime-10.0` (или соответствующий пакет)
2. Создайте БД (SqlServer) или используйте Sqlite (по умолчанию).
3. Настройте `appsettings.json` / User Secrets (строка подключения, JWT-ключ, workspace).
4. Опубликуйте:
   ```bash
   dotnet publish IIChatTools.API -c Release -o ./publish
   ```
5. Запустите `dotnet IIChatTools.API.dll` (Kestrel) или настройте IIS.

Для **offline-развёртывания** — скопируйте папку `publish/` вместе с `LocalPackages/`
и `NuGet.Config` (если потребуется пересборка на целевой машине).

---

## Известные проблемы

Актуальный реестр — [`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md).
Все найденные проблемы фиксируются с номером `KI-XXX`, приоритетом и статусом.

---

## Лицензия

© 2026 RuChating (iilmchat). Все права защищены.