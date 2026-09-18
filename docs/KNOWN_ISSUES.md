# Известные проблемы IIChatTools

Файл ведётся с версии 1.0 (сентябрь 2026).
Формат: `KI-XXX` — название, приоритет, статус, файлы.

Приоритеты: 🔴 Critical / 🟠 High / 🟡 Medium / 🟢 Low
Статусы: Open / In Progress / Fixed / Documented / Deferred / Won't Fix / Resolved

---

## v1.1.0 — Миграция на .NET 10 LTS

### KI-015 — Миграция с .NET Core 3.1 на .NET 10 LTS
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Обнаружено:** 2026-09-16
- **Описание:** .NET Core 3.1 снят с поддержки с 13.12.2022. Требовалась миграция на актуальный LTS с сохранением всей функциональности (40 инструментов, Identity, аудит, браузерная автоматизация).
- **Затронуты:** все `.csproj`, `Directory.Build.props`, `global.json`, `NuGet.Config`, `Program.cs`, `AppVersion.cs`, `_Layout.cshtml`, `SharedResources.resx`, `SharedResources.ru.resx`.
- **Решение:**
  - `TargetFramework` → `net10.0` во всех проектах, `LangVersion` → `latest`.
  - EF Core 3.1.32 → **10.0.4**; `Microsoft.Extensions.*` → **10.0.4**; `Microsoft.AspNetCore.*` → **10.0.4**.
  - `IdentityModelVersion` → **8.14.0**; `HtmlAgilityPack` → **1.12.1**.
  - Удалён устаревший `Microsoft.AspNetCore.Identity` 2.2.0 (типы доступны из `Microsoft.Extensions.Identity.Stores`).
  - `global.json` → SDK `10.0.401`, `rollForward: latestFeature`.
  - `Program.cs` со старым `Host.CreateDefaultBuilder` + `Startup.cs` — **работает без переписывания** на top-level statements (отложено на отдельную задачу).
- **Результат:** все 4 проекта собираются под .NET 10, приложение стартует, миграции SQLite проходят, 40 инструментов доступны, аутентификация и аудит работают. Подтверждено end-to-end.

### KI-016 — `global.json` в `configs/` не находился `dotnet`
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `configs/global.json` → `global.json`
- **Причина:** `dotnet` ищет `global.json` вверх по дереву от текущей директории. Из подпапки `configs/` файл не подхватывался.
- **Решение:** `git mv configs/global.json global.json`.

### KI-017 — `NuGet.Config` в `configs/` игнорировался
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `configs/NuGet.Config` → `NuGet.Config`
- **Причина:** NuGet резолвит конфиги от cwd вверх по дереву. Файл в подпапке не работал → restore ходил в `nuget.org` вместо `LocalPackages`.
- **Решение:** `git mv configs/NuGet.Config NuGet.Config`, путь `LocalPackages` сделан относительным.

### KI-018 — Дублирование `PuppeteerSharpVersion` в `Directory.Build.props`
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `Directory.Build.props`
- **Причина:** Свойство `PuppeteerSharpVersion` было объявлено дважды (2.0.4 и 7.1.0). MSBuild брал последнее значение, но дубль сбивал с толку.
- **Решение:** удалено старое значение, оставлено 7.1.0.

### KI-019 — NU1510: избыточные ссылки на `Microsoft.Extensions.Logging.*`
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `IIChatTools.API/IIChatTools.API.csproj`
- **Причина:** Начиная с .NET 6+, `Microsoft.NET.Sdk.Web` включает логирование в shared framework. Явные `PackageReference` избыточны.
- **Решение:** удалены `Microsoft.Extensions.Logging`, `.Console`, `.Debug` из API-проекта.

### KI-020 — `LocalPackages` в формате global-packages folder вместо source
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `NuGet.Config`, `LocalPackages/`
- **Причина:** `dotnet restore --packages <path>` задаёт **global-packages folder**, а не source. При `<clear/>` + `<add key="LocalPackages" .../>` NuGet не находил пакеты (например, `Microsoft.EntityFrameworkCore 10.0.4` в source структура была в 3.1.32).
- **Решение:**
  1. Временный `NuGet.Config.online` с nuget.org.
  2. `dotnet restore --configfile NuGet.Config.online` → пакеты в дефолтный global кэш (`~/.nuget/packages`).
  3. Копирование `.nupkg` из кэша в `LocalPackages` с сохранением иерархии `<id>/<version>/`.
- **Артефакты:** `NuGet.Config.online` добавлен в `.gitignore`.

### KI-021 — Кодировка PowerShell искажает вывод `dotnet`
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `$PROFILE` PowerShell
- **Причина:** PS 7.6.6 Core наследует кодовую страницу 866 от русской локали Windows. `dotnet` пишет UTF-8, PS читает как cp866 → «╨б╨▒╨╛╤А╨║╨░».
- **Решение:** в `$PROFILE`: `chcp 65001`, `[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)`, `$OutputEncoding = ...`.

### KI-022 — NU1903: уязвимость в `SQLitePCLRaw.lib.e_sqlite3` 2.1.11
- **Приоритет:** 🟠 High | **Статус:** Deferred | **Запланировано:** v1.1.x
- **Файлы:** `IIChatTools.API/`, `IIChatTools.Tests/` (транзитивно через EF Core Sqlite)
- **Описание:** Транзитивный пакет тянет версию с уязвимостью [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).
- **Замечание:** Версия `SQLitePCLRaw.bundle_e_sqlite3 3.0.0` не существует. Правильный таргет — `SQLitePCLRaw.lib.e_sqlite3` ≥ 2.1.12, но нужно проверить доступность в `LocalPackages`.
- **Статус:** отложено до стабилизации сборки; не блокер для v1.1.0.

### KI-030 — Двойной `©` в логе запуска
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `IIChatTools.API/Program.cs`
- **Причина:** В формате `LogInformation("© {Copyright}", ...)` уже был символ `©`, и в `AppVersion.Copyright` он тоже есть.
- **Решение:** убран литерал `© ` из строки формата.

### KI-031 — Версия `1.0.2` / `v1.0` в UI и логах вместо `1.1.0`
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `AppVersion.cs`, `_Layout.cshtml`, `Home/Index.cshtml`, `SharedResources.resx`, `SharedResources.ru.resx`
- **Причина:** Версия захардкожена в 6 местах в разных форматах.
- **Решение:**
  - `AppVersion.Current` — свойство, читается из `AssemblyInformationalVersionAttribute` (источник истины — `<Version>` в `Directory.Build.props`).
  - `AppVersion.Copyright` — вычисляется автоматически.
  - `_Layout.cshtml` использует `@AppVersion.Current` вместо хардкода.
  - Ключ `"Добро пожаловать в IIChatTools v1.0"` → `"WelcomeTitle"` с плейсхолдером `{0}`.
  - В `Home/Index.cshtml` — `@string.Format(Localizer["WelcomeTitle"].Value, AppVersion.Current)`.

### KI-031a — Версия в **ключе** .resx ломает локализацию при смене версии
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `SharedResources.resx`, `SharedResources.ru.resx`
- **Причина:** Ключ `"Добро пожаловать в IIChatTools v1.0"` содержит версию. При смене версии пришлось бы править ключ и все ссылки в Razor.
- **Решение:** ключ переименован в `WelcomeTitle`, версия вынесена в плейсхолдер `{0}`.

### KI-032 — `Unprotect ticket failed` (старые cookies от .NET Core 3.1)
- **Приоритет:** 🟡 Medium | **Статус:** Documented
- **Файлы:** браузер клиента
- **Причина:** DataProtection не может расшифровать cookie, выданные на .NET Core 3.1 (изменились ключи/алгоритм).
- **Решение:** очистить cookies для `localhost:5001` в браузере. Опционально — задать общий key ring через `DataProtection:KeysPath` в appsettings.

### KI-033 — `favicon.ico` → 404
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `_Layout.cshtml`
- **Решение:** в `<head>` добавлен `<link rel="icon" href="data:," />` — браузер не дёргает 404.

### KI-034 — ASP.NET Core developer certificate не доверен
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Решение:** `dotnet dev-certs https --trust`.

### KI-035 — 4 интеграционных теста ApprovalService падали
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.0
- **Файлы:** `IIChatTools.Tests/IntegrationTests/TestDbContextFactory.cs`, `ApprovalServiceTests.cs`
- **Обнаружено:** 2026-09-16 (при прогоне `dotnet test` после миграции на .NET 10)
- **Описание:** 4 теста (`CreatePendingActionAsync_SetsCorrectExpiration`, `ApproveAsync_UpdatesStatusAndApprover`, `RejectAsync_SavesRejectionReason`, `ApproveAsync_AlreadyApproved_ReturnsFalse`) падали с `Пользователь с Id=1 не найден`.
- **Причина:** `ApprovalService.CreatePendingActionAsync` (строка 52) содержит защитную проверку существования пользователя (введена для предотвращения FK-ошибок SQLite). Тесты используют `userId=1`, но `TestDbContextFactory.Create()` создавал пустой контекст без пользователей. Тесты устарели относительно реализации — падали и до миграции, просто их не запускали.
- **Решение:** `TestDbContextFactory.Create(bool seedUsers = true)` — по умолчанию сидирует трёх пользователей с детерминированными Id=1, 2, 3. Параметр позволяет отключить сидирование для тестов, которым нужна пустая БД.
- **Результат:** 14 тестов пройдено, 0 падений.

### KI-036 — Хардкод прокси-credentials в `Program.cs`
- **Приоритет:** 🟠 High | **Статус:** Open | **Запланировано:** v1.1.x
- **Файлы:** `IIChatTools.API/Program.cs` (метод `ConfigureDefaultProxy`)
- **Обнаружено:** 2026-09-16 (при ревизии конфигурации)
- **Описание:** Fallback-значения переменных окружения `IICHATTOOLS_PROXY` (`http://proxy.example.local:8080`), `IICHATTOOLS_PROXY_USER` (`proxy_user`), `IICHATTOOLS_PROXY_PASS` (`CHANGE_ME`) захардкожены в исходниках. Публичный репозиторий → утечка учётных данных.
- **Решение:**
  - Убрать дефолтные значения (пустые строки).
  - Читать прокси только из env/User Secrets.
  - Логировать факт наличия/отсутствия прокси без вывода секретов.
  - Обновить `.gitignore`/`User Secrets` документацией.
- **Статус:** не блокер v1.1.0, но требует исправления до публичного релиза.

### KI-037 — Дублирование строк подключения
- **Приоритет:** 🟢 Low | **Статус:** Open | **Запланировано:** v1.1.x
- **Файлы:** `IIChatTools.API/appsettings.json`
- **Обнаружено:** 2026-09-16
- **Описание:** Два ключа содержат одну и ту же строку: `ConnectionStrings:DefaultConnection` и `Database:SqlServerConnectionString`. Неясно, какой из них приоритетный; при смене один может отстать от другого.
- **Решение:** определить, какой ключ используется в коде (`AppDbContext`/`DbContextOptionsExtensions`), оставить только один; второй удалить. Обновить README и appsettings.

---

### KI-038 — Локальные пути разработчиков в `appsettings.Development.json`
- **Приоритет:** 🟡 Medium | **Статус:** Documented | **Запланировано:** v1.1.x
- **Файлы:** `IIChatTools.API/appsettings.Development.json`
- **Обнаружено:** 2026-09-17
- **Описание:** `Workspace:RootPath` содержит абсолютный путь, специфичный для машины разработчика (`D:\Projects\...` у одного, `G:\AI\...` у другого). При merge с `origin/main` путь был перезаписан на чужой → `list_directory` и все FS-инструменты падали с `IOException: Устройство не готово` (диск `D:` отсутствует).
- **Решение (временное):** после merge откатили `RootPath` на локальный `G:\AI\IIChatTools\Workspace`.
- **Решение (долгосрочное, v1.1.x):**
  - Перенести `Workspace:RootPath` в **User Secrets** (`dotnet user-secrets set "Workspace:RootPath" "..."`).
  - В `appsettings.Development.json` оставить нейтральный placeholder `%USERPROFILE%\IIChatToolsWorkspace`.
  - Тот же подход — к `Browser:ProxyServer` и `Database:SqliteConnectionString`.

---

### KI-039 — Утечка прокси-credentials и email автора в git-истории
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-18 | **Устранено:** 2026-09-18
- **Описание:** В публичной истории git в открытом виде хранились реальные прокси-credentials (URL, логин, пароль) и email автора коммитов. Найдено в 13+ файлах: `appsettings*.json`, `Program.cs`, `scripts/setup/*.ps1`, `configs/NuGet*.Config`, `docs/development/*.txt`, `UPDATES/*`.
- **Действия:**
  1. Пароль прокси сменён на самом сервере (до очистки).
  2. Репозиторий сделан приватным на время очистки.
  3. `git filter-repo --replace-text` + `--mailmap` + `--invert-paths`.
  4. `git push --force --all` + `--force --tags`.
  5. KI-036 — удалён хардкод fallback из `Program.cs` и скриптов.
  6. Удалены: `UPDATES/`, `configs/NuGet1.Config`, `configs/NuGet111.Config`, `scripts/setup/download-*.ps1`, локальные SQLite-БД.
  7. Секреты перенесены в User Secrets и `$env:IICHATTOOLS_PROXY*`.
  8. GitHub Secret Scanning — «No secrets found».
- **Урок:** секреты — только через User Secrets / env / secret manager. Регулярно прогонять `git log --all -p | grep ...` перед push.

---

## v1.0.2 и ранее

### KI-001 — Неинформативное сообщение при отклонении действия
- **Приоритет:** 🟡 Medium | **Статус:** Open | **Запланировано:** v1.0.3
- **Файлы:** `test.js`, `approvals.js`
- **Решение:** Показывать причину отклонения в поле «Результат».

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

### KI-005 — Неявный `git add -A` при пустом списке файлов
- **Приоритет:** 🟠 High | **Статус:** Documented | **Запланировано:** v1.0.3
- **Файлы:** `GitAddTool.cs`
- **Причина:** Пустой `files` трактуется как `-A`. Документировано в описании инструмента.

### KI-006 — gh-инструменты не поддерживали подкаталоги
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `BaseGhTool.cs` + 6 `Gh*Tool.cs`
- **Решение:** Параметр `path` + `GhContextValidationResult` + `RunGhInDirAsync`.

### KI-007 — gh issue create требует заранее созданные метки
- **Приоритет:** 🟢 Low | **Статус:** Documented
- **Причина:** Ограничение `gh` CLI. Метки должны быть созданы заранее.

### KI-008 — Хрупкость git-истории при манипуляциях
- **Приоритет:** 🟢 Low | **Статус:** Resolved
- **Причина:** `--allow-unrelated-histories` + `reset --hard` + пересоздание веток → расхождение историй.
- **Решение:** Чистый `git clone` восстанавливает целостность.

### KI-009 — ERR_CONNECTION_RESET на сайтах с SSO
- **Приоритет:** 🟡 Medium | **Статус:** Documented
- **Причина:** Kinopoisk и подобные сайты требуют SSO-редирект, Chromium через прокси сбрасывает соединение.
- **Решение:** Использовать нейтральные сайты. Инъекция cookies — v1.1.

### KI-010 — Репозиторий раздулся до 154 МБ из-за бинарных артефактов
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Описание:** В историю Git попали `LocalPackages/*.nupkg`, `bin/`, `obj/`, `Data/*.db`, `logs/`, `Workspace/`, `nuget*.exe`. Размер репозитория достиг 154 МБ.
- **Решение:** `.gitignore` расширен; `git filter-repo --invert-paths` (три прохода); `git gc --prune=now --aggressive`; создан `.gitattributes`.
- **Результат:** размер упал с 154 МБ до 1.51 МБ.

### KI-011 — `Workspace/` попал в индекс как gitlink
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Описание:** `Workspace/users/1/git-test/` (с вложенным `.git`) попал в индекс как `mode 160000`.
- **Решение:** `git rm -r --cached Workspace/users/1/git-test` + правило `Workspace/` в `.gitignore`.

### KI-012 — Множественные `obj/` в индексе
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Решение:** `git rm -r --cached <path>/obj` для каждого проекта + правило `**/obj/`.

### KI-013 — Нереорганизованная структура репозитория
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Решение:** `docs/`, `scripts/`, `configs/`, `assets/images/`.

### KI-014 — Временные файлы отладки в корне
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Решение:** Перемещены в `docs/development/archive/` и `assets/images/`.

---

## Сводка по статусам

| Статус | Кол-во |
|--------|--------|
| Fixed (v1.0.2) | 8 |
| Fixed (v1.1.0) | 13 |
| Fixed (v1.1.1) | 2 |    <!-- KI-036, KI-039 -->
| Documented | 6 |
| Open | 4 |
| Deferred | 1 |
| Resolved | 1 |
| **Всего** | **33** |

**Расшифровка «Open»:** KI-001, KI-005, KI-036, KI-037.
**«Deferred»:** KI-022.
**«Documented»:** KI-007, KI-009, KI-032, KI-038 (и 2 устаревших).

---

## Правила ведения

1. Любая найденная проблема/ограничение → запись с номером `KI-XXX`.
2. Даже если это «не баг» — запись нужна для истории.
3. Приоритет: 🔴 Critical / 🟠 High / 🟡 Medium / 🟢 Low.
4. Статус: Open / In Progress / Fixed / Documented / Deferred / Won't Fix / Resolved.
5. Для Fixed — обязательно указывается «Исправлено в: vX.Y.Z» и список файлов.
6. Для Deferred — указывается «Запланировано: vX.Y.Z» и причина отсрочки.