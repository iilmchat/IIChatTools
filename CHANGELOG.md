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
- **Docker**: `Dockerfile` (multi-stage: SDK 10.0 → ASP.NET Runtime 10.0), `docker-compose.yml` (prod + опциональный SQL Server), `docker-compose.override.yml` (dev), `.dockerignore`, `.env.example`. Опциональный Chromium через build-arg `INSTALL_BROWSER`.
- **CI/CD**: GitHub Actions workflows — `ci.yml` (build + test на push/PR + coverage artifacts), `docker-publish.yml` (сборка и публикация образа в ghcr.io на main/теги v*), `dependabot.yml` (еженедельные обновления NuGet + Actions).
- **README**: бейджи CI/Docker Publish, раздел «CI/CD».
- **Health checks**: `/health/live` (liveness), `/health/ready` (БД + Workspace), `/health` (полный JSON-отчёт, включая LM Studio). Анонимные endpoints для Docker/k8s/monitoring. Реализованы кастомные проверки: `DatabaseHealthCheck`, `WorkspaceHealthCheck`, `LmStudioHealthCheck`.
- `Dockerfile`: healthcheck переведён на `/health/live`.
- **Scripts**: `scripts/setup/enable-online-restore.ps1` (создаёт `NuGet.Config.online` из шаблона), `scripts/setup/fill-local-packages.ps1` (наполняет `LocalPackages/` из глобального NuGet-кэша; параметры `-Clean`, `-DryRun`).
- **Config**: `NuGet.Config.example.online` — шаблон онлайн-конфига (не коммитить реальный `NuGet.Config.online` — он в `.gitignore`).
- **Rate limiting**: `AddAppRateLimiting` — политики `per-user`, `tools-execute` (строже для `/api/tools/execute`), `auth` (brute-force для `/auth/*`). JSON-ответ с `retryAfterSeconds` при 429. Health-эндпоинты исключены. Настройка через `RateLimiting` в `appsettings.json`.
- **Rate limiting**: собственный `RateLimitingMiddleware` на базе `System.Threading.RateLimiting`. Политики: `per-user` (100/min), `tools-execute` (30/min для `/api/tools/execute`), `auth` (5/min для `/auth/*`). JSON-ответ 429 с `retryAfterSeconds`. `/health/*` исключены. Настройка через секцию `RateLimiting` в `appsettings.json`.
- **Prometheus метрики**: `/metrics` (анонимный). Стандартные `http_requests_*`, `http_request_duration_seconds` (из `prometheus-net.AspNetCore 8.2.1`). Кастомные: `iichattools_tool_executions_total{tool_name,status}`, `iichattools_tool_execution_duration_seconds{tool_name}`, `iichattools_pending_approvals`, `iichattools_active_users`, `iichattools_audit_entries_total{status}`, `iichattools_lmstudio_requests_total{status}`. Gauge обновляются фоновым сервисом `MetricsRefreshBackgroundService` раз в 30 сек.

### Changed
- `Startup.cs`: `app.UseRateLimiter()` после `UseAuthentication` (политики per-user видят `User`).
- **Directory.Build.props**: версии Microsoft-пакетов синхронизированы с ref-pack SDK 10.0.401 (10.0.12). `EFCoreVersion`, `EntityFrameworkSqliteVersion`, `MicrosoftAspNetCoreVersion`, `MicrosoftExtensionsVersion` → 10.0.12.

### Fixed
- **KI-040**: `PathHelper` нормализует оба разделителя (`/` и `\`) до валидации пути. Устранён обход path-traversal через backslash на Linux. Также исправлены: сравнение путей (case-sensitive на Linux), обрезка завершающего разделителя (корень `/` больше не превращается в `""`). CI на Linux выявил проблему.
- **Docker**: `Dockerfile` — используется предустановленный пользователь `app` из базового образа `dotnet/aspnet:10.0` (GID/UID 1000 уже существует с .NET 8). Ранее `groupadd` падал с `group 'app' already exists` в GitHub Actions.
- **Docker**: убран флаг `--no-restore` из `dotnet publish` в `Dockerfile` — восстановление пакетов повторяется, что устраняет ошибку `NETSDK1064: Package Microsoft.CodeAnalysis.Analyzers was not found` в CI (устаревший кэш `/root/.nuget/packages`).
- **KI-041**: в README и KNOWN_ISSUES упоминались файлы `NuGet.Config.online` и `scripts/setup/fill-local-packages.ps1`, которых не было в репозитории. Добавлены шаблон и оба скрипта; README обновлён.
- **KI-042**: `AddRateLimiter` недоступен в SDK 10.0.401 — `Microsoft.AspNetCore.RateLimiting.dll` отсутствует в compilation API shared framework. Реализован собственный middleware на `System.Threading.RateLimiting`.

### Security
- **KI-040**: cross-platform обход `PathHelper.TryGetSafeFullPath` через `\` на Linux (потенциальный path traversal в FS/git/shell-инструментах).

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

### Security
- **KI-039**: устранена утечка прокси-credentials и email автора в git-истории (публичный репозиторий).
  - `git filter-repo --replace-text` + `--mailmap` + `--invert-paths`.
  - Все ветки и теги перезаписаны через `git push --force`.
  - GitHub Secret Scanning: «No secrets found».
- **KI-036**: удалён хардкод прокси-credentials из `Program.cs` и скриптов.
  - Значения читаются из `IICHATTOOLS_PROXY*` (env) или User Secrets.
  - Fallback-значения убраны.

### Fixed
- **KI-037**: дедупликация строк подключения в `appsettings.json`.
- **KI-038**: локальные пути → User Secrets (`Workspace:RootPath`).
- **KI-005**: явное поведение `git_add` при пустом `files` (без `-A`).
- **KI-001**: причина отклонения теперь отображается в поле «Результат» на `/test`.

### Removed
- Папка `UPDATES/` (устаревшие копии).
- Дубли `configs/NuGet1.Config`, `configs/NuGet111.Config`.
- Ad-hoc скрипты `scripts/setup/download-missing*.ps1`, `downloader*.ps1`.

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