# Changelog

Все значимые изменения проекта IIChatTools документируются в этом файле.

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/).
Проект придерживается [Semantic Versioning](https://semver.org/lang/ru/).

Типы изменений:
- **Added** — новая функциональность
- **Changed** — изменения в существующей функциональности
- **Deprecated** — функции, помеченные как устаревшие
- **Removed** — удалённая функциональность
- **Fixed** — исправления
- **Security** — исправления уязвимостей и утечек

---

## [Unreleased]

### Added
- **Chat UI (v1.3 Фаза 1.1)**: сущности `Chat` и `ChatMessage` + миграция `AddChatAndChatMessages`. Таблицы `Chats`, `ChatMessages` в БД; индексы `IX_Chats_UserId_UpdatedAt`, `IX_ChatMessages_ChatId_CreatedAt`.
- **ChatService (v1.3 Фаза 1.2)**: `IChatService` + `ChatService` — CRUD чатов и сообщений, проверка владения (`userId`), `DeleteOldChatsAsync` для retention.
- **LM Studio SSE (v1.3 Фаза 1.3)**: `ILmStudioClient.ChatStreamAsync` — `IAsyncEnumerable<ChatCompletionChunk>` для стриминга. DTO `ChatCompletionChunk` (DeltaContent, DeltaReasoning, DeltaToolCall, FinishReason, Usage, IsDone). Парсер SSE-формата (`data: {...}`, `[DONE]`, tool_calls частями). Сохранён `CompleteAsync` для `SubAgentService`.
- **Design doc v1.3**: `docs/development/v1.3/DESIGN.md` — Chat UI (sidebar, SSE, инлайн tool calls), API-контракты, схема данных, план работ.
- **Unit-тесты**: 5 новых на парсинг SSE (`LmStudioSseParseTests`). Всего: **24/24**.
- **ChatController (v1.3 Фаза 1.4)**: REST API чатов — `GET /api/chats` (список), `GET /api/chats/{id}` (детали + история), `POST /api/chats` (создать), `PATCH /api/chats/{id}` (обновить title/model/systemPrompt), `DELETE /api/chats/{id}` (удалить). DTO: `ChatListItemDto`, `ChatDetailDto`, `ChatMessageDto`, `CreateChatRequest`, `UpdateChatRequest`. Проверка владения (`userId` из claims), `[Authorize]` на всех методах.
- **Tool calling infrastructure (v1.3 Фаза 1.6.A)**: `ToolDefinitionsBuilder` — единый построитель JSON-схем инструментов в формате OpenAI Function Calling. Используется Chat (10 инструментов из `SubAgent:DefaultAllowedTools`) и SubAgent. Поддерживает белый список и список исключений.
- **Tool calling — SSE events (v1.3 Фаза 1.6.A.2.1)**: `ChatStreamEvent.ToolCall` и `ChatStreamEvent.ToolResult`. DTO `ChatToolCallDto` (`id`, `name`, `arguments`, `requiresApproval`) и `ChatToolResultDto` (`id`, `name`, `success`, `content`, `message`). Готовит почву для multi-turn loop в `ChatStreamService`.
- **Tool calling — аккумулятор (v1.3 Фаза 1.6.A.2.2)**: `ToolCallsAccumulator` — накопление инкрементальных `delta.tool_calls` из SSE-стрима LM Studio. Складывает `arguments` по `index`, собирает завершённые вызовы формата OpenAI.
- **Tool calling — multi-turn loop (v1.3 Фаза 1.6.A.2.3)**: `ChatStreamService.StreamAsync` расширен до multi-turn цикла (до 5 итераций). Стрим → накопление `tool_calls` → выполнение через `IToolRegistry` → SSE-события `tool_call` / `tool_result` → сохранение в БД → повторный стрим. При `Request.UseTools == false` — старое поведение (без tools). Инструменты с `RequiresApprovalByDefault == true` возвращают `ToolResult.Fail` (полная реализация — Фаза 1.7).
- **Тесты tool calling (v1.3 Фаза 1.6.B)**: 2 новых интеграционных теста в `ChatStreamServiceTests` — успешный multi-turn loop (list_directory → результат → финальный ответ) и approval-инструмент (save_file → ToolResult.Fail без выполнения). FakeLmStudioClient расширен — поддержка итераций. Всего: **29/29**.
- **Approvals в чате — каркас (v1.3 Фаза 1.7.1)**: `ChatApprovalDecision` (enum), `ChatApprovalRequiredDto`, `ChatApprovalResolvedDto`, `IChatApprovalCoordinator`. Дополнены `ChatStreamEvent.ToolApprovalRequired` и `.ToolApprovalResolved`. Готовит почву для Singleton-координатора (Шаг 1.7.2).
- **Approvals в чате — REST-endpoints (v1.3 Фаза 1.7.4)**: `POST /api/chat/approvals/{callId}/approve` и `POST /api/chat/approvals/{callId}/reject`. Вызывают `IChatApprovalCoordinator.ResolveAsync`, будят ожидающий SSE-стрим. `[Authorize]` — только аутентифицированные. Если ожидающий не найден (таймаут) — `{ success: false }`.
- **Approvals в чате (v1.3 Фаза 1.7 — полная реализация)**: 
  - `POST /api/chat/approvals/{callId}/approve` — подтвердить вызов инструмента.
  - `POST /api/chat/approvals/{callId}/reject` — отклонить.
  - SSE-события `tool_approval_required` / `tool_approval_resolved`.
  - Координатор Singleton + cleanup (KI-043 учтён).
  - camelCase в SSE-событиях (единый стиль).
  - Smoke-тест end-to-end: `save_file` → approval_required → reject → tool_result(fail) → финал.
- **Фаза 1.7 v1.3 закрыта.** Backend полностью готов к Chat UI.
- **Chat UI — каркас страницы `/chat` (v1.3 Фаза 2.0.1)**: 
  - `ChatViewController` + Razor-страница `Views/Chat/Index.cshtml`.
  - Layout: sidebar (список чатов) + область сообщений + поле ввода.
  - `wwwroot/css/chat.css` — стили чата.
  - `wwwroot/js/modules/chat.js` — заглушка (инициализация без логики).
  - `_Layout.cshtml`: пункт меню «Чат» + `@RenderSection("Styles")`.
  - **Chat UI — локализация (v1.3 Фаза 2.0.1)**: 16 новых ключей в `SharedResources.resx` (EN) и `SharedResources.ru.resx` (RU). Ключи для страницы `/chat`: `Чат`, `Новый чат`, `Удалить чат`, `Выберите чат или создайте новый`, `Введите сообщение…`, `Отправить`. Резерв для Шагов 2.0.2–2.0.4 (sidebar CRUD, SSE-ошибки). Соответствует правилу 1.14.
- **Chat UI — endpoint моделей (v1.3 Фаза 2.0.2a)**: `GET /api/models` — возвращает список моделей LM Studio (`/v1/models`) + модель по умолчанию из `LmStudio:Model` (всегда первая, `isDefault: true`). Fallback: если LM Studio недоступен — возвращается только default (UI работает в offline-режиме). `[Authorize]`. DTO `ModelInfoDto` (`id`, `name`, `isDefault`). Готовит почву для UI-селектора модели (DESIGN.md § 13.1).
- **Chat UI — sidebar (v1.3 Фаза 2.0.2b)**: `chat.js` реализован полностью для sidebar:
  - `loadChats()` / `loadModels()` — загрузка списка чатов и моделей.
  - `createChat()` — создание (модель по умолчанию из `/api/models`).
  - `selectChat(id)` — переключение с загрузкой истории.
  - `deleteChat(id)` — удаление с подтверждением.
  - `renderChatList()` / `renderMessages()` — рендер sidebar и ленты.
  - Синхронизация активного чата с URL через `history.replaceState` (`?chatId=N`).
  - Относительные даты (`только что`, `5 мин назад`, `вчера`, `22.09`).
  - Минимальное отображение tool calls (серые блоки с именем инструмента).
  - Автоскролл к последнему сообщению.
  - `chat.css` дополнен стилями сообщений и tool-блоков.
  - **Chat UI — SSE-стриминг (v1.3 Фаза 2.0.3)**: `chat.js` умеет отправлять сообщения и стримить ответ:
  - `sendMessage()` — `POST /api/chat/stream` с `useTools: true`.
  - `readSseStream()` — чтение SSE через `fetch` + `ReadableStream`.
  - `handleSseEvent()` — обработка `start`/`delta`/`done`/`error`/`tool_call`/`tool_result`.
  - `tool_approval_required` / `tool_approval_resolved` — заглушки (полная обработка — Фаза 2.0.4).
  - Оптимистичное отображение user-пузыря, индикатор «Печатает…» (три точки), потоковая отрисовка delta.
  - Блокировка input/кнопки на время стрима, автоскролл (только если пользователь был внизу).
  - Enter — отправка, Shift+Enter — новая строка, автоувеличение textarea.
  - Локальное обновление `UpdatedAt` в sidebar после `done`.
  - `chat.css`: стили `chat-typing-indicator`, `chat-message-streaming`, `chat-message-error`, `chat-tool-result.success/error`.
  - **Chat UI — Approvals (v1.3 Фаза 2.0.4)**: интеграция `_ApprovalModal` в чат.
  - `approvals.js`: рефакторинг — общая логика вынесена в `_showApprovalModal({ approveUrl, rejectUrl, askReason })`.
  - Новый экспорт `requestChatApproval(callId, ...)` — для flow `/api/chat/approvals/{callId}/...` (без prompt причины, см. Q6 Фазы 1.7).
  - `requestApproval(actionId, ...)` — сохранён (обратная совместимость с `/test`).
  - `chat.js`: обработка SSE-события `tool_approval_required` — показ модалки (fire-and-forget, не блокирует SSE reader).
  - `chat.js`: `tool_approval_resolved` — логирование (UI-индикатор — v1.3.x).
  - Стрим продолжается автоматически после решения: `tool_approval_resolved` → `tool_result` → `delta` → `done`.

### Changed
- **ChatStreamService (v1.3 Фаза 1.6.A.2.4)**: добавлена зависимость `IWorkspaceResolver`. `ToolExecutionContext.WorkspaceRoot` теперь реально резолвится (было `null`) — FS-инструменты в чате работают.
- **Config (v1.3 Фаза 1.6.A.2.4)**: `SubAgent:DefaultAllowedTools` обновлён — только **read-only** инструменты без approval: `read_file`, `find_files`, `get_file_metadata`, `fuzzy_find_local_files`, `git_status`, `git_log`, `git_diff`, `web_search`, `wikipedia_search`, `get_system_info`. Mutating-инструменты (`list_directory`, `save_file`, `replace_text_in_file`, `execute_command`, `git_add`, …) требуют approval и станут доступны в чате после Фазы 1.7.
- **`appsettings.Development.json`**: добавлен `SubAgent:DefaultAllowedTools` (ранее отсутствовал — все 40 инструментов попадали в чат).
- **`ListDirectoryTool` (v1.3 Фаза 1.6.A.2.4)**: `RequiresApprovalByDefault` → `false`. `list_directory` — read-only операция (возвращает список файлов, ничего не меняет), не требует подтверждения. Добавлен обратно в `SubAgent:DefaultAllowedTools`.
- **Документация (v1.3 Фаза 1.6.B)**: обновлены `docs/KNOWN_ISSUES.md` — KI-051 → Resolved, KI-049 дополнен, добавлены KI-054 (approvals в чате, Фаза 1.7) и KI-055 (tool calling реализован). KI-048 дополнен планом автоматизации git config.
- **Тест `StreamAsync_ToolCalling_RequiresApproval_DoesNotExecute` (v1.3 Фаза 1.7.3)**: обновлён под новую логику — проверяет SSE-события `tool_approval_required` и `tool_approval_resolved` + сообщение «Пользователь отклонил вызов инструмента» (ранее — «Требуется подтверждение»).
- **`.gitignore` (v1.3 Фаза 2.0.2b)**: добавлены правила для `cookies.txt` и `.commit-msg.txt` — артефакты локальных smoke-тестов и here-string-коммитов.
- **Chat UI — sidebar (v1.3 Фаза 2.0.2b)**: кнопка удаления чата добавлена в каждый элемент sidebar (появляется при hover / на активном чате). `<a>` → `<div role="button" tabindex="0">` — позволяет вкладывать `<button>` без семантических конфликтов. Кнопка 🗑 в header (`#btn-delete-chat`) сохранена для активного чата.

### Fixed
- **v1.3 Фаза 1.1**: warnings CS0108 (Chat.UpdatedAt скрывает BaseEntity.UpdatedAt — намеренно, `new`), CS0618 (HasName → HasDatabaseName в EF Core 10).
- **v1.3 Фаза 1.3**: warnings CS1574 (cref ArgumentNullException и др. — добавлен `using System;`), CA2024 (reader.EndOfStream в async — заменён на проверку `line == null`).
- **KI-057 (v1.3 Фаза 2.0.2a)**: `GET /api/models` — embedding-модели (например, `text-embedding-nomic-embed-text-v1.5`) исключаются из списка heuristic-фильтром по подстроке `embed`. Причина: эти модели не поддерживают `/v1/chat/completions` и приводят к 400 при выборе в чате.
- **Chat UI — approvals UX (v1.3 Фаза 2.0.4)**: две проблемы, выявленные на smoke-тесте:
  - **Модалку нельзя было подвинуть** — добавлен drag-and-drop по `.modal-header` (курсор `move`, `position: fixed` на время drag, сброс стилей при `hidden.bs.modal`).
  - **Закрытие крестиком (X) подвешивало UI** — модалка закрывалась, но reject на сервер не отправлялся; SSE-стрим ждал 5 минут, input/btn-send оставались заблокированными. Теперь **X = Reject**: при `hidden.bs.modal` без решения автоматически отправляется reject на сервер (в /chat — без причины, в /test — с reason «Закрыто пользователем»).
  - Исправлена утечка listener'ов `hidden.bs.modal` — `{ once: true }`.
  - `getOrCreateInstance` вместо `new bootstrap.Modal(...)` — нет warning'ов при повторных открытиях.

### Security
- Н/Д

---

## [1.2.0] — 2026-09-21

### Added
- **Docker**: `Dockerfile` (multi-stage, SDK 10.0 → ASP.NET Runtime 10.0), `docker-compose.yml` (prod + опциональный SQL Server), `docker-compose.override.yml` (dev), `.dockerignore`, `.env.example`. Опциональный Chromium через build-arg `INSTALL_BROWSER`.
- **CI/CD**: GitHub Actions — `ci.yml` (build + test + coverage artifacts), `docker-publish.yml` (образ в ghcr.io на main и теги v*), `dependabot.yml` (авто-обновления NuGet + Actions).
- **Health checks**: `/health/live` (liveness), `/health/ready` (БД + Workspace), `/health` (полный JSON-отчёт). Анонимные endpoints.
- **Rate limiting**: собственный `RateLimitingMiddleware` на `System.Threading.RateLimiting`. Политики: per-user (100/min), tools-execute (30/min), auth (5/min). JSON-ответ 429 с `retryAfterSeconds`. `/health/*` без лимита.
- **Prometheus метрики**: `/metrics` (анонимный). Стандартные `http_requests_received_total`, `http_request_duration_seconds`, `dotnet_collection_count_total`, `process_*`. Кастомные: `iichattools_tool_executions_total`, `iichattools_tool_execution_duration_seconds`, `iichattools_pending_approvals`, `iichattools_active_users`, `iichattools_audit_entries_total`, `iichattools_lmstudio_requests_total`, `iichattools_audit_cleanup_total`.
- **Audit retention**: `AuditRetentionService` — фоновый BackgroundService чистит `AuditLogs` (ExecuteDeleteAsync) и `logs/audit/*.jsonl` по retention policy. Настраивается через `Audit:CleanupIntervalHours`, `Audit:DatabaseRetentionDays`, `Audit:FileRetentionDays`.
- **MSSQL migrations**: `IIChatTools.Data/Migrations/SqlServer/20260921102238_InitialSqlServer` — версионирование схемы для SQL Server. `__EFMigrationsHistory` в БД.
- **Скрипты setup**: `scripts/setup/enable-online-restore.ps1` (создаёт `NuGet.Config.online`), `scripts/setup/fill-local-packages.ps1` (наполняет `LocalPackages`).
- **Config**: `NuGet.Config.example.online` — шаблон онлайн-конфига.

### Changed
- **Directory.Build.props**: версии Microsoft-пакетов синхронизированы с ref-pack SDK 10.0.401 → **10.0.12**.
- **`AppMetrics`** перенесён в `IIChatTools.Services/Metrics` — восстановлена слоистость (Data → Services → API).
- **`MetricsRefreshBackgroundService`** перенесён в `IIChatTools.API/BackgroundServices`.
- **`Startup.cs`**: `UseMiddleware<RateLimitingMiddleware>()` вместо `UseRateLimiter()` (см. KI-042).

### Fixed
- **KI-042**: `Microsoft.AspNetCore.RateLimiting` недоступен в SDK 10.0.401 — реализован собственный `RateLimitingMiddleware`.
- **KI-045**: HELP-описания метрик переведены на английский (устранены кракозябры в PowerShell с CP866).
- **Auth**: `[FromForm]` для `LoginAsync` и `RegisterAsync` — устранён HTTP 415 при работе с Razor-формой (побочный эффект `[ApiController]`).

### Documented
- **KI-043**: утечка памяти в `RateLimitingMiddleware` (лимитеры не очищаются при истечении окна) — запланировано на v1.2.x.
- **KI-044**: `iichattools_audit_entries_total` и `iichattools_lmstudio_requests_total` пока не инкрементируются — запланировано на v1.2.x.

---

## [1.1.1] — 2026-09-18

### Changed
- **KI-037**: удалён legacy-ключ `ConnectionStrings:DefaultConnection`. Единственный источник строки SqlServer — `Database:SqlServerConnectionString`. Fallback в `DbContextOptionsExtensions` заменён на явную ошибку.
- **KI-038**: `Workspace:RootPath` вынесен в User Secrets. `WorkspaceResolver` раскрывает env-переменные через `Environment.ExpandEnvironmentVariables`. Устранены мержи локальных путей.
- **KI-005** (API-изменение): `git_add` требует явного `all: true` для добавления всех изменений или непустой `files`. Устранён неявный `git add -A` при пустом `files`.

### Fixed
- **KI-001**: на `/test` при отклонении действия в поле «Результат» отображается причина отклонения. `requestApproval` возвращает `{ decision, reason }`; `ToolsController` при `Rejected` передаёт `data.rejectionReason`. Отмена диалога причины (`Cancel`) больше не вешает модалку.
- **KI-005**: `git_add` — исправлено формирование CLI: `--` добавляется один раз перед списком файлов (а не перед каждым).

### Security
- **KI-022**: обновлён `SQLitePCLRaw.bundle_e_sqlite3` до 2.1.13 (GHSA-2m69-gcr7-jv3q, High).
- **KI-036**: удалён хардкод прокси-credentials из `Program.cs` и скриптов. Значения — только через env/User Secrets.
- **KI-039**: устранена утечка прокси-credentials и email автора в git-истории. `git filter-repo --replace-text` + `--mailmap` + `--invert-paths`, force-push всех веток и тегов. GitHub Secret Scanning: «No secrets found».

### Removed
- Папка `UPDATES/` (устаревшие копии).
- Дубли `configs/NuGet1.Config`, `configs/NuGet111.Config`.
- Ad-hoc скрипты `scripts/setup/download-missing*.ps1`, `downloader*.ps1`.
- Локальные SQLite-БД `IIChatTools.API/Data/*.db` (не коммитились).

---

## [1.1.0] — 2026-09-18

### Added
- **Native LM Studio tool calling** — подтверждено end-to-end.
- **Subagent E2E loop** — многошаговые задачи с автоотладкой.
- **Offline-развёртывание** через `LocalPackages/` (без интернета).
- **`LmStudioTestController`** — endpoint для тестирования LM Studio.
- **`SubAgentController`** — API для суб-агентов.
- **`scripts/diagnostics/*.ps1`** — диагностические скрипты для tool calls.
- **`docs/KNOWN_ISSUES.md`**: KI-015 … KI-038 (24 новых записи).

### Changed
- **.NET Core 3.1 → .NET 10 LTS** (KI-015).
  - `TargetFramework`: `netcoreapp3.1` → `net10.0`, `LangVersion latest`.
  - EF Core 3.1.32 → **10.0.4**.
  - ASP.NET Core / Microsoft.Extensions → **10.0.4**.
  - IdentityModel 6.35.0 → **8.14.0**, HtmlAgilityPack 1.11.61 → **1.12.1**.
- **Версионирование**: `AppVersion.Current` читается из `AssemblyInformationalVersion` (источник — `<Version>` в `Directory.Build.props`).
- **Локализация**: ключ `WelcomeTitle` с плейсхолдером `{0}` вместо версии в ключе (KI-031a).
- **`global.json` и `NuGet.Config`** перенесены в корень репозитория (KI-016, KI-017).
- **`_Layout.cshtml`**: `@(AppVersion.Current)` вместо `@AppVersion.Current` (Razor-парсер путал с email).
- **`Localizer["..."]`** → **`Localizer["..."].Value`** во всех контроллерах (JSON-сериализация `LocalizedString`).
- **`README.md`** полностью переписан под .NET 10 LTS.

### Fixed
- **KI-018**: дублирование `PuppeteerSharpVersion` в `Directory.Build.props`.
- **KI-019**: избыточные ссылки на `Microsoft.Extensions.Logging.*` (NU1510).
- **KI-020**: `LocalPackages` в формате global-packages folder вместо source.
- **KI-021**: кодировка PowerShell искажала вывод `dotnet`.
- **KI-030**: двойной `©` в логе запуска.
- **KI-031**: версия `1.0.2` / `v1.0` в UI вместо `1.1.0`.
- **KI-033**: `favicon.ico` → 404.
- **KI-034**: ASP.NET Core developer certificate не доверен.
- **KI-035**: 4 интеграционных теста `ApprovalService` падали (сидирование пользователей в `TestDbContextFactory`).

### Removed
- Устаревший пакет `Microsoft.AspNetCore.Identity` 2.2.0.
- Избыточные `Microsoft.Extensions.Logging`, `.Console`, `.Debug` из API-проекта.
- Файл `IIChatTools.API/Resources/Как использовать в коде.txt`.

### Security
- Пароль прокси сменён (инцидент 2026-09-18, см. v1.1.1).

---

## [1.0.2] — 2026-09-16

### Added
- Гибридная БД: SqlServer / Sqlite / InMemory (выбор через `Database:Provider`).
- `CompositeAuditService` — аудит в БД + JSONL-файл (`logs/audit/`).
- `.gitattributes` для нормализации LF/CRLF.
- `Directory.Build.props` — централизованные версии пакетов.
- **ChatStream DTO (v1.3 Фаза 1.5)**: `ChatStreamRequest` и `ChatStreamEvent` (start/delta/done/error) — типы для SSE-стриминга ответов LLM.
- **ChatStreamService (v1.3 Фаза 1.5)**: `IChatStreamService` + `ChatStreamService` — координатор SSE-стриминга. Сохраняет user message → строит историю (JArray, OpenAI-формат) → вызывает `ILmStudioClient.ChatStreamAsync` → сохраняет assistant message с токенами → возвращает `IAsyncEnumerable<ChatStreamEvent>`.
- **ChatStreamController (v1.3 Фаза 1.5)**: `POST /api/chat/stream` — SSE-эндпоинт. Заголовки `text/event-stream`, `X-Accel-Buffering: no` (обход прокси/nginx), flush после каждого события. События: `start`, `delta`, `done`, `error`.
- **Unit-тесты ChatStreamService (v1.3 Фаза 1.5)**: 3 теста с `FakeLmStudioClient` — успешный стрим (start/delta/done + сохранение), отсутствие чата (error), пустое сообщение (error). Всего: **27/27**.

### Changed
- Git-инструменты: параметр `path` (каталог репо) + `filePath` (файл) — KI-004.
- GitHub-инструменты: параметр `path` для работы в подкаталогах — KI-006.
- Браузер: `--remote-allow-origins=*`, `--proxy-bypass-list=<-loopback>`, `--ignore-certificate-errors` (Chromium 111+).
- Реалистичный User-Agent для обхода headless-детекции (KI-003).

### Fixed
- **KI-002**: 407 Proxy Authentication Required для веб-инструментов.
- **KI-003**: 403 Forbidden от Wikipedia API.
- **KI-004**: git-инструменты не работали в подкаталогах.
- **KI-006**: gh-инструменты не работали в подкаталогах.
- **KI-050**: rate limiting на HTML-эндпоинтах (`/auth/login`, `/auth/register`) — теперь возвращает **303 redirect** с query `error=ratelimit&retryAfter=N` вместо сырого JSON. `Login.cshtml` / `Register.cshtml` показывают красный banner «Слишком много попыток входа. Попробуйте снова через N секунд». Для API-клиентов (`Accept: application/json`) сохранён прежний 429 JSON.
- **`AuthController`**: удалён атрибут `[ApiController]` — это UI-контроллер (возвращает Razor-Views), и автоматическая модель-валидация возвращала `ProblemDetails` 400 вместо формы с ошибками.

### Removed
- Бинарные артефакты из истории (`bin/`, `obj/`, `LocalPackages/`, `logs/`, `Workspace/`, `*.db`).
  - `git filter-repo --invert-paths` + `git gc --prune=now --aggressive`.
  - Размер `.git`: **154 МБ → 1.51 МБ** (KI-010).
- Gitlink `Workspace/users/1/git-test/` (KI-011).
- Множественные `obj/` из индекса (KI-012).

---

## [1.0.1] — 2026-09-14

### Changed
- Реорганизация структуры репозитория: `docs/`, `scripts/`, `configs/`, `assets/images/` (KI-013).
- Перемещение временных файлов отладки в `docs/development/archive/` (KI-014).

### Fixed
- Мелкие правки после v1.0.

---

## [1.0.0] — 2026-09-13

### Added
- Первый публичный релиз.
- **40 инструментов**: файловая система (13), выполнение кода (3), веб (3), Git (7), GitHub (7), браузер (4), суб-агенты (1), утилиты (2).
- **Мультипользовательность**: роли Admin/User, изоляция workspace.
- **Аутентификация**: cookie (Razor) + JWT (API).
- **Система подтверждений**: `PendingAction` + polling `/api/approvals/pending`.
- **Локализация RU/EN**: `SharedResources.resx` + `IStringLocalizer<SharedResources>`.
- **Суб-агенты**: `ISubAgentService` + `Func<ISubAgentService>` (разрыв DI-цикла).
- **Браузерная автоматизация**: PuppeteerSharp 7.1.0 + Edge/Chrome через прокси.
- **Аудит**: `AuditLog` в БД.
- **Razor UI**: главная, статус, админка, тест, логин/регистрация.
- **ES-модули**: `api`, `ui`, `status`, `approvals`, `admin`, `test`.

---

## Ссылки

- [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/)
- [Semantic Versioning](https://semver.org/lang/ru/)
- [GitHub Releases](https://github.com/iilmchat/IIChatTools/releases)
- [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) — реестр проблем

---

## Как обновлять

1. **В процессе работы** — добавляйте записи в секцию `[Unreleased]`.
2. **При релизе**:
   - Замените `[Unreleased]` на `[X.Y.Z] — YYYY-MM-DD`.
   - Создайте пустую `[Unreleased]` сверху.
   - Обновите `AppVersion.Current` и `<Version>` в `Directory.Build.props`.
   - Создайте git-тег: `git tag -a vX.Y.Z -m "..."`.
3. **Типы записей** — только из списка: Added / Changed / Deprecated / Removed / Fixed / Security.
4. **Ссылки на KI** — обязательно, если изменение связано с реестром проблем.