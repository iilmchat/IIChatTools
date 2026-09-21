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

### KI-046 — MessageCount в ChatListItemDto всегда 0
- **Приоритет:** 🟢 Low | **Статус:** Open | **Запланировано:** v1.3.0
- **Обнаружено:** 2026-09-21
- **Файлы:** `IIChatTools.API/Controllers/ChatController.cs`, `IIChatTools.Services/Implementation/ChatService.cs`
- **Описание:** В `ChatListItemDto.MessageCount` возвращается 0 (заглушка) — для sidebar не критично, но приятнее показывать число сообщений в чате.
- **Решение:** Добавить в `IChatService` метод `GetMessageCountsAsync(int userId)`, возвращающий `Dictionary<int, int>` (chatId → count). Один SQL-запрос `GROUP BY ChatId`.
- **Не блокер:** UI работает без счётчика.

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

---

### KI-049 — tokensIn / tokensOut = null в SSE-стриме
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** v1.3.x
- **Обнаружено:** 2026-09-21
- **Файлы:** `IIChatTools.Services/Implementation/LmStudioClient.cs` (ChatStreamAsync), `IIChatTools.Services/Implementation/ChatStreamService.cs`
- **Описание:** В SSE-стриме LM Studio не отдаёт `usage` (в отличие от `stream=false`). В последнем чанке `tokensIn`/`tokensOut` = null, хотя в `ChatCompletionResponse` (non-streaming) usage приходит.
- **Решение (запланировано):** Если LM Studio не отдаёт usage в stream-режиме — либо запрашивать usage отдельным вызовом (не оптимально), либо использовать `tiktoken` для подсчёта, либо оставить null (не критично для UX).
- **Не блокер:** Chat UI работает без счётчика токенов.

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
- **Приоритет:** 🟢 Low | **Статус:** In Progress | **Запланировано:** v1.3.x
- **Обнаружено:** 2026-09-21
- **Файлы:** `.editorconfig`, `IIChatTools.Services/Implementation/ToolRegistry.cs`, `IIChatTools.Services/Implementation/LmStudioClient.cs`, `IIChatTools.API/RateLimiting/RateLimitingMiddleware.cs`
- **Описание:** Три анализатора дают warnings. Часть исправлена.
  - ✅ **CA1869** — `RateLimitingMiddleware.cs:113` — `JsonSerializerOptions` теперь в `static readonly`. **Fixed.**
  - ⏳ **CA1854** — `ToolRegistry.cs:41` — `ContainsKey` + индексатор → `TryGetValue`. Запланировано.
  - ⏳ **CA2016** — `LmStudioClient.cs` (5 мест) — `cancellationToken` не передаётся в `ReadAsStringAsync` и др. Запланировано.
- **Решение:** Продолжить починку, вернуть `severity = warning`.

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
| Fixed (v1.3.0) | 1 |   <!-- +KI-050 -->
| Open | 3 |             <!-- было 4, -KI-050 -->
| In Progress | 1 |      <!-- +KI-051 -->
| Documented | 10 | <!--  +1 (KI-049) -->
| Open | 3 |      <!-- было 0, +KI-046, +1 KI-050 +KI-047 (Deferred) -->
| Deferred | 1 |  <!-- было 0 -->
| Resolved | 1 |
| **Всего** | **33** |

**«Fixed (v1.1.1)»:** KI-001, KI-005, KI-022, KI-036, KI-037, KI-038, KI-039, KI-040, KI-041, KI-042, KI-045.
**«Open»:** (пусто).
**«Deferred»:** (пусто).
**«Documented»:** KI-007, KI-009, KI-032, KI-043, KI-044 (и 2 устаревших из v1.0.x).

---

## Правила ведения

1. Любая найденная проблема/ограничение → запись с номером `KI-XXX`.
2. Даже если это «не баг» — запись нужна для истории.
3. Приоритет: 🔴 Critical / 🟠 High / 🟡 Medium / 🟢 Low.
4. Статус: Open / In Progress / Fixed / Documented / Deferred / Won't Fix / Resolved.
5. Для Fixed — обязательно указывается «Исправлено в: vX.Y.Z» и список файлов.
6. Для Deferred — указывается «Запланировано: vX.Y.Z» и причина отсрочки.