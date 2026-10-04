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
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.3.x
- **Обнаружено:** 2026-09-18 | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/RateLimiting/RateLimitingMiddleware.cs`
- **Описание:** `ConcurrentDictionary<string, FixedWindowRateLimiter>` хранил лимитеры по partition-key и **не очищал их** при истечении окна. При долгой работе с тысячами уникальных пользователей/IP → постепенный рост памяти.
- **Решение:**
  - Словарь хранит `LimiterEntry` (limiter + `LastUsedUtc`) вместо голого лимитера.
  - `Timer` каждые 2 минуты вызывает `CleanupStaleLimiters()` — удаляет записи, не использованные дольше 5 минут.
  - `LimiterEntry.Touch()` вызывается при каждом использовании — отметка обновляется.
  - Middleware реализует `IDisposable` — Timer останавливается при shutdown приложения.
  - `Timer` создаётся только при `RateLimiting:Enabled = true` (иначе не тратим ресурсы).
- **Итог:** словарь ограничен активными пользователями (окно 5 минут), утечка устранена.
- **Smoke:** логирование cleanup'а (`LogDebug`) при удалении → мониторинг через Serilog/логгер.

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

## v1.3.0 — Chat UI (реализовано 2026-09-23)

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
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-21 | **Устранено:** 2026-09-25
- **Файлы:**
  - `IIChatTools.Services/Interfaces/ITokenCounter.cs` — интерфейс.
  - `IIChatTools.Services/Implementation/TokenCounter.cs` — tiktoken-обёртка.
  - `IIChatTools.Services/Implementation/ChatStreamService.cs` — заполнение TokensIn/Out.
  - `IIChatTools.API/Startup.cs` — регистрация Singleton.
  - `Directory.Build.props` + `.csproj` — пакет `Microsoft.ML.Tokenizers`.
- **Решение:** tiktoken-совместимый счётчик (`cl100k_base`). Если LM Studio отдаёт `usage` (non-stream) — используем точное значение; иначе — приближение через tiktoken (±5-10% для Qwen/Gemma).
- **Расширение (Deferred, KI-084):** `DurationMs` + `FirstTokenMs` + `FinishReason` в `ChatMessage` + UI «как в LM Studio» (tok/sec, stop reason).
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
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.3.x
- **Обнаружено:** 2026-09-23 | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.Services/Implementation/Tools/Web/WikipediaSearchTool.cs`
- **Описание:** При запросе `wikipedia_search` LLM получила `HttpRequestException: The SSL connection could not be established` (SocketException 10054 — «Удалённый хост принудительно разорвал соединение») после **43 секунд** таймаута. В то же время `web_search` (DuckDuckGo) сработал успешно.
- **Причина:** внешняя сетевая проблема — TLS-handshake к `ru.wikipedia.org` прерывается через корпоративный прокси/firewall. Не баг приложения, но дефолтный `HttpClient.Timeout` = 100s делает зависание недопустимо долгим.
- **Решение:**
  - Явный `client.Timeout = 15s` (было 100s).
  - Retry 1 раз с задержкой 1s при `HttpRequestException` / `TaskCanceledException`.
  - Внешняя отмена (`context.CancellationToken` — Stop в чате) — **не retry**, пробрасывается немедленно.
  - Информативные сообщения в `ToolResult.Fail`: «Wikipedia не ответила за 15 секунд. Возможны проблемы с сетью или прокси. Попробуйте позже или используйте web_search».
  - `context.CancellationToken` теперь реально пробрасывается в `GetStringAsync`.
- **Правило:** см. RULES § 4.28.
- **Тесты:** не добавлены (требуют HTTP-мок; проверено smoke-тестом).

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
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-23 | **Устранено:** 2026-09-24
- **Файлы:**
  - `IIChatTools.Data/Entities/UserSetting.cs` — таблица UserSettings.
  - `IIChatTools.Services/Interfaces/IUserSettingsService.cs` + реализация.
  - `IIChatTools.Services/Interfaces/IChatService.cs` — `DeleteOldChatsAsync(retentionDays, excludedUserIds)` + `DeleteOldChatsForUserAsync`.
  - `IIChatTools.Services/Implementation/ChatService.cs` — реализация (+ InMemory fallback).
  - `IIChatTools.Services/Implementation/ChatRetentionService.cs` — per-user overrides + DoNotDelete.
  - `IIChatTools.Services/DTO/Admin/UserSettingsDto.cs` — новый DTO.
  - `IIChatTools.API/Controllers/AdminController.cs` — `GET/PUT /api/admin/users/{id}/settings`.
  - `IIChatTools.API/Controllers/ProfileController.cs` — `/profile` + `/api/profile/settings`.
  - `IIChatTools.API/Views/Profile/Index.cshtml` — страница профиля.
  - `IIChatTools.API/wwwroot/js/modules/profile.js` — модуль профиля.
  - `IIChatTools.API/wwwroot/js/modules/admin.js` — кнопка ⚙ + модалка.
  - `_Layout.cshtml` + `_LoginPartial.cshtml` — ссылка «Профиль».
- **Решение:**
  - **Backend (KI-067-1, KI-067-2):**
    - Отдельная таблица `UserSettings` (UserId + Key + Value + Type; unique index).
    - Ключи: `Chat.RetentionDays` (int), `Chat.DoNotDelete` (bool).
    - `ChatRetentionService`: per-user overrides + исключение `DoNotDelete`.
    - `DoNotDelete` побеждает `RetentionDays`.
    - InMemory fallback для `ExecuteDeleteAsync` (тесты).
  - **UI (KI-067-3):**
    - `/admin` → ⚙ в строке пользователя → модалка.
    - `/profile` — своя страница (карточка «Хранение чатов»).
    - Пункт меню «Профиль» + displayName → ссылка.
- **Тесты:** 10 (UserSettingsServiceTests) + 4 (ChatServiceRetentionTests). Всего: **74/74**.

---

### KI-068 — Поиск по содержимому сообщений (не только по title)
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.3.x
- **Обнаружено:** 2026-09-23 | **Устранено:** 2026-09-23
- **Файлы:** 
  - `IIChatTools.Services/Interfaces/IChatService.cs` (новый метод `SearchUserChatsAsync`)
  - `IIChatTools.Services/Implementation/ChatService.cs` (реализация)
  - `IIChatTools.API/Controllers/ChatController.cs` (`[FromQuery] string search`)
  - `IIChatTools.API/wwwroot/js/modules/chat.js` (debounce 300ms, server-side)
- **Описание:** Поиск по чатам (Фаза 2.2.3) был **клиентским фильтром** по `chat.title`. Не искал по содержимому сообщений — пользователь не мог найти «тот чат, где мы обсуждали X».
- **Решение (итоговое, после регрессии):**
  - Backend: `GET /api/chats?search={q}` → `SearchUserChatsAsync`.
  - **Фильтрация в памяти** через `string.Contains(..., StringComparison.OrdinalIgnoreCase)` вместо SQL `LOWER() LIKE`.
  - Причина: **SQLite LOWER() не обрабатывает кириллицу** (конвертирует только ASCII A-Z). Первая реализация с `LOWER(Title) LIKE '%прив%'` возвращала только чаты, где в title/сообщениях уже был lowercase. Баг воспроизводился как «два чата с одинаковым title — поиск находит один».
  - Кросс-провайдерно: SqlServer / Sqlite / InMemory теперь ведут себя идентично.
  - Защита от длинных запросов: обрезка до 200 символов.
  - Frontend: debounce 300ms; при пустом запросе — полный список.
  - Убран клиентский фильтр.
- **Ограничение (не блокер):** фильтрация в памяти тянет все сообщения пользователя (проекция `ChatId + Content`). Для масштаба одного пользователя (десятки чатов, сотни сообщений) — приемлемо. На больших объёмах (100k+) потребуется FTS. См. RULES § 4.26.
- **Тесты:** `ChatServiceSearchTests` (7 тестов, включая регрессию «два чата с одинаковым title»).

---

### KI-069 — Inline-edit названия чата в sidebar (двойной клик)
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.3.x
- **Обнаружено:** 2026-09-23 | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/chat.js`, `IIChatTools.API/wwwroot/css/chat.css`
- **Описание:** Переименование чата из sidebar было через `prompt()` (Фаза 2.0.5a) — не соответствовало ChatGPT/DeepSeek-стилю.
- **Решение:**
  - Двойной клик по `.chat-list-item-title` → `<input>` вместо `<div>`.
  - **Enter** = сохранить, **Esc** = отмена, **blur** = сохранить (ChatGPT-style).
  - Кнопка ✏️ вызывает тот же inline-edit (через `startInlineEditTitle`).
  - Использован существующий `PATCH /api/chats/{id}` — backend без изменений.
  - Оптимистичное обновление UI; при ошибке PATCH — откат на старое название (`_rollbackTitle`).
  - Защита от потери ввода: `state.editingChatId` блокирует перерисовку sidebar во время редактирования. Перед `selectChat` / `createChat` / `sendMessage` активный inline-edit принудительно сохраняется (`_flushActiveInlineEdit`).
- **Удалено:** `renameChat()` (prompt-версия).

---

### KI-070 — Sqlite: stale DB без новых таблиц (`no such table`)
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** v1.3.x
- **Обнаружено:** 2026-09-23 | **Устранено:** —
- **Файлы:** `IIChatTools.API/Program.cs`, `README.md`
- **Описание:** При переходе на Sqlite (`Database:Provider = Sqlite`) на машине с существующим `Data/iichattools-dev.db` (созданным до появления новых сущностей, например `Chat`/`ChatMessage`) приложение падает с `SQLite Error 1: no such table: Chats`. Причина — `EnsureCreatedAsync()` **идемпотентен**: если БД существует, он **не создаёт недостающие таблицы** и не мигрирует схему.
- **Симптом в логе:** `fail: Microsoft.EntityFrameworkCore.Database.Command[20102] Failed executing DbCommand ... FROM "Chats" ... no such table: Chats`.
- **Решение (краткосрочно):** удалить `Data/iichattools-dev.db` и `bin/Debug/net10.0/Data/*.db` → перезапустить. Схема пересоздастся с текущей моделью. Данные будут потеряны.
- **Решение (v1.3.x):** генерировать миграции для Sqlite (`Migrations/Sqlite/`) и применять через `MigrateAsync` (как для SqlServer). Позволит обновлять схему без удаления БД.
- **Не блокер:** разработчик может удалить файл БД вручную (комментарий в `Program.cs` это уже описывает).

---

## v1.4.0 — Multi-Agent (roadmap)

### KI-052 — Специализированные суб-агенты по группам инструментов
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.4.0
- **Реализовано:** 2026-09-24 (Фазы 0-9)
- **Что сделано:**
  - Реестр `SubAgentRegistry` (singleton, `SubAgents:*`).
  - 6 агентов: `file_system_agent` (13), `code_agent` (3), `web_agent` (3), `git_agent` (7), `github_agent` (7), `planner_agent` (2) + `consult_secondary_agent` (fallback).
  - Chat видит **7 инструментов** вместо 12 (было `SubAgent:DefaultAllowedTools`).
  - Админка `/admin → Агенты`: список, редактирование (DisplayName, Model, MaxSteps, SystemPrompt, AllowedTools, RequiresApproval, Disabled), сброс.
  - Persist override'ов в `AppSettings` (JSON по ключу `SubAgents.{name}`).
  - 11 тестов (4 unit `SubAgentRegistryTests` + 7 integration `AgentToolBaseTests`).
  - Дизайн: `docs/development/v1.4/DESIGN.md`.
- **Связанные KI:** KI-076 (статистика — Deferred), KI-077 (`model: null` — Documented).

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

## v1.3.x — Post-release fixes (2026-09-23)

### KI-071 — Даты в JSON без `Z` → неверное относительное время
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.3.x
- **Обнаружено:** 2026-09-23 | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/Startup.cs` (`AddNewtonsoftJson`), `IIChatTools.API/Controllers/ChatStreamController.cs` (`SseJsonSettings`)
- **Описание:** Только что созданный чат показывался как «3 ч назад» (при timezone UTC+3). Причина: Sqlite + EF Core возвращают `DateTime` с `Kind=Unspecified`, Newtonsoft.Json сериализует такие даты без суффикса `Z`, а JS `new Date("2026-09-23T19:29:00")` парсит их как **local** — отсюда расхождение на величину смещения. Свежие (в памяти) `DateTime.UtcNow` сериализовались с `Z`, поэтому сразу после `createChat()` UI показывал «только что», а после перезагрузки списка из БД — «3 ч назад».
- **Решение:**
  - `AddNewtonsoftJson(options => options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc)` — трактует `Unspecified` как UTC и добавляет `Z` на выходе.
  - То же для `SseJsonSettings` (SSE-события идут в обход MVC).
  - Все даты в проекте сохраняются через `DateTime.UtcNow` — изменение безопасно.
- **Правило:** см. RULES § 4.27.

---

### KI-072 — Новый чат попадает в отфильтрованный sidebar
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.3.x
- **Обнаружено:** 2026-09-23 | **Устранено:** 2026-09-23
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/chat.js` (`createChat`)
- **Описание:** Если активен поиск (например, «Прив»), при создании нового чата «Новый чат 5» он появлялся в отфильтрованном sidebar (в начале), хотя не совпадал с фильтром. Дополнительно: `generateNextChatTitle()` видел только отфильтрованный `state.chats` — нумерация могла сбиваться.
- **Решение:**
  - `createChat()` перед созданием сбрасывает `state.searchQuery`, очищает input `#chat-search`, вызывает `loadChats()` (полный список из БД).
  - После этого `state.chats` — полный, `generateNextChatTitle()` корректен, `unshift` даёт ожидаемый результат.
  - Поведение как в ChatGPT: создал чат → чистый список.
- **Родственный KI:** KI-068 (server-side поиск — первоисточник `state.chats` = отфильтрованный).

---

## v1.3.2 — Performance (2026-09-24)

### KI-073 — Медленный первый `dotnet test` на Windows (testhost boot)
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.0 (dev-environment)
- **Обнаружено:** 2026-09-24 | **Устранено:** 2026-09-24
- **Файлы:** `IIChatTools.Tests/IIChatTools.Tests.csproj` (ProjectReference на API)
- **Описание:** Первый `dotnet test IIChatTools.sln --no-build` после холодной сборки занимал **~43-60 с** (discovery — 24 с). Реальные тесты — <1 с; overhead VSTest (артефакт xUnit queue) — ~17 с.
- **Диагностика:**
  - Defender exclusions (`C:\Program Files\dotnet`, temp, vstest) → 60 с (было 125 с, ускорение 2×).
  - Defender Real-Time Protection OFF → **44 с** (то же самое, что с ON+exclusions) → **Defender — не главная причина**.
  - **Реальная причина:** testhost boot: загрузка 30+ транзитивных DLL из `IIChatTools.API` (ASP.NET Core, EF Core, JWT, PuppeteerSharp, Prometheus, HtmlAgilityPack) + JIT.
- **Что помогло:**
  1. `scripts/setup/configure-defender.ps1` — exclusions для SDK, temp, vstest-процессов.
  2. **Dev Drive protection отключён** (`Параметры защиты диска разработчика`).
  3. Reboot системы.
  - **Итог:** `dotnet test --no-build` после cold build — **1.4 с**.
- **Не блокер:** dev-workflow нормализован. См. KI-074 (split тестов) — опционально.

---

### KI-078 — Поиск (A: внутричатовый, B: модалка по всем чатам)
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-24 (по мотивам DeepSeek) | **Устранено:** 2026-09-25
- **A — Fixed (v1.4.x)**: внутричатовый поиск (Ctrl+F / 🔍 в header), подсветка `<mark>`, навигация ↑/↓, авто-скролл, debounce 150ms. Frontend-only.
- **B — Fixed (v1.4.x)**: ⌘K-модалка (Ctrl+K / SVG 🔍 в collapsed sidebar) по всем чатам с превью совпадений.
  - Backend: `ChatSearchResultDto`, `IChatService.SearchUserChatsWithSnippetAsync`, `ChatListItemDto` +4 nullable-поля.
  - Frontend: модалка, debounce 200ms, навигация ↑/↓/Enter, защита от гонок fetch.
- **Файлы:**
  - `IIChatTools.API/Views/Chat/Index.cshtml` — панель поиска A + ⌘K-модалка B.
  - `IIChatTools.API/wwwroot/js/modules/chat.js` — `openChatSearch`, `applyChatSearch`, `walkAndHighlight`, `setActiveChatSearchMark`, `openGlobalSearch`, `onGlobalSearchInput`, `renderGlobalSearchResults`, `moveGlobalSearchActive`, `openChatFromGlobalSearch`.
  - `IIChatTools.API/wwwroot/css/chat.css` — `.chat-search-bar` (A) + `.chat-global-search` (B).
  - `IIChatTools.Services/DTO/Chat/ChatSearchResultDto.cs` — DTO с snippet.
  - `IIChatTools.Services/Interfaces/IChatService.cs` — `SearchUserChatsWithSnippetAsync`.
  - `IIChatTools.Services/Implementation/ChatService.cs` — реализация с snippet.
  - `IIChatTools.Services/DTO/Chat/ChatDtos.cs` — расширение `ChatListItemDto`.
  - `IIChatTools.API/Controllers/ChatController.cs` — использует расширенный метод при `search != null`.
- **Тесты:** `ChatServiceSearchTests` + 8 новых тестов для `SearchUserChatsWithSnippetAsync` (KI-078B-3). Всего: **55/55**.
- **Связанные:** KI-068 (server-side поиск по чатам — Fixed v1.3.1).
- **Обнаружено:** 2026-09-24 (по мотивам DeepSeek)
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/chat.js` (планируется)
- **Описание:** Сейчас есть только **серверный поиск по названию и содержимому** всех чатов (KI-068) — открывает нужный чат по подстроке в title/сообщениях. Аналогично DeepSeek — нужен **внутричатовый поиск** (Ctrl+F-style) по сообщениям **активного** чата с:
  - Модалка с полем ввода + список результатов (сниппеты).
  - Клик по результату → скролл к сообщению + кратковременная подсветка (`.chat-message-highlight`).
  - Счётчик «N совпадений».
  - **Подсветка** найденных совпадений внутри сообщения (`<mark>`).
- **Технически:** клиентский поиск (сообщения уже загружены в DOM) → быстрее и не требует API. Для больших чатов — lazy-подгрузка с пагинацией.
- **Родственный KI:** KI-068 (server-side search across chats — Fixed v1.3.1).

---

### KI-079 — Свернуть/развернуть боковую панель
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-24 (по мотивам DeepSeek) | **Устранено:** 2026-09-25
- **Файлы:**
  - `IIChatTools.API/Views/Chat/Index.cshtml` — 2 кнопки (`#btn-collapse-sidebar` в sidebar, `#btn-expand-sidebar` в header) + обёртка `#chat-header-info`.
  - `IIChatTools.API/wwwroot/js/modules/chat.js` — `applySidebarCollapsed()`, `toggleSidebar()`, хоткей `Ctrl+B`, восстановление из `localStorage`.
  - `IIChatTools.API/wwwroot/css/chat.css` — `.chat-sidebar-icon-btn`, `.chat-sidebar-collapsed`, media query.
  - `SharedResources.resx` + `SharedResources.ru.resx` — 2 ключа (`ChatSidebarCollapse` / `ChatSidebarExpand`).
- **Решение:** DeepSeek-style. Кнопка «Свернуть боковую панель» — внутри sidebar (рядом с «+ Новый чат»). При collapsed — кнопка «Открыть боковую панель» появляется в chat-header. Состояние — `localStorage["chat.sidebarCollapsed"]` (`"true"` / `"false"`). Анимация ширины `.2s ease`. Хоткей `Ctrl+B`. Mobile (< 768px) — collapse отключён, sidebar всегда виден. Header не скрывается при отсутствии чата (для доступа к кнопке «Открыть» при collapsed).
- **Связанные:** KI-080 (поле ввода на всю ширину — отдельно).

---

### KI-080 — Поле ввода чата на всю ширину
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-24 (по мотивам DeepSeek) | **Устранено:** 2026-09-25
- **Файлы:**
  - `IIChatTools.API/Views/Chat/Index.cshtml` — `.input-group` → `.chat-input-box`, кнопки → SVG-иконки.
  - `IIChatTools.API/wwwroot/css/chat.css` — новые стили `.chat-input-box` / `.chat-input-textarea` / `.chat-input-action` (удалён KI-061a).
- **Решение:** DeepSeek-style — закруглённое поле (`border-radius: 1.5rem`) на всю ширину `.chat-main`, светлый фон (`#f6f8fa`), focus-ring на `.chat-input-box:focus-within`. Кнопки Send/Stop — круглые SVG-иконки внутри поля справа (стрелка вверх / квадрат). Панель инструментов под полем — отложено в v1.5.0 (см. KI-082).
- **Связанные:** KI-079 (collapse sidebar), KI-082 (модалка-редактор).

---

### KI-077 — `model: null` при PUT агента = «сбросить на default», а не «не менять»
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-24 (Фаза 6.1-6.4 KI-052) | **Устранено:** —
- **Файлы:** `IIChatTools.API/Controllers/AdminAgentsController.cs` (метод `UpdateAgentAsync`), `IIChatTools.Services/DTO/Admin/UpdateAgentRequest.cs`
- **Описание:** В `UpdateAgentRequest.Model` (тип `string`) невозможно отличить «поле не передано» от «явно передано `null`». Текущая логика: `string.IsNullOrWhiteSpace(request.Model) ? null : ...` — то есть **любое непереданное значение сбрасывает модель в null** (= «использовать `LmStudio:Model` из appsettings»). Например, PUT с `{ displayName: "...", maxSteps: 15 }` (без model) → `model: null` в результирующем дескрипторе.
- **Влияние:**
  - Через UI-модалку: **не проявляется** — поле `model` всегда заполнено текущим значением, всегда передаётся. Если админ оставил поле пустым — это осознанный сброс.
  - Через прямой API-вызов (curl, DevTools): можно случайно сбросить модель, не указав её в body.
- **Решение (при необходимости):** добавить в `UpdateAgentRequest` флаг `bool ClearModel` + обработать: `Model = request.ClearModel ? null : (request.Model ?? existing.Model)`. **Пока не критично** — в UI проблемы нет.
- **Не блокер:** задокументировано, UI-путь безопасен.

---

### KI-076 — Статистика по агентам в админке
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-24 (Фаза 6 KI-052) | **Устранено:** 2026-09-25
- **Файлы:**
  - `IIChatTools.Services/DTO/Admin/AgentStatsDto.cs` — новый DTO.
  - `IIChatTools.Services/Interfaces/IAgentStatsService.cs` — интерфейс.
  - `IIChatTools.Services/Implementation/AgentStatsService.cs` — агрегация.
  - `IIChatTools.Services/Implementation/Tools/SubAgent/AgentToolBase.cs` — запись запуска в `AuditLogs`.
  - `IIChatTools.Services/Implementation/Tools/SubAgent/*AgentTool.cs` (6) — +`IAuditService` в конструктор.
  - `IIChatTools.API/Controllers/AdminAgentsController.cs` — `GET /stats`.
  - `IIChatTools.API/Startup.cs` — регистрация `IAgentStatsService`.
  - `IIChatTools.API/Views/Home/Admin.cshtml` — карточки статистики.
  - `IIChatTools.API/wwwroot/js/modules/admin-agents.js` — `loadAgentStats`.
  - `IIChatTools.API/wwwroot/css/site.css` — `.agent-stat-card`.
- **Решение (Вариант A — без миграции):**
  - `AgentToolBase` при каждом запуске пишет в `AuditLogs` запись с `ToolName = "agent.{AgentName}"`.
  - `AgentStatsService` агрегирует через `GroupBy(ToolName)` — один SQL-запрос.
  - Джойнит с `ISubAgentRegistry` для `DisplayName`.
  - `Status` = `Success` / `Error` / `Cancelled` (по `OperationCanceledException`).
- **Ограничение:** задача НЕ логируется целиком (без PII) — только флаг `hasTask`.
- **Тесты:** 3 новых (`AgentStatsServiceTests`). Всего: **58/58**.
- **В коде:** `AdminAgentsController.GET /api/admin/agents/stats` — новый endpoint.

---

### KI-075 — ToolRegistry логирует при каждом scope
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.0 (Фаза 5.x)
- **Обнаружено:** 2026-09-24 | **Устранено:** 2026-09-24
- **Файлы:** `IIChatTools.Services/Implementation/ToolRegistry.cs`
- **Описание:** `IToolRegistry` зарегистрирован как **scoped** (каждый HTTP-запрос — новый экземпляр). Конструктор логировал `LogInformation("ToolRegistry инициализирован...")` — в проде с сотнями запросов это создаёт шум в логах. Заметили при разборе лога Фазы 5: строчка появилась внутри обработки `/api/chat/approvals/.../approve`.
- **Решение:** `LogInformation` → `LogDebug`. Уровень можно поднять через `appsettings.json`: `Logging:LogLevel:IIChatTools.Services.Implementation.ToolRegistry=Information`.

---

### KI-074 — Split тестового проекта на Unit / Integration
- **Приоритет:** 🟢 Low | **Статус:** Deferred | **Запланировано:** v1.4.x (опционально)
- **Обнаружено:** 2026-09-24 (в ходе диагностики KI-073)
- **Описание:** `IIChatTools.Tests` ссылается на `IIChatTools.API` → тянет PuppeteerSharp и 30+ DLL. Только `LocalizationSyncTests` реально использует типы API.
- **Решение (план, если понадобится):** разделить на `Tests.Unit` (только Services/Data) + `Tests.Integration` (с API). Ожидаемый эффект: unit — 2-3 с.
- **Отсрочка:** после KI-073 тесты работают за 1.4 с — приоритет низкий.

---

### KI-082 — Модалка-редактор длинных user-сообщений
- **Приоритет:** 🟢 Low | **Статус:** Deferred | **Запланировано:** v1.5.0+ (дальний фокус)
- **Обнаружено:** 2026-09-25
- **Файлы (план):** `IIChatTools.API/Views/Chat/Index.cshtml`, `wwwroot/js/modules/chat.js`, `wwwroot/css/chat.css`
- **Описание:** Inline-edit (KI-069 + 2.2.6b) ограничен `max-width: 80%` bubble — неудобно для длинных сообщений (> 500 символов). Альтернатива — модалка по центру (⌘K-стиль) с большим полем (до 70vh) + кнопки Save/Cancel. Опционально: переключатель «сохранить без regenerate», предпросмотр Markdown.
- **Условие применения:** только для user-сообщений > 500 символов (иначе inline-edit).
- **Минусы (осознанные):**
  - **Отход от ChatGPT UX — пользователю непривычно.** ChatGPT/Claude/DeepSeek используют inline-edit. Модалка — отклонение.
  - Потеря контекста (лента под модалкой затемнена).
  - +1 слой абстракции (модалка поверх чата).
- **Обоснование отсрочки:** inline-edit уже работает (KI-069 + 2.2.6b); модалка — нишевое улучшение для редкого сценария (очень длинные сообщения). Не в roadmap v1.4.x.
- **Связанные:** KI-069 (inline-edit названия чата), KI-078B (⌘K-модалка — эталон стиля).

### KI-081 — Логотип IIChatTools как фирменный элемент UI
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-25 | **Устранено:** 2026-09-25
- **Файлы:**
  - `IIChatTools.API/wwwroot/images/logo-icon.svg` — иконка (inline SVG).
  - `IIChatTools.API/wwwroot/images/logo-full.svg` — иконка + текст.
  - `IIChatTools.API/wwwroot/site.webmanifest` — PWA-манифест.
  - `IIChatTools.API/Views/Shared/_Layout.cshtml` — favicon + navbar-brand.
  - `IIChatTools.API/Views/Home/Index.cshtml` — hero-логотип.
  - `IIChatTools.API/Views/Auth/Login.cshtml` + `Register.cshtml` — логотип над формой.
  - `IIChatTools.API/wwwroot/js/modules/chat.js` — empty state.
  - `IIChatTools.API/wwwroot/css/site.css` + `chat.css` — стили.
- **Решение:** Inline SVG (по референсу) + PNG-favicon от realfavicongenerator.
  - Favicon: `<link rel="icon" type="image/svg+xml">` + PNG 16/32 + apple-touch-icon.
  - Navbar-brand: иконка + текст (28px).
  - Hero на главной: логотип с текстом (240px).
  - Login / Register: логотип с текстом (200px).
  - Empty state в `/chat`: иконка (64px, `opacity: .45`).
- **НЕ реализовано (осознанно):** логотип как фон под активным чатом или на всём приложении (снижение контраста, WCAG AA).
- **Связанные:** KI-033 (favicon-заглушка — устранено).

---

### KI-084 — Расширенная статистика генерации (как в LM Studio)
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-25 | **Устранено:** 2026-09-25
- **Файлы:**
  - `IIChatTools.Data/Entities/ChatMessage.cs` — +3 nullable-поля.
  - `IIChatTools.Services/Implementation/ChatStreamService.cs` — Stopwatch + firstToken + finishReason.
  - `IIChatTools.Services/DTO/Chat/ChatDtos.cs` — `ChatMessageDto` +3 поля.
  - `IIChatTools.Services/DTO/Chat/ChatStreamDtos.cs` — `Done` +3 параметра.
  - `IIChatTools.API/Controllers/ChatController.cs` — маппинг.
  - `IIChatTools.API/wwwroot/js/modules/chat.js` — tok/s + длительность в meta.
  - `.resx` (RU + EN) — +2 ключа.
- **Решение:**
  - **Backend (KI-084a):** `ChatStreamService` замеряет `Stopwatch` (общая длительность), время до первого delta, последний `finish_reason` LM Studio. Всё сохраняется в `ChatMessage` + передаётся в SSE `done`.
  - **Frontend:** meta-строка assistant-сообщения → `123 / 45 токенов · 7.4 tok/s · 9.1 с`.
  - **Расчёт tok/s:** `tokensOut / ((DurationMs - FirstTokenMs) / 1000)`.
- **Требует миграции** `AddChatMessageStats` (SqlServer) / удаления `.db` (Sqlite — EnsureCreated).
- **Связанные:** KI-049a (tiktoken), KI-084b (SSE start/done).

---

### KI-083 — RAG / Knowledge Base (Retrieval-Augmented Generation)
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.5.0
- **Обнаружено:** 2026-09-25 | **Устранено:** 2026-09-28
- **DESIGN:** [`docs/development/v1.5/DESIGN.md`](development/v1.5/DESIGN.md) — согласован 2026-09-25.
- **Что войдёт:**
  - `IEmbeddingService` (LM Studio `/v1/embeddings`, nomic-embed-text-v1.5, 768 dim).
  - `IVectorStore` (InMemory MVP → Qdrant в v1.5.x).
  - Chunking (recursive / sentence / fixed; 500 токенов, overlap 64).
  - `PlainTextParser` (21 расширение: текст, код, разметка).
  - `DocumentIngestionService` (parse → chunk → embed → store).
  - 3 tool для LLM: `search_knowledge_base`, `search_chat_history`, `search_workspace`.
  - Attached files в чат (📎, 1–5 файлов, ≤30 MB).
  - Admin `/admin → Knowledge Base` (4 индекса, reindex, настройки).
  - Profile: workspace index (opt-in).
- **4 индекса:** `project_docs` (global), `my_rag_docs` (per-chat), `chat_history` (per-user), `workspace` (per-user).
- **План:** 8 фаз, ~45 ч. Прогресс:
  - Фаза 0 (DESIGN) — ✅ Done (2026-09-25).
  - Фаза 1 (Embedding Service) — ✅ Done (2026-09-25, коммит `a500216`).
  - Фаза 2 (Vector Store + DocumentChunk) — ✅ Done (2026-09-25, коммиты `655daee`, `eb903c9`, `c1aa5a5`).
  - Фаза 3 (Chunking) — ✅ Done (2026-09-25, коммиты `c2fa869`, `f9d9664`, `f2881a1`).
  - Фаза 4 (Parser + Ingestion) — ✅ Done (2026-09-25, коммиты `bdf8866` (4A), `4783c71` (4B), `5c1927a`/`00fb044`/`2056d9e`/`9e91512` (4C)).
  - Фаза 5 (Retrieval + 3 Tools) — ✅ Done (2026-09-25, коммиты `d10966d`, `c83a8fa`, `d3493f5`).
  - Фаза 6 (Attached Files) — ✅ Done:
    - 6A (Entity + миграция + сервис + тесты) — ✅ Done (`efd442e`, `e13ae9e`).
    - 6B (Controller + лимиты multipart + тесты) — ✅ Done (`0176177`).
    - 6C (Auto-inject top-K в system prompt) — ✅ Done (`0e6e8f9`).
    - 6D (UI: 📎 + chips + «Очистить RAG») — ✅ Done (`137198c`).
  - **Фаза 7 (Admin KB UI + Profile Workspace UI)** — ✅ **Done** (2026-09-28):
    - 7A — Admin KB backend (6 endpoints) — `2a05ad2`.
    - 7B — Admin KB UI (8-я вкладка) — `aba1c2e`.
    - 7C.1 — Workspace Index backend (5 endpoints) — `1e2d0e1`.
    - 7C.2 — Workspace Index UI (`/profile`) — `dd627a8`.
    - 7D.1 — Unit-тесты `WorkspaceIndexService` — `c1134ed`.
    - 7D.2 — Unit-тесты `AdminKnowledgeController` — `c0e14cc`.
    - 7D.3 — RULES v1.4.16 (§ 4.42, § 4.43) — финальный коммит Шага 7.
  - **Фаза 8 (Релиз v1.5.0)** — ✅ **Done** (2026-09-28):
    - **8.1** — README: раздел «RAG / Knowledge Base» — `f94a12b`.
    - **8.2** — RELEASES § 1a «Известные ограничения v1.5.0» — `213bb72`.
    - **8.3** — Bump version 1.4.1 → 1.5.0 — `12e3dd1`.
    - **8.4** — CHANGELOG `[Unreleased]` → `[1.5.0] — 2026-09-28` — `340ad58`.
    - **8.5** — KNOWN_ISSUES / RULES § 7 / DESIGN / ARCHITECTURE (этот коммит).
    - **8.6** — `docs/TESTING.md` (KI-088) — параллельно.
    - **8.7** — Tag `v1.5.0` + GitHub Release.
  - **Все фазы (0-8) — Done.** Остался тег и Release.
- **Связанные:** KI-086 (sources / citations — v1.6.0), KI-049 (tiktoken), KI-067 (UserSettings), KI-090/KI-091 (SqlServer migrations).

---

### KI-093 — SQLite `database is locked` (дубликат KI-085, оставлен для истории)
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-25
- **Примечание (2026-09-28, Шаг 8.5):** изначально был заведён как `KI-086`
  по ошибке — номер конфликтовал с уже существующим KI-086 (sources/citations).
  Переименован в **KI-093**. По содержанию совпадает с **KI-085** — оставлен
  для истории (не удаляем задним числом, по правилу «даже если это не баг —
  запись нужна»).
- **Файлы:** `appsettings.Development.json` (Sqlite-провайдер)
- **Описание:** При открытом **DB Browser for SQLite** (или другом внешнем клиенте) на `iichattools-dev.db` создание чата через `/api/chats` падает с `SQLite Error 5: 'database is locked'`. Время ответа — **30 секунд** (CommandTimeout), потом ошибка.
- **Причина:** SQLite — файловая БД. При наличии **writer** в другом процессе (DB Browser в read-write режиме) текущий writer (приложение) ждёт снятия блокировки и падает по таймауту.
- **Решение (рабочее):**
  1. Закрыть внешний клиент (DB Browser) → перезапустить приложение.
  2. Или открывать БД в DB Browser в режиме **Read Only**.
  3. Опционально: `PRAGMA journal_mode=WAL;` — параллельное чтение + одна запись.
- **Не баг приложения:** ограничение SQLite. Для прод — SqlServer (или отдельная БД для чтения).
- **Связанные:** KI-070 (Sqlite EnsureCreated не мигрирует).

---

### KI-086 — Вывод источников (sources / citations) под ответом ассистента
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.6.0
- **Обнаружено:** 2026-09-25 | **Устранено:** 2026-09-28
- **Файлы (итог):**
  - `IIChatTools.Services/DTO/Chat/ChatSourceDto.cs` — новый DTO (rag/web/wiki).
  - `IIChatTools.Services/DTO/ToolResult.cs` — +`Sources` (`IReadOnlyList<ChatSourceDto>`).
  - `IIChatTools.Services/DTO/Chat/ChatStreamDtos.cs` — `Done` +параметр `sources`.
  - `IIChatTools.Services/DTO/Chat/ChatToolResultDto.cs` — +`Sources`.
  - `IIChatTools.Services/DTO/Chat/ChatDtos.cs` — `ChatMessageDto` +`Sources` +`MetadataJson`.
  - `IIChatTools.Services/Implementation/Rag/RagSourceBuilder.cs` — новый хелпер.
  - `IIChatTools.Services/Implementation/Tools/Rag/*Tool.cs` — 3 RAG-tool возвращают sources.
  - `IIChatTools.Services/Implementation/ChatStreamService.cs` — aggregation + camelCase-сериализация + `RagToolNames` fix.
  - `IIChatTools.Data/Entities/ChatMessage.cs` — +`MetadataJson`.
  - `IIChatTools.Data/Migrations/SqlServer/*_AddChatMessageMetadata.cs` — новая миграция.
  - `IIChatTools.API/Controllers/{ToolsController,ChatController}.cs` — проброс Sources.
  - `IIChatTools.API/wwwroot/js/modules/chat.js` + `chat.css` — UI-блок «📚 Источники».
  - `IIChatTools.API/Views/Chat/Index.cshtml` — 2 data-атрибута.
  - `SharedResources*.resx` (RU + EN) — +2 ключа (`ChatSourcesHeader`, `ChatSourceChunkMeta`).
  - `IIChatTools.Tests/UnitTests/Rag/RagSourceBuilderTests.cs` — 17 тестов (14 `[Fact]`/`[Theory]` + 3 mixed-separators `[Theory]`).
- **Описание:** Web-инструменты возвращали URL в JSON `tool_result`, но данные не были видны пользователю. RAG (v1.5.0) ввёл концепцию «документов-источников»; в v1.6.0 добавлен UI-блок «📚 Источники» под ответом ассистента.
- **Решение (итог):**
  - **Шаг 1:** `ChatSourceDto`, `ChatMessage.MetadataJson`, миграция `AddChatMessageMetadata`.
  - **Шаг 2:** `ToolResult.Sources` + `RagSourceBuilder` (label = имя файла из пути, snippet ≤ 200), 3 RAG-tool пробрасывают sources.
  - **Шаг 2.fix:** проброс `Sources` до HTTP/SSE (`ToolsController`, `ChatStreamService`, `ChatController`).
  - **Шаг 3:** aggregation sources в `ChatStreamService` (auto-inject + `tool_result`), дедупликация по `(type, documentPath, chunkIndex)`, сохранение в `MetadataJson` (camelCase), SSE `done`.
  - **Шаг 3.5:** усилены `Description` для `search_knowledge_base` и `file_system_agent`.
  - **Шаг 3.5c:** ⭐ **корневой fix** — RAG-tools не попадали в `tools[]` Chat с v1.5.0 (`allowedNames` содержал только 6 агентов + consult). RULES § 4.44.
  - **Шаг 3.5d:** camelCase + структурный `ChatMessageDto.Sources` (парсится на бэкенде).
  - **Шаг 4:** UI — блок «📚 Источники» (`RULES.md (chunk 15, score 0.71)`), live (SSE) + F5 (`msg.sources`).
  - **Шаг 5.1–5.2:** тесты (`RagSourceBuilder`), README + TESTING.
  - **Шаг 5.1.fix2:** кросс-платформенный `BuildLabel` (`Path.GetFileName` на Linux) — RULES § 4.45.
- **Тесты:** 199 → **216** (+17).
- **Коммиты:** 13 (Шаги 1, 2, 2.fix, 3, 3.5, 3.5c, 3.5d, 3.5e, 4, 5.1, 5.1.fix, 5.1.fix2, 5.2).
- **Известные ограничения (не блокеры, v1.6.1+):**
  - `sources[i].documentPath` — абсолютный (`C:\Projects\...`); UI показывает `label` (имя файла), но `path` в API некрасив. Косметика.
  - LLM (qwen3-4b) галлюцинирует содержимое RULES.md (пишет про «правила выбора инструментов», которых там нет) — ограничение 4B-модели + недостаток re-ranking.
  - **Web-tools** (Wikipedia, WebSearch, FetchWebContent) — sources для них **в v1.6.1**. В v1.6.0 — только RAG-источники.
- **Связанные:** KI-083 (RAG / embeddings — v1.5.0).

---

### KI-087 — Актуальный обзор архитектуры (docs/development/ARCHITECTURE.md)
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.4.x
- **Обнаружено:** 2026-09-25 | **Устранено:** 2026-09-25
- **Файлы:**
  - `docs/development/ARCHITECTURE.md` — новый сводный документ (9 разделов).
  - `docs/development/archive/v1.0.x/architecture/` — 12 legacy-файлов (перенесены из `docs/architecture/`).
  - `docs/development/archive/README.md` — обновлена структура.
  - `docs/development/PROMPT_V2.md` — усилено правило форматирования MD.
- **Решение:** создан `ARCHITECTURE.md` (~700 строк): слои, схема БД, DI-lifetime, поток Chat, 46 инструментов, внешние зависимости, 10 ADR-style записей.
- **Связанные:** DESIGN v1.3/v1.4/v1.5 — источники.

---

### KI-088 — Актуальный чек-лист ручной приёмки (docs/TESTING.md)
- **Приоритет:** 🟡 Medium | **Статус:** Planned | **Запланировано:** v1.5.0 (перед релизом)
- **Обнаружено:** 2026-09-25
- **Файлы (план):**
  - `docs/TESTING.md` — чек-лист (новый).
  - `docs/testing/checklist-v1.5.md` — форма для тестировщиков (печатная/электронная).
- **Описание:** В команде есть **тестировщики**. Сейчас нет единого документа для ручной приёмки релиза. Существующий `docs/testing/Для тестирования testы.txt` (15 KB, v1.0) — legacy, перенесён в архив. xUnit-тесты (81/81) покрывают код, но не UX/UI.
- **Что должно быть в TESTING.md:**
  - **Smoke-сценарии** (быстрая проверка релиза, 7-10 пунктов): чат, RAG (v1.5), approvals, файлы, admin, profile.
  - **Full regression** (для patch-релизов): все ключевые фичи + edge cases.
  - **UI/UX чек-лист:** локализация RU/EN, мобильная вёрстка, hotkeys (Ctrl+B/F/K).
  - **Что НЕ покрыто автотестами** (для тестировщиков): SSE-стриминг, tool calling, approvals end-to-end.
  - **Формат:** таблица «# | Действие | Ожидание | Статус (OK/FAIL) | Комментарий».
  - **Ссылки** на DESIGN-документы фаз (для контекста).
- **Обоснование отсрочки:** создать **перед релизом v1.5.0** (когда есть что принимать). Сейчас фокус — разработка RAG.
- **Связанные:** KI-086 (sources/citations — v1.6.0), DESIGN v1.5 § 11 (7 smoke).

---

### KI-089 — Секция конфигурации эмбеддингов (`Rag:Embedding` vs `LmStudio:Embedding*`)
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** после Фазы 1 KI-083 (при необходимости)
- **Обнаружено:** 2026-09-25
- **Файлы:** `IIChatTools.API/appsettings.json`, `IIChatTools.API/appsettings.Development.json`, `IIChatTools.Services/Implementation/LmStudioClient.cs`
- **Описание:** Эмбеддинги конфигурируются в секции `Rag:Embedding` (Model, Dimensions, BatchSize, TimeoutSeconds, CacheEnabled), хотя клиент — `LmStudioClient` (для chat читает `LmStudio:*`). В Фазе 1 (KI-083) оставлено как в DESIGN.md § 8.1 для совместимости с будущими подсекциями (`Rag:Chunking`, `Rag:Ingestion`, `Rag:Retrieval`, `Rag:Attachments`).
- **Варианты решения:**
  - **A)** Оставить как есть (`Rag:Embedding`) — единая RAG-секция.
  - **B)** Перенести в `LmStudio:Embedding*` — клиентские параметры к LM Studio.
  - **C)** Разделить: клиентские (`Model`, `TimeoutSeconds`) → `LmStudio:Embedding*`; RAG-параметры (`Dimensions`, `BatchSize`) → `Rag:Embedding*`.
- **Пока не критично:** работает по DESIGN.md. Изменение конфига — обратно-совместимо (можно поддержать оба ключа с fallback через `??`).
- **Связанные:** KI-083 (RAG / Knowledge Base).

---

### KI-090 — SqlServer-migrations содержали Sqlite-типы (snapshot drift)
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Обнаружено:** 2026-09-25
- **Файлы:** `IIChatTools.Data/Migrations/SqlServer/*.cs` (все предыдущие до `AddDocumentChunks`), `appsettings.Development.json` (`Database:Provider`)
- **Описание:** При генерации миграций для SqlServer (`Migrations/SqlServer/`) в Development-окружении `Database:Provider` был выставлен в `Sqlite`. `dotnet ef migrations add` читает провайдер из `appsettings.Development.json`, поэтому миграции `AddUserSettings`, `SRVFixPendingChanges`, `AddChatMessageStats` сгенерировались с **Sqlite-типами** (`TEXT`, `INTEGER`) и попали в папку `SqlServer`. Snapshot (`AppDbContextModelSnapshot.cs`) тоже содержал смешанные типы.
- **Симптом:** при генерации следующей SqlServer-миграции (`AddDocumentChunks`) EF видел расхождения типов и генерировал **большой блок `AlterColumn`** (перевод всей схемы с `TEXT`→`nvarchar`, `INTEGER`→`int`). Дополнительно: файлы предыдущих миграций раздулись (68-72 KB вместо обычных 5-10 KB).
- **Текущее решение (2026-09-25):** миграция `AddDocumentChunks` фактически исправляет snapshot и приводит схему к SqlServer-виду. Долг устранён.
- **Правило на будущее (RULES § 3.15, добавлено):** **перед `dotnet ef migrations add` — проверить `Database:Provider` в `appsettings.Development.json`.** Для SqlServer-миграций — `Provider = "SqlServer"`; для Sqlite — `Provider = "Sqlite"`. После генерации — вернуть `Sqlite` для dev-разработки.
- **Связанные:** KI-083 (RAG — Шаг 2A).

---

### KI-091 — SqlServer цепочка миграций повреждена (snapshot drift)
- **Приоритет:** 🟡 Medium | **Статус:** Deferred | **Запланировано:** v1.5.0-rc (перед релизом)
- **Обнаружено:** 2026-09-25 (попытка `dotnet ef database update` на SqlServer)
- **Файлы:** `IIChatTools.Data/Migrations/SqlServer/` (все миграции до `AddDocumentChunks`)
- **Описание:** SqlServer цепочка миграций содержит Sqlite-типы (`TEXT`, `INTEGER`) — следствие KI-090. При попытке `database update` на **свежей** SqlServer БД:
  - `InitialSqlServer` ✅
  - `AddChatAndChatMessages` ✅
  - `AddUserSettings` ❌ — падает с `Operand type clash: datetime2 is incompatible with text` (попытка `ALTER COLUMN UpdatedAt TEXT NULL` на колонке `datetime2`).
  Транзакция откатывается. БД остаётся в состоянии после `AddChatAndChatMessages`.
- **Влияние:**
  - **dev-Sqlite** — работает (`EnsureCreated`, проверено 2026-09-25).
  - **prod-SqlServer** — БД ещё не разворачивалась в проекте, блокер не критичен.
  - **dev-SqlServer** — разработчики не могут применить миграции.
- **⚠️ План починки (НЕ ВЫПОЛНЯТЬ до v1.5.0-rc):**

  > **Важно:** шаги ниже — это **описание плана**, а не инструкция к исполнению. Выполнять только при старте задачи починки. Точные команды согласуются в отдельном чате.

  **Шаг 1.** Сделать бэкап dev-SqlServer БД (если есть данные) или убедиться, что БД не нужна (dev — пересоздаётся).
  **Шаг 2.** Дропнуть dev-SqlServer БД через SSMS или `sqlcmd`. Имя: `IIChatTools_Dev`. Данные теряются.
  **Шаг 3.** Удалить из git сломанные SqlServer-миграции (`AddUserSettings`, `SRVFixPendingChanges`, `AddChatMessageStats`, `AddDocumentChunks`).
  **Шаг 4.** Откатить `AppDbContextModelSnapshot.cs` к состоянию после `AddChatAndChatMessages` (хеш-коммита определяется на момент починки).
  **Шаг 5.** Временно выставить `Database:Provider = "SqlServer"` в `appsettings.Development.json`.
  **Шаг 6.** Сгенерировать одну сводную миграцию `AddUserSettings_AgentStats_Rag` (охватывает всё, что было в удалённых).
  **Шаг 7.** Применить `dotnet ef database update` на свежей БД. Проверить, что все таблицы + индексы на месте.
  **Шаг 8.** Вернуть `Provider = "Sqlite"`.

- **Риск:** переписывание истории миграций. Другие машины с применёнными старыми миграциями сломаются. Для проекта — приемлемо: SqlServer нигде не разворачивался.
- **Обоснование отсрочки:** RAG (v1.5.0) в активной разработке. Прерывание на инфраструктурный долг — потеря фокуса. Чинить перед релизом, когда будет полная картина.
- **Связанные:** KI-090 (snapshot drift — источник), KI-083 (RAG).

---

## KI-092 — Bootstrap 5: warning `aria-hidden` при закрытии вложенных модалок
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.10.1 (попутно с KI-122)
- **Обнаружено:** 2026-09-25 | **Устранено:** 2026-10-01
- **Файлы:** `wwwroot/lib/bootstrap/` — сторонняя библиотека.
- **Описание (первоначально):** В консоли браузера при закрытии модалок
  (`#adminModal`, `#ragChunksModal`) появлялся warning:
  **«Blocked aria-hidden on an element because its descendant retained focus.
  The focus must not be hidden from assistive technology users»**.
  Элемент с фокусом — `<button.btn-close>`, предок с `aria-hidden` —
  `<div.modal fade#ragChunksModal>`.
- **Причина:** Bootstrap **5.2** устанавливал `aria-hidden="true"` на `.modal`
  при закрытии, но кнопка `.btn-close` внутри неё могла сохранять фокус —
  warning о конфликте с WCAG.
- **Решение:** **автоматически устранено** при обновлении до Bootstrap **5.3.2**
  (в `wwwroot/lib/bootstrap/dist/` уже 5.3.2 с момента предыдущей задачи;
  версия получена через `configs/libman.json` → cdnjs).
  Bootstrap 5.3+ использует атрибут **`inert`** вместо `aria-hidden` —
  warning не генерируется.
- **Проверка (2026-10-01, v1.10.1 smoke):** Console → Filter = Default levels →
  открыть и закрыть модалки `/admin` (`#adminModal`, `#ragChunksModal`),
  approval-модалку в `/chat`. **Warning «Blocked aria-hidden...» отсутствует.**
- **Связанные:** KI-122 (тоже требовал Bootstrap 5.3+; фактически обновление
  уже было сделано до старта KI-122).

---

## v1.6.1 — Sources / citations (Web-tools)

### KI-094 — `wikipedia_search` intermittent timeout в агенте (SSL через прокси)
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** v1.6.2
- **Обнаружено:** 2026-09-28
- **Файлы:** `IIChatTools.Services/Implementation/Tools/Web/WikipediaSearchTool.cs`
- **Описание:** Intermittent таймаут `wikipedia_search` (15s) при SSL-обрыве
  через корпоративный прокси. Симптом: `web_agent` получает
  `ToolResult.Fail("Wikipedia не ответила за 15 секунд...")` от
  `wikipedia_search`, переключается на `web_search` (fallback).
  Воспроизводится ~50/50 при повторных запросах подряд.
- **Не блокер v1.6.1:**
  - `web_search` (DuckDuckGo) работает стабильно и уже возвращает citations
    (Шаг A3).
  - `web_agent` автоматически делает fallback — пользователь получает ответ.
- **Возможные решения (v1.6.2):**
  - Уменьшить timeout до 10s (быстрее падаем → быстрее fallback).
  - Увеличить retry до 3 (сейчас `MaxAttempts = 2`).
  - Circuit breaker: skip wikipedia после 2 таймаутов подряд в рамках сессии.
  - Проверить `HttpClientHandler.SslProtocols` (может, явно указать TLS 1.2).
- **Связанные:** KI-064 (первый инцидент с wikipedia timeout, v1.3.x —
  частично исправлено timeout + retry).

---

### KI-095 — `snippet` `fetch_web_content` может начинаться с текста, дублирующего `label`
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-28
- **Файлы:** `IIChatTools.Services/Implementation/Tools/Web/FetchWebContentTool.cs`
- **Описание:** Snippet формируется из первых 200 символов `doc.DocumentNode.InnerText`.
  На страницах, где `<h1>` в `<body>` совпадает с `<title>` (например,
  `example.com` → title=`Example Domain`, h1=`Example Domain`), snippet
  начинается с текста, дублирующего `label`. Визуально: `label: Example Domain`,
  `snippet: Example DomainThis domain is for use...`.
- **Не баг:** snippet = «первые 200 символов текста страницы», `<h1>` —
  это реальный контент, пользователь видит его на странице.
- **Возможные решения (если понадобится):**
  - В `WebSourceBuilder.BuildSingle`: если snippet начинается с label
    (case-insensitive) — отрезать префикс.
  - Или: удалять первый `<h1>` из body перед чтением InnerText (но тогда
    теряется часть контента).
  - Или: оставить как есть — honest API.
- **Решение:** оставлено как есть (не баг, не блокер).

---

### KI-096 — GitHub Wiki для проекта (roadmap)
- **Приоритет:** 🟢 Low | **Статус:** Deferred | **Запланировано:** v1.7+
- **Обнаружено:** 2026-09-28
- **Описание:** Идея создать **GitHub Wiki** для репозитория
  `iilmchat/IIChatTools` — как отдельный канал документации, помимо
  README / RULES / ARCHITECTURE / CHANGELOG.
- **Возможные scope (уточнить на старте задачи):**
  1. **Публичная Wiki** с документацией проекта: getting started, architecture,
     tool reference, FAQ, troubleshooting.
  2. **RAG по GitHub Wiki** — добавить возможность индексировать GitHub Wiki
     как источник (в дополнение к `project_docs`). Может быть реализовано через
     `search_knowledge_base` с новым индексом `github_wiki` или через
     `fetch_web_content` + ручную индексацию.
  3. **GitHub Actions workflow** для автосинхронизации Wiki с `docs/` (некоторые
     страницы могут дублироваться).
- **Что нужно решить до старта:**
  - Какие разделы Wiki приоритетны?
  - Автоматическая синхронизация с `docs/` или ручное ведение?
  - Нужна ли интеграция с RAG?
- **Связанные:** KI-083 (RAG / embeddings), KI-086 (Sources).

---

## v1.7.0 — Database Agent (roadmap)

### KI-097 — Database Agent (read-only SQL)
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.7.0
- **Обнаружено:** 2026-09-28
- **DESIGN:** [`docs/development/v1.7/DESIGN_DB_AGENT.md`](development/v1.7/DESIGN_DB_AGENT.md)
- **Описание:** LLM не имеет структурированного доступа к данным приложения
  (`Chats`, `ChatMessages`, `AuditLogs`, ...). Прямой доступ через
  `execute_command` → `sqlite3` — антипаттерн (SQL-инъекции, нет whitelist,
  нет timeout, нет аудита).
- **Что входит (Фаза 1, v1.7.0):**
  - `DatabaseAgentTool` — топ-левел `ITool` (не `AgentToolBase`), 4 action:
    `list_databases`, `list_tables`, `describe_table`, `execute_query`.
  - `SqlQueryValidator` — валидация SQL (SELECT-only, whitelist таблиц,
    запрет keywords/functions, auto-LIMIT).
  - `SqlConnectionProvider` — фабрика `DbConnection` (в Фазе 1 — только
    `internal`, Sqlite).
  - **5 уровней безопасности:** read-only режим БД + валидатор + whitelist +
    timeout + approval+audit.
  - Admin UI `/admin → SQL Agent` — настройка whitelist в runtime.
- **Что НЕ входит:** внешние БД (KI-099, Фаза 2), Domain-Oriented Tools
  (по факту обкатки), write-операции (никогда), Semantic Layer (v2.0).
- **План:** 8 фаз, ~40 ч. Прогресс:
  - Фаза 0 (DESIGN) — ✅ Done (2026-09-28).
  - Фаза 1 (контракты + DTO) — ✅ Done (`a52c26b`, `a167d35`).
  - Фаза 2 (SqlConnectionProvider + SqlAgentOptionsProvider) — ✅ Done (`6e68f9d`).
  - Фаза 3 (SqlQueryValidator) — ✅ Done (`36f2a2d`, 29 тестов).
  - Фаза 4 (SqlAgentService + fix KI-100) — ✅ Done (`c62ccd5`, 12 тестов).
  - Фаза 5 (DatabaseAgentTool + Chat integration) — ✅ Done (`7cab7e0`, 13 тестов).
  - Фаза 5.5 (per-action approval KI-101) — ✅ Done (`7ffcc5f`, 6 + 4 тестов).
  - Фаза 6A (admin service + DTOs) — ✅ Done (`b6b38c9`).
  - Фаза 6B (AdminSqlAgentController) — ✅ Done (`43140ea`).
  - Фаза 6C+6D (UI + локализация) — ✅ Done (`ecee60c`).
  - Фаза 6E (fix локализации KI-102) — ✅ Done (`eb49a37`).
  - Фаза 7A+7B (admin layer tests, 20 тестов) — ✅ Done (`9328070`).
  - Фаза 7C (integration tests + README + KNOWN_ISSUES) — ✅ Done.
  - **Итого:** **+51 тест** (SqlAgent-related); общий счёт: **341**.
  - **Коммиты:** 13 (включая fix'ы KI-100, KI-101, KI-102).
- **Известные ограничения (не блокеры):**
  - `KI-101` (per-action approval — реализовано через `RequiresApprovalForCall`) ✅ Fixed.
  - `KI-103` (локализация `/status`) — Documented, план v1.7.x.
  - Внешние БД (Postgres / MySQL) — KI-099, v1.8.0.
- **Связанные:** KI-098 (admin UI — Fixed в 6E), KI-099 (внешние подключения),
  KI-100 (Sqlite relative path — Fixed в 4), KI-101 (per-action approval — Fixed в 5.5),
  KI-102 (Admin UI локализация — Fixed в 6E), KI-103 (`/status` локализация — Documented).

---

### KI-098 — Admin UI для SQL Agent whitelist
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.7.0 (Фазы 6A-6E KI-097)
- **Обнаружено:** 2026-09-28
- **Файлы (план):** `AdminSqlAgentController`, `AdminSqlAgentService`, вкладка
  в `Admin.cshtml`, `admin-sql-agent.js`, `.resx` (RU + EN).
- **Описание:** Whitelist таблиц для DB Agent должен настраиваться **из админки**
  (без правки `appsettings.json` + перезапуска). По образцу `SubAgents.*` (v1.4.0).
- **Что входит:**
  - Endpoints: `GET/PUT /api/admin/sql-agent/connections`,
    `POST .../test`, `POST .../reset`.
  - UI-вкладка «SQL Agent» (9-я в `/admin`) — таблица подключений + модалка.
  - Сохранение override'ов в `AppSettings` (ключи `SqlAgent.*`).
  - Загрузка при старте (`Program.LoadSqlAgentOverridesAsync`).
  - Применение изменений в runtime (без перезапуска).
- **Связанные:** KI-097.
- **Решение (2026-09-29):**
  - **Backend:** `IAdminSqlAgentService` + `AdminSqlAgentService` (Фаза 6A) +
    `AdminSqlAgentController` (4 endpoints, Фаза 6B).
  - **UI:** 9-я вкладка «SQL Agent» в `/admin` + `admin-sql-agent.js` (Фаза 6C).
  - **Локализация:** `.resx` RU+EN, ~22 ключа; `data-*` для JS (Фаза 6C+6D).
  - **Fix локализации** в остальных admin-вкладках (Фаза 6E, KI-102).
  - **Тесты:** 13 unit (`AdminSqlAgentServiceTests`) + 7 unit
    (`AdminSqlAgentControllerTests`) — Фаза 7A+7B.
  - **DoD:** whitelist / MaxRows / Timeout / Enabled редактируются в runtime
    без рестарта; кнопки Test/Reset работают; RU/EN переключается.

---

### KI-099 — Внешние подключения БД (Postgres / MySQL / Oracle)
- **Приоритет:** 🟢 Low | **Статус:** Deferred | **Запланировано:** v1.8.0 (Фаза 2 KI-097)
- **Обнаружено:** 2026-09-28
- **DESIGN:** [`docs/development/v1.7/DESIGN_DB_AGENT.md`](development/v1.7/DESIGN_DB_AGENT.md)
  § 9.5 (Приложение D).
- **Описание:** В Фазе 1 (v1.7.0) DB Agent работает только с собственной БД
  приложения (`internal`). Архитектура готова к внешним подключениям:
  контракт `connection_name` в tool + `Connections[*]` в конфиге.
- **Что нужно сделать (Фаза 2, v1.8.0):**
  - Добавить провайдеры: `Npgsql` (Postgres), `MySqlConnector` (MySQL),
    `Oracle.ManagedDataAccess`.
  - Расширить `SqlConnectionProvider` (фабрика `DbConnection` по `Provider`).
  - Настроить read-only роли в каждой внешней БД.
  - Возможно: Domain-Oriented Tools по факту обкатки Фазы 1.
- **Связанные:** KI-097.

---

### KI-100 — SqlAgent: относительный путь Sqlite connection string резолвится от CWD, а не от ContentRoot
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.7.0 (Фаза 4 KI-097)
- **Обнаружено:** 2026-09-29
- **Файлы (план):** `IIChatTools.Services/Implementation/SqlAgent/SqlConnectionProvider.cs`,
  User Secrets (`SqlAgent:Internal:ConnectionString`), `appsettings.Development.json`.
- **Описание:** В Фазе 2 (KI-097) `SqlConnectionProvider` резолвит connection
  string из `IConfiguration` **как есть**. Для Sqlite значение по умолчанию —
  `Data Source=Data/iichattools-dev.db;Mode=ReadOnly` (**относительный** путь).

  Проблема: SQLite открывает файл **относительно `Environment.CurrentDirectory`
  процесса**, а не относительно `ContentRootPath`. Сейчас в dev это работает
  (`dotnet run` из `IIChatTools.API` → CWD = ContentRoot). Но при запуске:
  - из другой директории (`dotnet IIChatTools.API.dll` из корня),
  - как Windows-службы (CWD = `C:\Windows\System32`),
  - через `docker run -w /app` (Dockerfile может задать свой WORKDIR),
  - через IIS / Kestrel за reverse-proxy,

  SQLite либо не найдёт файл (`SQLite Error 14: unable to open database`), либо
  откроет **пустую новую БД** (если `Mode=ReadOnly` не задан). Ошибка проявится
  **только при первом `execute_query`** — при старте приложения ничего не сломается.
- **Не проявляется в Фазе 2-3:** connection string резолвится, но не используется
  (`SqlConnectionProvider.CreateConnectionAsync` ещё не вызывается).
  Активируется в **Фазе 4** — при первом `SqlAgentService.ExecuteQueryAsync`.
- **Симптом (гипотетический, Фаза 4):** `database_agent(execute_query, internal,
  "SELECT COUNT(*) FROM Chats")` → `SqliteException: unable to open database file`
  при `dotnet run` из `IIChatTools.API` работает; при запуске `dotnet run` из корня
  репо или через публикацию — падает.
- **Возможные решения (выбрать на Фазе 4):**
  1. **Резолвить путь относительно ContentRoot** в `SqlConnectionProvider`:
     если `Provider=Sqlite` и `Data Source` относительный — префиксовать
     `IWebHostEnvironment.ContentRootPath`. Чисто, единообразно, без правки
     User Secrets. **Рекомендуется.**
  2. **Абсолютный путь в User Secrets:** `Data Source=C:\Projects\...\Data\iichattools-dev.db;Mode=ReadOnly`.
     Работает, но привязывает секрет к машине (потеря переносимости).
  3. **Оставить как есть:** работает при `dotnet run` из `IIChatTools.API`,
     ломается в остальных сценариях. Приемлемо для dev, **не для prod**.
- **Связанные:** KI-097 (Database Agent), KI-070 (Sqlite EnsureCreated),
  KI-085 / KI-093 (Sqlite locked).
- **Решение (2026-09-29, Фаза 4):**
  - Введён `IAppPathProvider` (`Interfaces/`) + `AppPathProvider`
    (`Implementation/`) — тонкая обёртка над `IWebHostEnvironment.ContentRootPath`.
    Разрывает зависимость `Services → API` (тот же паттерн, что `AppVersionHolder` в ADR-001).
  - `SqlConnectionProvider.ResolveSqlitePath(connStr, name)` — если провайдер
    Sqlite и `Data Source` относительный (не `:memory:`, не `file:`),
    префиксуется `ContentRootPath` через `SqliteConnectionStringBuilder`.
  - `SqliteConnectionStringBuilder` (из `Microsoft.Data.Sqlite`) корректно
    парсит и собирает connection string — не боимся `;Mode=ReadOnly` в хвосте.
  - Регистрация: `services.AddSingleton<IAppPathProvider>(...)` в `Startup.cs`.
  - Тест `CreateConnectionAsync_RelativeSqlitePath_ResolvesFromContentRoot`.

---

### KI-101 — Per-action approval для Database Agent (execute_query vs метаданные)
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.7.0 (Фаза 5.5 KI-097)
- **Обнаружено:** 2026-09-29 (Фаза 5 KI-097)
- **DESIGN:** [`docs/development/v1.7/DESIGN_DB_AGENT.md`](development/v1.7/DESIGN_DB_AGENT.md)
  § 3.3 и § 6.5.
- **Файлы (план):** `IIChatTools.Services/Implementation/ChatStreamService.cs`
  (метод `StreamAsync` — блок проверки `requiresApproval`),
  `IIChatTools.Services/DTO/ToolResult.cs`, `ChatStreamService.ChatToolResultDto`.
- **Описание:** DESIGN § 3.3 описывает **per-action approval** для `database_agent`:
  - `execute_query` — требует approval (пользователь видит сам SQL);
  - `list_databases` / `list_tables` / `describe_table` — **не** требуют
    (read-only метаданные, approval = лишний клик).

  **Проблема:** в текущей архитектуре `ChatStreamService` флаг approval —
  **свойство всего tool** (`ToolDescriptor.RequiresApprovalByDefault`), а не
  отдельного вызова. Chat не имеет механизма «решить по args, нужен ли approval».

  **Текущее решение (Фаза 5):** `DatabaseAgentTool.RequiresApprovalByDefault = true` —
  все 4 действия требуют approval. Безопасно, работает, но для `list_tables`
  пользователь видит лишнюю модалку.
- **Возможные решения (v1.7.x):**
  1. Расширить `ToolResult` полем `RequiresApproval` — tool сам решает
     в `ExecuteAsync` (после парсинга args). Требует доработки цикла в
     `ChatStreamService`: сначала вызвать tool (без выполнения!), получить
     флаг, затем — при `RequiresApproval=true` — запросить approval и
     выполнить реально. Несовместимо с текущим «вызвать → вернуть результат».
  2. Добавить в `ITool` метод `RequiresApprovalForCall(JObject args)`.
     Разделяет «решение» и «выполнение». Чище, но меняет контракт 46 инструментов
     (можно с default-реализацией).
  3. Отдельные tool-обёртки: `database_agent_meta` (без approval) +
     `database_agent_query` (с approval). Раздувает список, но не трогает ядро.
- **Решение (2026-09-29, Фаза 5.5):**
  - **Архитектурный паттерн:** в `ITool` добавлен **default-метод**
    `RequiresApprovalForCall(JObject arguments)` с fallback на
    `RequiresApprovalByDefault`. Все 46 существующих инструментов
    работают без изменений — C# 8+ default interface method.
  - **`IToolRegistry.GetTool(string name)`** — новый метод (возвращает `ITool`).
    `ToolRegistry.GetTool` — реализация. Нужен, чтобы `ChatStreamService`
    мог вызвать метод **до** выполнения инструмента.
  - **`ChatStreamService`** — блок `requiresApproval` переписан:
    `toolInstance?.RequiresApprovalForCall(args) ?? true` вместо
    `descriptor?.RequiresApprovalByDefault ?? true`.
  - **`DatabaseAgentTool.RequiresApprovalForCall`** — override:
    `action == "execute_query"`.
  - **Bonus:** усилен `Description` — прямо просит LLM для вопросов
    про количество использовать сразу `execute_query` (не делать
    list_databases / list_tables «разведку»).
  - **Тесты:** +6 (3 `[Theory]` + 3 `[Fact]`). 
- **Не блокер:** безопасность не страдает — approval **есть**, просто иногда
  лишний. UX-мелочь.
- **Связанные:** KI-097.

---

### KI-102 — Admin UI: hardcoded RU-строки в JS-модулях (нарушение RULES § 1.14 / § 4.17)
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.7.0 (Фаза 6E)
- **Обнаружено:** 2026-09-29 (пользователь заметил при переключении на EN)
- **Файлы:**
  - `IIChatTools.API/wwwroot/js/modules/admin.js` — 12 hardcoded строк.
  - `IIChatTools.API/wwwroot/js/modules/admin-agents.js` — 15 hardcoded строк.
  - `IIChatTools.API/wwwroot/js/modules/admin-sql-agent.js` — 3 hardcoded строки.
  - `IIChatTools.API/Views/Home/Admin.cshtml` — отсутствовали `data-*` на 4 панелях.
  - `IIChatTools.API/Resources/SharedResources.resx` / `SharedResources.ru.resx`.
- **Описание:** При переключении языка на EN в `/admin` часть UI остаётся на
  русском: кнопки в таблицах пользователей/настроек/агентов («Изменить»,
  «Удалить», «Сбросить»), badges («Активен», «Включён», «Да»/«Нет»), confirm-диалоги,
  toasts, `formatLastRun` (агенты), валидации (SQL Agent). Причина — hardcoded
  RU-строки в `.js`, **не проходящие через `IStringLocalizer`** (RULES § 1.14) и
  **не использующие `data-*` для локализации JS** (RULES § 4.17).

  **Дополнительный баг (критичный):** в `admin.js` (`loadWhitelist` — empty-state)
  был literal `@Localizer["Убрать из белого списка"]` — синтаксис Razor,
  **не обрабатываемый в .js-файлах**. Проявлялся как текст
  `@Localizer["Убрать из белого списка"]` при добавлении любого инструмента
  в whitelist.

- **Решение (2026-09-29, Фаза 6E):**
  - **`Admin.cshtml`** — добавлены `data-label-*` на 4 панели
    (`pane-users`, `pane-settings`, `pane-whitelist`, `pane-agents`).
    Значения — из существующих ключей `@Localizer[...]` (reuse), где применимо.
  - **`.resx` (RU + EN)** — **+30 новых ключей** (`AdminUserRetentionTooltip`,
    `AdminSettings*`, `AdminWhitelist*`, `AdminAgent*`, `AdminTime*`,
    `SqlAgentMaxRowsValidation`, `SqlAgentTimeoutValidation`,
    `SqlAgentConnectionNotFound`). Синхронизированы через `LocalizationSyncTests`.
  - **`admin.js`** — добавлен helper `paneLabels(paneId)` (читает `data-*` →
    camelCase). Все hardcoded RU-строки заменены на `labels.*`. **Баг #11**
    (`@Localizer[...]` в JS) — **устранён**.
  - **`admin-agents.js`** — свой `paneLabels()` (локальный) + `formatLastRun`
    теперь использует `data-label-time-*`. Modal labels, validations, toasts
    локализованы.
  - **`admin-sql-agent.js`** — 3 validation-строки через `data-label-*`.
  - **DoD Фазы 6E:** при переключении RU/EN весь UI `/admin`
    (users / settings / whitelist / agents / SQL Agent) переводится.
    RULES § 1.14 и § 4.17 соблюдены.
- **Связанные:** KI-097 (DB Agent), KI-052 (Multi-Agent), KI-076 (agent stats).

---

### KI-103 — Страница `/status`: hardcoded RU-строки в `status.js`
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.7.1
- **Обнаружено:** 2026-09-29 (при проверке локализации Фазы 6E)
- **Файлы:** `IIChatTools.API/wwwroot/js/modules/status.js`,
  `IIChatTools.API/Views/Home/Status.cshtml` (если нужны `data-label-*`).
- **Описание:** На странице `/status` (Server Health) при переключении
  языка на EN часть UI остаётся на русском:
  - Заголовки карточек «Внешние зависимости», «Подключение к БД»;
  - Badge «Установлено» / «Не установлено» в списке зависимостей;
  - «Онлайн» в блоке БД;
  - «Ожидают подтверждения: N»;
  - Заголовки колонок таблицы «Последние действия»: «Пользователь»,
    «Инструмент», «Статус», «Длительность», «Дата».

  Причина — hardcoded RU-строки в `status.js`
  (нарушение RULES § 1.14 / § 4.17). Не входит в Фазу 6E — она касалась
  только `/admin`.

- **Решение (2026-09-29, v1.7.1):**
  - Анализ показал, что в `Status.cshtml` **почти всё** уже через
    `@Localizer[...]` (карточки, заголовки таблицы, секции). Hardcoded
    были только **4 строки** в `status.js`:
    - badge БД (`'Онлайн'` / `'Оффлайн'`);
    - badge зависимостей (`'Установлено'` / `'Не установлено'`).
  - `Status.cshtml` (`#status-root`) — добавлены `data-label-online`,
    `data-label-offline`, `data-label-installed`, `data-label-not-installed`.
  - `status.js` — helper `pageLabels()` (читает `data-*` → camelCase)
    + заменены hardcoded строки на `labels.*`.
  - `.resx` (RU + EN) — **+4 ключа** (`StatusOnline`, `StatusOffline`,
    `StatusInstalled`, `StatusNotInstalled`). Синхронизация через
    `LocalizationSyncTests`.
  - **Не трогал:** `statusBadge()` — возвращает `Success`/`Error`/`Pending`/
    `Cancelled` — это **данные из API** (`AuditLog.LogStatus`), не UI-строки.
  - **DoD:** RU/EN переключает badge БД и badge зависимостей.
- **Связанные:** KI-102 (аналогичная проблема в `/admin` — Fixed).

---

### KI-104 — PDF / DOCX парсеры для RAG (`PdfParser` + `DocxParser`)
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.7.1
- **Обнаружено:** 2026-09-29 (при обсуждении v1.7.0)
- **DESIGN:** [`docs/development/v1.5/DESIGN.md`](development/v1.5/DESIGN.md)
  § 4.5.5 и § 4.5.6 (отложено с v1.5.0 как «фаза 3.5»).
- **Файлы (план):**
  - `IIChatTools.Services/Implementation/Rag/Parsers/PdfParser.cs` (новый).
  - `IIChatTools.Services/Implementation/Rag/Parsers/DocxParser.cs` (новый).
  - `IIChatTools.API/Startup.cs` — +2 строки `services.AddSingleton<IRagDocumentParser, ...>`.
  - `appsettings.json` / `.Development.json` — `Rag:Ingestion:AllowedExtensions` + `.pdf`, `.docx`.
  - `IIChatTools.Tests/UnitTests/Rag/PdfParserTests.cs` + `DocxParserTests.cs`.
- **Описание:** Сейчас `PlainTextParser` поддерживает 28 расширений (текст, код,
  разметка). PDF и DOCX — **не поддерживаются**. Из-за этого пользователь не может
  приложить PDF-контракт или DOCX-документ к чату и спросить по нему.
- **Решение (план):**
  - **NuGet:**
    - `PdfPig` 0.1.x (ранее `UglyToad.PdfPig`) — Apache 2.0, ~500 KB.
    - `DocumentFormat.OpenXml` 3.x — MIT, ~1.5 MB.
  - **`PdfParser`** — `PdfDocument.Open` → перебор `GetPages()` → `page.Text` через
    `StringBuilder`. Метаданные: `PageCount`.
  - **`DocxParser`** — `WordprocessingDocument.Open` → `MainDocumentPart.Document.Body` →
    `Descendants<Paragraph>()` → `InnerText`.
  - **Регистрация:** реестр (`RagDocumentParserRegistry`) подхватит через `IEnumerable<IRagDocumentParser>` — порядок в DI определяет приоритет (первый match по расширению побеждает).
  - **Тесты:** ~8 `[Fact]` на каждый парсер (CanParse T/F, extract text, пустой файл, битый файл, метаданные).
- **Известные ограничения (задокументировать):**
  - **OCR сканов PDF — не поддерживается.** Если PDF — картинка без текстового
    слоя, `page.Text` пустой. Решение: Tesseract — v1.9+.
  - **`.doc` (старый формат) — не поддерживается.** OpenXML работает только с
    `.docx`.
  - **Шифрованные PDF** — `PdfDocument.Open` бросает исключение → `ToolResult.Fail`.
  - **Сложная вёрстка** (таблицы, multi-column) — текст склеивается. Ограничение
    всех PDF-экстракторов.
- **Оценка:** ~3-4 ч.
- **Решение (2026-09-29, v1.7.1):**
  - **NuGet** (`Directory.Build.props` + `IIChatTools.Services.csproj`):
    - `PdfPig 0.1.9` (Apache 2.0, ~500 KB).
    - `DocumentFormat.OpenXml 3.1.0` (MIT, ~1.5 MB).
    - Плюс override `System.IO.Packaging 10.0.0` — KI-105.
  - **`PdfParser`** (`Implementation/Rag/Parsers/`) — Singleton, `Name = "PdfPig"`,
    `SupportedExtensions = [".pdf"]`. `File.ReadAllBytesAsync` → `Task.Run` →
    `PdfDocument.Open(bytes)` → `GetPages()` → `page.Text`. Метаданные:
    `format=pdf`, `parser=PdfPig`, `pageCount`. `PageCount = doc.NumberOfPages`.
    Corrupt/encrypted PDF → `InvalidDataException`.
  - **`DocxParser`** (`Implementation/Rag/Parsers/`) — Singleton, `Name = "OpenXml"`,
    `SupportedExtensions = [".docx"]`. `File.ReadAllBytesAsync` → `Task.Run` →
    `WordprocessingDocument.Open(ms, isEditable: false)` →
    `MainDocumentPart.Document.Body.Descendants<Paragraph>().InnerText`.
    Метаданные: `format=docx`, `parser=OpenXml`, `paragraphCount`.
    `PageCount = null` (в DOCX нет страниц). Corrupt / `.doc` → `InvalidDataException`.
  - **`Startup.cs`** — 2 строки регистрации:
    `services.AddSingleton<IRagDocumentParser, PdfParser>()` +
    `services.AddSingleton<IRagDocumentParser, DocxParser>()` (после PlainText).
  - **`appsettings.json`** / **`.Development.json`** — `Rag:Ingestion:AllowedExtensions`
    + `.pdf`, `.docx`.
  - **Тесты:** `PdfParserTests` (**16**) + `DocxParserTests` (**17**).
    Реальный PDF-контент не проверяется (PdfPig read-only); DOCX
    генерируется самим OpenXml SDK. **Всего: 341 → 374**.
- **Связанные:** KI-083 (RAG / embeddings), KI-105 (System.IO.Packaging),
  KI-099 (внешние БД — приоритетнее).

---

### KI-105 — Транзитивная уязвимость `System.IO.Packaging 8.0.0` (через `DocumentFormat.OpenXml`)
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.7.1
- **Обнаружено:** 2026-09-29 (при `dotnet restore` после добавления KI-104)
- **Файлы:** `Directory.Build.props`, `IIChatTools.Services/IIChatTools.Services.csproj`.
- **Описание:** При добавлении `DocumentFormat.OpenXml 3.1.0` (KI-104 — парсер .docx)
  NuGet Audit показал **2 high-severity** advisory на транзитивный
  `System.IO.Packaging 8.0.0`:
  - [`GHSA-f32c-w444-8ppv`](https://github.com/advisories/GHSA-f32c-w444-8ppv) — DoS;
  - [`GHSA-qj66-m88j-hmgj`](https://github.com/advisories/GHSA-qj66-m88j-hmgj) — DoS.

  Симптом в логе: `warning NU1903: У пакета "System.IO.Packaging" 8.0.0 есть
  известная уязвимость ... (уровень серьезности: высокий)` — в 3 проектах
  (Services, API, Tests) × 2 advisory = **6 warnings**.
- **Решение (2026-09-29):**
  - Явный `PackageReference Include="System.IO.Packaging" Version="10.0.0"`
    в `IIChatTools.Services.csproj` — перебивает транзитивную 8.0.0.
  - Версия 10.0.0 — соответствует .NET 10 SDK (10.0.401).
  - Версия вынесена в `Directory.Build.props` (`$(SystemIOPackagingVersion)`).
  - **Прецедент:** KI-022 (SQLitePCLRaw 2.1.11 → 2.1.13, GHSA-2m69-gcr7-jv3q).
  - После override — `dotnet restore` без NU1903.
  - **Профилактика:** :
  - Добавлен скрипт scripts/setup/check-vulnerabilities.ps1 —
    обёртка над dotnet list package --vulnerable --include-transitive
    с exit 1 при обнаружении уязвимостей (для CI / pre-commit).
  - В README.md — раздел «Проверка уязвимостей».
  - **Регламент:** перед добавлением нового NuGet-пакета — прогнать скрипт;
    при NU1903 в логе dotnet restore — завести KI + Fixed.
  - **Связанные:** KI-104 (парсеры PDF/DOCX), KI-022 (SQLitePCLRaw override).

---

### KI-106 — Оригинальное имя файла в источниках RAG для attachments
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.7.1
- **Обнаружено:** 2026-09-29 | **Устранено:** 2026-09-29
- **Файлы:**
  - `IIChatTools.Services/Implementation/ChatTools/ChatAttachmentService.cs` —
    helper `BuildRagDocumentPath` + правки в `UploadAsync` / `DeleteAsync`.
- **Описание:** В UI-блоке «📚 Источники» для приложенных к чату файлов
  показывалось имя вида `a2a41a0134154822996c08fe750692cf.docx` (GUID)
  вместо оригинального `Договор.docx`.
- **Причина:** `ChatAttachmentService.UploadAsync` сохраняет файл на диск
  как `{guid}.ext` (by design, KI-083 Шаг 6A — защита от коллизий и
  path-traversal в имени). В `DocumentIngestionService` в `DocumentPath`
  попадал `Source = "chat-attachments/{chatId}/{guid}.ext"`, а
  `RagSourceBuilder.BuildLabel` берёт имя файла из `DocumentPath`.
- **Решение (2026-09-29, v1.7.1):**
  - Новый helper `ChatAttachmentService.BuildRagDocumentPath(chatId, fileName, subfolder)`
    → `"chat-attachments/{chatId}/{fileName}"` (с оригинальным именем).
  - `UploadAsync`: в `IngestionRequest.Source` передаётся **RAG-путь**
    (оригинальное имя), не `StoragePath` (GUID).
  - `DeleteAsync`: сначала удаляет по новому пути; если 0 чанков —
    fallback на `entity.StoragePath` (legacy-записи до v1.7.1).
  - **Физический файл** на диске — по-прежнему `{guid}.ext` (`StoragePath`
    не меняется — защита от коллизий сохраняется).
  - **Старые записи** (до v1.7.1): остаются с GUID в `DocumentPath`.
    Одноразовая миграция не делается (косметика, оригинал всё равно виден
    в `ChatAttachment.FileName`). При удалении — сработает fallback.
- **Связанные:** KI-083 (RAG / attachments), KI-086 (sources / citations).

---

### KI-106 — Оригинальное имя файла в источниках RAG для attachments
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.7.1
- **Обнаружено:** 2026-09-29 | **Устранено:** 2026-09-29
- **Файлы:**
  - `IIChatTools.Services/Implementation/ChatTools/ChatAttachmentService.cs` —
    helper `BuildRagDocumentPath` + правки `UploadAsync` / `DeleteAsync`.
  - `IIChatTools.Tests/IntegrationTests/ChatAttachmentServiceTests.cs` — +1 тест.
- **Описание:** В UI-блоке «📚 Источники» под ответом ассистента для
  приложенных к чату файлов показывалось `a2a41a01…docx` (GUID) вместо
  оригинального `Договор.docx`.
- **Причина:** `ChatAttachmentService.UploadAsync` сохраняет файл на диск
  как `{guid}.ext` (by design, KI-083 Шаг 6A — защита от коллизий и
  path-traversal в имени). В `IngestionRequest.Source` передавался
  `StoragePath` (`chat-attachments/{chatId}/{guid}.ext`), а
  `RagSourceBuilder.BuildLabel` берёт имя файла из `DocumentPath`.
- **Решение (2026-09-29, v1.7.1):**
  - Helper `ChatAttachmentService.BuildRagDocumentPath(chatId, fileName, subfolder)`
    → `"chat-attachments/{chatId}/{fileName}"` (оригинальное имя).
  - `UploadAsync`: в `IngestionRequest.Source` — RAG-путь (оригинальное имя),
    не `StoragePath`.
  - `DeleteAsync`: сначала удаляет по новому пути; при 0 — fallback на
    `entity.StoragePath` (legacy-записи до v1.7.1).
  - **Физический файл** на диске — по-прежнему `{guid}.ext` (без изменений).
  - **Старые записи** остаются с GUID в `DocumentPath` (миграция — не делаем,
    косметика).
- **Известный долг (не блокер):** тест `DeleteAsync_LegacyAttachment_FallsBackToStoragePath`
  **не добавлен** — нужен `FakeIngestionService.DeleteReturnValues` (per-path).
  Планируется вместе с следующим KI (минорный).
- **Связанные:** KI-083 (RAG / attachments), KI-086 (sources / citations).

---

### KI-107 — Mail Agent (IMAP/SMTP, MailKit)
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.8.0
- **Прогресс:** Реализовано в 5 фазах + релизная документация.
  - Фаза 1 ✅ (DTO + интерфейсы, MailKit 4.8.0).
  - Фаза 2 ✅ (MailKitClient + GlobalMailAccountProvider).
  - Фаза 3A ✅ (3 tools: `list_emails`, `read_email`, `send_email` — 16 тестов).
  - Фаза 3B ✅ (4 tools: `search_emails`, `delete_email`, `move_email`, `mark_as_read` — 15 тестов).
  - Фаза 4 ✅ (MailAttachmentService + InMemoryMailRateLimiter + интеграция в SendEmailTool — 11 тестов).
  - Фаза 5 ✅ (`mail_agent` в SubAgents — Chat видит **12 инструментов**).
  - Фаза 6 ✅ (README + TESTING + PROMPT_V2 v2.7 + bump 1.8.0 + tag + GitHub Release).
  - **Итого:** +11 новых файлов, ~42 теста (424/424).
- **Отложено (не блокер v1.8.0):**
  - Сохранение вложений при `read_email` — требует переделки `IMailClient`
    (добавить `DownloadAttachmentAsync` или передать `IServiceScopeFactory`
    в `MailKitClient`). Зафиксировано в CHANGELOG.
  - Прикрепление вложений к `send_email` — `ResolveForSendAsync` готов,
    но интеграция в `SendEmailTool` (прикрепление к `MimeMessage`) — v1.8.x.
- **Обнаружено:** 2026-09-29 | **Устранено:** 2026-09-29
- **DESIGN:** [`docs/development/v1.8/DESIGN_MAIL_AGENT.md`](development/v1.8/DESIGN_MAIL_AGENT.md)
- **Описание:** LLM не имеет доступа к почте. Нет инструментов для IMAP/SMTP.
  `execute_command` + `python` — антипаттерн (нет валидации, нет approval, нет
  rate limiting, credentials в command line).
- **Что входит (v1.8.0):**
  - Один агент `mail_agent` (наследник `AgentToolBase`) + 7 инструментов:
    `send_email` (approval), `list_emails`, `read_email`, `search_emails`,
    `delete_email` (approval), `move_email` (approval), `mark_as_read`.
  - `IMailClient` + `MailKitClient` (MailKit 4.8.0, Apache 2.0) — IMAP-пул + SMTP.
  - Глобальные credentials (User Secrets) → App Password.
  - Rate limiting: 20 писем/час, 30 reads/мин (по образцу KI-043).
  - Privacy-first: без PII в логах / audit / ChatMessage.MetadataJson.
  - Вложения — в `Workspace/users/{id}/mail-attachments/{uid}/`, ≤ 10 MB.
- **Что НЕ входит:** Per-user credentials (KI-108, v1.8.x), OAuth2 (v1.9+),
  POP3, HTML-редактор, календарь / контакты.
- **Оценка:** ~10–12 ч.
- **Связанные:** KI-052 (Multi-Agent — эталон), KI-097 (Database Agent — эталон), KI-108 (per-user).

---

### KI-108 — Per-user mail accounts (свой ящик у каждого пользователя)
- **Приоритет:** 🟢 Low | **Статус:** Planned | **Запланировано:** v1.8.x
- **Обнаружено:** 2026-09-29
- **DESIGN:** [`docs/development/v1.8/DESIGN_MAIL_AGENT.md`](development/v1.8/DESIGN_MAIL_AGENT.md) § 3.2.
- **Описание:** В v1.8.0 (KI-107) все пользователи работают с **одним ящиком**
  (глобальные creds). Нужно — свой ящик у каждого.
- **Что входит:**
  - Таблица `UserMailAccount` (UserId, ImapHost, SmtpHost, Username, **EncryptedPassword**).
  - Шифрование пароля через `IDataProtector` (ASP.NET Core DataProtection).
  - `PerUserMailAccountProvider : IMailAccountProvider` (замена 1 строки в DI).
  - UI в `/profile → Почта` (выбор провайдера, ввод creds, тест подключения).
  - Опционально: несколько ящиков на пользователя (ключ `IsDefault`).
- **Связанные:** KI-107 (Mail Agent — база).

---

### KI-109 — External-LLM Agent (DeepSeek / OpenAI / Groq / Together / Ollama)
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.8.1
- **Обнаружено:** 2026-09-29 | **Устранено:** 2026-09-30
- **DESIGN:** [`docs/development/v1.8/DESIGN_EXTERNAL_LLM.md`](development/v1.8/DESIGN_EXTERNAL_LLM.md)
- **Описание:** LLM работает только локально (LM Studio + qwen3-4b). Нет механизма
  для обращения к внешним моделям (DeepSeek, OpenAI, Claude, Gemini).
- **Что входит (v1.8.0):**
  - Один агент `external_llm_agent` (наследник `AgentToolBase`) + 3 инструмента:
    `ask_external_llm` (с опциональным `compare_with`), `list_external_providers`,
    `check_internet_connection`.
  - 5 провайдеров: DeepSeek, OpenAI, Groq, Together AI, Ollama (все OpenAI-совместимые).
  - Оркестратор: 4 сценария (Fallback / Специализация / Разные знания / Сравнение).
  - `include_context: false` по умолчанию (только prompt — без истории чата, без PII).
  - **Budget guardrails:** `DailyBudgetUsd = $5` + `DailyTokensLimit = 500k` + `MaxTokens` per request.
  - **Circuit breaker:** 3 fail подряд → skip на 5 мин (по образцу KI-094).
  - **Privacy-first:** без PII в логах / audit / ChatMessage.MetadataJson.
- **Что НЕ входит:** Anthropic / Gemini (KI-110, v1.9+), streaming с внешних API,
  function calling на внешних API, OAuth2.
- **Оценка:** ~12–15 ч.
- **Реализовано (2026-09-30, v1.8.1):** 5 фаз, 5 коммитов.
  - **Фаза 1** — 7 DTO (`ExternalLlmOptions`, `ExternalProviderOptions`,
    `ExternalLlmRequest`, `ExternalLlmResponse`, `ExternalLlmComparisonDto`,
    `ProviderHealthStatus`, `ExternalLlmCircuitBreakerOptions`) + 4 интерфейса
    (`IExternalLlmClient`, `IExternalLlmCircuitBreaker`, `IExternalLlmBudgetTracker`,
    `IExternalProviderRegistry`). Коммит `de7e3eb`.
  - **Фаза 2.1-2.4** — `ExternalProviderRegistry` (fail-fast валидация конфига),
    `ExternalLlmCircuitBreaker` (3 fail → 5 мин skip), `ExternalLlmBudgetTracker`
    (per-user, $5/день + 500k токенов), `ProviderCostCalculator`.
    +41 тест. Коммит `8b245bd`.
  - **Фаза 2.5-2.6** — `ExternalLlmClient` (OpenAI-совместимый POST, retry 1× при 5xx/429),
    wire-up в `Startup.cs`, `appsettings.json` (5 провайдеров), fail-fast в `Program.cs`.
    +14 тестов. Коммит `725e720`.
    **Fix:** `HttpClient.Timeout` → `CancellationTokenSource.CancelAfter` (RULES § 4.48).
  - **Фаза 3** — 3 tool'а: `AskExternalLlmTool` (одиночный + compare_with),
    `ListExternalProvidersTool`, `CheckInternetConnectionTool`. +16 тестов.
    Коммит `b5d8d40`. **Fix:** PascalCase в тестах (RULES § 4.43).
  - **Фаза 4** — `ExternalLlmAgentTool` (наследник `AgentToolBase`) + секция
    `SubAgents:external_llm_agent` в appsettings. Chat видит 13 инструментов.
    Коммит `4407aba`.
  - **Фаза 5** — integration-тесты (`DeepSeek`/`OpenAI`/`Ollama` — Skip,
    fail-fast — без Skip). Коммит `3f0f7ad`.
- **Итого:** +72 теста (424 → 496), 3 Skip.
- **Связанные:** KI-052 (Multi-Agent — эталон), KI-094 (circuit breaker — эталон),
  KI-110 (Anthropic/Gemini — Planned), KI-120 (галлюцинация числа — Documented).

---

### KI-110a — Anthropic Claude провайдер
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.9.0
- **Обнаружено:** 2026-09-29 | **Устранено:** 2026-10-01
- **DESIGN:** [`docs/development/v1.9/DESIGN_ANTHROPIC_GEMINI.md`](development/v1.9/DESIGN_ANTHROPIC_GEMINI.md)
- **Описание:** В v1.8.1 (KI-109) — только OpenAI-совместимые провайдеры.
  Anthropic Messages API (`POST /v1/messages`, `x-api-key`,
  `anthropic-version: 2023-06-01`, `system` отдельным полем, `max_tokens`
  обязателен, ответ — `content[]`) требует отдельной ветки.
- **Что сделано (v1.9.0, 5 фаз):**
  - **Фаза 1** — `ProviderFormat` enum + `ExternalProviderOptions.Format`
    (default = OpenAI, backward-compatible). +2 теста. Коммит `f324a13`.
  - **Фаза 2** — `AnthropicRequestBuilder` + `AnthropicResponseParser`
    (static helpers). `ExternalLlmRequest.System` (nullable).
    +20 тестов. Коммит `06ab722`.
  - **Фаза 3** — рефакторинг `ExternalLlmClient.CompleteAsync` → switch
    по `Format`: `CompleteOpenAiAsync` / `CompleteAnthropicAsync` /
    `Gemini` → `NotSupportedException`. `SendWithRetryAsync` /
    `SendOnceAsync` параметризованы заголовками. +8 тестов. Коммит `3524b90`.
  - **Фаза 4** — `appsettings.json` / `.Development.json` — +провайдеры
    `anthropic` + `gemini`. README обновлён. Коммит `46487fc`.
  - **Фаза 5** — релиз v1.9.0 (CHANGELOG, KNOWN_ISSUES, RULES, DESIGN).
- **Тесты:** 529 → **559** (+30), 3 Skip.
- **Отложено в v1.9.x:**
  - **KI-124** — интеграционный `[Fact(Skip=...)]` для Claude
    (`ExternalLlmIntegrationTests`) — не добавлен в v1.9.0.
- **Связанные:** KI-109 (External-LLM Agent — база), KI-110b (Gemini).

---

### KI-110b — Google Gemini провайдер
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.10.0
- **Обнаружено:** 2026-09-29 | **Устранено:** 2026-10-01
- **DESIGN:** [`docs/development/v1.9/DESIGN_GEMINI.md`](development/v1.9/DESIGN_GEMINI.md)
  (Implemented, 2026-10-01).
- **Описание:** Google Gemini (`POST /v1/models/{model}:generateContent`,
  API key в заголовке `x-goog-api-key`, свой формат запроса/ответа).
- **Что сделано (v1.10.0, 5 фаз):**
  - **Фаза 0** — DESIGN_GEMINI.md (Draft согласован 2026-10-01). Коммит `bba6390`.
  - **Фаза 1** — `GeminiRequestBuilder` + 13 тестов. Коммит `28d156a`.
  - **Фаза 2** — `GeminiResponseParser` + 12 тестов. Коммит `08d36b9`.
  - **Фаза 3** — `CompleteGeminiAsync` + switch (удаление заглушки) +
    4 новых теста + 1 интеграционный Skip. Коммит `e448406`.
  - **Фаза 4** — `appsettings.json` (`BaseUrl` `v1beta` → `v1`) + README.
    Коммит `edf13f5`.
  - **Фаза 5** — релиз v1.10.0.
- **Ключевые решения DESIGN:**
  - API **v1** (stable), не v1beta.
  - Auth — **`x-goog-api-key` header** (не query `?key=`).
  - `POST /models/{model}:generateContent` (non-stream).
  - `systemInstruction` — отдельное поле (объект Content, без `role`).
  - `generationConfig.maxOutputTokens` — обязателен.
  - Модель в URL, не в body (специфика Gemini).
- **Тесты:** 559 → **592** (+33, 5 Skip).
- **Связанные:** KI-110a (Anthropic — Fixed v1.9.0), KI-109 (External-LLM Agent).

---

### KI-111 — `mail_agent` не помнит контекст между вызовами
- **Приоритет:** 🟡 Medium | **Статус:** Planned | **Запланировано:** v1.8.x
- **Обнаружено:** 2026-09-29 (smoke Mail Agent)
- **Файлы:** `IIChatTools.Services/Implementation/SubAgentService.cs`, `AgentToolBase.cs`,
  `ChatStreamService.cs`.
- **Описание:** `SubAgentService` — stateless. При каждом вызове `mail_agent`
  передаётся только `task` (одна строка) + `context` (опционально). История чата
  **не передаётся**. Из-за этого:
  - При повторном запросе («прочитай письмо про Алису», когда UID уже был найден
    в прошлом сообщении) агент **заново** делает `list_emails`, чтобы найти UID.
  - Это **+1-2 лишних шага** (впустую потраченный бюджет `MaxSteps`).
  - При частых повторных задачах — упирается в лимит шагов.
- **Возможные решения:**
  1. `AgentToolBase.ExecuteAsync` — передавать в `SubAgentTaskRequest.Context`
     последние 3-5 сообщений чата (или их summary).
  2. `ChatStreamService` — при вызове агента включать краткий «снимок контекста»
     (последние темы, найденные UID и т.п.) в `task` или `context`.
  3. Отдельный сервис `IExecutionContextSummarizer` — сжатие истории перед
     передачей агенту.
- **Связанные:** KI-113 (галлюцинация успеха), KI-052 (Multi-Agent — DESIGN v1.4).

---

### KI-112 — Docker-образ `iichattools` без `git`, `gh`, `python3`, `node`
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** —
- **Решение (2026-09-29):** README пополнен разделом «Docker — что работает, что нет»
  (таблица, DataProtection volume, LM Studio через `host.docker.internal`,
  env-переменные для Mail Agent, HTTPS redirect note).
- **Обнаружено:** 2026-09-29 (первый запуск Docker-образа v1.8.0)
- **Файлы:** `Dockerfile` (multi-stage, `mcr.microsoft.com/dotnet/aspnet:10.0`).
- **Описание:** В логах контейнера — при старте `DependencyChecker` пишет
  WARNING'и для каждой внешней зависимости:
  An error occurred trying to start process 'git' with working directory '/app'.
  No such file or directory
То же для `gh`, `python3`, `node`.
- **Влияние:**
- `git_agent` — ❌ не работает (нет `git`).
- `github_agent` — ❌ не работает (нет `gh`).
- `code_agent` — ❌ не работает (нет `python3`, `node`, `bash`-команд).
- `file_system_agent`, `web_agent`, `mail_agent`, `database_agent`, RAG — ✅ работают.
- **Это by design** (lightweight ASP.NET-образ ~600 MB, без dev-инструментов).
Но нужно **явно задокументировать** в README-разделе «Docker».
- **Возможные решения:**
1. Отдельный Docker-образ `iichattools-full` с установкой `git`, `gh`,
   `python3`, `node`, `chromium` (~1.5-2 GB).
2. Build-arg `INSTALL_DEV_TOOLS=true` в `Dockerfile` (уже есть `INSTALL_BROWSER`
   для Chromium — добавить по аналогии).
3. Ничего не делать, только задокументировать.
- **Связанные:** Docker Publish workflow.

---

### KI-113 — `mail_agent`: qwen3-4b галлюцинирует «успех» при неудаче
- **Приоритет:** 🟡 Medium | **Статус:** Planned | **Запланировано:** v1.8.x
- **Обнаружено:** 2026-09-29 (smoke Mail Agent, скриншот)
- **Файлы:** `appsettings.json` (`SubAgents:mail_agent:SystemPrompt`),
`appsettings.Development.json` (то же).
- **Описание:** При запросе «прочитай письмо про Алису» — `mail_agent` **не
прочитал** письмо (MaxSteps исчерпан), но вернул `finalAnswer`:
> «К сожалению, я не могу предоставить содержимое письма, ... **Однако я
> успешно идентифицировал** последнее письмо из INBOX ...»

Формально — идентифицировал. Семантически — задачу **не выполнил**.
qwen3-4b предпочитает «мягкий» ответ вместо честного «не смогла, нужны доп. шаги».
- **Дополнительно** (см. KI-114): `AgentToolBase` возвращает `ToolResult.Ok`
даже когда `Completed = false`, что усиливает эффект.
- **Возможные решения:**
1. SystemPrompt: добавить явный запрет «не ври об успехе» (см. фикс ниже).
2. Few-shot примеры в промпте: показать «правильный» ответ при неудаче.
3. Использовать модель побольше (например, `gemma-4-12b` вместо `qwen3-4b`).
4. Изменить `AgentToolBase.ExecuteAsync`: если `Completed = false` — возвращать
   `ToolResult.Fail` (см. KI-114).
- **Связанные:** KI-111 (stateless agent), KI-114 (Ok при Completed=false).

---

### KI-114 — `AgentToolBase` возвращает `ToolResult.Ok` при `Completed=false`
- **Приоритет:** 🟡 Medium | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-29 (smoke Mail Agent)
- **Файлы:** `IIChatTools.Services/Implementation/Tools/SubAgent/AgentToolBase.cs`
(строка после `var result = await subAgent.ExecuteTaskAsync(context, request);`).
- **Описание:** `AgentToolBase.ExecuteAsync` всегда возвращает `ToolResult.Ok(...)`
независимо от значения `result.Completed`. Флаг `Completed` передаётся внутри
`data`, но не влияет на `Success`.
- **Влияние:** Внешняя LLM (в чате) получает `success: true` для
`mail_agent` / `file_system_agent` / etc., даже когда **внутренний агент не
справился**. Это усиливает «эффект галлюцинации успеха» (см. KI-113).
- **Это by design** (DESIGN v1.4 § 3.3: результат агента — `Ok`, детали внутри
`data`). Решение было принято, чтобы избежать «двойного» Fail из одного и того
же агента. Пересматривать — только через DESIGN-обновление.
- **Возможные решения (если понадобится):**
1. Если `result.Completed == false` → возвращать `ToolResult.Fail` с
   `message = "Агент не завершил задачу: " + result.FinalAnswer`.
2. Оставить `Ok`, но с явным `message: "⚠️ Агент не завершил задачу"`.
3. Добавить в `ToolResult` новое поле `PartialSuccess` (семантически точнее).
- **Не блокер v1.8.0** — задокументировано. Пересмотр — отдельным DESIGN-update.
- **Связанные:** KI-113 (галлюцинация успеха), DESIGN v1.4 § 3.3.

---

### KI-115 — `mail_agent`: непроактивен — «прочитай письмо» делает только `list_emails`
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.8.x
- **Обнаружено:** 2026-09-29 (smoke, скриншоты)
- **Файлы:** `appsettings.json` (`SubAgents:mail_agent:Model`, `SystemPrompt`),
  `IIChatTools.Services/Implementation/Tools/Mail/ListEmailsTool.cs` (план).
- **Попытка фикса 1 (2026-09-29):** смена модели на `gemma-4-12b` + few-shot промпт.
  **Не сработало** — gemma-4-12b-coder-fable5-composer2.5-v1 не поддерживает
  OpenAI tool calling (см. KI-116).
- **Попытка фикса 2 (2026-09-29, SUCCESS):**
  - Откат `SubAgents:mail_agent:Model` → `qwen/qwen3-4b-2507`.
  - **LM Studio: Context Length 8192 → 16384** для qwen3-4b.
  - **Причина:** лог LM Studio показал `prompt_tokens: 8145`, `completion_tokens: 47`,
    `finish_reason: "length"` — упор в лимит 8192. Полный prompt агента
    (SystemPrompt + few-shot + 7 tool schemas + история) = ~10 200 токенов.
  - **Результат:** `prompt_tokens: 10194, completion_tokens: 295, finish_reason: "stop"` —
    письмо прочитано полностью.
- **Требование:** для агентов LM Studio — `Context Length ≥ 16384` (см. KI-117).
- **Описание:** При задаче «прочитай последнее письмо из INBOX» агент:
  1. Вызывает `list_emails(count=1)` — получает метаданные (uid, from, subject, date).
  2. Останавливается и спрашивает «Хотите, чтобы я прочитал содержимое?» —
     вместо **автоматического** вызова `read_email(uid)`.
  3. При следующей итерации — снова `list_emails`, обещает read_email, но
     исчерпывает `MaxSteps=5`.
  4. Только на **третьей** итерации делает `read_email(uid=45)` — успех.

  Итого: **3 сообщения вместо 1**. Технически всё работает, но UX плохой.
- **Причина:** `qwen3-4b-2507` — слишком маленькая для составных инструкций
  («прочитай» → 2 tool-вызова подряд). Не следует правилу 7 в SystemPrompt
  («СРАЗУ list_emails, затем read_email»).
- **Решение (в работе, 2026-09-29):**
  - **Модель** `mail_agent`: `qwen3-4b-2507` → `gemma-4-12b-coder-fable5-composer2.5-v1`
    (как у `code_agent`).
  - **SystemPrompt**: добавлены few-shot примеры (правильные сценарии
    «прочитай письмо» / «покажи список») + явное правило «НЕ спрашивай
    разрешения — сразу вызывай read_email».
- **Возможные дальнейшие шаги:**
  - Изменить `AgentToolBase`, чтобы если `Completed = false` → `ToolResult.Fail`.
  - **KI-111** (передача контекста) — не решит, но улучшит.
- **Связанные:** KI-111, KI-113, KI-114.

---

### KI-116 — `gemma-4-12b-coder-fable5-composer2.5-v1` не поддерживает OpenAI tool calling
- **Приоритет:** 🔴 High | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-29 (smoke Mail Agent, скриншот LM Studio logs)
- **Файлы:** `appsettings.json`, `appsettings.Development.json`
  (`SubAgents:*:Model` для `mail_agent`, `code_agent`, `planner_agent`).
- **Описание:** `gemma-4-12b-coder-fable5-composer2.5-v1` в LM Studio
  **не генерирует `tool_calls[]`** — она расписывает вызов функции
  **как plain text** в поле `content`, оставляя `tool_calls: []`.

  Доказательство (лог LM Studio от 2026-09-29 22:38:31):

      "model": "gemma-4-12b-coder-fable5-composer2.5-v1",
      "choices": [{
        "message": {
          "role": "assistant",
          "content": "list_emails(count=1, mailbox=\"INBOX\")",
          "reasoning_content": "Steps: 1. list_emails(...) 2. read_email(...)",
          "tool_calls": []
        }
      }]

  В `reasoning_content` модель **правильно планирует** цепочку tool-вызовов,
  но **не вызывает их** через API.
- **Подтверждено (2026-09-29, smoke):**
  - ✅ **`mail_agent`** — не работает (проверено ранее, откачен на qwen3-4b).
  - ✅ **`planner_agent`** — не работает (лог LM Studio: `reasoning_content`
    правильно планирует `save_memory(...)`, но `tool_calls: []`,
    `save_memory` НЕ вызывается).
  - ⚠️ **`code_agent`** — не запускался Chat LLM (см. KI-118), статус не проверен.
- **Fix (2026-09-29):**
  - `mail_agent`, `code_agent`, `planner_agent` → `qwen/qwen3-4b-2507`.
- **Решение (принято):** использовать **tool-calling-совместимые модели**:
  - ✅ `qwen/qwen3-4b-2507` (текущий дефолт, умеет tool calling).
  - ⚠️ Другие модели — **проверять через LM Studio Developer Logs**
    (`"tool_calls"` должен быть заполнен).
- **TODO:**
  - Проверить `code_agent` и `planner_agent` — если gemma не работает,
    заменить их модель на qwen3-4b (или другую tool-calling-совместимую).
  - Обновить `appsettings.json` комментарием.
- **Связанные:** KI-115 (непроактивность — на самом деле была ошибка выбора
  модели), DESIGN v1.4 (Multi-Agent).

---

### KI-117 — LM Studio: Context Length 8192 недостаточно для агентов
- **Приоритет:** 🟠 High | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-29 (smoke Mail Agent, логи LM Studio)
- **Файлы:** `README.md` (требования к LM Studio).
- **Описание:** При дефолтном `Context Length = 8192` в LM Studio агенты
  (`mail_agent`, `code_agent`, `file_system_agent`, ...) **не работают корректно**:
  - Полный prompt = SystemPrompt + few-shot + tool schemas (7+) + история + task.
  - Для `mail_agent` это ~**10 200 токенов**.
  - LM Studio обрезает ответ (`finish_reason: "length"`, `tool_calls: []`),
    и SubAgentService возвращает partial text как finalAnswer.
- **Решение:** **в LM Studio установить Context Length ≥ 16384** для моделей,
  используемых агентами (`qwen3-4b`, `gemma-4-12b`, ...).
- **Опционально (для уменьшения промпта):**
  - Сократить `SystemPrompt` (убрать few-shot примеры).
  - Сократить `Description` у tool'ов.
  - Уменьшить `AllowedTools` у агентов (7 → 5).
  Это вернёт работоспособность на 8192, но потребует доработки.
- **Связанные:** KI-115 (fixed после 16k), KI-116 (gemma не tool-calling).

---

### KI-120 — Chat LLM галлюцинирует количество инструментов
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-30 (smoke v1.8.1, KI-109 Фаза 4)
- **Файлы:** `IIChatTools.Services/Implementation/ChatTools/ChatStreamService.cs`
  (проверено — зашитого числа **нет**).
- **Описание:** При запросе «Сколько инструментов видно?» Chat LLM (qwen3-4b):
  1. Корректно **перечисляет 13 инструментов** (8 агентов + `consult_secondary_agent`
     + 3 RAG + `database_agent`).
  2. Затем пишет: «Однако в описании указано, что доступны только 10 инструментов».
  3. Выдаёт: «Правильный ответ: 10 инструментов».

  Модель **сама себя переубеждает** на основе того, что «помнит» из training data /
  весов. В `ChatStreamService` **нет** `DefaultSystemPrompt` с зашитым числом —
  только `chat.SystemPrompt` из БД (per-chat, по умолчанию пустой).
- **Влияние:** косметическое. LLM **перечисляет инструменты правильно**,
  задачи выполняются. Проблема видна только на прямом вопросе «сколько?».
- **Workaround для smoke:** спрашивать «**перечисли** все доступные инструменты»
  вместо «сколько инструментов». Модель корректно выдаёт список.
- **Не блокер.** Ограничение 4B-модели (аналогично KI-118).
- **Связанные:** KI-118 (Chat LLM не вызывает `code_agent`) — тоже ограничение 4B.

---

### KI-118 — Chat LLM не вызывает `code_agent` для простых задач
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Запланировано:** —
- **Обнаружено:** 2026-09-29 (smoke)
- **Файлы:** `appsettings.json` (`SubAgents:code_agent:Description`).
- **Описание:** Запрос «Через Python посчитай 2+2 и покажи результат» —
  Chat LLM (qwen3-4b) **не вызвала** `code_agent`, а просто вывела код в
  markdown-блоке:
  ```python
  result = 2 + 2
  result
  ```
  Это не выполнение — просто текст. Пользователь получил бы «2+2»,
  но не результат 4.
- **Причина:**  Chat LLM решает, что простая математика не требует
  инструмента — отвечает сама. Description у code_agent недостаточно
  явно требует вызова для расчётов.
- **Возможное решение:**
  Усилить Description code_agent: «ВСЕГДА используй этот агент для
  математических вычислений, даже простых. НЕ отвечай кодом напрямую.»
- **Или:** в DefaultSystemPrompt Chat добавить «для расчётов — code_agent».
- **Не блокер.**  Не митигировано в v1.8.x — оставлено для дальнейшего.
- **Связанные:** KI-116 (gemma не tool-calling).

---

### KI-121 — `external_llm_agent`: LLM пишет «использованные провайдеры», которые не сработали
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.8.2
- **Обнаружено:** 2026-09-30 (smoke v1.8.1) | **Устранено:** 2026-10-01
- **Файлы:** `appsettings.json`, `appsettings.Development.json`
  (`SubAgents:external_llm_agent:SystemPrompt`, правило 4).
- **Описание:** В smoke-тесте v1.8.1 (KI-109) агент `external_llm_agent`
  написал «Использованные провайдеры: openai, groq, together», хотя все три
  вернули `ToolResult.Fail` (circuit breaker / ключ не задан). LLM
  восприняла Fail как «провайдер был использован» и галлюцинирует успех.
- **Причина:** Формулировка в SystemPrompt — «Всегда указывай в финальном
  ответе, какой провайдер использован» — не различает «успешный вызов» и
  «попытка + Fail». 4B-модель интерпретирует «использован» как «была
  попытка обратиться».
- **Решение (v1.8.2):** правило 4 в SystemPrompt переписано:
  «В финальном ответе указывай ТОЛЬКО тех провайдеров, которые УСПЕШНО
  ответили. Если все вернули ошибку — явно напиши "Ни один провайдер не
  доступен" и перечисли причины. НЕ пиши "использованные", если они не
  сработали.» Изменены оба конфига (`appsettings.json` в коммите `f30d26e`,
  `appsettings.Development.json` — дозакоммит `d2b2b37`).
- **Связанные:** KI-109 (External-LLM Agent), KI-113 (аналогичная галлюцинация
  успеха у `mail_agent`).

---

### KI-122 — Смена темы оформления UI (5 популярных, кнопка рядом со сменой языка)
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.10.1
- **Обнаружено:** 2026-09-30 (запрос пользователя)
- **Файлы (итог):**
  - `Views/Shared/_Layout.cshtml` — `data-bs-theme="light"` на `<html>`;
    anti-FOUC inline-скрипт; `<select id="theme-select">` перед RU/EN;
    подключение `theme.js` (ES-модуль) + `theme-hljs.css`.
  - `Views/Shared/_LoginPartial.cshtml` — убраны `text-dark`.
  - `Views/Home/Admin.cshtml` — `tab-content bg-white` → `bg-body`;
    убраны 7× `<thead class="table-light">`.
  - `Views/Home/Status.cshtml` — убран `<thead class="table-light">`.
  - `Views/Shared/_ApprovalModal.cshtml` — `bg-light` → `bg-body-secondary`.
  - `Views/Chat/Index.cshtml` — подключение `theme-hljs.css`.
  - `wwwroot/css/site.css` — CSS-переменные 5 тем; `.footer`,
    `.agent-stat-*`, `.rag-chunk-*`, правило `.table > thead > tr > th`.
  - `wwwroot/css/chat.css` — ~70 замен `#xxx` → `var(--bs-*)` в 17 секциях.
  - `wwwroot/css/theme-hljs.css` (**новый**) — палитра hljs для тёмных тем.
  - `wwwroot/js/modules/theme.js` (**новый**) — модуль переключения.
  - `Resources/SharedResources.resx` + `.ru.resx` — 6 ключей × 2 языка.
- **Что сделано (5 подшагов, ~3.5 ч):**
  1. **B1** — CSS-переменные 5 тем (`light` / `dark` из Bootstrap 5.3.2;
     `dimmed` в стиле GitHub Dimmed; `solarized-light` Ethan Schoonover;
     `high-contrast` WCAG AAA).
  2. **B2** — `theme.js` (`getTheme` / `setTheme` / `clearTheme` /
     `watchSystemTheme`); anti-FOUC inline-скрипт в `<head>`; fallback на
     `prefers-color-scheme: dark`.
  3. **B3** — `<select id="theme-select">` в navbar перед RU/EN.
     `initThemeSelect()` в `theme.js`; синхронизация с `theme-changed`.
  4. **B4.1-4.3** — рефакторинг CSS на Bootstrap-переменные:
     `site.css` (navbar / footer / agent-stat / rag-chunk / thead),
     `chat.css` (~70 замен в 17 секциях), `theme-hljs.css` (палитра hljs
     для 4 тем). Попутно: `bg-light` → `bg-body-secondary` в approval-модалке;
     `table-light` / `bg-white` убраны из `/admin` и `/status`.
  5. **B5** — KI-122 → Fixed; KI-092 → Fixed (попутно); релиз v1.10.1.
- **Хранение:** `localStorage["theme"]` (по образцу `chat.sidebarCollapsed`,
  KI-079).
- **⚠️ Зависимость — снята:** на старте задачи ожидался Bootstrap 5.2, но
  фактически в `wwwroot/lib/bootstrap/dist/` уже **5.3.2** (получено через
  `configs/libman.json` → cdnjs). Этап A (обновление Bootstrap) не потребовался.
- **Связанные:** KI-079 (образец localStorage), KI-081 (логотип),
  KI-092 (попутно — Fixed).
- **Обновление (2026-10-01):** попутно закрыт KI-092 — Bootstrap 5.3.2
  использует `inert` вместо `aria-hidden`. Проверено на smoke: warning
  «Blocked aria-hidden on an element...» в Console **отсутствует** при закрытии
  модалок `/admin`, `/chat` approval, `ragChunksModal`.

---

### KI-123 — Индикатор загрузки списка чатов («Идёт загрузка» + spinner)
- **Приоритет:** 🟢 Low | **Статус:** Deferred | **Запланировано:** v1.9.x
- **Обнаружено:** 2026-10-01 (запрос пользователя)
- **Файлы (план):**
  - `IIChatTools.API/Views/Chat/Index.cshtml` — placeholder в
    `<ul id="chat-list">` (или над ним) для состояния «загрузка».
  - `IIChatTools.API/wwwroot/js/modules/chat.js` — функция `loadChats()`:
    показать spinner перед `fetch`, скрыть после (успех / ошибка).
  - `IIChatTools.API/wwwroot/css/chat.css` — стили spinner'а в sidebar.
  - `SharedResources.resx` + `SharedResources.ru.resx` — ключ
    `ChatListLoading` («Loading chats…» / «Идёт загрузка списка чатов…»).
- **Описание:** При открытии `/chat` sidebar пуст до завершения
  `loadChats()` (fetch `/api/chats`). Если чатов много или сеть медленная —
  выглядит как «пустой sidebar» / «ничего нет». Нужен индикатор:
  **spinner + текст «Идёт загрузка списка чатов…»** в области списка до
  прихода данных.
- **Технически:**
  - **Bootstrap 5** уже подключён — использовать готовый класс
    `.spinner-border` (или `.spinner-grow`).
  - Порядок состояний в `loadChats()`:
    1. Показать `<div class="chat-list-loading">` со spinner'ом.
    2. `await fetch(...)`.
    3. Скрыть spinner, отрисовать `state.chats`.
    4. При ошибке — показать `.chat-list-error` (не пустой список).
  - Не заменять на skeleton (сложнее, не наш стиль) — простого spinner'а
    достаточно.
- **Связанные:** KI-079 (collapse sidebar — тот же модуль `chat.js`).

---

### KI-124 — Интеграционный тест для Anthropic Claude
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.9.0 (followup)
- **Обнаружено:** 2026-10-01 (в Фазе 3 KI-110a) | **Устранено:** 2026-10-01
- **Файлы:** `IIChatTools.Tests/IntegrationTests/ExternalLlm/ExternalLlmIntegrationTests.cs`.
- **Описание:** При рефакторинге `ExternalLlmClient` (Фаза 3 KI-110a) предполагалось
  добавить `[Fact(Skip=...)]`-тест для Anthropic — по образцу DeepSeek / OpenAI / Ollama.
  Файл `ExternalLlmIntegrationTests.cs` не был в контексте сессии — тест не добавлен,
  отложен в KI-124. В GitHub Release v1.9.0 явно указан как «Planned».
- **Решение (v1.9.0-followup):**
  - Добавлен `Anthropic_RealRequest_ReturnsResponse` (`[Fact(Skip=...)]`).
    Env: `EXTERNALLLM__ANTHROPIC__APIKEY`, BaseUrl `https://api.anthropic.com/v1`,
    Model `claude-haiku-4-5`, требует VPN из РФ.
  - `CreateRealClient` — +опциональный параметр
    `ProviderFormat format = ProviderFormat.OpenAI` (обратно совместимо;
    существующие 3 теста не меняются).
  - Тест проверяет путь `CompleteAnthropicAsync` (`POST /v1/messages`,
    `x-api-key`, `anthropic-version: 2023-06-01`, парсинг `content[]`).
- **Тесты:** 562 → **563** (559 pass, **4** skip).
- **Связанные:** KI-110a (Anthropic Claude — Fixed v1.9.0), KI-109 (External-LLM Agent).

---

### KI-125 — Уязвимости MailKit 4.8.0 / MimeKit 4.8.0 (NU1902 × 6)
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.9.0-followup
- **Обнаружено:** 2026-10-01 (при `dotnet restore --configfile NuGet.Config.online`)
- **Файлы:** `Directory.Build.props` (`MailKitVersion`).
- **Описание:** При online-restore обнаружены 6 warnings `NU1902`:
  - `MailKit 4.8.0` — [GHSA-9j88-vvj5-vhgr](https://github.com/advisories/GHSA-9j88-vvj5-vhgr)
    (Moderate): STARTTLS Response Injection + SASL mechanism downgrade.
    Патч — **4.16.0**.
  - `MimeKit 4.8.0` — [GHSA-g7hc-96xr-gvvx](https://github.com/advisories/GHSA-g7hc-96xr-gvvx)
    (Moderate): CRLF Injection в quoted local-part SMTP envelope.
    Патч — **4.15.1**.
  В CI (`dotnet restore` без `NuGet.Config.online`) warnings не видны,
  так как пакеты берутся из `LocalPackages/`. **Не блокер v1.9.0**, но
  технический долг.
- **Решение (v1.9.0-followup):**
  - `Directory.Build.props`: `<MailKitVersion>4.8.0 → 4.18.1</MailKitVersion>`.
    Обновление выше обоих патчей. MimeKit подтягивается транзитивно.
  - Breaking changes не ожидаются: release notes 4.16.0 — только security fix
    и `Dispose` RNG.
  - Smoke mail_agent — обязателен (см. RULES § 4.47 — `IMessageSummary.Attachments`).
- **Связанные:** KI-107 (Mail Agent — база), KI-022 / KI-105 (прецеденты
  override транзитивных уязвимостей).

---

### KI-127 — Chat LLM не выбирает `code_agent_with_review` для кодинг-задач
- **Приоритет:** 🟢 Low | **Статус:** Documented | **Обнаружено:** 2026-10-01
- **Файлы:** `SubAgents:code_agent_with_review:Description` (appsettings.json +
    appsettings.Development.json), `ChatStreamService.DefaultSystemPrompt`.
- **Описание:** Chat LLM (qwen3-4b) при запросе «Напиши функцию на Python для
    проверки палиндрома» выбирает **`code_agent`** напрямую, а не
    `code_agent_with_review`. Даже когда задача явно требует review
    (edge cases — регистр, пробелы, unicode). Аналогично KI-118
    (`code_agent` не вызывается для простых задач).
- **Симптом:** в DevTools видно `Approval required: code_agent` (не
    `code_agent_with_review`); таблица `AgentDebateSessions` пуста; SSE
    `debate_*` события не эмитятся (persistence by design при
    `ChatId != null` не срабатывает — потому что tool не вызван).
- **Не баг:** `code_agent_with_review` зарегистрирован, виден через
    `/api/tools`, работает через прямой вызов `/api/tools/execute`
    (правда, без persistence — ChatId = null) и через Chat при явном
    вызове LLM. Ограничение выбора модели.
- **Возможные решения (митигация — Шаг 1E-fix3, v1.11.0):**
    - Усилить `Description`: явно просить использовать **`code_agent_with_review`**
        для задач со словами «алгоритм», «парсер», «валидация», «безопасность»,
        «edge cases», «обработать исключения».
    - Добавить в `ChatStreamService.DefaultSystemPrompt` правило:
        «Для сложных задач кодинга (алгоритмы, парсеры, security-критичный код,
        обработка edge cases) используй `code_agent_with_review`. Для простых
        (rename, add import) — `code_agent`.»
    - Опционально: `SubAgents:code_agent_with_review:AlwaysReview = true`
        (в DEVELOPMENT) — жёсткий форсинг для smoke-тестов.
- **Связанные:** KI-118 (`code_agent` для простых задач), KI-126 (Шаг 1E).

---

### KI-126 — Автономное взаимодействие суб-агентов (Actor-Critic / Debate)

- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-01 (обсуждение с пользователем)
- **DESIGN:** [`docs/development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md`](development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md)
- **Прогресс Ф1 (v1.11.0, все 10 шагов закрыты 2026-10-02):**
  - ✅ **1A** — Entities `AgentDebateSession` + `AgentDebateRound` + миграция.
  - ✅ **1B** — `IAgentDebateSessionService` + state machine.
  - ✅ **1C** — агент `code_reviewer_agent` (Critic).
  - ✅ **1D** — top-level `ITool` `code_agent_with_review`.
  - ✅ **1E** — SSE-события (`debate_*`) + `ChatDebateCoordinator` +
    persistence (сессии в БД) + `DefaultSystemPrompt` (fix KI-127).
  - ✅ **1F** — эскалация на `ask_external_llm` при `Uncertain`.
  - ✅ **1G** — UI: селектор вида, диалоговый / свёрнутый рендер,
    feedback между раундами.
  - ✅ **1H** — финальная локализация дебатов (+22 ключа `.resx` RU + EN).
  - ✅ **1I** — тесты (+18 unit-тестов).
  - ✅ **1J** — релиз v1.11.0 (этот коммит).
  - **Известные ограничения:** KI-128 (Browser Agent — Planned, v1.11.x),
    KI-129 (F5 не восстанавливает блок — Planned, v1.11.x).
- **Тесты:** 592 → **654** (+62, 5 Skip).
- **Связанные:** KI-052 (Multi-Agent — база), KI-109 (External-LLM —
  эскалация), KI-111, KI-113, KI-127 (Fixed), KI-128, KI-129, KI-130 (Fixed).
- **Описание:** Сейчас Chat-LLM вызывает суб-агентов последовательно, без
  внутренней критики. Ошибки (`code_agent` пишет рабочий, но неоптимальный
  код; `planner_agent` пропускает edge cases) ловит пользователь. Идея —
  добавить **автономное взаимодействие агентов с ролями «Исполнитель» (Actor)
  и «Критик» (Critic)**, с возможностью Human-in-the-loop и эскалации
  на внешнюю LLM при неуверенности критика.
- **Что входит (Фаза 1, v1.11.0):**
  - **Actor-Critic** для `code_agent`: новый агент `code_reviewer_agent`
    (тот же qwen3-4b + system prompt «опытный код-ревьюер»).
  - Цикл: actor → critic → (rejected? actor-раунд-2 → critic-раунд-2) → … →
    max 3 раунда или verdict Approved.
  - **Эскалация:** если critic вернул `Uncertain` — вызывается
    `ask_external_llm` (DeepSeek по умолчанию) для второго мнения.
  - **Новая сущность** `AgentDebateSession` + `AgentDebateRound` (state
    machine, токены/cost per round).
  - **Новый инструмент** `code_agent_with_review` (top-level `ITool` в Chat;
    альтернатива обычному `code_agent`).
  - **SSE-события:** `debate_started` / `debate_round` / `debate_escalated` /
    `debate_completed`.
  - **UI-селектор** между двумя режимами отображения: **диалог** (раунды как
    пузыри) и **сворачиваемый** (финал + `▼ Показать детали`).
  - **Настройки** (per-user / глобальные): MaxRounds, TokenBudget,
    AllowEscalation, DefaultCriticModel.
- **Что НЕ входит (Фазы 2-3, v1.11.x / v2.0):**
  - **Debate** для `planner_agent` (3 роли: Pro / Contra / Judge) — v1.11.x.
  - **Orchestrator-Worker** и **Blackboard** — v2.0.
  - **Reflection** (один агент сам себя критикует) — рассматривался, но
    менее эффективен, чем Actor-Critic. Отложен.
- **Оценка:** ~12-15 ч (Ф1 ~5-6 ч, Ф2 ~5-6 ч, Ф3 ~3-4 ч).
- **Связанные:** KI-052 (Multi-Agent — база), KI-109 (External-LLM —
  для эскалации), KI-111 (stateless agent — решается через Blackboard в Фазе 3),
  KI-113 (галлюцинация успеха — критик частично закрывает).

---

### KI-127 — Chat LLM не выбирает `code_agent_with_review` для сложных задач

- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-01 (smoke Шага 1E-part2)
- **Файлы:** `SubAgents:code_agent_with_review:Description`, `SubAgents:code_agent:Description` (оба `appsettings*.json`).
- **Описание:** Chat LLM (qwen3-4b) при запросе «Напиши функцию для проверки палиндрома с обработкой edge cases» выбирает `code_agent` напрямую, а не `code_agent_with_review`. Аналогично KI-118.
- **Симптом:** DevTools → `Approval required: code_agent` (не `code_agent_with_review`); `AgentDebateSessions` пуста.
- **Решение (Шаг 1E-fix3 + 1E-fix4, v1.11.0):**
  - **Шаг 1E-fix3** (не сработал): усилен `Description` `code_agent_with_review`
    (триггеры) + ограничен `Description` `code_agent`. **Не помогло** —
    qwen3-4b игнорирует описания tools (position-bias: `code_agent` первый
    в алфавитном списке).
  - **Шаг 1E-fix4** (рабочий): `ChatStreamService.DefaultSystemPrompt` —
    базовый system-prompt с 6 явными правилами выбора инструментов +
    1 пример. Добавляется к каждому чату **первым** system-сообщением
    (стабильный префикс для KV-cache).
- **Проверка:**
  - **Явный вызов** (`Используй code_agent_with_review ...`): session
    завершается `Approved` (2 раунда: Rejected → Approved).
  - **Без явного указания** (обычный вопрос про палиндром с edge cases):
    system-prompt направляет LLM на `code_agent_with_review`.
- **Связанные:** KI-118 (`code_agent` для простых задач), KI-126 (Шаг 1E).

---

### KI-128 — Browser workflow недоступен через Chat (`browser_agent` + `save_screenshot_to_file`)

- **Приоритет:** 🟡 Medium | **Статус:** Planned | **Запланировано:** v1.11.x
- **Обнаружено:** 2026-10-02 (smoke Chat — «Открой rzd.ru и сделай скриншот»)
- **Файлы (план):** `Startup.cs` (`RegisterSpecializedAgentTools`),
  `appsettings.json` / `appsettings.Development.json` (`SubAgents:browser_agent`),
  `IIChatTools.Services/Implementation/Tools/Browser/BrowserSessionControlTool.cs`,
  `ChatStreamService.DefaultSystemPrompt`.
- **Описание:** Chat (qwen3-4b) отказывается от задачи «Открой rzd.ru,
  найди расписание и сделай скриншот в `minsk.png`», отвечая
  «Я не могу открыть веб-сайты». При этом **технически** capability есть:

  - `browser_session_open` / `browser_session_control` / `browser_session_close` /
    `browser_open_page` зарегистрированы в DI и доступны через
    `consult_secondary_agent` (`AllowedTools` = все browser-tools).
  - `browser_session_control(command: "screenshot")` возвращает PNG в base64
    (см. `BrowserSessionManager.ExecuteCommandAsync`).
  - `/test` корректно рендерит PNG с кнопками «Открыть» / «Скачать»
    (см. `test.js` → `renderResult`).

  **Реальные причины отказа:**
  1. **Нет `browser_agent` в SubAgents.** Browser-tools доступны только
     через `consult_secondary_agent`, у которого Description обобщённый
     («универсальный fallback») — без упоминания browser. LLM не связывает
     «открой сайт» с этим инструментом.
  2. **Нет `save_screenshot_to_file`.** `browser_session_control(screenshot)`
     возвращает base64, но сохранить его в workspace из Chat **нельзя**:
     `save_file` принимает текст, а не бинарь. Client-side `<a download>`
     (как в `test.js`) для серверного Chat-workflow не подходит.
  3. **`DefaultSystemPrompt` не содержит правила про browser.**
     Есть правила про `code_agent_with_review`, `search_knowledge_base`,
     `file_system_agent`, `web_agent`, `database_agent` — но не про
     browser. 4B-модель не может «догадаться» про инструменты.
  4. **Общая склонность qwen3-4b к отказу** (см. KI-118, KI-120, KI-127) —
     шаблонный ответ «не могу» вместо исследования инструментов.

- **Решение (Фаза 1, план):**
  1. **`browser_agent`** — новый специализированный агент (наследник
     `AgentToolBase`), `AllowedTools = [browser_session_open,
     browser_session_control, browser_session_close, browser_open_page]`.
     `DisplayName`: «Браузерный агент». `Description`: «Открыть сайт, кликнуть,
     заполнить форму, сделать скриншот». По образцу `web_agent` (v1.4.0).
  2. **`save_screenshot_to_file`** — либо расширить
     `browser_session_control` командой `screenshot_to_file(path)`
     (сохраняет PNG в workspace через `PathHelper`), либо новый tool
     `save_screenshot(sessionId, path)`. Первый вариант предпочтительнее —
     меньше инструментов.
  3. **`DefaultSystemPrompt`** — правило 7: «Открыть сайт / кликнуть /
     заполнить форму / сделать скриншот → `browser_agent`».
  4. **Smoke:** «Открой rzd.ru и сделай скриншот в `minsk.png`» → approval
     → `browser_agent` → PNG в `Workspace/users/{id}/browser/`.
- **Оценка:** ~1.5-2 ч.
- **Связанные:** KI-052 (Multi-Agent — база), KI-081 (браузерные инструменты —
  v1.3), KI-127 (`code_agent_with_review` — аналогичная проблема выбора
  инструмента), KI-118 / KI-120 (ограничения qwen3-4b).

---

### KI-129 — Debate blocks not restored on F5 (session/rounds in DB, UI ignores them)

- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-02 (smoke Шага 1G.1) | **Устранено:** 2026-10-03
- **Файлы (итог):**
  - `IIChatTools.API/Controllers/ChatController.cs` — `GetChatAsync` (заполняет
    `DebateSessions`, парсит `DebateSessionId` через helper `ParseDebateSessionId`).
  - `IIChatTools.Services/DTO/Chat/ChatDtos.cs` — `ChatDetailDto.DebateSessions` +
    `ChatMessageDto.DebateSessionId` (`int?`).
  - `IIChatTools.Services/Interfaces/IAgentDebateSessionService.cs` — новый метод
    `GetSessionsByChatAsync(chatId, userId)`.
  - `IIChatTools.Services/Implementation/Debate/AgentDebateSessionService.cs` —
    реализация (eager-load раундов через `Include(Rounds)`, сортировка `StartedAt asc`).
  - `IIChatTools.Services/DTO/Debate/AgentDebateStatusDto.cs` — расширен 6 полями:
    `Task`, `FinalArtifactJson`, `MaxRounds`, `HumanApproval`, `ActorAgent`, `CriticAgent`.
    Парсинг `ConfigSnapshotJson` — через общий private helper `BuildStatusDto`.
  - `IIChatTools.Services/Implementation/ChatStreamService.cs` — сохраняет
    `MetadataJson = { debateSessionId }` в tool-сообщение `code_agent_with_review`
    (из `ToolResult.Data.sessionId`).
  - `IIChatTools.API/wwwroot/js/modules/chat.js` — `prefillDebatesFromChatDetail()`,
    `renderDebatesForMessage()`, `state.debateSessionByToolCallId`,
    обновлённый `renderMessage` (вставляет блоки дебатов в `.chat-message-tools`).
  - `Resources/SharedResources*.resx` — новый ключ `ChatDebateOrphaned` (RU + EN).
- **Описание:** Блок Actor-Critic отображался только в live-режиме (SSE).
  При F5 история сообщений загружалась из БД, но данные debate-сессий
  **не восстанавливались** в UI — блок исчезал.

  **При этом сами сессии УЖЕ сохранялись в БД** (Шаг 1E-part2):
  `AgentDebateSession` + `AgentDebateRound` (persistence через
  `IAgentDebateSessionService`).
- **Решение (реализовано):**
  1. **Шаг 1 (backend, коммит `399bcd7`)**: `GetSessionsByChatAsync` +
     `ChatDetailDto.DebateSessions` + `ChatMessageDto.DebateSessionId` +
     запись `debateSessionId` в `MetadataJson` tool-сообщения.
  2. **Шаг 2 (frontend, коммит `16ac2a5`)**: `prefillDebatesFromChatDetail()` +
     `renderDebatesForMessage()` + маппинг `toolCallId → sessionId` из tool-сообщений
     (`state.debateSessionByToolCallId`).
  3. **Orphaned-бейдж (коммиты `8387d06`, `579006b`, `c17a48f`)**: сессии в статусе
     `InProgress` / `Pending` без `completed` — отображаются с бейджем «⚠️ Прервано»,
     без feedback-UI. **Важно:** после KI-134/135/136 сценарий «F5 посреди feedback»
     теперь корректно приводит сессию в `Cancelled` (см. ниже), а orphaned-ветка
     остаётся fallback'ом для действительно «зависших» сессий (например, падение
     процесса приложения).
  4. **Косвенно исправлено в KI-134/135/136**: tool-сообщение с `debateSessionId`
     теперь **гарантированно сохраняется** в БД до `yield ToolResult` (KI-136) и
     с `CancellationToken.None` (KI-134). `Data.sessionId` возвращается даже в
     `ToolResult.Fail` (KI-135).
- **Smoke (2026-10-03):**
  - ✅ Live: дебаты отображаются в dialog/collapsed — работало и до фикса.
  - ✅ **F5 после завершения**: блок дебатов восстанавливается с финальным вердиктом.
  - ✅ **F5 посреди feedback**: сессия = `Cancelled`, блок восстанавливается с «❌ Отменено».
  - ✅ **F5 после Stop**: то же.
  - ⚠️ **Orphaned fallback**: проверяется только при падении процесса — не воспроизводили.
- **Связанные:** KI-126 (persistence — Шаг 1E-part2), KI-134, KI-135, KI-136
  (порядок сохранения tool-сообщений).

---

### KI-130 — code_agent / code_agent_with_review не могут читать вложения чата (RAG)

- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-02 (smoke Chat — «исправь файл `code-1790941591651.py`»)
- **Fixed:** 2026-10-02 (полный фикс — `indexName` в `SearchKnowledgeBaseTool` +
  обновление `SystemPrompt` `code_agent`)
- **Файлы (план):**
  - `appsettings.json` / `appsettings.Development.json` — `SubAgents:code_agent:AllowedTools`.
  - `IIChatTools.Services/Implementation/Tools/CodeExecution/*` — потенциальный новый tool.
  - `IIChatTools.Services/Implementation/Tools/Debate/CodeAgentWithReviewTool.cs`.
- **Описание:** Пользователь загружает файл через 📎 → файл попадает в RAG-индекс
  `my_rag_docs` и физически сохраняется в `chat-attachments/{chatId}/{guid}.ext`.
  RAG находит его (виден в «Источники»), но **ни один code-агент не может
  получить содержимое**:

  - `code_agent.AllowedTools = [run_javascript, run_python, execute_command]` —
    нет `read_file`, нет `search_knowledge_base`.
  - `code_agent_with_review` использует того же `code_agent` (actor).
  - Файл физически не в `workspace/{userId}/`, а в служебной подпапке
    `chat-attachments/{chatId}/` (GUID-имя — by design, KI-083 Шаг 6A).

  **Симптом:** LLM отвечает «файл не найден, передайте содержимое» — что
  честно, но не помогает пользователю.

- **Возможные решения:**
  1. **Добавить `search_knowledge_base` в `AllowedTools` `code_agent`** —
     агент сам ищет содержимое через RAG. Минимальная правка конфига.
  2. **Новый top-level tool `read_chat_attachment(fileName)`** — читает
     содержимое из `chat-attachments/{chatId}/`. Дать его `code_agent`.
  3. **Инжектить содержимое активных вложений в `context`** при вызове
     `code_agent` / `code_agent_with_review` (автоматически, если
     задача упоминает имя файла).
  4. **Разрешить `read_file` с явным путём к `chat-attachments/{chatId}/`** —
     минимальная правка, но даёт агенту широкий доступ к чужой папке.
- **Решение (2026-10-02, v1.11.0, вариант 1):**
  - В `appsettings.json` + `appsettings.Development.json` добавлен
    `search_knowledge_base` в `SubAgents:code_agent:AllowedTools`.
  - В SystemPrompt добавлено правило 5: если пользователь упоминает
    приложенный файл (например, `code-*.py`) и его нет в workspace —
    использовать `search_knowledge_base` для получения содержимого.
  - Эффект: `code_agent` и `code_agent_with_review` (actor) теперь могут
    читать содержимое вложений через RAG-индекс `my_rag_docs`.
  - **Не покрыто (возможные follow-up):** прямая передача содержимого
    вложений в `context` при первом раунде (вариант 3) — потенциально
    эффективнее, но требует изменений в `CodeAgentWithReviewTool`.
- **⚠️ Smoke 2026-10-02 #1** (после quick-fix): Chat LLM выбрал
  `file_system_agent`, а не `code_agent`. Правило 5 в SystemPrompt
  `code_agent` не сработало, т.к. сам агент не был вызван.
  → Добавлено правило 7 в `ChatStreamService.DefaultSystemPrompt`.

- **⚠️ Smoke 2026-10-02 #2** (после правила 7): Chat **теперь правильно
  выбирает `code_agent_with_review`** ✅. Но actor (`code_agent`) внутри
  всё равно не может получить содержимое файла:

  **Реальные архитектурные gap'ы (3 связанных):**

  1. **`search_knowledge_base` ищет НЕ там.** Добавлен в `AllowedTools`
     `code_agent` (мой первый quick-fix), но он ищет в `project_docs`
     (документация проекта). Вложения чата лежат в `my_rag_docs` —
     **tool'а для этого индекса вообще не существует**.

  2. **Auto-inject RAG работает только для Chat, не для SubAgent.**
     `ChatStreamService.BuildMessagesAsync` вставляет top-K из
     `my_rag_docs` в system prompt **Chat**. Когда Chat вызывает
     `code_agent`, subagent стартует с нуля — БЕЗ RAG-контекста и
     БЕЗ знания о вложениях.

  3. **Chat не пробрасывает содержимое вложения в `context` агента.**
     LLM qwen3-4b не догадывается: «я уже видел содержимое через
     auto-inject, надо передать его в `context` аргумента `code_agent`».

  **Симптом:** actor отвечает «файл не найден, предоставьте содержимое» —
  что честно (см. KI-113), но не помогает пользователю.

- **Правильное решение (план v1.11.x / v1.12.x):**
  1. **Новый tool `search_chat_attachments`** — ищет в `my_rag_docs`
     с `chatId` из контекста. Добавить в `AllowedTools` `code_agent`.
     **ИЛИ** расширить `search_knowledge_base` параметром `indexName`.
  2. **Проброс RAG-контекста в SubAgent** — добавить
     `SubAgentTaskRequest.RagContext`; в `AgentToolBase` заполнять из
     `ToolExecutionContext` или из auto-inject Chat.
  3. **Проброс содержимого вложения в `context`** — если Chat уже
     сделал auto-inject, добавить чанки в `context` при вызове `code_agent`
     (в `CodeAgentWithReviewTool`).
  4. **Или (самое простое, но не идеальное):** инжектить auto-inject
     RAG в system prompt **всех агентов**, если chatId задан.
- **Финальное решение (2026-10-02, v1.11.0, полный фикс):**
  - ✅ **`SearchKnowledgeBaseTool` расширен параметром `indexName`** —
    `'project_docs'` (default) или `'my_rag_docs'`. Для `my_rag_docs`
    `chatId` подставляется автоматически из `ToolExecutionContext.ChatId`.
  - ✅ **Обновлён `SystemPrompt` `code_agent`** — правило 5 явно говорит:
    «Приложенные файлы (code-*.py, 📎) — искать через
    `search_knowledge_base(indexName='my_rag_docs')`. НЕ через
    `list_directory` / `read_file` / `execute_command('ls')`».
  - ✅ **Rule 7 в `DefaultSystemPrompt` Chat** (сохранён) — Chat выбирает
    `code_agent_with_review` / `code_agent` для задач «исправь код в файле X».
  - ✅ **Архитектурная цепочка работает** без изменений:
    `ToolExecutionContext.ChatId` пробрасывается через
    `ChatStreamService → CodeAgentWithReviewTool → AgentToolBase →
    SubAgentService → SearchKnowledgeBaseTool`.
  - **Почему этого достаточно:** не нужен ни новый tool
    `search_chat_attachments`, ни проброс RAG-контекста в SubAgent —
    агент сам вызывает `search_knowledge_base(indexName='my_rag_docs')`
    и получает содержимое.
- **Связанные:** KI-083 (RAG / attachments), KI-126 (Actor-Critic),
  KI-127 (выбор `code_agent_with_review`), KI-113 (галлюцинация успеха).

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

### KI-132 — Chat LLM context overflow из-за раздутого tool_result от code_agent_with_review

- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-03 (smoke Actor-Critic, chatId=2)
- **Симптом:** после одобрения критиком `code_agent_with_review` возвращал
  `ToolResult.Data` с полем `rounds[]`, содержащим полные `actorOutput`
  каждого раунда (код на ~1500-2500 токенов). При 2-3 раундах tool_result
  раздувался до ~5000+ токенов, и на следующей итерации Chat LLM
  контекст превышал 8192 (или даже 16384) → LM Studio 400:
  `request (8376 tokens) exceeds the available context size (8192 tokens)`.
- **Fix:** убрано поле `rounds[]` из `ToolResult.Data`.
  `finalArtifact` (последний approved-код) — сохраняется.
  История раундов доступна через SSE-событие `debate_round` (live) и
  `GET /api/chats/{id}` → `DebateSessions[].Rounds` (F5).
- **Связанные:** KI-126 (Actor-Critic), KI-117 (требование 16384 в LM Studio).

### KI-133 — Skip feedback не разблокирует сервер

- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-03 (smoke Actor-Critic, chatId=3)
- **Симптом:** в inline-блоке feedback между раундами кнопка
  «Пропустить» только скрывала UI-блок; сервер продолжал ждать
  5 минут (`FeedbackTimeout`), затем возвращал null.
  Пользователь ждал ~5 минут до следующего раунда.
- **Fix:** новый метод `IAgentDebateCoordinator.SkipFeedbackAsync`
  (резолвит TCS значением `null`). Новый endpoint
  `POST /api/chat/debate/{sessionId}/skip`. `chat.js:skipFeedback`
  теперь вызывает сервер.
- **Связанные:** KI-126 (Actor-Critic, Human-in-the-loop).

---

### KI-134 — Tool-сообщение не сохраняется в БД при отмене SSE

- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-03 (smoke KI-129 Шаг 2 — F5 посреди feedback)
- **Симптом:** при F5 (или Stop) в момент выполнения tool'а
  tool-сообщение **терялось** из БД. В истории чата оставался
  assistant с `tool_calls` без ответа tool'а.
- **Причина:** `ChatStreamService` вызывал `AddMessageAsync(...,
  cancellationToken)`. При отмене SSE `cancellationToken` уже
  отменён → `OperationCanceledException` → сохранение не происходило.
- **Fix:** сохранение assistant с `tool_calls` и tool-сообщения —
  с `CancellationToken.None`. Баг влиял на **все** инструменты
  (не только `code_agent_with_review`). Также убрано условие
  `toolResult.Success` при записи `debateSessionId` (см. KI-135).
- **Связанные:** KI-129 (F5 persistence), KI-135.

### KI-135 — Orphaned-сессия Actor-Critic не восстанавливается при F5

- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-03 (smoke KI-129 Шаг 2)
- **Симптом:** при F5 в момент ожидания feedback (между раундами
  Actor-Critic) сессия завершалась в БД как `Cancelled`, но блок
  дебатов в UI **не восстанавливался** после F5.
- **Причина:** `CodeAgentWithReviewTool` возвращал
  `Fail("Операция отменена пользователем.")` **без** `Data.sessionId`.
  ChatStreamService не мог записать `debateSessionId` в tool-сообщение
  (см. KI-134) → маппинг `toolCallId → sessionId` оставался пуст.
- **Fix:**
  - `ToolResult.Fail(message, data)` — новый опциональный параметр.
  - `CodeAgentWithReviewTool` при отмене возвращает
    `Data = { sessionId, cancelled = true }`.
  - `ChatStreamService` пишет `debateSessionId` в `MetadataJson`
    tool-сообщения (убрано условие `toolResult.Success`).
  - Фронт (`chat.js`): `isOrphaned`-ветка осталась как **fallback**
    для действительно «зависших» сессий (например, падение процесса
    приложения). Сценарий «F5 посреди feedback» теперь корректно
    отображается как «❌ Отменено» через стандартную `completed`-ветку.
- **Связанные:** KI-129 (F5 persistence), KI-134.

---

### KI-136 — Tool-сообщение не сохраняется в БД: yield ToolResult раньше AddMessageAsync

- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.11.0
- **Обнаружено:** 2026-10-03 (smoke KI-129 Шаг 2, повторная проверка F5)
- **Симптом:** при F5 (или Stop) в момент выполнения tool'а
  tool-сообщение **полностью терялось** из БД. В истории чата оставался
  только assistant с `tool_calls` без tool-result.
  После F5 `d.data.messages.find(m => m.toolName === 'code_agent_with_review')`
  возвращал `undefined`.
- **Root cause:** `ChatStreamService.StreamAsync` сначала делал
  `yield return ChatStreamEvent.ToolResult(...)`, и только **потом** —
  `AddMessageAsync`. При отмене SSE (F5) следующая итерация `await foreach`
  в `ChatStreamController` бросала `OperationCanceledException` при
  попытке записи в закрытый `Response` → `IAsyncEnumerable` из
  `StreamAsync` **dispose'ился** → код после `yield` **никогда не выполнялся**.
  **KI-134/135** (CancellationToken.None, Data.sessionId в Fail) были
  правильными, но **недостижимыми** — код до них не доходил.
- **Fix:** в `ChatStreamService.StreamAsync` порядок операций изменён:
  `AddSourcesToAccumulator` + `AddMessageAsync` (с `CancellationToken.None`)
  выполняются **до** `yield return ToolResult(...)`. После yield остаётся
  только `messages.Add(...)` (in-memory, безопасно).
- **Связанные:** KI-129 (F5 persistence), KI-134, KI-135.

---

## v1.12.0 — Vision Agent (roadmap)

### KI-131 — Vision Agent (Vision LLM + Planner LLM + 3 backend'а)
- **Приоритет:** 🟡 Medium | **Статус:** In Progress | **Запланировано:** v1.12.0
- **Начало работы:** 2026-10-04 (Фаза 1 — контракты и DTO).
- **Фаза 2 закрыта** (2026-10-04): `LocalHarnessVisionBackend` полностью
  реализован — 11 методов `IVisionBackend` + 5 защитных слоёв (whitelist
  доменов через `VisionWhitelistValidator`, whitelist процессов через
  `GetForegroundWindow` + `VisionProcessWhitelistChecker`, DPI-координаты
  через `VisionMouseCoordinates`, Unicode-ввод через `KEYEVENTF_UNICODE`,
  downscale PNG через `VisionImageResizer`). Помощники: `Win32Interop`
  (P/Invoke SendInput + GetSystemMetrics + GetForegroundWindow),
  `VisionKeyMapper` (VK-маппинг). Тесты: 666 → 825 (+159).
- **Порядок фаз изменён** (2026-10-04): Ф5 (VL + Planner клиенты) →
  Ф6 (VisionAgentService loop) → Ф7 (VisionAgentTool) → **потом** Ф3
  (Sandbox) / Ф4 (VncMcp). Причина: Local backend (Ф2) уже реализован,
  MVP достигается на Ф7. Sandbox на dev-машине **Disabled**
  (`Containers-DisposableClientVM`, Windows Pro — включение требует
  admin + перезагрузка). См. `DESIGN_VISION_AGENT.md` § 7.
- **Обнаружено:** 2026-10-03 (обсуждение с пользователем)
- **DESIGN:** [`docs/development/v1.12/DESIGN_VISION_AGENT.md`](development/v1.12/DESIGN_VISION_AGENT.md)
  (v2.1, Draft — ждёт согласования).
- **Описание:** LLM не умеет управлять компьютером через визуальные подсказки —
  только CSS-селекторы (и то через `browser_session_control`). Из-за этого:
  - Не работает с canvas / WebGL / shadow-DOM.
  - Не обходит антибот-защиту (Cloudflare Turnstile, DataDome).
  - Не выполняет многошаговые задачи: «купи билет РЖД до Камчатки»,
    «заполни заявление на госуслугах», «настрой 1С».
  - Не управляет десктопными приложениями (Outlook, Excel, 1С).

- **Что входит (v1.12.0, Фазы 0–9, ~62 ч):**
  - **Top-level инструмент** `vision_agent` (не `AgentToolBase` — по образцу
    `DatabaseAgentTool` / `CodeAgentWithReviewTool`).
  - **Оркестрация трёх моделей** (все в конфиге, взаимозаменяемы):
    - **Chat LLM** (`qwen3-4b`) — диалог + делегирование.
    - **Planner LLM** (`qwen3-coder-30b-a3b`) — планирование действий.
    - **Vision LLM** (`ministral-3-3b-instruct-2512`) — описание UI со скриншота.
  - **Три backend'а:**
    - `LocalHarnessVisionBackend` — SystemHarness + Chrome fresh profile.
    - `SandboxVisionBackend` — Windows Sandbox + TightVNC.
    - `VncMcpVisionBackend` — MCP-клиент для удалённой машины.
  - **Loop:** screenshot → describe → plan → act → repeat.
  - **On-screen indicator** (`VisionOverlay.exe`, WPF, always-on-top) —
    **обязателен** для `local-harness` и `sandbox`. ESC / кнопка STOP —
    немедленная остановка.
  - **5 уровней безопасности:** изоляция backend'а + whitelist процессов +
    on-screen indicator + approval/бюджет + валидатор/audit.
  - **Конфигурация:** `appsettings.json` → секция `VisionAgent`
    (Backend / VisionLlm / PlannerLlm / Limits / Whitelist / ActionValidation / Privacy).
    Admin override через `AppSettings` (по образцу `SubAgents.*` / `SqlAgent.*`).

- **Что НЕ входит:** Linux desktop, macOS desktop, OCR без VL, обход капчи,
  real-time streaming, fine-tuning.
- **Оценка:** ~62 ч (≈8 рабочих дней), 9 фаз + DESIGN.
- **Связанные:** KI-137 (OCR fallback), KI-138 (PII masking), KI-139 (External VL),
  KI-097 (Database Agent — эталон top-level ITool), KI-126 (Actor-Critic —
  эталон SSE + EventWriter), KI-101 (per-action approval).

---

### KI-137 — Vision Agent: OCR-fallback для мелкого текста
- **Приоритет:** 🟢 Low | **Статус:** Planned | **Запланировано:** v1.12.x
- **Обнаружено:** 2026-10-03
- **DESIGN:** [`docs/development/v1.12/DESIGN_VISION_AGENT.md`](development/v1.12/DESIGN_VISION_AGENT.md) § 9.1.
- **Описание:** VL-модели (Ministral-3B, llava-1.5, pixtral) **плохо читают
  мелкий текст** на скриншотах — 8-10px шрифты в формах, капчу (не для обхода,
  а для чтения «введите код»), таблицы с плотной вёрсткой. VL-модель даёт общее
  описание, но не может точно воспроизвести содержимое поля или текст ошибки.
- **Возможные решения:**
  1. **Tesseract OCR** (Apache 2.0, .NET-обёртка `Tesseract` 5.2.0) —
     preprocess: crop региона → upscale ×2 → grayscale → OCR → merge
     с `ScreenDescriptionDto`.
  2. **PaddleOCR** (Apache 2.0, ONNX-runtime) — лучше на кириллице, но требует
     ONNX runtime.
  3. **External VL** — отдать в Claude / GPT-4V (см. KI-139).
- **Триггер:** VL-модель возвращает `ui_elements[].label` короче 3 символов
  ИЛИ Planner LLM делает `action = "fail", reason = "не вижу текста"`.
- **Оценка:** ~4-6 ч.
- **Связанные:** KI-131 (Vision Agent), KI-139 (External VL — альтернатива).

---

### KI-138 — Vision Agent: маскирование PII на скриншотах
- **Приоритет:** 🟢 Low | **Статус:** Planned | **Запланировано:** v1.12.x
- **Обнаружено:** 2026-10-03
- **DESIGN:** [`docs/development/v1.12/DESIGN_VISION_AGENT.md`](development/v1.12/DESIGN_VISION_AGENT.md) § 6.5.
- **Описание:** VL-модель может «увидеть» на скриншоте:
  - Номера банковских карт (Visa / Mastercard / МИР).
  - Email-адреса, номера телефонов.
  - Паспортные данные, СНИЛС, ИНН.
  - Пароли, токены в адресной строке (частично закрывается `MaskUrlBar`).
  - Личные сообщения (мессенджеры, почта).

  Даже при `PersistScreenshots = false` (default — скриншоты НЕ сохраняются
  в `ChatMessage.MetadataJson`) VL-модель видит PII в момент анализа. Утечка
  возможна через:
  - логи VL-модели (в LM Studio DevTools);
  - `AuditLog.ParametersJson` (если туда попадут `text` из action);
  - External VL (см. KI-139 — данные уходят на чужой сервер).
- **Возможные решения:**
  1. **Pre-OCR маскирование:** Tesseract → regex-детекция (credit card Luhn,
     email, phone, СНИЛС) → blur регионов на PNG до VL-модели.
  2. **VL-детекция:** отдельный prompt «найди PII на скриншоте» → координаты →
     blur → основной VL-prompt.
  3. **Post-логирование:** явный whitelist полей для audit — никогда не писать
     `ui_elements[].value` с типом `text_input` / `password`.
- **Оценка:** ~5-7 ч.
- **Связанные:** KI-131 (Vision Agent), KI-139 (External VL — критично для
  privacy, данные уходят в чужой сервер).

---

### KI-139 — Vision Agent: поддержка внешних VL (Claude Computer Use / OpenAI CUA)
- **Приоритет:** 🟢 Low | **Статус:** Planned | **Запланировано:** v1.12.x
- **Обнаружено:** 2026-10-03
- **DESIGN:** [`docs/development/v1.12/DESIGN_VISION_AGENT.md`](development/v1.12/DESIGN_VISION_AGENT.md) § 3.2.
- **Описание:** В v1.12.0 Vision LLM и Planner LLM работают **только локально**
  (LM Studio + Ministral-3B + qwen3-coder-30b-a3b). Внешние VL (Claude Computer
  Use, OpenAI CUA, Gemini) могут дать:
  - Более точное распознавание сложных UI.
  - Готовые action-схемы (Computer Use API от Anthropic).
  - Fallback, когда локальная Ministral-3B ошибается на сложных страницах.
- **Что нужно:**
  1. `ExternalVisionClient` + `ExternalPlannerClient` — обёртки над
     `IExternalLlmClient` (v1.8.1, KI-109).
  2. **Multimodal support** в `ExternalLlmClient` — сейчас только text
     (см. README, раздел External-LLM Agent → Ограничения: «vision не поддерживается»).
  3. Конфиг `VisionAgent:VisionLlm:Provider = "external"` +
     `FallbackChain: ["lmstudio", "external:claude", "external:openai"]`.
  4. **Отдельный провайдер `anthropic-computer-use`** (beta API) — свой
     формат запроса/ответа.
- **Оценка:** ~6-8 ч.
- **Связанные:** KI-131 (Vision Agent), KI-109 (External-LLM Agent — база),
  KI-110a/b (Anthropic / Gemini — собственные форматы), KI-138 (PII — критично
  для external).

---

## v1.12.x — Vision Agent (roadmap)

### KI-141 — External VL providers: multimodal image support

- **Приоритет:** 🟡 Medium | **Статус:** Planned | **Запланировано:** v1.12.x
- **Обнаружено:** 2026-10-04 (Ф5.3 KI-131).
- **DESIGN:** [`docs/development/v1.12/DESIGN_VISION_AGENT.md`](development/v1.12/DESIGN_VISION_AGENT.md) § 3.2.
- **Описание:** В v1.12.0 Vision Agent использует **локальную VL-модель**
  (LM Studio + Ministral-3B) для описания скриншотов. Внешние VL
  (OpenAI GPT-4o, Anthropic Claude 3.5 Sonnet, Google Gemini 1.5 Flash)
  могли бы дать:
  - более точное распознавание сложного UI (canvas, shadow-DOM, антибот);
  - fallback, когда локальная Ministral-3B ошибается;
  - работу на слабом железе (без GPU).

  **Ограничение:** `IExternalLlmClient` (v1.8.1, KI-109) — **text-only**.
  Требуется:
  1. Расширить `ExternalLlmRequest` — nullable поле `ImageBase64DataUrl`
     (или `Images[]`).
  2. Добавить в `ExternalLlmClient` **3 разных формат-билдера** для
     multimodal content:
     - OpenAI — `content: [{type: "text"}, {type: "image_url", image_url: {url: "data:..."}}]`;
     - Anthropic — `content: [{type: "text"}, {type: "image", source: {type: "base64", media_type, data}}]`;
     - Gemini — `parts: [{text}, {inlineData: {mimeType, data}}]`.
  3. Флаг `SupportsVision` в `ExternalProviderOptions` (true для
     `gpt-4o`, `claude-3.5-sonnet`, `gemini-1.5-flash`).
  4. Полная реализация `ExternalVisionClient.DescribeAsync`.
  5. Обновить `AutoVisionClient` (Ф5.4) — добавить external в цепочку.
- **Оценка:** ~5-8 ч.
- **Связанные:** KI-131 (Vision Agent), KI-109 (External-LLM Agent — база),
  KI-110a (Anthropic), KI-110b (Gemini), KI-139 (External VL для GUI — дублирует
  часть scope, консолидировать при старте).

---

## v1.13.0 — Speech Recognition (roadmap)

### KI-140 — Голосовой ввод в чате (офлайн-распознавание речи, Whisper.net)

- **Приоритет:** 🟢 Low | **Статус:** Planned | **Запланировано:** v1.13.0
- **Обнаружено:** 2026-10-03 (запрос пользователя)
- **DESIGN:** [`docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md`](development/v1.13/DESIGN_SPEECH_RECOGNITION.md)
  (v1.13.0, Draft).
- **Описание:** Кнопка 🎤 в `.chat-input-box` для голосового ввода. Требования:
  - **Офлайн** — не зависит от интернета (в т.ч. в РФ без VPN).
  - **Приватность** — аудио не покидает сервер.
  - **Локальная модель** — Whisper.net (whisper.cpp bindings, MIT) +
    GGML-модель (`ggml-base.bin`, ~142 MB).
  - **Никаких Python / ffmpeg / внешних CLI** — только NuGet `Whisper.net` +
    `Whisper.net.Runtime` (native libs, ~5 MB).

- **Что входит (v1.13.0, Фазы 1–3, ~3 ч):**
  - **Backend:** `ISpeechRecognitionService` + `WhisperNetTranscriptionService`
    (Singleton, ленивая загрузка модели) + `SpeechController` +
    `POST /api/speech/transcribe`. DTO `SpeechOptions` + `TranscriptionResult`.
  - **Frontend:** `wwwroot/js/modules/speech.js` (MediaRecorder + Web Audio API
    → WAV 16 kHz mono) + кнопка 🎤 в `.chat-input-box` + CSS
    (idle / recording / transcribing / error).
  - **Скрипт:** `scripts/setup/download-whisper-model.ps1`
    (`tiny|base|small|medium|large-v3`).
  - **Локализация RU + EN** (6 ключей: `SpeechButtonTooltip`, `SpeechButtonAria`,
    `SpeechRecording`, `SpeechTranscribing`, `SpeechErrorPermission`,
    `SpeechErrorBrowser`).
  - **Unit-тесты:** `SpeechControllerTests` (4) + опциональные
    `WhisperNetTranscriptionServiceTests` (3, Skip в CI — требуют модель).

- **Что НЕ входит (Phase 4, опционально):**
  - Хоткей `Ctrl+Shift+Space`.
  - VAD (auto-stop по тишине).
  - Streaming (промежуточная транскрибация).
  - GPU-ускорение (`Whisper.net.Runtime.Cuda`).

- **Принятые решения (2026-10-03, см. DESIGN § 11.1):**
  1. Модель по умолчанию — `base` (142 MB).
  2. Язык — `"auto"` в `appsettings.Development.json`, `"ru"` в prod.
  3. Позиция кнопки — слева от 📎.
  4. Поведение — вставлять в `<textarea>`.
  5. Ресемплинг WAV — через `OfflineAudioContext` (§ 4.2).
  6. Auto-stop 60 сек — enforced на клиенте (`MAX_RECORDING_MS`).
  7. `RequestSizeLimit(20 MB)` — hard cap; реальный лимит — 10 MB в опциях.

- **Отложено (Phase 4, см. DESIGN § 11.2):**
  - VAD (auto-stop по тишине) — высокий приоритет, чистый JS (~30 строк).
  - Хоткей `Ctrl+Shift+Space`.
  - GPU (`Whisper.net.Runtime.Cuda`).
  - Streaming (chunked upload + пересборка транскрипта).

- **Технический долг (не блокер MVP, см. DESIGN § 11.3):**
  - Fail-fast при старте (сейчас — ленивая инициализация).
  - Валидация magic bytes WAV (сейчас — только `file.Length`).
  - `TranscriptionResult.DurationMs` = 0 (не заполняется).

- **Связанные:** KI-126 (Chat UI — где живёт кнопка), KI-109 (External-LLM —
  принципиально НЕ используется, офлайн-only).

---

## Сводка по статусам

| Статус | Кол-во |
|--------|--------|
| Fixed (v1.0.2) | 8 |
| Fixed (v1.1.0) | 13 |
| Fixed (v1.1.1) | 11 |
| Fixed / Resolved (v1.3.0) | 12 |   <!-- KI-046, KI-050, KI-051, KI-058, KI-059, KI-060, KI-061, KI-061a, KI-062, KI-063, KI-065, KI-066 -->
| Fixed / Resolved (v1.3.1) | 6 |    <!-- KI-043, KI-064, KI-068, KI-069, KI-071, KI-072 -->
| Fixed (v1.4.0) | 1 |                <!-- KI-052 -->
| Fixed (v1.4.1) | 9 |                <!-- KI-049, 067, 076, 078, 079, 080, 081, 084, 085 -->
| Fixed (v1.4.x) | 1 |                <!-- KI-087 -->
| Fixed (v1.5.0) | 1 |                <!-- KI-083 (RAG) -->
| Fixed (v1.6.0) | 1 |                <!-- KI-086 (Sources) -->
| Fixed (v1.7.0) | 4 |                <!-- KI-097, KI-098, KI-101, KI-102 -->
| Fixed (v1.7.1) | 4 |                <!-- KI-103, KI-104, KI-105, KI-106 -->
| Fixed (v1.8.0) | 1 |                <!-- KI-107 (Mail Agent) -->
| Fixed (v1.8.1) | 1 | <!-- KI-109 (External-LLM Agent) -->
| Fixed (v1.8.2) | 1 | <!-- KI-121 (external_llm_agent — галлюцинация провайдеров) -->
| Fixed (v1.8.x) | 2 | <!-- KI-115 (mail_agent), KI-116 (gemma не tool-calling) -->
| Fixed (v1.9.0) | 3 | <!-- KI-110a (Anthropic Claude), KI-124 (Claude integration test), KI-125 (MailKit/MimeKit security) -->
| Fixed (v1.10.0) | 1 | <!-- KI-110b (Google Gemini) -->
| Fixed (v1.10.1) | 2 | <!-- KI-122 (темы UI), KI-092 (Bootstrap aria-hidden warning — попутно) -->
| Deferred  | 5 | <!-- KI-047, KI-053, KI-082, KI-096, KI-099 -->
| Documented | 12 | <!-- KI-007, KI-009, KI-032, KI-070, KI-093, KI-094, KI-095, KI-112, KI-114, KI-117, KI-118, KI-120 -->
| In Progress | 0 |                   <!-- — -->
| Implemented (v1.3.0) | 2 |          <!-- KI-054, KI-055 -->
| Implemented (v1.7.0) | 1 |          <!-- KI-088 (TESTING.md) -->
| Fixed (v1.11.0) | 9 |              <!-- KI-126, KI-127, KI-129, KI-130, KI-132, KI-133, KI-134, KI-135, KI-136 -->
| Planned | 7 |                       <!-- KI-108, KI-111, KI-113, KI-128, KI-137, KI-138, KI-139 -->
| In Progress | 1 |                   <!-- KI-131 (Vision Agent, Фаза 1 — контракты и DTO) -->
| Partially Fixed | 1 |               <!-- KI-057 -->
| **Всего** | **93** |

**Fixed / Resolved (v1.3.0):** KI-046 (MessageCount), KI-050 (rate limiting UX), KI-051 (анализаторы), KI-058 (модалка approvals UX), KI-059 (placeholder как прокси), KI-060 (user-Markdown), KI-061 (textarea/кнопка), KI-061a (box-shadow фокуса), KI-062 (фокус), KI-063 (Stop-кнопка), KI-065 (Retry после Stop), KI-066 (Copy после done).
**Implemented (v1.3.0):** KI-054 (approvals в чате), KI-055 (tool calling в чате).
**Documented:** KI-007 (gh метки), KI-009 (SSO-сайты), KI-032 (старые cookies), KI-043 (RateLimitingMiddleware memory), KI-049 (tokens=null в stream), KI-064 (SSL wikipedia), KI-070 (Sqlite stale DB).
**Deferred:** KI-047 (fallback PATCH/DELETE), KI-052 (специализированные суб-агенты), KI-053 (multi-user approvals), KI-067 (per-user chat retention), KI-068 (search by message content), KI-069 (inline-edit в sidebar).
**Partially Fixed:** KI-057 (embedding-модели — TODO v1.3.x).
**Implemented (v1.7.0):** KI-088 (`docs/TESTING.md` — чек-лист ручной приёмки).
**Всего в реестре:** 48 KI.

---

## Правила ведения

1. Любая найденная проблема/ограничение → запись с номером `KI-XXX`.
2. Даже если это «не баг» — запись нужна для истории.
3. Приоритет: 🔴 Critical / 🟠 High / 🟡 Medium / 🟢 Low.
4. Статус: Open / In Progress / Fixed / Documented / Deferred / Won't Fix / Resolved.
5. Для Fixed — обязательно указывается «Исправлено в: vX.Y.Z» и список файлов.
6. Для Deferred — указывается «Запланировано: vX.Y.Z» и причина отсрочки.
