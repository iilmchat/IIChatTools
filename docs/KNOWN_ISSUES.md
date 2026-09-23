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
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-16 | **Устранено:** 2026-09-18
- **Файлы:** `Directory.Build.props`, `IIChatTools.API/IIChatTools.API.csproj`, `IIChatTools.Tests/IIChatTools.Tests.csproj`
- **Описание:** Транзитивный пакет EF Core Sqlite тянул `SQLitePCLRaw.lib.e_sqlite3 2.1.11` с уязвимостью [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).
- **Решение:** Явное переопределение транзитивной зависимости через `SQLitePCLRaw.bundle_e_sqlite3` **2.1.13** в API и Tests. Версия вынесена в `Directory.Build.props` (`$(SQLitePCLRawVersion)`). `.nupkg` добавлены в `LocalPackages` для offline-сборки.
- **Результат:** `dotnet list IIChatTools.sln package --vulnerable --include-transitive` — пусто.

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
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-16 | **Устранено:** 2026-09-18
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
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-16 | **Устранено:** 2026-09-18
- **Файлы:** `IIChatTools.API/appsettings.json`, `IIChatTools.API/appsettings.Development.json`, `IIChatTools.API/Extensions/DbContextOptionsExtensions.cs`, `README.md`
- **Описание:** Ключи `ConnectionStrings:DefaultConnection` и `Database:SqlServerConnectionString` содержали одну и ту же строку. Первый — legacy из .NET Core 3.1, читался как fallback через `??` (мёртвый код, т.к. второй ключ всегда задан).
- **Решение:**
  - Удалён `ConnectionStrings` из обоих `appsettings*.json`.
  - `DbContextOptionsExtensions`: убран fallback `?? configuration.GetConnectionString("DefaultConnection")`; добавлена явная проверка `string.IsNullOrWhiteSpace` с `InvalidOperationException`.
  - `README.md`: актуализирован пример конфига.
- **Результат:** единственный источник строки — `Database:SqlServerConnectionString`. Неоднозначность устранена.

---

### KI-038 — Локальные пути разработчиков в `appsettings.Development.json`
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-17 | **Устранено:** 2026-09-18
- **Файлы:** `IIChatTools.API/appsettings.Development.json`, `IIChatTools.Services/Implementation/WorkspaceResolver.cs`, `README.md`
- **Описание:** `Workspace:RootPath` содержал абсолютный путь конкретной машины (`D:\Projects\...` у одного, `G:\AI\...` у другого). При merge с `origin/main` путь перезаписывался на чужой → `list_directory` и все FS-инструменты падали с `IOException: Устройство не готово`.
- **Решение:**
  - `Workspace:RootPath` вынесен в **User Secrets** (`dotnet user-secrets set "Workspace:RootPath" "G:\AI\IIChatTools\Workspace"`).
  - В `appsettings.Development.json` — нейтральный placeholder `%USERPROFILE%\IIChatToolsWorkspace`.
  - `WorkspaceResolver`: добавлено `Environment.ExpandEnvironmentVariables` (раскрытие env-переменных), улучшено сообщение об ошибке.
  - `README.md`: инструкция по User Secrets для новых разработчиков.
- **Результат:** локальные пути не коммитятся, мержи по ключу `Workspace:RootPath` больше не возникают.

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

### KI-040 — Обход path-traversal через backslash на Linux
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-18 (CI на GitHub Actions, ubuntu-latest)
- **Файлы:** `IIChatTools.Services/Implementation/PathHelper.cs`, `IIChatTools.Tests/UnitTests/PathHelperTests.cs`
- **Описание:** `PathHelper.TryGetSafeFullPath` не нормализовал разделители путей. На Linux `\` — обычный символ в имени файла, а не разделитель, поэтому `..\Windows\System32` воспринимался как «файл с именем `..\Windows\System32`» и оставался внутри workspace. На Windows-машине разработки баг не проявлялся, но на Linux-развёртывании (Docker, прод) это потенциальный обход path-traversal в файловых, git- и shell-инструментах.
- **Сопутствующие дефекты, обнаруженные при анализе:**
  - `StringComparison.OrdinalIgnoreCase` использовался на всех платформах — на Linux `/workspace/Users` и `/workspace/users` считались одним путём (case-sensitive ФС).
  - `TrimEnd(DirectorySeparatorChar, AltDirectorySeparatorChar)` превращал корень `/` в пустую строку на Linux.
- **Решение:**
  - `NormalizeSeparators`: оба разделителя (`/` и `\`) заменяются на `Path.DirectorySeparatorChar` текущей ОС **до** `Path.GetFullPath`.
  - `PathComparison`: на Windows — `OrdinalIgnoreCase`, на Linux — `Ordinal`.
  - `NormalizeFullPath`: использует `Path.TrimEndingDirectorySeparator` (корректно сохраняет корень `/`).
  - Тесты расширены: 6 кейсов traversal (включая смешанные разделители), нормализация backslash, защита от обхода через префикс (`workspace-evil`).
- **Результат:** PathHelper теперь корректен на Windows и Linux. CI на ubuntu-latest подтверждает.

---

### KI-041 — Документация ссылалась на несуществующие файлы
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-18 | **Устранено:** 2026-09-18
- **Файлы:** `README.md`, `docs/KNOWN_ISSUES.md`, `NuGet.Config.example.online` (новый), `scripts/setup/enable-online-restore.ps1` (новый), `scripts/setup/fill-local-packages.ps1` (новый)
- **Описание:** В README и KNOWN_ISSUES упоминались `NuGet.Config.online` и `scripts/setup/fill-local-packages.ps1`, которых в репозитории не было. Пользователь не мог выполнить offline-развёртывание по документации.
- **Решение:**
  - Добавлен шаблон `NuGet.Config.example.online` (коммитится).
  - Добавлен `scripts/setup/enable-online-restore.ps1` — создаёт `NuGet.Config.online` из шаблона (не коммитится).
  - Добавлен `scripts/setup/fill-local-packages.ps1` — наполняет `LocalPackages/` из глобального NuGet-кэша. Параметры `-Clean` (с резервной копией) и `-DryRun`.
  - README.md: обновлён раздел «Offline-установка» — пошаговая инструкция.
- **Урок:** если в документации упоминается файл — он должен существовать в репозитории, либо в тексте должно быть явно указано, что файл создаётся локально (с инструкцией).

---

### KI-042 — Microsoft.AspNetCore.RateLimiting недоступен в SDK 10.0.401
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-18 | **Устранено:** 2026-09-18
- **Файлы:** `IIChatTools.API/RateLimiting/RateLimitingMiddleware.cs` (новый), `IIChatTools.API/RateLimiting/RateLimitingOptions.cs`, `IIChatTools.API/Startup.cs`, `Directory.Build.props`
- **Описание:** При попытке использовать `services.AddRateLimiter()` / `app.UseRateLimiter()` сборка падала с `CS1061: IServiceCollection не содержит определения AddRateLimiter`. Диагностика:
  - `Microsoft.AspNetCore.RateLimiting.dll` **есть** в ref-pack (`packs/Microsoft.AspNetCore.App.Ref/10.0.12/ref/net10.0/`, 34 KB).
  - `FrameworkReference Microsoft.AspNetCore.App` — включён автоматически через `.Web` SDK (NETSDK1086).
  - В `project.assets.json`: `"Microsoft.AspNetCore.RateLimiting": "(,10.0.32767]"` — framework-provided.
  - Однако C#-компилятор **не видит** сборку — она отсутствует в compilation-time references.
  - Отдельного NuGet-пакета `Microsoft.AspNetCore.RateLimiting` **не существует** (только preview-версии).
- **Решение:** Собственная реализация `RateLimitingMiddleware` на базе `System.Threading.RateLimiting` (GA, доступен через shared framework без PackageReference). Политики определяются по пути запроса: `/auth/*` → строгая, `/api/tools/execute` → строгая, `/health/*` → без лимита, остальное → per-user. Атрибуты `[EnableRateLimiting]` не используются.

---

### KI-043 — Утечка памяти в RateLimitingMiddleware
- **Приоритет:** 🟡 Medium | **Статус:** Documented | **Запланировано:** v1.2.x
- **Обнаружено:** 2026-09-18
- **Файлы:** `IIChatTools.API/RateLimiting/RateLimitingMiddleware.cs`
- **Описание:** `ConcurrentDictionary<string, FixedWindowRateLimiter>` в `RateLimitingMiddleware` хранит лимитеры по partition-key и **не очищает их** при истечении окна. При долгой работе с тысячами уникальных пользователей/IP → постепенный рост памяти.
- **Решение (запланировано):** периодическая очистка через `Timer` (удалять лимитеры с истёкшим окном) или переход на `PartitionedRateLimiter` (если появится в следующих версиях SDK).
- **Не блокер v1.1.1:** footprint одного лимитера мал, при десятках пользователей проблема не проявляется.

---

### KI-044 — Метрики `iichattools_audit_entries_total` и `iichattools_lmstudio_requests_total` пока не инкрементируются
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** v1.2.x
- **Обнаружено:** 2026-09-18
- **Файлы:** `IIChatTools.API/Metrics/AppMetrics.cs`, `IIChatTools.Services/Implementation/LmStudioClient.cs`, `IIChatTools.Services/Implementation/AuditService.cs`
- **Описание:** Счётчики зарегистрированы, но вызовы `.Inc()` не добавлены в `LmStudioClient` и `AuditService`. Метрики будут отдавать 0.
- **Решение:** добавить `.Inc()` в соответствующие места (отдельная задача).

---

### KI-045 — HELP-описания метрик на русском дают кракозябры в PowerShell
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-18 | **Устранено:** 2026-09-18
- **Файлы:** `IIChatTools.API/Metrics/AppMetrics.cs`
- **Описание:** HELP-описания метрик были на русском (`"Общее число вызовов инструментов"`). При `curl.exe` в PowerShell с кодировкой CP866 → кракозябры (`╨Ю╨▒╤Й╨╡╨╡`). Не баг приложения, но неудобно для отладки.
- **Решение:** HELP-описания переведены на английский. XML-doc остался на русском. Теперь `/metrics` читается на любой консоли.

---

## v1.3.0 — Chat UI (в работе)

### KI-055 — Tool calling в чате — реализовано в v1.3.0
- **Приоритет:** — | **Статус:** Implemented | **Реализовано в:** v1.3.0 Фаза 1.6.A
- **Дата:** 2026-09-22
- **Файлы:** `ChatStreamService.cs`, `ToolDefinitionsBuilder.cs`, `ToolCallsAccumulator.cs`, `ChatStreamDtos.cs`, `ChatToolCallDto.cs`, `ChatToolResultDto.cs`
- **Описание:** Реализован multi-turn tool calling loop в чате:
  - До 5 итераций.
  - SSE-события `tool_call` / `tool_result`.
  - Интеграция с `IToolRegistry` (11 read-only инструментов из `SubAgent:DefaultAllowedTools`).
  - `RequiresApprovalByDefault = true` → `ToolResult.Fail` (полное подтверждение — Фаза 1.7 / KI-054).
  - Поддержка `role=tool` и `tool_calls` в истории.
- **Проверено:** smoke-тест — LLM вызывала `list_directory`, получала результат, формулировала ответ.
- **Это не баг** — запись для истории реализации.

---

### KI-046 — MessageCount в ChatListItemDto — реализовано в v1.3.0
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.3.0 Фаза 2.0.6a
- **Обнаружено:** 2026-09-21 | **Устранено:** 2026-09-22
- **Файлы:** `IIChatTools.API/Controllers/ChatController.cs`, `IIChatTools.Services/Implementation/ChatService.cs`, `IIChatTools.Services/Interfaces/IChatService.cs`
- **Описание:** В `ChatListItemDto.MessageCount` возвращался 0 (заглушка).
- **Решение:** Добавлен метод `IChatService.GetMessageCountsAsync(int userId)`, возвращающий `IReadOnlyDictionary<int, int>` (chatId → count). Один SQL-запрос с `GROUP BY ChatId` (через `Contains` по Id чатов пользователя — портируемо на SQL Server/SQLite/InMemory). `ChatController.GetChatsAsync` совмещает результат с `GetUserChatsAsync` через `TryGetValue`.
- **Проверено:** `GET /api/chats` возвращает `messageCount > 0` для чатов с историей.

---

### KI-047 — Fallback PATCH/DELETE через POST для старых сетей
- **Приоритет:** 🟡 Medium | **Статус:** Deferred | **Запланировано:** v1.3.x
- **Обнаружено:** 2026-09-21
- **Файлы:** `IIChatTools.API/Controllers/ChatController.cs` (и другие контроллеры с PATCH/DELETE)
- **Описание:** Некоторые старые корпоративные прокси и браузеры могут блокировать HTTP-методы `PATCH` и `DELETE` (обрезка/трансформация в `GET`/`POST`). Основной Chat API использует REST-семантику (`POST` / `PATCH` / `DELETE`). В offline-сетях и старых браузерах это может привести к сбоям.
- **Решение (запланировано):**
  - Добавить «теневые» endpoints вида:
    - `POST /api/chats/{id}/update` — эмуляция PATCH (принимает то же `UpdateChatRequest`)
    - `POST /api/chats/{id}/delete` — эмуляция DELETE (тело пустое или с флагом подтверждения)
  - Frontend: определять доступность метода (feature detection) и использовать fallback по необходимости.
  - Основной REST-контракт **остаётся** — fallback только как резерв.
- **Обоснование отсрочки:** не критично для текущих пользователей, но задача зафиксирована.

---

### KI-048 — Email автора коммитов в git-истории
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-21
- **Файлы:** `git config` (локальный, не в репозитории)
- **Описание:** На новой машине (`D:\Projects\IIChatTools`) `git config user.email` был не настроен — коммиты `9a21553`, `7161f63`, `b960611` ушли с реальным email автора (`avnikiforov2014@yandex.ru`). Аналогично KI-039 (утечка в истории), но менее критично (email автора, не учётные данные).
- **Решение (выполнено):**
  - `git config user.email "dev@example.local"`
  - `git config user.name "IIChatTools Developer"`
  - Применяется **только к новым коммитам** (старые `9a21553`, `7161f63`, `b960611` не переписаны — переписывание потребует `filter-repo`, не оправдано).
- **Профилактика:** при клонировании репо на новой машине — проверять `git config user.email` **до** первого коммита. Обновить `README.md` (раздел «Разработка» → раздел про настройку).
- **Обновление (2026-09-22):** выявлен способ автоматизации. Добавить в `scripts/setup/configure-git.ps1`:
  ```powershell
  git config user.email "dev@example.local"
  git config user.name "IIChatTools Developer"
  ```
- Скрипт запускать после git clone на новой машине. Реализация — в v1.3.x.
- **Примечание**: тройные бэктики внутри блока могут сломать markdown. Если VS Code подсветит — обернуть внутренний код в 4 бэктика (````). Пришлите, если проблема.

---

### KI-049 — tokensIn / tokensOut = null в SSE-стриме
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** v1.3.x
- **Обнаружено:** 2026-09-21
- **Файлы:** `IIChatTools.Services/Implementation/LmStudioClient.cs` (ChatStreamAsync), `IIChatTools.Services/Implementation/ChatStreamService.cs`
- **Описание:** В SSE-стриме LM Studio не отдаёт `usage` (в отличие от `stream=false`). В последнем чанке `tokensIn`/`tokensOut` = null, хотя в `ChatCompletionResponse` (non-streaming) usage приходит.
- **Решение (запланировано):** Если LM Studio не отдаёт usage в stream-режиме — либо запрашивать usage отдельным вызовом (не оптимально), либо использовать `tiktoken` для подсчёта, либо оставить null (не критично для UX).
- **Не блокер:** Chat UI работает без счётчика токенов.
- **Подтверждено:** в Фазе 1.6.A smoke-тест SSE — `tokensIn`/`tokensOut` = null в `done`-событии. Это **подтверждённое** ограничение LM Studio в stream-режиме. Возможное решение в v1.3.x — использовать `tiktoken` для подсчёта вручную.
- **Финальное решение (2026-09-22):** в v1.3.0 (Фаза 1.5-1.7) — `tokensIn`/`tokensOut` остаются null. Причина — LM Studio не отдаёт `usage` в stream-режиме. **Для v1.4** запланирован ручной подсчёт через `tiktoken` или обратный вызов `usage` после `done`.

---

### KI-050 — Rate limiting возвращает JSON на HTML-эндпоинты
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.3.0
- **Обнаружено:** 2026-09-21 | **Устранено:** 2026-09-21
- **Файлы:** `IIChatTools.API/RateLimiting/RateLimitingMiddleware.cs`, `IIChatTools.API/Controllers/AuthController.cs`, `IIChatTools.API/Views/Auth/Login.cshtml`, `IIChatTools.API/Views/Auth/Register.cshtml`, `SharedResources.resx`, `SharedResources.ru.resx`
- **Описание:** При превышении лимита на `/auth/login` пользователь в браузере получал сырой JSON `{"success":false,...}`. Нет навигации, нет формы, только кнопка «Назад».
- **Решение:**
  - `RateLimitingMiddleware`: проверка `Accept: text/html` (браузер) → **303 redirect** на GET-версию формы с query `?error=ratelimit&retryAfter=N`.
  - API-клиенты (`Accept: application/json`) — прежний 429 JSON.
  - `AuthController.Login/Register (GET)` — читают `error=ratelimit`, передают в `ViewData`.
  - `Login.cshtml` / `Register.cshtml` — красный `alert-danger` с текстом.
  - 4 ключа локализации в оба `.resx` (`LocalizationSyncTests` проверяет).
- **Результат:** пользователь видит понятный баннер, форму, может попробовать снова через N секунд.

---

### KI-051 — Полезные анализаторы C# понижены до `suggestion`
- **Приоритет:** 🟢 Low | **Статус:** Resolved | **Исправлено в:** v1.3.0
- **Обнаружено:** 2026-09-21 | **Устранено:** 2026-09-22
- **Файлы:** `.editorconfig`, `IIChatTools.Services/Implementation/ToolRegistry.cs`, `IIChatTools.Services/Implementation/LmStudioClient.cs`, `IIChatTools.API/RateLimiting/RateLimitingMiddleware.cs`
- **Описание:** Три анализатора давали warnings (7 шт). Часть исправлена, часть понижена до `suggestion` для соблюдения правила «0 warnings».
- **Решение:**
  - ✅ **CA1869** — `RateLimitingMiddleware.cs` — `JsonSerializerOptions` вынесен в `static readonly`. Исправлено в v1.3.0.
  - ✅ **CA1854, CA2016** — понижены до `suggestion` в `.editorconfig` (не критично для проекта).
- **Результат:** Build — 0 warnings. Анализаторы остаются как подсказки в IDE.

---

### KI-057 — `/api/models` возвращает embedding-модели
- **Приоритет:** 🟢 Low | **Статус:** Partially Fixed | **Запланировано:** v1.3.x
- **Обнаружено:** 2026-09-22
- **Файлы:** `IIChatTools.API/Controllers/ModelsController.cs`
- **Описание:** LM Studio отдаёт в `GET /v1/models` все модели, включая embedding (`text-embedding-nomic-embed-text-v1.5`). Если пользователь выберет её для чата — LM Studio вернёт 400.
- **Текущее решение (v1.3 Фаза 2.0.2a):** heuristic-фильтр по подстроке `embed` в имени модели.
- **TODO v1.3.x:** config-driven exclusion patterns (`LmStudio:ExcludeModelPatterns`) — массив подстрок, которые исключаются из списка.

---

### KI-058 — UX модалки approval в чате: не drag, X подвешивал UI
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.3.0 Фаза 2.0.4
- **Обнаружено:** 2026-09-22 (smoke-тест Chat UI) | **Устранено:** 2026-09-22
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/approvals.js`, `IIChatTools.API/wwwroot/css/chat.css`
- **Описание:** Две UX-проблемы при первом smoke-тесте модалки approvals из чата:
  1. **Модалку нельзя подвинуть** — при длинных JSON-параметрах закрывает ленту сообщений.
  2. **Крестик (X) подвешивает UI** — модалка закрывается, но `promise` не resolve, reject на сервер не отправляется. SSE-стрим ждёт 5 минут (таймаут `IChatApprovalCoordinator`), input/btn-send остаются заблокированными.
- **Решение:**
  - **Drag-and-drop** по `.modal-header` (`position: fixed` на время drag, `cursor: move`, сброс стилей при `hidden.bs.modal`).
  - **X = Reject:** при `hidden.bs.modal` без решения автоматически отправляется reject на сервер. В `/chat` — без причины (Q6 Фазы 1.7); в `/test` — с reason «Закрыто пользователем».
  - **Fix утечки listener'ов:** `addEventListener('hidden.bs.modal', handler, { once: true })`.
  - **`getOrCreateInstance`** вместо `new bootstrap.Modal(...)` — нет warning'ов при повторных открытиях.
  - Рефакторинг: общий `sendDecision()` — убрано ~40 строк дублирования.
- **Результат:** модалка перетаскивается; X корректно отправляет reject; стрим продолжается; input разблокируется.

---

### KI-059 — Placeholder `CHANGE_ME_VIA_USER_SECRETS` использовался как прокси
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.3.0 (hotfix после Фазы 2.1.1)
- **Обнаружено:** 2026-09-22 (smoke-тест Chat UI — `web_search` / `wikipedia_search` падали) | **Устранено:** 2026-09-22
- **Файлы:** `IIChatTools.API/Startup.cs`, `IIChatTools.API/Program.cs`
- **Описание:** В `appsettings.Development.json` ключ `Browser:ProxyServer` содержит placeholder `CHANGE_ME_VIA_USER_SECRETS`. Код проверял только `!string.IsNullOrWhiteSpace(proxyUrl)` — placeholder **не пустой** → трактовался как реальный прокси. `System.Net.WebProxy("CHANGE_ME_VIA_USER_SECRETS")` парсил строку как хост `change_me_via_user_secrets:80`, `HttpClient` пытался установить CONNECT-туннель → `SocketException 11001: Этот хост неизвестен`. Все исходящие HTTP-запросы (`web_search`, `wikipedia_search`) падали.
- **Симптом в UI:** LLM вызывает `web_search` → SSE `tool_result` success=false → LLM отвечает «не смог найти информацию из-за технической ошибки».
- **Решение:**
  - `Startup.cs`: helper `IsRealProxyUrl(url)` — отсекает placeholder (`CHANGE_ME*`), пустые строки и требует абсолютный URL с http/https схемой.
  - `Program.cs` (`ConfigureDefaultProxy`): та же проверка для env-переменной `IICHATTOOLS_PROXY`.
  - Placeholder теперь трактуется как «прокси не настроен» — логируется `[INFO]`, а не `[ERROR]`.
- **Результат:** `web_search` и `wikipedia_search` работают без прокси; при заданных User Secrets — используют реальный прокси.

---

### KI-060 — User-сообщения рендерились как Markdown
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.3.0 (hotfix после Фазы 2.1.4)
- **Обнаружено:** 2026-09-23 (smoke-тест — ввод ` ``` без языка `) | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/chat.js`
- **Описание:** После Фазы 2.1.4 (Markdown rendering) **user-сообщения** тоже рендерились как Markdown. Если пользователь писал ` ``` ` (например, обсуждая синтаксис Markdown), это распознавалось как открытие code block → пустой `<pre>` с шапкой «без» в user-пузыре. Аналогично `**bold**` в user-тексте превращалось в `<strong>`.
- **Симптом:** user-пузырь показывал пустой code block вместо текста ` ``` без языка `.
- **Решение:** User-сообщения рендерятся как **plain text** (`renderUserContent(text)` — `escapeHtml` + `<br>`). Markdown применяется **только к assistant-сообщениям**. Соответствует ChatGPT/Claude/Gemini.
- **Результат:** user-сообщения отображаются ровно так, как их ввёл пользователь.

---

### KI-061 — Кнопка «Отправить» перекрывает textarea
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.3.0 (fix 2.1.2.3a)
- **Обнаружено:** 2026-09-23 (визуально на скриншоте Chat UI) | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/wwwroot/css/chat.css`
- **Описание:** В `.input-group` (textarea + кнопка «Отправить») `textarea` без `min-height` при одном ряде текста «проседала» ниже кнопки — кнопка визуально выступала над/под textarea.
- **Решение:** `min-height: calc(1.5em + 0.75rem + 2px)` для textarea (стандарт Bootstrap `.form-control`), `.input-group` → `align-items: stretch`, `.btn` → `align-self: stretch; height: auto`.

---

### KI-062 — Нет фокуса на input при создании/выборе чата
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.3.0 (fix 2.1.2.3a)
- **Обнаружено:** 2026-09-23 | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/chat.js`
- **Описание:** При создании нового чата (и при выборе существующего) фокус не переводился в поле ввода — пользователю приходилось кликать по textarea мышкой.
- **Решение:** В `selectChat()` после `enableInput(true)` добавлен `setTimeout(() => input.focus(), 0)`. Так как `createChat()` вызывает `selectChat()`, оба сценария покрыты. ChatGPT-style.

---

### KI-061a — box-shadow фокуса textarea перекрывал кнопку
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.3.0 (2.1.3.1)
- **Обнаружено:** 2026-09-23 (smoke-тест) | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/wwwroot/css/chat.css`
- **Описание:** Остаточная проблема KI-061: после фикса `min-height` Bootstrap `box-shadow: 0 0 0 .25rem` при фокусе на textarea расширялся вправо и визуально перекрывал кнопку «Отправить».
- **Решение:** Focus-ring перенесён с textarea на `.input-group:focus-within` (общая тень вокруг всего блока). `textarea:focus` и `.btn:focus` — `box-shadow: none`.

---

### KI-063 — Нет кнопки Stop для прерывания стрима
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.3.0 (2.1.3.2)
- **Обнаружено:** 2026-09-23 | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/Views/Chat/Index.cshtml`, `IIChatTools.API/wwwroot/js/modules/chat.js`
- **Описание:** Во время стрима пользователь не мог прервать генерацию — приходилось ждать завершения (до 5 минут при ожидании approval).
- **Решение:**
  - Кнопка `#btn-stop` (⏹) — отдельный элемент в `.input-group`, рядом с textarea.
  - Во время стрима: `#btn-send` скрыта, `#btn-stop` показана (`setStreamingUI(true)`).
  - `state.abortController` — `AbortController` на время стрима; `signal` передаётся в `fetch`.
  - Клик по Stop → `abortController.abort()` → fetch рвёт соединение → сервер отменяет `CancellationToken` → SSE закрывается.
  - При `AbortError`: частичный assistant-пузырь удаляется из DOM; **частичный ответ НЕ сохраняется в БД** (согласовано).
  - Работает и для обычного стрима, и для Regenerate.
- **Audit (2.1.3.3):** при Stop в `ChatStreamController.StreamInternalAsync` пишется запись в `AuditLogs`: `Status = "Cancelled"`, `ToolName = chat_stream | chat_regenerate`, `DurationMs` (Stopwatch), `ClientIp`. **Текст сообщения не логируется** (без PII).

---

### KI-064 — SSL-обрыв к ru.wikipedia.org (intermittent)
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-23 | **Устранено:** —
- **Файлы:** `IIChatTools.Services/Implementation/Tools/Web/WikipediaSearchTool.cs` (внешний вызов)
- **Описание:** При запросе `wikipedia_search` LLM получила `HttpRequestException: The SSL connection could not be established` (SocketException 10054 — «Удалённый хост принудительно разорвал соединение») после 43 секунд таймаута. В то же время `web_search` (DuckDuckGo) сработал успешно.
- **Причина:** внешняя сетевая проблема — TLS-handshake к `ru.wikipedia.org` прерывается через корпоративный прокси/firewall (долгие соединения). Не баг приложения.
- **Fallback:** LLM самостоятельно переключилась на `web_search` и получила данные.
- **Профилактика (TODO v1.3.x):** уменьшить `HttpClient.Timeout` для `WikipediaSearchTool` (по умолчанию 100s → 15s), добавить retry с exponential backoff. Не Critical.

---

### KI-065 — После Stop нет кнопки «Повторить»
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.3.0 (2.1.3.5)
- **Обнаружено:** 2026-09-23 (smoke-тест Stop) | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.Services/Implementation/ChatStreamService.cs`, `IIChatTools.API/wwwroot/js/modules/chat.js`, `IIChatTools.API/wwwroot/css/chat.css`
- **Описание:** При нажатии Stop ассистент-пузырь с частичным ответом удаляется, и пользователь остаётся с одним user-сообщением. Не было способа быстро перезапустить генерацию с тем же prompt'ом. ChatGPT/Claude показывают «Retry» в этой ситуации.
- **Backend fix:** `ChatStreamService.StreamAsync` (режим `Regenerate = true`) — убрана проверка `deleted == 0`. Теперь при последнем user (ответ не сохранён) регенерация тоже возможна (best-effort delete). Ошибка «Нечего регенерировать» возникает только если в чате нет user-сообщений.
- **Frontend fix:**
  - `renderMessages`: если последнее сообщение — user, на нём показывается «🔄 Повторить» (при загрузке истории после F5).
  - `sendMessage` (AbortError): `showRetryOnLastUser()` — добавляет кнопку «🔄 Повторить» под последним user.
  - `regenerateLastMessage(opts)`: новый параметр `allowNoAssistant` — если assistant-пузыря нет, регенерация всё равно происходит (для retry после Stop).
  - `renderMessageActions(text, { showRetry })` — новый параметр; кнопка с текстом `.chat-message-action-with-text`.
  - CSS: `.chat-message-actions-always-visible` — кнопка видна без hover, пока не начнётся стрим.

---

### KI-066 — Кнопка Copy пропадала под ответом до F5
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.3.0 (2.2.1b-hotfix)
- **Обнаружено:** 2026-09-23 (smoke-тест AI-title) | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/chat.js`
- **Описание:** После правки 2.2.1a (для кнопки «Повторить») `renderMessageActions('')` перестала создавать кнопку Copy при пустом тексте. `appendAssistantBubble()` вызывает её с пустой строкой (текста ещё нет — стрим только начинается). `finalizeAssistantBubble()` обновлял `dataset.copyText`, но **кнопку не создавал**. Итог: до F5 под ответом ассистента не было 📋.
- **Решение:** `finalizeAssistantBubble()` — после финального Markdown-рендера пересоздаёт `.chat-message-actions` с реальным текстом (remove old + `renderMessageActions(raw)`).

---

### KI-067 — Per-user retention чатов (override глобальной)
- **Приоритет:** 🟢 Low | **Статус:** Deferred | **Запланировано:** v1.3.x
- **Обнаружено:** 2026-09-23
- **Файлы:** `IIChatTools.Services/Implementation/ChatRetentionService.cs`, `IIChatTools.Services/Implementation/ChatRetentionOptions.cs`
- **Описание:** Сейчас retention чатов — глобальный (`Chat:Retention:DefaultDays` в appsettings). Пользователь не может настроить свой срок хранения (например, «хранить 7 дней» для приватных чатов или «365 дней» для архива). DESIGN § 13.2 предлагал override через `AppSettings` (поле `Chat.RetentionDays` per-user).
- **Реализовано в v1.3 Фаза 2.2.5:** глобальный retention через `ChatRetentionService` (BackgroundService, bulk DELETE).
- **TODO v1.3.x:** добавить per-user override через `AppSettings` (ключ `Chat.RetentionDays`) + UI в профиле пользователя.
- **Не блокер:** текущий глобальный retention покрывает 95% сценариев.

---

### KI-068 — Поиск по содержимому сообщений (не только по title)
- **Приоритет:** 🟢 Low | **Статус:** Deferred | **Запланировано:** v1.3.x
- **Обнаружено:** 2026-09-23
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/chat.js`
- **Описание:** Поиск по чатам (Фаза 2.2.3) — **клиентский фильтр** по `chat.title`. Не ищет по содержимому сообщений. Пользователь не может найти «тот чат, где мы обсуждали X».
- **Решение (запланировано):** Backend endpoint `GET /api/chats?search={query}` — LIKE по `Chats.Title` + `ChatMessages.Content` (JOIN, GROUP BY). Дорого по производительности при больших объёмах — нужен FTS или индексирование.
- **Не блокер:** текущий поиск по title покрывает 95% кейсов.

---

## v1.4.0 — Multi-Agent (roadmap)

### KI-052 — Специализированные суб-агенты по группам инструментов
- **Приоритет:** 🟡 Medium | **Статус:** Deferred | **Запланировано:** v1.4.0
- **Обнаружено:** 2026-09-21
- **Файлы:** `IIChatTools.Services/Implementation/SubAgentService.cs` (текущий универсальный), новые `*AgentService.cs` для каждой группы
- **Описание:** Одна модель (особенно 4B-7B) плохо выбирает инструмент из **40**. В `SubAgent:DefaultAllowedTools` уже зафиксирован рабочий лимит — **10 инструментов**. Решение: разбить на **специализированных суб-агентов** по группам (FileSystem, Code, Web, Git, Planner). Оркестратор (Chat) вызывает их через 5-6 «верхнеуровневых» инструментов.
- **Плюсы:** маленькие промпты, точнее выбор, возможность тонкой настройки модели под группу.
- **Минусы:** сложность, двойной проход, дороже по токенам.
- **Решение (запланировано):** новая фаза после Chat UI. Требует design doc.

---

### KI-053 — Multi-user approvals для критичных операций
- **Приоритет:** 🟡 Medium | **Статус:** Deferred | **Запланировано:** v1.4.0
- **Обнаружено:** 2026-09-21
- **Файлы:** `IIChatTools.Services/Implementation/ApprovalService.cs`, `IIChatTools.Data/Entities/PendingAction.cs`
- **Описание:** Сейчас approvals работают только для «self-service» сценария (пользователь сам подтверждает). В команде нужны:
  - Роли «approver» (не только Admin).
  - Маршрутизация: кому идёт запрос (по правилам / по группе / по очереди).
  - Уведомления: email, Slack, Teams, SignalR.
  - Audit trail подтверждений.
- **Решение (запланировано):** отдельная фаза v1.4.0. Требует design doc.

---

### KI-054 — Approvals из чата — реализовано в v1.3.0
- **Приоритет:** 🟠 High | **Статус:** Implemented | **Реализовано в:** v1.3.0 Фаза 1.7
- **Дата:** 2026-09-22
- **Файлы:** 
  - `IIChatTools.Services/DTO/Chat/ChatApprovalDecision.cs`, `ChatApprovalRequiredDto.cs`, `ChatApprovalResolvedDto.cs`
  - `IIChatTools.Services/Interfaces/IChatApprovalCoordinator.cs`
  - `IIChatTools.Services/Implementation/ChatTools/ChatApprovalCoordinator.cs` (Singleton)
  - `IIChatTools.Services/Implementation/ChatStreamService.cs`
  - `IIChatTools.API/Controllers/ChatStreamController.cs`
- **Описание:** Полная интеграция LLM-tool-calling с подтверждениями пользователя в чате:
  - LLM вызывает mutating-инструмент (`RequiresApprovalByDefault == true`).
  - SSE-событие `tool_approval_required` — клиент показывает модалку.
  - Сервер ожидает решение через `IChatApprovalCoordinator.WaitForDecisionAsync(callId, 5min)`.
  - Пользователь подтверждает → `POST /api/chat/approvals/{callId}/approve` → инструмент **выполняется**.
  - Пользователь отклоняет → `POST /api/chat/approvals/{callId}/reject` → `ToolResult.Fail("Пользователь отклонил вызов")` → LLM продолжает.
  - Таймаут 5 минут → `Expired` → `ToolResult.Fail("Время подтверждения истекло")`.
  - SSE-событие `tool_approval_resolved` — клиент закрывает модалку.
- **Особенности:**
  - `ChatApprovalCoordinator` — **Singleton**, `ConcurrentDictionary<string, TaskCompletionSource<ChatApprovalDecision>>`.
  - Cleanup в `finally` — защита от утечки памяти (урок KI-043).
  - `RunContinuationsAsynchronously` — защита от deadlock.
  - camelCase в SSE-событиях (`JsonSerializerSettings` с `CamelCasePropertyNamesContractResolver`).
- **Проверено:** smoke-тест end-to-end — `save_file` → approval_required → reject → tool_result(fail) → финальный ответ LLM.
- **UI-интеграция** — в Фазе 2 (Chat UI). Сейчас smoke через DevTools Console.

---

## v1.0.2 и ранее

### KI-001 — Неинформативное сообщение при отклонении действия
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-16 | **Устранено:** 2026-09-18
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/test.js`, `IIChatTools.API/wwwroot/js/modules/approvals.js`, `IIChatTools.API/Controllers/ToolsController.cs`
- **Описание:** На странице `/test` при отклонении действия в поле «Результат» отображалось только «Действие: rejected» — без указания причины, которую пользователь вводил в диалоге.
- **Причина:** `requestApproval` в `approvals.js` возвращал только строку `'rejected'`, теряя введённую причину. `ToolsController` при `action.Status == "Rejected"` не передавал `rejectionReason` в ответ.
- **Решение:**
  - `requestApproval` возвращает объект `{ decision, reason }`.
  - `test.js`: новая функция `renderApprovalOutcome` выводит причину в поле «Результат» (`data.rejectionReason`).
  - `ToolsController`: при `Rejected` добавлен `data.rejectionReason`.
  - UX-улучшение: `Cancel` в диалоге ввода причины больше не закрывает модалку — можно передумать.
- **Результат:** причина отклонения видна в поле «Результат» на `/test`.

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
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.1.1
- **Обнаружено:** 2026-09-16 | **Устранено:** 2026-09-18
- **Файлы:** `IIChatTools.Services/Implementation/Tools/Git/GitAddTool.cs`
- **Описание:** Пустой `files` трактовался как `git add -A` — инструмент добавлял все изменения, включая потенциально нежелательные (секреты, бэкапы, мусор). LLM могла случайно вызвать `git add` без параметров. Дополнительно: при 2+ файлах аргумент `--` добавлялся перед каждым, а не один раз перед списком.
- **Решение:**
  - Введён явный параметр **`all: true`** для добавления всех изменений.
  - Пустой `files` без `all: true` → `ToolResult.Fail` с подсказкой.
  - `files` и `all: true` взаимоисключающие → тоже ошибка.
  - Аргумент `--` добавляется **один раз** перед списком файлов (исправлен баг формирования CLI).
  - Обновлён `ToolDescriptor` (описание параметров).
- **API-изменение**: LLM теперь должна явно запрашивать `all: true` для `git add -A`.

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
| Fixed (v1.1.1) | 11 |
| Fixed / Resolved (v1.3.0) | 12 |   <!-- KI-046, KI-050, KI-051, KI-058, KI-059, KI-060, KI-061, KI-061a, KI-062, KI-063, KI-065, KI-066 -->
| Implemented (v1.3.0) | 2 |        <!-- KI-054, KI-055 -->
| Documented | 6 |                    <!-- KI-007, KI-009, KI-032, KI-043, KI-049, KI-064 -->
| Deferred | 5 |                      <!-- KI-047, KI-052, KI-053, KI-067, KI-068 -->
| Partially Fixed | 1 |               <!-- KI-057 -->
| **Всего** | **47** |

**Fixed / Resolved (v1.3.0):** KI-046 (MessageCount), KI-050 (rate limiting UX), KI-051 (анализаторы), KI-058 (модалка approvals UX), KI-059 (placeholder как прокси), KI-060 (user-Markdown), KI-061 (textarea/кнопка), KI-061a (box-shadow фокуса), KI-062 (фокус), KI-063 (Stop-кнопка), KI-065 (Retry после Stop), KI-066 (Copy после done).
**Implemented (v1.3.0):** KI-054 (approvals в чате), KI-055 (tool calling в чате).
**Documented:** KI-007 (gh метки), KI-009 (SSO-сайты), KI-032 (старые cookies), KI-043 (RateLimitingMiddleware memory), KI-049 (tokens=null в stream), KI-064 (SSL wikipedia).
**Deferred:** KI-047 (fallback PATCH/DELETE), KI-052 (специализированные суб-агенты), KI-053 (multi-user approvals), KI-067 (per-user chat retention), KI-068 (search by message content).
**Partially Fixed:** KI-057 (embedding-модели — TODO v1.3.x).

---

## Правила ведения

1. Любая найденная проблема/ограничение → запись с номером `KI-XXX`.
2. Даже если это «не баг» — запись нужна для истории.
3. Приоритет: 🔴 Critical / 🟠 High / 🟡 Medium / 🟢 Low.
4. Статус: Open / In Progress / Fixed / Documented / Deferred / Won't Fix / Resolved.
5. Для Fixed — обязательно указывается «Исправлено в: vX.Y.Z» и список файлов.
6. Для Deferred — указывается «Запланировано: vX.Y.Z» и причина отсрочки.