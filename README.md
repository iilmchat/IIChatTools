# IIChatTools v1.8.1
[![CI](https://github.com/iilmchat/IIChatTools/actions/workflows/ci.yml/badge.svg)](https://github.com/iilmchat/IIChatTools/actions/workflows/ci.yml)
[![Docker Publish](https://github.com/iilmchat/IIChatTools/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/iilmchat/IIChatTools/actions/workflows/docker-publish.yml)

**Платформа инструментального моста между локальной LLM (LM Studio) и средой разработчика.**

© 2026 RuChating (iilmchat) · IIChatTools v1.8.1

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
- **✉️ Mail Agent (v1.8.0)**: почтовый агент (IMAP/SMTP через MailKit 4.8.0). 7 инструментов: `send_email` (approval), `list_emails`, `read_email`, `search_emails`, `delete_email` (approval), `move_email` (approval), `mark_as_read`. Rate limiting 20 писем/час, 30 чтений/мин. Privacy-first (без PII в логах). Вложения в `mail-attachments/{uid}/`, ≤ 10 MB. Дизайн — [docs/development/v1.8/DESIGN_MAIL_AGENT.md](docs/development/v1.8/DESIGN_MAIL_AGENT.md).
- **✅ RAG / Knowledge Base (v1.5.0)**: семантический поиск по документам проекта, приложенным файлам и истории чатов. 4 индекса (project_docs, my_rag_docs, chat_history, workspace), 3 tool для LLM (search_knowledge_base, search_chat_history, search_workspace), auto-inject top-K из attached-чанков в system prompt. UI: 📎-вложения в чате, админка /admin → База знаний, opt-in в /profile → Workspace index. Дизайн — [docs/development/v1.5/DESIGN.md](docs/development/v1.5/DESIGN.md).
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

### Требования к LM Studio

**Context Length ≥ 16384** для моделей, используемых агентами (`qwen3-4b`,
`gemma-4-12b`, ...).

**Почему:** агенты (`mail_agent`, `code_agent`, `file_system_agent`, ...)
получают большой prompt: `SystemPrompt` + few-shot + tool schemas (7+) +
история чата. При дефолтном `Context Length = 8192` LM Studio обрезает ответ
(`finish_reason: "length"`), и агент возвращает частичный ответ.

**Как установить:**

1. В LM Studio открой загруженную модель.
2. Справа — раздел **Context and Offload**.
3. **Context Length:** `8192` → **`16384`** (или больше).
4. Reload модели (`Eject` + `Load Model`).

**Рекомендуется также:**

- **GPU Offload:** выкрутить в максимум (все слои на GPU). Даст 5-10× ускорение
  обработки prompt.
- **Evaluation Batch Size:** `2048` → `4096` (если хватает VRAM).

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
>
> **Строка подключения Database Agent (v1.7.0, KI-097).**
> SQL-агент работает через **read-only** подключение к БД приложения
> (в Фазе 1 — только `internal`). Строка подключения **не хранится
> в `appsettings.json`** — только в User Secrets / env.
>
> Dev (Sqlite):
> ```bash
> dotnet user-secrets set "SqlAgent:Internal:ConnectionString" "Data Source=Data/iichattools-dev.db;Mode=ReadOnly"
> ```
> Важно: `Mode=ReadOnly` — встроенная защита Sqlite. Даже если валидатор
> пропустит `DELETE`, БД вернёт ошибку.
>
> Prod (SqlServer, планируется v1.7.x):
> ```bash
> dotnet user-secrets set "SqlAgent:Internal:ConnectionString" "Server=localhost;Database=IIChatTools;User Id=iichattools_reader;Password=<...>;ApplicationIntent=ReadOnly;TrustServerCertificate=True"
> ```
> `ApplicationIntent=ReadOnly` + отдельная роль `db_datareader` — best practice.
> Пока работает только Sqlite-путь (SqlServer-миграции — KI-091).

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

**Sqlite / InMemory** (схема создаётся автоматически):
```bash
dotnet run --project IIChatTools.API
```

> **Важно для Sqlite:** `EnsureCreatedAsync` не мигрирует существующую схему.
> Если БД создана на старой версии (без новых таблиц/колонок) — приложение упадёт
> с `SQLite Error 1: no such table: <Table>`. Решение: удалить
> `Data/iichattools-dev.db` (и `bin/Debug/net10.0/Data/*.db`, если есть) →
> перезапустить. Схема пересоздастся с текущей моделью, данные будут потеряны.
> Для прод-Sqlite — миграции в `Migrations/Sqlite/` (план на v1.7.x, KI-070).

**При первом запуске автоматически:**
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
| `POST` | `/api/chat/{chatId}/attachments` | Загрузить файл к чату (multipart, RAG v1.5.0) |
| `GET` | `/api/chat/{chatId}/attachments` | Список вложений чата |
| `DELETE` | `/api/chat/{chatId}/attachments/{id}` | Удалить вложение |
| `POST` | `/api/chat/{chatId}/attachments/clear` | Очистить все вложения чата |
| `GET` | `/api/admin/knowledge/indexes` | Список 4 RAG-индексов (Admin) |
| `POST` | `/api/admin/knowledge/indexes/project-docs/reindex` | Переиндексировать project_docs (Admin) |
| `GET` | `/api/admin/knowledge/chunks?index=&page=&pageSize=` | Просмотр чанков (Admin) |
| `DELETE` | `/api/admin/knowledge/chunks/{id}` | Удалить чанк (Admin) |
| `GET` | `/api/admin/knowledge/settings` | Настройки RAG (Admin) |
| `PUT` | `/api/admin/knowledge/settings` | Сохранить настройки RAG (Admin) |
| `GET` | `/api/profile/workspace-index` | Статус Workspace-индекса (per-user) |
| `POST` | `/api/profile/workspace-index/enable` | Включить Workspace-индекс (opt-in) |
| `POST` | `/api/profile/workspace-index/disable` | Отключить + очистить |
| `POST` | `/api/profile/workspace-index/reindex` | Переиндексировать Workspace |
| `GET` | `/api/admin/sql-agent/connections` | Список подключений Database Agent (Admin) |
| `PUT` | `/api/admin/sql-agent/connections/{name}` | Обновить настройки подключения (Admin) |
| `POST` | `/api/admin/sql-agent/connections/{name}/test` | Проверить подключение (SELECT 1, Admin) |
| `POST` | `/api/admin/sql-agent/connections/{name}/reset` | Сбросить к baseline (Admin) |
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
| **+ RAG (v1.5.0)** | **+3** | — |
| **+ Database Agent (v1.7.0)** | **+1** | **✅** |
| **+ Mail Agent (v1.8.0)** | **+7** | **3 (send/delete/move)** |
| **+ External-LLM Agent (v1.8.1)** | **+3** | — |
| **Итого (ToolRegistry)** | **60** | — |

> **Примечание:** Chat видит **13 инструментов**:
> **8 специализированных агентов** из `SubAgentRegistry` — `file_system_agent`,
> `code_agent`, `web_agent`, `git_agent`, `github_agent`, `planner_agent`,
> `mail_agent` (v1.8.0, KI-107), `external_llm_agent` (v1.8.1, KI-109);
> **+ `consult_secondary_agent`** (универсальный fallback);
> **+ 3 RAG-tool**: `search_knowledge_base`, `search_chat_history`, `search_workspace`
> (v1.5.0, KI-083);
> **+ `database_agent`** (v1.7.0, KI-097).
> Все «сырые» инструменты доступны **внутри** агентов.

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
- 📎 **RAG-вложения** — прикрепить файлы к чату (📎): PlainText (28 расширений, ≤32 MB, ≤5 файлов). Чипы с метриками под полем ввода, кнопка «Очистить RAG». Auto-inject top-K из attached-чанков в system prompt (Шаг 6C).
- 📚 **Источники (Sources)** — под ответом ассистента: список использованных LLM источников с указанием имени/URL. Для RAG — `RULES.md · chunk 15 · score 0.71`; для web/wiki — кликабельная ссылка (`Москва — Википедия`, `example.com`). Snippet — в tooltip при hover. **Live-режим** (через SSE `done`) и **F5-режим** (из `ChatMessageDto.Sources`). Покрыто: RAG-чанки, `wikipedia_search`, `web_search`, `fetch_web_content` (включая проброс через агентов).
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

## RAG / Knowledge Base (v1.5.0)

**✅ Реализовано (все фазы 0-8 закрыты, релиз v1.5.0).** Семантический поиск
по документам проекта, приложенным к чату файлам и истории чатов.
Embeddings — LM Studio (`text-embedding-nomic-embed-text-v1.5`, 768 dim), векторное
хранилище — `InMemoryVectorStore` (Singleton, теряет данные при рестарте; для
`project_docs` возможна авто-переиндексация).

### 4 индекса

| Индекс | Область | Источник | Управление |
|:---|:---|:---|:---|
| `project_docs` | Global | README, RULES, KNOWN_ISSUES, CHANGELOG, RELEASES | `/admin → База знаний` |
| `my_rag_docs` | Per-chat | Файлы, приложенные к чату (📎) | `/chat` — «Очистить RAG» |
| `chat_history` | Per-user | История сообщений пользователя | (LLM — через `search_chat_history`) |
| `workspace` | Per-user (opt-in) | Файлы workspace пользователя | `/profile → Индексация workspace` |

### Как работает

1. **Ingestion:** файл → парсинг → чанкинг (recursive, 500 токенов, overlap 64) →
   embeddings (batch 64) → запись в `DocumentChunks` (БД) + `InMemoryVectorStore`.
2. **Retrieval:** запрос → embedding → cosine top-K → фильтрация по метаданным
   (chatId / userId) и по `MinScore` → enrichment из БД.
3. **Auto-inject (Шаг 6C):** при отправке сообщения, если у чата есть attached-чанки
   в `my_rag_docs`, top-K из них вставляется в system prompt (порог — `AutoInjectMinScore`).
4. **3 tool для LLM:** `search_knowledge_base`, `search_chat_history`, `search_workspace`.
5. **Sources / citations (v1.6.0):** под ответом ассистента — блок «📚 Источники»
   со списком RAG-чанков, реально использованных LLM. Собираются из двух
   источников: (а) auto-inject (system prompt); (б) `tool_result` от `search_*`.
   Дедупликация по `(type, documentPath, chunkIndex)`. Сохраняются в
   `ChatMessage.MetadataJson` (camelCase), отдаются в `ChatMessageDto.Sources`.

Chat видит **11 инструментов** (6 агентов + `consult_secondary_agent` + 3 RAG-tool
+ `database_agent` — v1.7.0, KI-097).

### Форматы и лимиты

- **Поддерживаемые форматы (PlainTextParser 28 + PdfParser + DocxParser = 30 расширений):** `.txt`, `.md`, `.csv`,
  `.tsv`, `.log`, `.json`, `.xml`, `.yaml`, `.yml`, `.html`, `.htm`, `.cs`, `.py`,
  `.js`, `.ts`, `.java`, `.go`, `.rs`, `.sql`, `.sh`, `.ps1`, `.razor`, `.cshtml`,
  `.css`, `.scss`, `.dockerfile`, `.gitignore`, `.editorconfig`.
- **Лимиты (дефолт):** 32 MB на файл, 30 MB суммарно на чат, 5 файлов на чат.
- **Кодировки:** BOM-детект (UTF-8 / UTF-16 LE/BE), fallback Windows-1251.
- **PDF / DOCX:** не поддерживаются в MVP (план — v1.5.x).

### UI

- **`/chat`** — 📎-кнопка рядом с полем ввода; чипы с метриками (`📄 file.pdf · 3 чанка · [×]`),
  строка `RAG: N чанков [Очистить RAG]`.
- **`/admin → База знаний`** — таблица 4 индексов, переиндексация `project_docs`,
  просмотр / удаление чанков (пагинация 20/стр.), модалка настроек RAG (8 полей).
- **`/profile → Индексация workspace`** — opt-in checkbox, статус, progress-bar,
  кнопка «Переиндексировать». Фоновая индексация через `Task.Run` + `IServiceScopeFactory`.

### Конфигурация (секция `Rag` в `appsettings.json`)

| Ключ | Дефолт | Описание |
|:---|:---|:---|
| `Rag:AutoIndexProjectDocs` | `false` | Индексировать `project_docs` при старте приложения |
| `Rag:Embedding:Model` | `text-embedding-nomic-embed-text-v1.5` | Модель эмбеддингов LM Studio |
| `Rag:Embedding:Dimensions` | `768` | Размерность вектора |
| `Rag:Embedding:BatchSize` | `64` | Максимум текстов за один вызов `/v1/embeddings` |
| `Rag:Embedding:TimeoutSeconds` | `30` | Таймаут эмбеддинг-запроса |
| `Rag:Chunking:Strategy` | `recursive` | `recursive` / `sentence` / `fixed` |
| `Rag:Chunking:ChunkSize` | `500` | Целевой размер чанка (токенов) |
| `Rag:Chunking:ChunkOverlap` | `64` | Перекрытие между чанками |
| `Rag:Chunking:MinChunkSize` | `100` | Минимальный размер чанка |
| `Rag:Ingestion:MaxFileSizeBytes` | `33554432` (32 MB) | Лимит файла |
| `Rag:Ingestion:MaxFilesPerChat` | `5` | Лимит файлов на чат |
| `Rag:Ingestion:MaxTotalSizePerChat` | `31457280` (30 MB) | Суммарный лимит на чат |
| `Rag:Ingestion:ProjectDocsPaths` | см. `appsettings.Development.json` | Пути для `project_docs` |
| `Rag:Retrieval:DefaultTopK` | `5` | Top-K по умолчанию |
| `Rag:Retrieval:OverFetchMultiplier` | `2` | Over-fetch для фильтрации |
| `Rag:Retrieval:MinScore` | `0.25` (dev) / `0.3` (prod) | Минимальный cosine score |
| `Rag:Attachments:AutoInjectTopK` | `5` | Сколько чанков из `my_rag_docs` вставлять в system prompt |
| `Rag:Attachments:AutoInjectMinScore` | `0.35` | Порог для auto-inject |
| `Rag:Attachments:StorageSubfolder` | `chat-attachments` | Подпапка в workspace для файлов |

### Ограничения (осознанные, MVP)

- **InMemoryVectorStore** теряет embeddings при рестарте → `project_docs` может быть
  переиндексирован автоматически (`AutoIndexProjectDocs=true`) или вручную через админку.
- **PDF / DOCX / OCR** — не в MVP (запланированы на v1.5.x).
- **Re-ranking (cross-encoder)** — не в MVP.
_(Sources / citations реализованы в v1.6.0 — см. раздел «Chat UI» и «RAG / Knowledge Base».)_

### API

См. «API (основные endpoints)» выше: 4 endpoint'а для вложений чата,
6 для админки Knowledge Base, 5 для Workspace-индекса в профиле.

---

## Database Agent (v1.7.0)

Read-only SQL-доступ LLM к БД приложения (чаты, сообщения, аудит, RAG-чанки,
вложения). **Никаких write-операций — принципиально.**

### 4 действия инструмента `database_agent`

| Action | Назначение | Approval |
|:---|:---|:---:|
| `list_databases` | Список подключений (метаданные, без connection string) | — |
| `list_tables` | Whitelist-таблицы подключения + row count | — |
| `describe_table` | Колонки таблицы + типы + пример значения | — |
| `execute_query` | Read-only SQL (`SELECT` / `WITH`), auto-LIMIT | ✅ |

### 5 уровней безопасности

1. **Read-only роль в БД** (`Mode=ReadOnly` для Sqlite, `db_datareader` + `DENY INSERT/UPDATE/DELETE` для SqlServer).
2. **Валидатор SQL** — `ISqlQueryValidator`: `SELECT`/`WITH` only, запрет keywords (`INSERT`, `DELETE`, `DROP`, ...), запрет функций (`load_extension`, `readfile`), single-statement.
3. **Whitelist таблиц** — только разрешённые админом.
4. **Timeout + Auto-LIMIT** — 15 сек на запрос, ≤ 100 строк по умолчанию.
5. **Approval + Audit** — пользователь видит SQL перед выполнением; все вызовы — в `AuditLogs`.

### Примеры вопросов для LLM

- «Сколько чатов у меня в базе?» → `execute_query(sql='SELECT COUNT(*) FROM Chats')`
- «Какие таблицы доступны?» → `list_tables(connection='internal')`
- «Покажи последние 5 сообщений из чата N» → `execute_query(sql='SELECT * FROM ChatMessages WHERE ChatId = N ORDER BY CreatedAt DESC LIMIT 5')`

### Admin UI

`/admin → SQL Agent` (9-я вкладка): редактирование whitelist / MaxRows / Timeout / Enabled
в runtime, кнопка «Проверить подключение» (`SELECT 1`), «Сбросить» к значениям из `appsettings.json`.

### Конфигурация

Секция `SqlAgent` в `appsettings.json`:

```jsonc
"SqlAgent": {
  "Enabled": true,
  "DefaultConnection": "internal",
  "AdminUiEnabled": true,
  "Connections": {
    "internal": {
      "DisplayName": "IIChatTools DB",
      "Provider": "Sqlite",                       // Sqlite | SqlServer
      "ConnectionStringKey": "SqlAgent:Internal:ConnectionString",
      "AllowedTables": [ "Chats", "ChatMessages", "DocumentChunks", "AuditLogs" ],
      "DeniedTables":  [ "AspNetUsers", "AspNetRoles", "UserSettings" ],
      "MaxRows": 100,
      "StatementTimeoutSeconds": 15,
      "RequiresApproval": true
    }
  },
  "QueryValidation": {
    "DeniedKeywords": [ "INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "ALTER", ... ],
    "DeniedFunctions": [ "load_extension", "readfile", "writefile" ],
    "MaxSqlLength": 4000,
    "AutoLimitIfMissing": true
  }
}
```

### Строка подключения — только в User Secrets / env:

```bash
dotnet user-secrets set "SqlAgent:Internal:ConnectionString" \
    "Data Source=Data/iichattools-dev.db;Mode=ReadOnly"
```

### Ограничения (осознанные, MVP)

- **Только `internal`** (собственная БД приложения). Внешние Postgres / MySQL —
  Фаза 2 (v1.8.0, KI-099).
- **Domain-Oriented Tools** (`get_recent_chats`, `get_user_stats`) — обкатка
  на `execute_query`, потом добавим по факту.
- **Semantic Layer (knowledge graph)** — не входит, план на v2.0.

---

## Mail Agent (v1.8.0)

Работа с электронной почтой через IMAP4/SMTP. **Требует явного `Mail:Enabled = true`** —
иначе агент и его инструменты не регистрируются в DI.

### 7 инструментов внутри агента `mail_agent`

| Tool | Approval | Назначение |
|---|:---:|---|
| `send_email` | ✅ | Отправка (to/cc/bcc/subject/body/isHtml/attachments) |
| `list_emails` | — | Список писем из папки |
| `read_email` | — | Чтение письма по UID |
| `search_emails` | — | Поиск (from/subject/since/before/unseenOnly) |
| `delete_email` | ✅ | Удаление (перемещение в Trash) |
| `move_email` | ✅ | Перемещение между папками |
| `mark_as_read` | — | Пометка прочитанным (approval уже на `mail_agent`) |

**Chat видит один инструмент** — `mail_agent`. LLM вызывает его с задачей:
«Прочитай последнее письмо от Иванова», «Отправь счёт на X» и т.п.
Модалка approval — **одна на всю задачу** (не на каждый tool внутри).

### Безопасность

- **Approval** — на `mail_agent` (1 модалка на задачу).
- **Rate limiting:** 20 писем/час, 2 письма/мин, 30 чтений/мин (per-user).
- **Вложения:** только из workspace пользователя (`PathHelper`), ≤ 10 MB на письмо, ≤ 5 файлов.
- **Privacy:** в логах — только метаданные (uid, count, bytes). Никогда — Subject / From / To / Body.

### Конфигурация

Секция `Mail` в `appsettings.json`:

    "Mail": {
      "Enabled": false,
      "Imap": { "Host": "imap.yandex.ru", "Port": 993, "UseSsl": true },
      "Smtp": { "Host": "smtp.yandex.ru", "Port": 465, "UseSsl": true },
      "FromAddress": "agent@example.com",
      "FromDisplayName": "IIChatTools Agent",
      "Attachments": { "MaxFileSizeBytes": 10485760, "MaxFilesPerMessage": 5 },
      "RateLimit": { "SendsPerHour": 20, "SendsPerMinute": 2, "ReadsPerMinute": 30 }
    }

### Credentials — только в User Secrets (не в appsettings!)

    cd C:\Projects\AI\IIChatTools\IIChatTools.API

    dotnet user-secrets set "Mail:Imap:Host" "imap.yandex.ru"
    dotnet user-secrets set "Mail:Smtp:Host" "smtp.yandex.ru"
    dotnet user-secrets set "Mail:FromAddress" "you@yandex.ru"
    dotnet user-secrets set "Mail:Imap:Username" "you@yandex.ru"
    dotnet user-secrets set "Mail:Imap:Password" "<app-password>"
    dotnet user-secrets set "Mail:Smtp:Username" "you@yandex.ru"
    dotnet user-secrets set "Mail:Smtp:Password" "<app-password>"
    dotnet user-secrets set "Mail:Enabled" "true"

### Как получить App Password (RU-специфика)

- **Yandex** (рекомендуется в РФ, работает без VPN): https://id.yandex.ru/security → «Пароли приложений» → «Почта» → «IMAP-клиент».
- **Mail.ru**: https://account.mail.ru/user/2-step-auth/passwords → включить 2FA → «Пароли для внешних приложений» → «IMAP».
- **Gmail** (требует VPN в РФ): https://myaccount.google.com/security → 2FA → «Пароли приложений» → «Почта».
- **OAuth2** (для Gmail / Outlook / Exchange) — запланировано на **v1.9+**.

### Troubleshooting (Yandex и другие)

**Симптом:** `MailKit.Security.AuthenticationException: LOGIN invalid credentials or IMAP is disabled`.

Yandex отдаёт это **одно и то же** сообщение в **трёх** случаях:

**Причина 1 — IMAP не включён в веб-интерфейсе** (самое частое).

Fix:

1. Открой https://mail.yandex.ru → Настройки (⚙) → «Почтовые программы».
2. Поставь галку «С сервера imap.yandex.ru по протоколу IMAP».
3. Внизу → «Сохранить изменения».

**App Password ≠ IMAP.** Это две разные настройки. App Password без включённого IMAP не работает.

**Причина 2 — пароль = обычный, а не App Password.**

App Password — 16 символов только `a-z0-9`. Если сохранённый пароль длиннее, содержит заглавные или спецсимволы — это обычный пароль.

Проверка:

    cd <repo-root>/IIChatTools.API
    dotnet user-secrets list | Select-String "Password"

Ожидание: `Mail:Imap:Password = abcdefghijklmnop` (16 символов, без пробелов).

Yandex показывает App Password группами (`abcd efgh ijkl mnop`) — **пробелы убрать**.

**Причина 3 — Username не полный email.**

Yandex требует `user@yandex.ru`, а не `user`.

Проверка:

    dotnet user-secrets list | Select-String "Username|FromAddress"

Ожидание:

    Mail:FromAddress   = user@yandex.ru
    Mail:Imap:Username = user@yandex.ru
    Mail:Smtp:Username = user@yandex.ru

**Проверка через внешний IMAP-клиент** (Thunderbird / Outlook) — отсекает 90% проблем:

    IMAP сервер: imap.yandex.ru
    Порт:       993
    Шифрование: SSL/TLS
    Логин:      user@yandex.ru
    Пароль:     App Password, 16 символов без пробелов

Если внешний клиент **подключается**, а IIChatTools — **нет** → проблема в User Secrets (опечатка, лишние пробелы).

Если внешний клиент **тоже падает** → проблема в Yandex (IMAP не включён / пароль не App Password).

**Симптом:** `MailKit.Net.Imap.ImapProtocolException` / timeout.

- Порт IMAP: **993** (SSL). Порт SMTP: **465** (SSL). Проверь, что фаервол / прокси не блокирует.
- Хост: `imap.yandex.ru` / `smtp.yandex.ru` (не `mail.yandex.ru`).
- Тест сети: `Test-NetConnection imap.yandex.ru -Port 993` → `TcpTestSucceeded: True`.

**Симптом:** `Не задан Workspace:RootPath`.

- `dotnet user-secrets set "Workspace:RootPath" "<путь>"` (см. раздел «Профиль разработки»).

**Симптом:** `Превышен лимит: 20 писем/час`.

- Rate limiter сработал. Подожди или перезапусти приложение (сброс in-memory счётчика).

### Ограничения v1.8.0

- **Один глобальный ящик** для всех пользователей (per-user — v1.8.x, KI-108).
- **Сохранение вложений при `read_email`** — отложено (требует переделки `IMailClient`).
- **Прикрепление вложений к `send_email`** — `ResolveForSendAsync` готов, но привязка к `MimeMessage` — v1.8.x.
- **POP3, календарь, контакты** — не входят.

### API

Mail-tools — не имеют собственных REST-endpoint'ов. Вызываются через:
- **Chat UI:** LLM вызывает `mail_agent` → SSE `tool_call` → approval → `tool_result`.
- **`/api/tools/execute`:** прямой вызов (`{"toolName": "list_emails", "arguments": {...}}`).

---

## External-LLM Agent (v1.8.1)

Обращение к внешним LLM (DeepSeek / OpenAI / Groq / Together AI / Ollama).
Все провайдеры — **OpenAI-совместимые** (единый формат `/v1/chat/completions`).

**Требует явного `ExternalLlm:Enabled = true`** + API-ключи в User Secrets.

### 3 инструмента внутри агента `external_llm_agent`

| Tool | Approval | Назначение |
|---|:---:|---|
| `ask_external_llm` | — | Запрос к внешней модели. Параметры: `provider?`, `prompt`, `compare_with?`, `include_context?=false`, `max_tokens?`, `temperature?` |
| `list_external_providers` | — | Список провайдеров + статус + тарифы |
| `check_internet_connection` | — | Лёгкая проверка доступности |

**Chat видит один инструмент** — `external_llm_agent`. LLM вызывает его с задачей:
«Спроси DeepSeek, что нового в .NET 10», «Сравни ответы ChatGPT и DeepSeek про X».

**Approval не требуется.** Защита — **дневной бюджет** ($5/день, per-user) +
**circuit breaker** (3 fail → 5 мин skip) + **per-request MaxTokens** (8192).

### Оркестратор — 4 сценария (внутри агента)

1. **Fallback** — локальная модель не справляется → LLM зовёт внешнюю.
2. **Специализация** — код → DeepSeek, свежие данные → OpenAI, быстро → Groq.
3. **Разные знания** — 2 модели дают разные ответы.
4. **Сравнение** — `ask_external_llm(provider="deepseek", compare_with="openai")` → 2 параллельных запроса + таблица сравнения.

### Privacy

- **`include_context: false` по умолчанию** — во внешнюю модель уходит **только prompt**, не история чата.
- **Не логируются** prompt / ответ (только метаданные: провайдер, длина, токены, стоимость).
- **Не сохраняются** в `ChatMessage.MetadataJson`.

### Конфигурация

Секция `ExternalLlm` в `appsettings.json`:

    "ExternalLlm": {
      "Enabled": false,
      "DefaultProvider": "deepseek",
      "DailyBudgetUsd": 5.0,
      "DailyTokensLimit": 500000,
      "CircuitBreaker": { "FailureThreshold": 3, "BreakDurationSeconds": 300 },
      "Providers": {
        "deepseek": { "BaseUrl": "https://api.deepseek.com/v1", "Model": "deepseek-chat", ... },
        "openai":   { "BaseUrl": "https://api.openai.com/v1",   "Model": "gpt-4o-mini", ... },
        "groq":     { "BaseUrl": "https://api.groq.com/openai/v1", "Model": "llama-3.3-70b-versatile", ... },
        "together": { "BaseUrl": "https://api.together.xyz/v1", "Model": "meta-llama/Llama-3.3-70B-Instruct-Turbo", ... },
        "ollama":   { "BaseUrl": "http://localhost:11434/v1", "Model": "qwen3:4b", ... }
      }
    }

### API-ключи — только в User Secrets

    cd C:\Projects\AI\IIChatTools\IIChatTools.API

    # DeepSeek (работает в РФ без VPN — рекомендован)
    dotnet user-secrets set "ExternalLlm:Enabled" "true"
    dotnet user-secrets set "ExternalLlm:DeepSeek:ApiKey" "sk-..."

    # OpenAI (требует VPN из РФ)
    dotnet user-secrets set "ExternalLlm:OpenAI:ApiKey" "sk-proj-..."

    # Groq (free tier, через VPN)
    dotnet user-secrets set "ExternalLlm:Groq:ApiKey" "gsk_..."

    # Ollama (локально, без оплаты) — ключ не нужен
    dotnet user-secrets set "ExternalLlm:DefaultProvider" "ollama"

**Получить ключи:**
- **DeepSeek** — https://platform.deepseek.com/ (оплата: крипта / ЮMoney через посредников).
- **OpenAI** — https://platform.openai.com/ (VPN + зарубежная карта).
- **Groq** — https://console.groq.com/ (free tier, через VPN).
- **Ollama** — https://ollama.com/download (локально, без регистрации).

Подробности — [docs/development/v1.8/DESIGN_EXTERNAL_LLM.md](docs/development/v1.8/DESIGN_EXTERNAL_LLM.md) § 5.4.

### Ограничения v1.8.1

- **Только non-streaming** (без SSE с внешних API).
- **Без function calling** на внешних API — только обычный prompt → text.
- **Anthropic Claude / Google Gemini** — не входят (свои форматы, v1.9+, KI-110).
- **Per-user API keys** — v1.8.x (по образцу KI-108 для Mail).

### API

External-LLM tools не имеют собственных REST-endpoint'ов. Вызываются через:
- **Chat UI:** LLM → `external_llm_agent` → внутри `ask_external_llm`.
- **`/api/tools/execute`:** прямой вызов (`{"toolName": "ask_external_llm", "arguments": {...}}`).

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

    docker pull ghcr.io/iilmchat/iichattools:latest
    docker pull ghcr.io/iilmchat/iichattools:v1.8.1
    docker pull ghcr.io/iilmchat/iichattools:1.8.1
    docker pull ghcr.io/iilmchat/iichattools:1.8
    docker pull ghcr.io/iilmchat/iichattools:1

### Развёртывание (Docker)

Базовая команда:

    docker run -d \
      --name iichattools \
      -p 8080:8080 \
      -e Jwt__Key="<ваш-секрет-≥32-символа>" \
      -v iichattools-data:/app/Data \
      -v iichattools-logs:/app/logs \
      -v iichattools-workspace:/app/Workspace \
      -v iichattools-keys:/home/app/.aspnet/DataProtection-Keys \
      ghcr.io/iilmchat/iichattools:latest

**Про volumes:**
- `iichattools-data` — БД (Sqlite), настройки.
- `iichattools-logs` — audit JSONL.
- `iichattools-workspace` — workspace пользователей.
- `iichattools-keys` — **важно!** DataProtection keys. Без него cookies сбрасываются при пересоздании контейнера.

### Docker — что работает, что нет

Образ `ghcr.io/iilmchat/iichattools` — **lightweight** (ASP.NET 10 runtime, ~600 MB).
Внутри **нет** `git`, `gh`, `python3`, `node`, `bash`-утилит разработчика.

| Компонент | Работает в контейнере? | Комментарий |
|---|:---:|---|
| **Chat UI** + SSE + tool calling | ✅ | Ядро приложения |
| **Mail Agent** (IMAP/SMTP) | ✅ | MailKit встроен (NuGet) |
| **RAG** (PDF/DOCX/embed) | ✅ | PdfPig, OpenXml, эмбеддинги LM Studio |
| **SqlAgent** / Database Agent | ✅ | Microsoft.Data.Sqlite / SqlClient |
| **`file_system_agent`** | ✅ | Workspace в контейнере |
| **`web_agent`** | ✅ | HttpClient встроен |
| **`code_agent`** | ❌ | Нет `python3`, `node`, `bash` |
| **`git_agent`** | ❌ | Нет `git` |
| **`github_agent`** | ❌ | Нет `gh` |
| **`browser_*`** (PuppeteerSharp) | ⚠️ | Chromium не установлен по умолчанию (нужен `--build-arg INSTALL_BROWSER=true`) |

При старте приложения в логах будут WARNINGи:

Ошибка при проверке зависимости git
System.ComponentModel.Win32Exception (2): ...'git'... No such file or directory
...
Зависимость git: не установлен

Это **ожидаемо** — `DependencyChecker` проверяет наличие утилит, но приложение работает без них (см. KI-112).

### LM Studio в контейнере

LM Studio обычно работает **на хосте** (`http://localhost:8034`), а контейнер общается с ним через `host.docker.internal`.

    docker run -d \
      --name iichattools \
      -p 8080:8080 \
      -e LmStudio__BaseUrl="http://host.docker.internal:8034" \
      -e Jwt__Key="<...>" \
      ...

**На Linux:** `--add-host=host.docker.internal:host-gateway` (иначе хост недоступен).

### Mail Agent в контейнере

User Secrets **не работают** в контейнере. Credentials — **через env-переменные** (префикс `Mail__`, двойное подчёркивание):

    docker run -d \
      --name iichattools \
      -p 8080:8080 \
      -e Jwt__Key="<...>" \
      -e Mail__Enabled="true" \
      -e Mail__Imap__Host="imap.yandex.ru" \
      -e Mail__Imap__Username="user@yandex.ru" \
      -e Mail__Imap__Password="<app-password>" \
      -e Mail__Smtp__Host="smtp.yandex.ru" \
      -e Mail__Smtp__Username="user@yandex.ru" \
      -e Mail__Smtp__Password="<app-password>" \
      -e Mail__FromAddress="user@yandex.ru" \
      -v iichattools-data:/app/Data \
      ghcr.io/iilmchat/iichattools:latest

### HTTPS redirect в Docker

Контейнер слушает **только HTTP** (порт 8080). В логах может появляться WARNING:

    Failed to determine the https port for redirect.

Это **не ошибка** — HTTPS-терминация обычно на reverse-proxy (nginx, Caddy, Traefik) перед контейнером. `UseHttpsRedirection` пропускает запросы, если HTTPS-порт не сконфигурирован.

Для **чистого запуска без HTTPS** можно выставить:

    -e ASPNETCORE_HTTPS_PORT=""

Если HTTPS-терминация на reverse-proxy — дополнительно:

    -e ASPNETCORE_FORWARDEDHEADERS_ENABLED="true"

---

## Сборка и тесты

```bash
dotnet build IIChatTools.sln -c Release
dotnet test IIChatTools.sln -c Release
```

**Статус**: 496/496 тестов проходят (unit + integration), 3 Skip (реальные провайдеры).

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

### Проверка уязвимостей (v1.7.1, KI-105)
**Перед push — проверить транзитивные NuGet-зависимости на известные уязвимости:** 
```powershell
    pwsh -ExecutionPolicy Bypass -File scripts/setup/check-vulnerabilities.ps1
```
    Скрипт делает dotnet restore + dotnet list package --vulnerable --include-transitive и возвращает exit 1, если что-то найдено. Прецеденты: KI-022 (SQLitePCLRaw), KI-105 (System.IO.Packaging).

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
