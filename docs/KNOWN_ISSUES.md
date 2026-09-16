# Известные проблемы IIChatTools

Файл ведётся с версии 1.0 (сентябрь 2026).
Формат: `KI-XXX` — название, приоритет, статус, файлы.

## Исправленные

### KI-002 — 407 Proxy Authentication Required для веб-инструментов
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `IIChatTools.API/Startup.cs`
- **Решение:** `HttpClientFactoryOptions.HttpMessageHandlerBuilderActions` + `WebProxy.Credentials`.

### KI-003 — 403 Forbidden от Wikipedia API
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `WikipediaSearchTool.cs`
- **Решение:** User-Agent `IIChatTools/1.0 (https://github.com/RuChating/IIChatTools; iilmchat@localhost)`.

### KI-004 — Git-инструменты не поддерживали подкаталоги
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `BaseGitTool.cs` + 7 `Git*Tool.cs`
- **Решение:** Параметр `path` + `GitContextValidationResult` + `RunGitInDirAsync`.
- **API-изменения:** `git_diff.path` → `filePath`; `git_log.path` → `filePath`; `git_add.paths` → `files`; `git_checkout.paths` → `files`.

### KI-006 — gh-инструменты не поддерживали подкаталоги
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `BaseGhTool.cs` + 6 `Gh*Tool.cs`
- **Решение:** Параметр `path` + `GhContextValidationResult` + `RunGhInDirAsync`.

### KI-010 — Репозиторий раздулся до 154 МБ из-за бинарных артефактов
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Описание:** В историю Git попали `LocalPackages/*.nupkg`, `bin/`, `obj/`, `Data/*.db`, `logs/`, `Workspace/` и `nuget*.exe`. Размер репозитория достиг 154 МБ.
- **Решение:**
  - `.gitignore` расширен: `**/obj/`, `**/bin/`, `LocalPackages/`, `*.nupkg`, `*.dll`, `*.exe`, `*.db`, `Workspace/`, `logs/`, `Data/`.
  - `git filter-repo --invert-paths` очистил историю от крупных файлов (три прохода).
  - `git gc --prune=now --aggressive` финализировал очистку.
  - Создан `.gitattributes` для корректной работы с LF/CRLF.
- **Результат:** размер упал с 154 МБ до 1.51 МБ.

### KI-011 — `Workspace/` попал в индекс как вложенный git-репозиторий (gitlink)
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Решение:** `git rm -r --cached Workspace/users/1/git-test` + правило `Workspace/` в `.gitignore`.

### KI-012 — Множественные `obj/` в индексе (не удалялись через filter-repo)
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Решение:** `git rm -r --cached <path>/obj` для каждого проекта + правило `**/obj/` в `.gitignore`.

### KI-013 — Нереорганизованная структура репозитория
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Решение:** Файлы сгруппированы: `docs/`, `scripts/`, `configs/`, `assets/images/`. В корне остались 7 файлов + 8 папок.

### KI-014 — Временные файлы отладки в корне
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Решение:** Перемещены в `docs/development/archive/` и `assets/images/`.

### KI-015 — Reasoning-модели требуют повышенного MaxTokens
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Файлы:** `IIChatTools.Services/Implementation/LmStudioClient.cs`, `appsettings.*.json`
- **Описание:** Reasoning-модели (DeepSeek-R1, gemma-reasoning, composer-мерджи) тратят бюджет токенов на служебный канал `reasoning_content`. При `MaxTokens=4096` и большом reasoning-блоке финальный `content` может прийти **пустым** — модель «додумалась», но не успела ответить.
- **Решение:**
  - Дефолт `LmStudio:MaxTokens` повышен 4096 → 8192.
  - В `ChatCompletionResponse` добавлено поле `ReasoningContent`.
  - В `ChatCompletionUsage` добавлено поле `ReasoningTokens` (парсится из `usage.completion_tokens_details.reasoning_tokens`).
  - Логируется расход по всем четырём счётчикам.

### KI-016 — AddIdentity перезаписывает схемы аутентификации и ломает JWT-авторизацию
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Файлы:** `IIChatTools.API/Startup.cs`
- **Описание:** `services.AddIdentity(...)` внутри себя выставляет `DefaultAuthenticateScheme` и `DefaultChallengeScheme` в `IdentityConstants.ApplicationScheme`, из-за чего `[Authorize(Policy = "AdminOnly")]` с Bearer-токеном **всегда падает в 401**, независимо от валидности JWT.
- **Решение:**
  - Явно установить `DefaultScheme` / `DefaultAuthenticateScheme` / `DefaultChallengeScheme` = `"SmartScheme"`.
  - `SmartScheme` — это `AddPolicyScheme(...)` с `ForwardDefaultSelector`: если в `Authorization` есть `Bearer ` → `JwtBearer`, иначе → `Identity.Application`.
  - `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear()` вызывается глобально — иначе claim `role` маппится в `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` и `RequireRole("Admin")` не находит его.
  - **Нюанс .NET Core 3.1:** `JwtBearerOptions.MapInboundClaims` доступен только с .NET 5+. В 3.1 работает только глобальный `DefaultInboundClaimTypeMap.Clear()`. `TokenValidationParameters.RoleClaimType` **не спасает** — маппинг происходит раньше.
- **Результат:** `GET /api/lmstudio/ping` с JWT возвращает `success: true` (см. KI-019).

### KI-017 — Fable/composer-мерджи не пригодны для tool calling
- **Приоритет:** 🔴 Critical | **Статус:** Resolved | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Файлы:** `appsettings.*.json`, `LmStudioClient.cs`
- **Симптом:** Модель `gemma-4-12b-coder-fable5-composer2.5-v1` при запросе с `tools` **распознаёт имя инструмента** (в логе LM Studio: `Start to generate a tool call...`, `Tool name generated: list_directory`), но затем **зацикливается** на генерации аргументов: