# Правила разработки IIChatTools

**Версия:** 1.4.28
**Обновлено:** 2026-10-03
**Назначение:** единый свод правил для команды и ассистента.

При работе над проектом **все** изменения должны соответствовать этим правилам.
Нарушение → комментарий в code review + ссылка на конкретный пункт.

---

## 1. Базовые правила (из стартового промпта v1.0)

| # | Правило |
|---|---------|
| 1.1 | Комментарии и XML-документация — **на русском языке** |
| 1.2 | XML-doc обязателен для всех **public** классов/методов/свойств: `<summary>`, `<param>`, `<returns>`, `<exception>` |
| 1.3 | DI — **только через конструктор** (никакого `ServiceLocator`, `HttpContext.RequestServices` вне middleware) |
| 1.4 | Все I/O-методы — с суффиксом **`Async`** (`ReadFileAsync`, `SendEmailAsync`) |
| 1.5 | Public члены — `PascalCase`, private поля — `_camelCase` |
| 1.6 | Контроллеры: `try-catch` → единый формат `{ success, data, message }` |
| 1.7 | Сервисы: `throw` с осмысленным сообщением, **не гасить стек** (`throw;`, не `throw ex;`) |
| 1.8 | `ILogger` — технические логи; `IAuditService` — действия пользователя |
| 1.9 | Все пути — через `PathHelper.TryGetSafeFullPath` (запрет traversal) |
| 1.10 | CLI — только через `ProcessStartInfo.ArgumentList` (не строковая конкатенация) |
| 1.11 | Версия — через `AppVersion.Current` (читается из `AssemblyInformationalVersion`) |
| 1.12 | Copyright: `© 2026 RuChating (iilmchat) · IIChatTools vX.Y.Z` |
| 1.13 | Все проблемы → `docs/KNOWN_ISSUES.md` с номером `KI-XXX` |
| 1.14 | Новые UI-строки — в **оба `.resx`** (RU + EN). Проверяется `LocalizationSyncTests` |
| 1.15 | Новый инструмент = **1 класс + 1 строка регистрации** в `Startup.cs` (соответствующая группа) |

---

## 2. Документация (обновляется **в том же коммите** с кодом)

| # | Правило |
|---|---------|
| 2.1 | **`CHANGELOG.md`** (Keep a Changelog) — обновляется вместе с кодом, не задним числом |
| 2.2 | Секция **`[Unreleased]`** — рабочая; при релизе → `[X.Y.Z] — YYYY-MM-DD`, сверху — пустая `[Unreleased]` |
| 2.3 | **`README.md`** — обновляется при изменении API endpoints, требований, установки |
| 2.4 | **`docs/KNOWN_ISSUES.md`** — любая найденная проблема (даже «не баг») → запись с KI-XXX |
| 2.5 | **`docs/development/vX.Y/DESIGN.md`** — дизайн-документ для крупных фаз (Chat UI, RAG) |
| 2.6 | Секреты/credentials — **никогда** в git (только User Secrets / env / secret manager) |
| 2.7 | После релиза — обновить `Directory.Build.props` (`<Version>`) + `AppVersion.Current` |
| 2.8 | Ссылки на KI в commit message — обязательны, если фикс связан с реестром. |
| 2.9 | **`docs/development/RELEASES.md` — единый чек-лист релиза.** Обновляется при изменении процесса (новые файлы в § 2, автоматизация в § 9). См. KI-071-инцидент (пропущенный Release v1.3.0). |
| 2.10 | **Markdown-файлы с внутренними code-блоками — выводить в 4 бэктиках снаружи** (````). Иначе при копировании из чата DeepSeek внешняя обёртка ```markdown «съедает» внутренние ```powershell/```csharp/```yaml — файл приходит с битой разметкой (2 инцидента: DESIGN.md v1.4.0, RELEASES.md). Правило действует для всех будущих .md-файлов, содержащих fenced code blocks. |
| 2.11 | **Большие MD-файлы (README, CHANGELOG, RULES, KNOWN_ISSUES, RELEASES) — НЕ выводить целиком в чате.** Вместо этого — **точечный diff** («Найти X / Заменить на Y») или отдельная секция (в 4 бэктиках снаружи, 3 внутри). Причина: третья итерация с развалившейся разметкой README.md (28 KB, ~800 строк) — при копировании внутренние code-блоки слипаются, восстанавливать вручную долго. Правило согласовано 2026-09-25. |

**Типы записей в CHANGELOG:** `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`.

---

## 3. Рабочий процесс (workflow)

| # | Правило |
|---|---------|
| 3.1 | **Маленькие шаги** — крупная фича разбивается на 3-5 подшагов |
| 3.2 | **1 шаг = 1 коммит** — не смешивать несвязанные изменения |
| 3.3 | **`git add -A`** вместо selective add (во избежание «пропущенных» файлов) |
| 3.4 | **Перед коммитом** — `git status --short` для проверки untracked/staged |
| 3.5 | **После push** — проверить Actions (CI + Docker Publish) |
| 3.6 | **Локально** — `dotnet build` **0 warnings, 0 errors** (обязательно) |
| 3.6a | **Первый `dotnet test` на Windows после cold build — 44-60 с** (testhost boot: 30+ DLL из API, включая PuppeteerSharp). Второй+ прогон — 2-3 с. Defender **не главная причина** (проверено: отключение Real-Time дало те же 44 с). Для dev — `dotnet watch test` (boot один раз). См. KI-073, KI-074. |
| 3.7 | **Тесты** — все зелёные перед push (`dotnet test`) |
| 3.8 | **Fallback** — при «не работает локально vs CI» — искать untracked/пропущенные файлы |
| 3.9 | **`dotnet restore --configfile NuGet.Config.online`** — после смены версий пакетов |
| 3.10 | **Перед `dotnet nuget locals all --clear`** — убить `dotnet`/`VBCSCompiler`/`MSBuild` |
| 3.11 | **На новой машине** — `git config user.email/name` **до** первого коммита |
| 3.12 | **Перед push** — `git log --all -p \| grep 'password\|secret'` (проверка на утечки) |
| 3.13 | **Удаление мусора** — после задач удалять временные скрипты (`fix-*.ps1`) |
| 3.14 | **Перед `dotnet build` — остановить запущенное приложение.** Иначе `MSB3027`/`MSB3021`: DLL залочены процессом `IIChatTools.API` (например, из `dotnet run`). **Fix:** `Get-Process IIChatTools.API -ErrorAction SilentlyContinue \| Stop-Process -Force`. Симптом в логе: `The process cannot access the file ... "IIChatTools.API (PID)" блокирует этот файл`. | |
| 3.15 | **Перед `dotnet ef migrations add` — проверить `Database:Provider`** в `appsettings.Development.json`. `dotnet ef` читает провайдер из env `Development` → тип колонок в миграции определяется этим ключом. Для SqlServer-миграций (`Migrations/SqlServer/`) — `Provider = "SqlServer"`; для Sqlite — `"Sqlite"`. После генерации — вернуть `"Sqlite"` для dev-разработки. Симптом ошибки: миграция для SqlServer содержит `TEXT`/`INTEGER`/`Sqlite:Autoincrement`, файлы миграций раздуваются до 60-70 KB из-за `AlterColumn` с `TEXT`→`nvarchar`. См. KI-090. | |

---

## 4. Технические правила C# / .NET 10 (уроки сессии)

| # | Правило | Ошибка / причина |
|---|---------|-------------------|
| 4.1 | `yield return` **запрещён** в `try-catch` | CS1631 |
| 4.2 | `yield return` в `try-finally` — **разрешён** | — |
| 4.3 | `IAsyncEnumerable<T>` требует `using System.Collections.Generic;` | CS0246 |
| 4.4 | `Response.WriteAsync(...)` — extension из `Microsoft.AspNetCore.Http` | CS1061 |
| 4.5 | `HasName()` в EF Core 10 устарел → `HasDatabaseName()` | CS0618 |
| 4.6 | `reader.EndOfStream` в async → CA2024. Использовать `line == null` | CA2024 |
| 4.7 | `cref` в XML-doc требует `using System;` для системных исключений | CS1574 |
| 4.8 | В .NET 8+ runtime-образах пользователь `app` **уже создан** | Docker — `groupadd: already exists` |
| 4.9 | `Microsoft.AspNetCore.RateLimiting` — недоступен в SDK 10.0.401 (KI-042) | `AddRateLimiter` не резолвится |
| 4.10 | Иерархия `new`-свойств — использовать осознанно, документировать в XML-doc | CS0108 |
| 4.11 | Смешивание версий Microsoft-пакетов в одном решении → NU1605 | Синхронизировать в `Directory.Build.props` |
| 4.12 | `<Version>` в `.csproj` **перебивает** `Directory.Build.props` | Рассинхрон версий UI и логов |
| 4.13 | Путь `obj/project.assets.json` — **создаётся** `restore`, удаляется с `obj/` | NETSDK1004 |
| 4.14 | `--no-restore` использует **существующий** `project.assets.json` | Старые версии пакетов |
| 4.15 | **Имена папок не должны совпадать с именами типов из `Entities`** — namespace `…Implementation.Chat` конфликтует с типом `Chat` (CS0118). Папка → `ChatTools`, `ChatHandlers` и т. п. |
| 4.16 | **Ключи `.resx` — case-insensitive.** `ResourceManager` не различает `"По умолчанию"` и `"по умолчанию"` → MSB3568 (duplicate resource name). Для новых ключей использовать camelCase-стиль `WelcomeTitle`, `ChatModelLabel`. Перед добавлением: `Select-String -Path *.resx -Pattern "имя"` — проверка коллизий. |
| 4.17 | **Локализация JS-строк** — через `data-*`-атрибуты на HTML-элементе (`data-label-default="@Localizer["ChatModelSuffixDefault"]"`). JS читает `el.dataset.labelDefault`. Не дублировать строки в JS. |
| 4.18 | **`AbortController` + `fetch(..., { signal })`** — единственный способ отменить SSE-стрим с клиента. `abort()` рвёт соединение → серверный `CancellationToken` отменяется. Для `AbortError` — `try/catch` с `ex.name === 'AbortError'`. |
| 4.19 | **Флаг завершения стрима** — `bubble.dataset.streamCompleted = '1'` в `case 'done'`. В `finally` проверять `dataset.streamCompleted === '1'` вместо дополнительных Promise/SetState. |
| 4.20 | **`setTimeout(() => el.focus(), 0)`** — для фокуса после re-render (sidebar/списка). Синхронный `.focus()` теряется при перерисовке DOM. |
| 4.21 | **Поиск последнего элемента в массиве** — обратный цикл: `for (let i = arr.length - 1; i >= 0; i--)`. Для «найти последний role='assistant'» и подобных сценариев. `Array.findLast()` — ES2023, не всегда поддерживается. |
| 4.22 | **`new ConcurrentDictionary<...>(StringComparer.Ordinal)`** — для ключей-callId (чувствительны к регистру). По умолчанию `ConcurrentDictionary` использует `EqualityComparer<string>.Default` (`Ordinal`), но явное указание — самодокументируемо. |
| 4.23 | **`TaskCompletionSource` — с `TaskCreationOptions.RunContinuationsAsynchronously`.** Иначе continuation выполнится в потоке вызывающего (`SetResult`) → потенциальный deadlock. См. `ChatApprovalCoordinator`. |
| 4.24 | **`finalizeAssistantBubble` — пересоздавать `.chat-message-actions`** после Markdown-рендера. Иначе кнопки (📋 / 🔄 / ✏️), созданные при пустом тексте, не синхронизируются с финальным содержимым. См. KI-066. |
| 4.25 | **Sqlite `EnsureCreatedAsync` не мигрирует существующую БД.** Если добавили новую сущность (или изменили модель) — старая `.db` не обновится, падает `no such table: <Table>`. Решение: **удалить `Data/*.db`** (и `bin/Debug/net10.0/Data/*.db`) → перезапустить. Для прод-Sqlite — миграции в `Migrations/Sqlite/`. См. KI-070. |
| 4.26 | **`LOWER()` в SQLite не обрабатывает не-ASCII (кириллицу).** SQLite LOWER конвертирует только ASCII `A-Z`. Запрос `LOWER(Title) LIKE '%прив%'` находит только строки в нижнем регистре и не матчит `'Привет'`. Аналогично `LOWER(Content)`. **Решение:** для регистронезависимого поиска по не-ASCII — фильтровать в памяти через `string.Contains(term, StringComparison.OrdinalIgnoreCase)`. Тянем кандидатов из БД, фильтруем в .NET. См. KI-068. |
| 4.27 | **Даты из Sqlite/EF Core приходят с `Kind=Unspecified`.** Newtonsoft.Json по умолчанию сериализует их без суффикса `Z`, и JS `new Date()` парсит как local → расхождение на смещение (в UTC+3 → «3 ч назад» для только что созданных сущностей). **Решение:** `AddNewtonsoftJson(o => o.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc)` (и то же для `SseJsonSettings`). Все даты в проекте — UTC. См. KI-071. |
| 4.28 | **Внешние HTTP-инструменты (Wikipedia, Web, FetchWebContent) — всегда явный `Timeout` (15s) + 1 retry.** Дефолтный `HttpClient.Timeout = 100s` недопустим: через корпоративный прокси SSL-обрыв превращается в 40+ секунд зависания чата. Retry — только при transient-ошибках (`HttpRequestException`, `TaskCanceledException`), **не при внешней отмене** (`context.CancellationToken.IsCancellationRequested`). См. KI-064. |
| 4.29 | **`ExecuteDeleteAsync`/`ExecuteUpdateAsync` не поддерживаются InMemory-провайдером EF Core 10.** Тесты на InMemory падают с `InvalidOperationException`. **Решение:** определять провайдер через `_dbContext.Database.ProviderName` (Contains "InMemory") и использовать fallback (`ToList` + `RemoveRange` + `SaveChangesAsync`). Для SqlServer/Sqlite — bulk-DELETE. См. `ChatService.DeleteOldChatsInternalAsync` (KI-067-2). |
| 4.30 | **`cref` в XML-doc с перегрузками → CS0419.** После добавления перегрузки метода все `cref="Type.Method"` становятся неоднозначными. **Решение:** либо уточнять сигнатуру (`Type.Method(int, CancellationToken)`), либо использовать `<c>Text</c>` вместо `<see>`. При добавлении перегрузки — grep по `cref="MethodName"` в проекте. См. `ChatRetentionService.cs` (KI-067-2). |
| 4.31 | **`Chat:Retention:Enabled = false` в `appsettings.Development.json` — намеренно.** Чтобы не удалять тестовые чаты при каждом запуске dev-сервера. Для проверки retention: временно `Enabled = true` + `CleanupIntervalHours = 1` + перезапуск. Первый прогон — через **2 минуты** после старта (Task.Delay), дальше — по `PeriodicTimer`. В логах искать: `ChatRetentionService запущен: интервал=1ч, срок=Nд`. См. KI-067-3 (smoke). |
| 4.32 | **`Microsoft.ML.Tokenizers` — это API, без данных.** Для `TiktokenTokenizer.CreateForEncoding("cl100k_base")` нужны **два пакета одной версии**: `Microsoft.ML.Tokenizers` (API) + `Microsoft.ML.Tokenizers.Data.Cl100kBase` (BPE-словарь). Иначе — `InvalidOperationException: The tokenizer data file ... could not be loaded`. Аналогично: `o200k_base` → `Microsoft.ML.Tokenizers.Data.O200kBase`, `p50k_base` → `...Data.P50kBase`. См. KI-049a. |
| 4.33 | **`yield return` и scope переменных.** В `IAsyncEnumerable<T>`-методах переменные, используемые в `yield return`, должны быть объявлены **вне** `try-catch`. Если объявить внутри `try` (например, `var contextTokens = ...` перед `AddMessageAsync`), то `yield return Done(..., contextTokens, ...)` после `try` даст **CS0103**. Решение: hoist объявление до `try`. См. KI-084b (ChatStreamService). |
| 4.34 | **Изменение интерфейса → grep по ВСЕМ реализациям И вызовам.** Три случая: <br/>**(а) Новый член интерфейса** → CS0535 в fake-заглушках: фейки должны реализовать **все** члены (либо реальной заглушкой, либо `throw new NotImplementedException()`). Прогон: `Select-String -Path IIChatTools.Tests\**\*.cs -Pattern ': IYourInterface'`. <br/>**(б) Новый опциональный параметр в методе интерфейса** (например, `bool ignoreEnabled = false` в середине сигнатуры) → **CS1503** во всех позиционных вызовах, где далее шли другие параметры (`cancellationToken`). Прогон: `Select-String -Path **\*.cs -Pattern 'MethodName\('`. <br/>**(в) `default interface method` (C# 8+)** → CS1061 при вызове через **конкретный тип** (метод виден только через интерфейс). Прогон: grep по типу класса. См. RULES § 4.46. <br/>Все три случая — **перед `dotnet build`** после любой правки интерфейса. Симптомы: **CS0535** / **CS1503** / **CS1061** в тестах или production. См. KI-083 Фаза 1 (случай а), KI-097 Фаза 6A (случай б). |
| 4.35 | **Mock `HttpMessageHandler` в тестах `HttpClient` должен уважать `CancellationToken`.** При тестировании `HttpClient.Timeout` handler **обязан** передавать `ct` в `Task.Delay` / `Task.WaitAsync` (`await Task.Delay(3s, ct)`), иначе `HttpClient` не отменит операцию — `SendAsync` не бросит `TaskCanceledException`. Причина: `HttpClient.Timeout` реализован через `CancellationTokenSource.CancelAfter`, и handler должен реагировать на отмену. Симптом: тест `*_Timeout_Throws` проходит без исключения (падает `Assert.Throws`). См. KI-083 Фаза 1. |
| 4.36 | **В тестах стратегий чанкинга не полагаться на фиксированные `ChunkSize`.** `TokenCounter` (cl100k_base) даёт приближение ±5-10%, и точное число токенов зависит от версии токенизатора. Если тест проверяет «абзац влезает / не влезает» — вычислять `ChunkSize` **динамически** через `Counter.CountTokens(...)` (например, `pTokens + 20%`). Симптом: тест `Chunk_ParagraphsSplitPreferred` падал на фиксированном `ChunkSize=80` — cl100k дал ~25 токенов на абзац вместо ожидаемых 40, два абзаца склеились в один. См. KI-083 Шаг 3B. |
| 4.37 | **CHANGELOG обновляется в КАЖДОМ коммите с кодом (RULES § 2.1), включая мелкие шаги.** В серии коммитов Фаз 2B-3C (KI-083) запись в `CHANGELOG.md` была пропущена для 5 шагов подряд — документация отстала на несколько коммитов. Перед `git commit` — **всегда** проверять: добавлена ли запись в `[Unreleased]`? Симптом: пользователь спрашивает «мы про документацию не забыли?». Восстановление — отдельный коммит `docs: sync CHANGELOG with code`. |
| 4.38 | **Не полагаться на `StreamReader` для BOM-детекции.** В .NET 10 комбинация «явный `encoding` + `detectEncodingFromByteOrderMarks: true`» **не гарантирует** стрип BOM. Надёжный подход для парсеров: читать файл байтами (`File.ReadAllBytesAsync`), находить BOM явно (`EF BB BF` = UTF-8, `FF FE` = UTF-16 LE, `FE FF` = UTF-16 BE), декодировать через `Encoding.GetString(bytes, offset, len)`. **Плюс** — defensive strip всех ведущих `\uFEFF` из строки (страховка от рантайм-нюансов). Симптом: тест «*_StripsBom» падает, `text[0]` = `65279`. См. KI-083 Шаг 4A. |
| 4.39 | **Не использовать `Assert.True(false, msg)` для диагностики в тестах.** Принудительный фейл теста ради вывода значений — плохая практика: (а) xUnit-анализатор ругается (xUnit2020); (б) если забыть удалить, тест всегда падает; (в) MSBuild-инкрементальная сборка может не инвалидировать старую DLL — `dotnet test --no-build` будет использовать **старую** версию теста (stale DLL). Правильно — `Console.WriteLine` (виден в `--logger "console;verbosity=detailed"`) или `ITestOutputHelper`. Если после правки теста `dotnet build` завершается за < 1 с и `--no-build` даёт старый результат — **чистить `bin/obj`** и запускать `dotnet test` **без** `--no-build`. См. KI-083 Шаг 4A. |
| 4.40 | **В тестах сервисов, сохраняющих файлы под GUID-именем, не проверяй исходное имя в `FilePath`.** Когда файл сохраняется как `{guid}.ext` (например, `chat-attachments/{chatId}/{guid}.ext`), исходное имя живёт только в БД-поле (`ChatAttachment.FileName`), а не в физическом пути. Проверяй расширение (сохраняется) + подпапку (`chat-attachments/{chatId}`) + сам факт существования файла. Симптом: тест `UploadAsync_..._SavesFileInUserWorkspace` падает с `Assert.Contains("hello", ...)` — путь содержит GUID, а не исходное имя. См. KI-083 Шаг 6A-тесты. |
| 4.41 | **`JToken.Value<T>()` без аргумента — это extension для `IEnumerable<JToken>`, а не для `JToken`.** На одиночном `JToken` доступен только instance-метод `Value<T>(object key)`, отсюда ошибка **CS7036** при попытке `token["field"]?.Value<bool>()`. **Решение:** использовать приведение `(bool?)token["field"] ?? false` или `token.Value<bool>("field")` (instance, с key). См. KI-083 Шаг 6B (fix тестов). |
| 4.42 | **`AddDbContext` + `UseInMemoryDatabase(name)` в тестах требует явного `InMemoryDatabaseRoot` + вычисления имени БД ДО лямбды.** (1) Без `InMemoryDatabaseRoot` EF Core создаёт свой root на каждый `IServiceScope` → `SetAsync` в одном scope не виден из другого (`GetBoolAsync` возвращает `defaultValue`). (2) Лямбда `opt => opt.UseInMemoryDatabase(...)` выполняется на **каждый** scope, поэтому если внутри лямбды есть `Guid.NewGuid()` (или любой не-идемпотентный вызов) — каждый scope получит своё имя БД, и данные снова не шарятся. **Правильно:** `var root = new InMemoryDatabaseRoot(); var dbName = "test_" + Guid.NewGuid().ToString("N"); services.AddSingleton(root); services.AddDbContext<T>(opt => opt.UseInMemoryDatabase(dbName, root));` — оба значения вычислить **до** лямбды, зарегистрировать как Singleton. **Симптом:** тест `EnableAsync_...` устанавливает флаг enabled=true в одном scope, а `GetStatusAsync` через 100 мс видит `enabled=false`. См. KI-083 Шаг 7D.1 (`WorkspaceIndexServiceTests`). |
| 4.45 | **`Path.GetFileName` / `Path.GetDirectoryName` — кросс-платформенные грабли.** На Windows `Path.*` распознаёт оба разделителя (`\` и `/`). На **Linux** — только `/`; `\` — обычный символ. Симптом: на Windows-машине тест проходит, на CI (`ubuntu-latest`) — падает с `Expected: "RULES.md", Actual: "C:\\Projects\\...\\RULES.md"`. **Решение:** перед `Path.GetFileName` нормализовать `\` → `/`: `var normalized = path.Replace('\\', '/'); Path.GetFileName(normalized);`. Тот же паттерн, что в `PathHelper` (KI-040 — traversal через backslash). При тестировании путей на Windows — проверять, что вход с `\` тоже обрабатывается (CI поймает). Симптом: красный CI при зелёном локальном `dotnet test`. См. KI-086 Шаг 5.1.fix2. |
| 4.46 | **Default interface method (C# 8+) не виден через конкретный тип — только через интерфейс.** Если метод объявлен в интерфейсе с default-реализацией (например, `ITool.RequiresApprovalForCall` — v1.7.0, KI-101), то класс, который его **не переопределил**, не получает метод в свой публичный API. Вызов на переменной типа класса → **CS1061**. **Решение:** объявлять переменную типом **интерфейса** (`ITool x = new MyTool()`), или переопределять метод в классе (тогда он виден и на конкретном типе). Симптом: `error CS1061: 'ClassName' не содержит определения 'MethodName'` в тестах, которые вызывают default-метод на конкретном fake-типе. См. KI-101 (Фаза 5.5, `ToolRegistryTests.FakeToolWithApproval`). |
| 4.47 | **MailKit `IMessageSummary.Attachments` — тип `IEnumerable<BodyPartBasic>`, а не `IList<>`.** Свойство `Count` — **extension-метод LINQ** (`Enumerable.Count()`), не свойство коллекции. Без скобок — **CS0019** (`Оператор ">" невозможно применить к операнду типа "группа методов" и "int"`). **Правильно:** `s.Attachments?.Any() == true` (универсально для `IEnumerable` / `IList`, null-safe). **Урок:** при работе с новым API (MailKit, MimeKit, …) — не полагаться на «типичный» `IList<T>.Count`, всегда проверять фактический тип (IntelliSense / F12 → Go to Definition). См. KI-107 Фаза 2 (fix CS1061 → fix CS0019). |
| 4.48 | **`HttpClient.Timeout` нельзя менять после первого `SendAsync`.** Симптом: `InvalidOperationException: This instance has already started one or more requests. Properties can only be modified before sending the first request.` Проявляется в **retry-цикле**, если `HttpClient` переиспользуется (mock-фабрика в тестах, кэш, `IHttpClientFactory` с Singleton-клиентом). **Решение:** не трогать `HttpClient.Timeout`, а использовать `CancellationTokenSource.CreateLinkedTokenSource(ct)` + `CancelAfter(TimeSpan)` — таймаут привязан к запросу, а не к клиенту. **Дополнительно:** handler **обязан** уважать `ct` (RULES § 4.35). Различать внешнюю отмену и timeout через `catch (OperationCanceledException) when (ct.IsCancellationRequested)` + `catch (OperationCanceledException)`. См. `ExternalLlmClient.SendOnceAsync` (Фаза 2.5, KI-109). |
| 4.44 | **Новый top-level `ITool` в DI — не значит, что Chat его видит.** `ToolRegistry` содержит **все** инструменты, но `ChatStreamService.StreamAsync` фильтрует их через `allowedNames` = `SubAgentRegistry.GetEnabled()` (6 агентов) + `consult_secondary_agent` + `RagToolNames` (3 RAG). При добавлении нового инструмента на верхний уровень Chat (не внутри агента) — **обязательно** добавить его имя в `allowedNames` в `ChatStreamService`, иначе LLM физически не сможет его вызвать. Симптом: инструмент работает через `/api/tools/execute`, но LLM в чате его никогда не выбирает. См. KI-086 Шаг 3.5c. |
| 4.43 | **У `JToken` нет `GetValue(string, StringComparison)` — CS1061.** Для case-insensitive чтения свойства `JObject` (нужно при тестировании ответов ASP.NET Core MVC: реальный MVC сериализует DTO в **camelCase** — `chunkCount`, `durationMs`; а `JsonConvert.SerializeObject(x)` в тесте — в **PascalCase** — `ChunkCount`, `DurationMs`) перебирать `obj.Properties()` вручную. Встроенные `Value<T>()` / `SelectToken()` работают только с точным совпадением. **Правильно:** `foreach (var prop in obj.Properties()) { if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) return prop.Value; }` — вернуть `null`, если не найдено. **Симптом:** `Assert.Equal("project_docs", data[0]["name"]?.ToString())` падает с `Expected: "project_docs", Actual: null`. См. KI-083 Шаг 7D.2 (`AdminKnowledgeControllerTests`). <br/>**Уточнение (2026-09-30, KI-109 Фаза 3):** в тестах tools, возвращающих **именованный DTO** (не анонимный объект) через `ToolResult.Data`, действует **то же правило**, но с обратным знаком: `JObject.FromObject(result.Data)` использует **дефолтный Newtonsoft-контракт** → **PascalCase** (`Primary`, `Secondary`, `TotalCostUsd`). А в реальном HTTP-ответе MVC сериализует тот же DTO в **camelCase** (`primary`, `secondary`). **Правило:** в тестах `JObject.FromObject` — читать **PascalCase**; в тестах HTTP-ответов (через `WebApplicationFactory` / `JsonConvert.DeserializeObject`) — **camelCase**. Анонимные объекты (`new { provider, content }`) — как названы в C#. Симптом: `Assert.Equal("deepseek", json["primary"]?["provider"]?.ToString())` → `Expected: "deepseek", Actual: null`. См. `AskExternalLlmToolTests.ExecuteAsync_WithCompare_RunsBothParallel` (Фаза 3, KI-109). |
| 4.49 | **`AddMemoryCache` / `AddOptions` / `AddHttpClient` принимают `Action<T>`, а не `Func<IServiceProvider, T>`.** В отличие от `AddDbContext<T>(sp => ...)` (там overload с `IServiceProvider` **есть**), у `AddMemoryCache` / `AddOptions` / `AddHttpClient` factory-overload'а нет. Симптом: `CS1929: "MemoryCacheOptions" не содержит определение для "GetRequiredService"` + `CS8030: Анонимная функция, преобразованная в делегата, возвращающего void, не может возвращать значение` — если писать по инерции `services.AddMemoryCache(sp => { var x = sp.GetRequiredService<...>(); return new MemoryCacheOptions{...}; })`. **Решение:** <br/>**(а)** читать конфиг напрямую через `Configuration.GetValue<T>("Section:Key", defaultValue)` в `Startup.ConfigureServices` — метод уже в scope, `IConfiguration` доступен как `this.Configuration`; <br/>**(б)** для dynamic-настройки — `services.PostConfigure<MemoryCacheOptions>(opts => {...})` (выполняется после `Configure`, но **до** первого resolve; `IServiceProvider` там тоже нет); <br/>**(в)** отдельный `IConfigureOptions<T>`-класс с DI-зависимостями (инжектится через конструктор — `IServiceProvider` доступен как параметр). **Дублирование `Configure<T>` и `AddMemoryCache(...)`** — норма (первый документирует намерение, второй применяет) — не пытаться «устранить» через factory. См. `Startup.cs` (v1.8.2, ToolCache). |
| 4.50 | **`params`-параметр + именованный аргумент в вызове = CS8323.** Компилятор C# **не разрешает** смешивать именованный аргумент с последующим **неименованным** (`error CS8323: Именованный аргумент "X" используется не на своем месте, но за ним следует неименованный аргумент`). Классический случай — helper с сигнатурой `void Helper(bool enabled = true, params T[] items)`: попытка вызвать `Helper(items: a, b)` (первый элемент именован, второй «просто так») — **CS8323**, потому что `params` при именованном аргументе требует, чтобы **всё содержимое массива** было в одном именованном виде, а `params`-синтаксис этого не умеет. **Симптом:** `Options(tools: ("web_search", true, 60), ("wikipedia_search", true, 60))` — ошибка на **второй** скобке. **Решение:** либо (а) **сделать все параметры позиционными** (`Helper(true, ("a"), ("b"))` — `enabled` идёт позиционно первым, `params` — вторым); либо (б) **заменить `params` на `IEnumerable<T>`** (`Helper(bool enabled, IEnumerable<T> items)` — тогда `Helper(enabled: true, items: new[]{...})` работает). **Не пытаться** «частично именовать» — C# это не поддерживает. **Общее правило:** если у helper'а есть и «флаг», и `params` — флаг **всегда позиционный первый**, `params` — последний. См. `ToolResultCacheTests.BuildOptions` (v1.8.2, Шаг 1.3). |
| 4.51 | **`ITool`, зависящий от `IToolRegistry` или `ISubAgentService`, должен инжектить `Func<T>`, а не прямой интерфейс.** `ToolRegistry` строится из `IEnumerable<ITool>`. Если хоть один `ITool` зависит от `IToolRegistry` напрямую — DI-контейнер обнаружит цикл при старте приложения (`ValidateOnBuild`). **Симптом:** `System.AggregateException: ... A circular dependency was detected for the service of type 'IIChatTools.Services.Interfaces.IToolRegistry'. IIChatTools.Services.Interfaces.IChatStreamService(...) -> IToolRegistry(ToolRegistry) -> IEnumerable<ITool> -> ITool(...) -> IToolRegistry`. **Сборка и тесты цикл НЕ ловят** — только `dotnet run` падает. **Решение (ADR-002):** `Func<IToolRegistry>` в конструкторе + вызов `_toolRegistryFactory()` **внутри** метода (1 раз на операцию). Регистрация: `services.AddScoped<Func<IToolRegistry>>(sp => () => sp.GetRequiredService<IToolRegistry>());` (рядом с `Func<ISubAgentService>`). В тестах — обернуть fake в лямбду: `new MyTool(() => fakeRegistry, ...)`. **Прецеденты:** `ConsultSecondaryAgentTool` / `AgentToolBase` (`Func<ISubAgentService>`), `CodeAgentWithReviewTool` (`Func<IToolRegistry>`, KI-126 Шаг 1D-fix2). |

---

## 5. Безопасность

| # | Правило |
|---|---------|
| 5.1 | Секреты — **только** в User Secrets / env / secret manager |
| 5.2 | Прокси-credentials — не хардкодить, читать из env (`IICHATTOOLS_PROXY*`) |
| 5.3 | `PathHelper` — единственный способ работы с путями |
| 5.4 | `ArgumentList` — единственный способ вызова CLI |
| 5.5 | **Периодически** — `git log --all -p \| grep 'password\|secret\|nikiforov'` перед push |
| 5.6 | При утечке — смена пароля/токена **немедленно**, потом `filter-repo` |
| 5.7 | Файлы `.env`, `appsettings.Local.json`, `*.pfx` — в `.gitignore` |
| 5.8 | GitHub Secret Scanning + Push Protection — включены |

---

## 6. Git-специфичные правила

| # | Правило |
|---|---------|
| 6.1 | `.gitignore` — обязательное место для `bin/`, `obj/`, `LocalPackages/`, `logs/`, `Workspace/`, `.continue/` |
| 6.2 | `.gitattributes` — `* text=auto eol=lf` (единый LF) |
| 6.3 | LF/CRLF warnings при `git add` — не критичны, но не игнорировать |
| 6.4 | Формат commit message: `<type>(<scope>): <subject>` + пустая строка + body |
| 6.5 | Типы: `feat`, `fix`, `docs`, `test`, `chore`, `refactor`, `security`, `perf` |
| 6.6 | **Force-push** — только `--force-with-lease`, никогда `--force` |
| 6.7 | **`filter-repo`** — только после смены скомпрометированных секретов |
| 6.8 | При merge с origin — **сначала** `git fetch`, потом анализ конфликтов |
| 6.9 | `.gitkeep` — для пустых папок, которые нужны в репо |

---

## 7. Отложенные задачи (актуальный список KI)

См. [`docs/KNOWN_ISSUES.md`](../KNOWN_ISSUES.md) — полный реестр.

**Краткая выжимка Open/Deferred/Documented (после релиза v1.13.1):**

| KI | Приоритет | Статус | Суть | План |
|----|-----------|--------|------|------|
| ~~KI-109~~ | — | ✅ **Fixed (v1.8.1)** | External-LLM Agent (DeepSeek/OpenAI/Groq/Together/Ollama). Все 5 фаз закрыты. +72 теста (424 → 496). DESIGN: `docs/development/v1.8/DESIGN_EXTERNAL_LLM.md`. | — |
| KI-044 | 🟢 | Documented | `iichattools_audit_entries_total` / `lmstudio_requests_total` не инкрементируются | v1.7.x |
| KI-047 | 🟡 | Deferred | Fallback PATCH/DELETE через POST (для старых сетей) | v1.7.x+ |
| KI-053 | 🟡 | Deferred | Multi-user approvals (роли approver, уведомления) | v1.7.x+ |
| KI-057 | 🟢 | Partially Fixed | Config-driven exclusion patterns моделей | v1.7.x |
| KI-070 | 🟢 | Documented | Sqlite stale DB / `EnsureCreated` не мигрирует | v1.7.x |
| KI-077 | 🟢 | Documented | `model: null` при PUT агента = «сброс» | — |
| KI-082 | 🟢 | Deferred | Модалка-редактор длинных user-сообщений | v1.8.0+ |
| KI-085 | 🟢 | Documented | SQLite `database is locked` (внешний клиент) | — |
| KI-090 | 🟢 | Documented | SqlServer-migrations snapshot drift | — |
| KI-091 | 🟡 | Deferred | SqlServer цепочка миграций повреждена | **v1.7.x (обязательно перед prod-SqlServer)** |
| KI-092 | 🟢 | Documented | Bootstrap 5.2 `aria-hidden` warning | — |
| KI-093 | 🟢 | Documented | SQLite locked (дубликат KI-085, оставлен для истории) | — |
| KI-094 | 🟢 | Documented | `wikipedia_search` intermittent timeout (SSL через прокси) | v1.7.x |
| KI-095 | 🟢 | Documented | snippet `fetch_web_content` дублирует label | — |
| KI-096 | 🟢 | Deferred | GitHub Wiki для проекта | v1.8.0+ |
| KI-099 | 🟢 | Deferred | Внешние БД (Postgres / MySQL) для Database Agent | v1.8.0 |
| KI-103 | 🟡 | Documented | `/status` — hardcoded RU в `status.js` | v1.7.x |
| KI-128 | 🟡 | Planned | Browser workflow недоступен через Chat (`browser_agent` + `save_screenshot_to_file`) | v1.11.x |
| KI-131 | 🟡 | Planned | Vision Agent (Vision LLM + Planner LLM + 3 backend'а) | **v1.12.0** |
| KI-137 | 🟢 | Planned | Vision Agent: OCR-fallback для мелкого текста | v1.12.x |
| KI-138 | 🟢 | Planned | Vision Agent: маскирование PII на скриншотах | v1.12.x |
| KI-139 | 🟢 | Planned | Vision Agent: внешние VL (Claude Computer Use / OpenAI CUA) | v1.12.x |
| KI-140 | 🟢 | **Fixed (v1.13.0)** | Speech Recognition — офлайн STT (Whisper.net) | — |
| KI-144 | 🟢 | Documented | Chrome может выбрать virtual audio device (Steam Streaming, `maxAbs=0`) | — |
| KI-145 | 🟡 | **Fixed (v1.13.1)** | Device picker в `/profile → 🎤 Аудио` | — |
| KI-146 | 🟢 | Planned | Device picker: fallback-полировка + дедупликация label | v1.13.x |
| KI-147 | 🟢 | Planned | ARCHITECTURE.md устарел (v1.7.0 → v1.13.1) | v1.13.x |

**Fixed в v1.7.0:** KI-097 (Database Agent), KI-098 (Admin UI whitelist), KI-101 (per-action approval), KI-102 (Admin UI локализация).

**Fixed в v1.11.0:** KI-126 (Actor-Critic Ф1), KI-127 (`code_agent_with_review` выбор), KI-129 (F5 persistence), KI-130 (code_agent + вложения), KI-132 (tool_result раздут), KI-133 (Skip feedback), KI-134 (tool-сообщение при отмене SSE), KI-135 (orphaned sessionId), KI-136 (порядок yield).

**Fixed в v1.12.0:** KI-131 (Vision Agent, MVP: `local-harness` + `vision_agent`).

**Fixed в v1.13.0:** KI-140 (Speech Recognition, Whisper.net).

**Fixed в v1.13.1:** KI-145 (Device picker в `/profile → 🎤 Аудио`).

**Documented в v1.13.1:** KI-144 (Chrome virtual audio device).

**Planned (v1.13.x+):** KI-146 (fallback-полировка + дедупликация), KI-147 (ARCHITECTURE.md).

**Implemented в v1.7.0:** KI-088 (`docs/TESTING.md`).

**Всего в реестре:** ~99 KI. **Fixed/Resolved:** ~88. **Deferred:** 5. **Documented:** 13. **Planned:** ~12. **Partially Fixed:** 1 (`KI-057`).

> **KI-068** (поиск по содержимому) исправлен **дважды**: первая версия использовала `LOWER() LIKE`, не работала с кириллицей на SQLite. Итоговое решение — фильтрация в памяти (см. § 4.26).
>
> **KI-097** (Database Agent) прошёл 8 фаз (0-7) + 3 fix-фазы (5.5, 6E, 7C). См. [`docs/development/v1.7/DESIGN_DB_AGENT.md`](v1.7/DESIGN_DB_AGENT.md).

---

## 8. История изменений правил

| Дата | Версия | Что добавлено |
|------|--------|---------------|
| 2026-09-13 | 1.0.0 | 15 базовых правил |
| 2026-09-16 | 1.0.2 | Правила 2.7, 2.8 (документация + KI в commit) |
| 2026-09-18 | 1.1.0 | Правила Git 6.x (force-with-lease, filter-repo) |
| 2026-09-21 | 1.2.0 | Правила 3.x (workflow) — маленькие шаги, `git add -A` |
| 2026-09-21 | 1.3.0 | Правила 4.x (технические C#/.NET 10) + 7 (KI-выжимка) |
| 2026-09-23 | 1.4.0 | Правила 4.16–4.24 (уроки v1.3: resx case-insensitive, data-* локализация, AbortController, TCS, `:has()`, фокус после re-render). Раздел 7 (актуальная KI-выжимка). **Релиз v1.3.0.** |
| 2026-09-24 | 1.4.1 | Правила 4.25 (Sqlite stale DB), 4.26 (LOWER в SQLite и не-ASCII), 4.27 (даты без `Z`), 4.28 (Timeout+retry для HTTP-инструментов). **Релиз v1.3.1.** |
| 2026-09-24 | 1.4.2 | Правила 2.9 (RELEASES.md — чек-лист релиза), 2.10 (MD-файлы в 4 бэктиках), 3.6a (testhost boot ~44-60 с). **Релиз v1.4.0** — Multi-Agent (KI-052). |
| 2026-09-24 | 1.4.3 | Правила 4.29 (InMemory + ExecuteDeleteAsync), 4.30 (cref + перегрузки). **KI-067** — per-user retention. |
| 2026-09-25 | 1.4.4 | Правило 4.31 (Retention:Enabled в Development). **KI-049** — tokensIn/Out через tiktoken. |
| 2026-09-25 | 1.4.5 | Правило 3.14 (остановить приложение перед build). **KI-049a** — пакет Data.Cl100kBase. |
| 2026-09-25 | 1.4.6 | Правило 4.32 (два пакета ML.Tokenizers: API + Data). **KI-085** — SQLite locked. |
| 2026-09-25 | 1.4.7 | Правило 2.11 (большие MD — только diff). **KI-084b** — токены в SSE + fix CS0103. |
| 2026-09-25 | 1.4.8 | § 7 — актуализация KI-выжимки. **Релиз v1.4.1** — Chat UX + retention + tiktoken. |
| 2026-09-25 | 1.4.9 | **DESIGN v1.5.0** (RAG) — согласован, KI-083 → In Progress. |
| 2026-09-25 | 1.4.10 | **KI-087** — актуальный ARCHITECTURE.md + архив docs/architecture/. |
| 2026-09-25 | 1.4.11 | Правила 4.34 (расширение интерфейса → grep по fake-заглушкам), 4.35 (mock HttpMessageHandler + CancellationToken). **KI-083 Фаза 1** — Embedding Service. |
| 2026-09-25 | 1.4.12 | Правило 3.15 (проверить `Database:Provider` перед `dotnet ef migrations add`). **KI-083 Шаг 2A** — DocumentChunk + KI-090. |
| 2026-09-25 | 1.4.13 | Правила 4.36 (динамический `ChunkSize` в тестах стратегий), 4.37 (CHANGELOG в каждом коммите с кодом). **KI-083 Шаги 2B-3C** — Vector Store + Chunking. |
| 2026-09-25 | 1.4.14 | Правила 4.38 (BOM-детект через байты, не StreamReader), 4.39 (не использовать `Assert.True(false)`, чистить `bin/obj` при stale DLL). **KI-083 Шаг 4A** — PlainTextParser. |
| 2026-09-25 | 1.4.15 | Правила 4.40 (GUID-имена файлов в тестах), 4.41 (`JToken.Value<T>()` без key → CS7036). **KI-083 Шаги 6A-тесты / 6B** — Attachments. |
| 2026-09-28 | 1.4.16 | Правила 4.42 (`InMemoryDatabaseRoot` + имя БД **до** лямбды `AddDbContext`; иначе разные scope = разные БД), 4.43 (`JToken.GetValue` не существует — `JObject.Properties` вручную). **KI-083 Шаги 7D.1 / 7D.2** — Unit-тесты WorkspaceIndexService / AdminKnowledgeController. |
| 2026-09-28 | 1.4.17 | § 7 — актуализация KI-выжимки после релиза v1.5.0 (KI-083 → Fixed, KI-091 → Deferred v1.5.0-rc, KI-092/093 Documented). **Релиз v1.5.0.** |
| 2026-09-28 | 1.4.18 | Правила 4.44 (новый top-level `ITool` → `allowedNames` Chat), 4.45 (`Path.GetFileName` — кросс-платформенные грабли; Linux не распознаёт `\`). § 7 — KI-086 → Fixed (v1.6.0). **В работе v1.6.0 (Sources).** |
| 2026-09-29 | 1.4.19 | Правило 4.46 (default interface method не виден через конкретный тип — CS1061). **KI-101 Фаза 5.5** — per-action approval (`ITool.RequiresApprovalForCall`). |
| 2026-09-29 | 1.4.20 | § 7 — актуализация KI-выжимки после релиза **v1.7.0** (Database Agent). **Fixed в v1.7.0:** KI-097, KI-098, KI-101, KI-102. **Implemented:** KI-088 (TESTING.md). **Documented:** +KI-094, KI-095, KI-103. |
| 2026-09-29 | 1.4.21 | § 4.47 — MailKit `IMessageSummary.Attachments` — `IEnumerable`, `Count` — extension (CS0019). **KI-107 Фаза 2** (Mail Agent). |
| 2026-09-30 | 1.4.22 | § 4.48 — `HttpClient.Timeout` нельзя менять после первого `SendAsync`; использовать `CancellationTokenSource.CancelAfter` в retry-цикле. **KI-109 Фаза 2.5** (ExternalLlmClient). |
| 2026-09-30 | 1.4.23 | § 4.43 — уточнение: `JObject.FromObject(DTO)` → PascalCase; MVC-ответ → camelCase. **KI-109 Фаза 3** (`AskExternalLlmToolTests`). |
| 2026-09-30 | 1.4.23b | § 7 — KI-109 → **In Progress** (Фазы 0–4 закрыты). **KI-109 Фаза 4** (`external_llm_agent`). |
| 2026-09-30 | **1.4.24** | § 7 — **KI-109 → Fixed (v1.8.1)**. **Релиз v1.8.1** (External-LLM Agent). § 4.43 — уточнение про PascalCase (KI-109 Фаза 3). § 4.48 — `HttpClient.Timeout` + CTS (KI-109 Фаза 2.5). |
| 2026-09-30 | 1.4.25 | § 4.49 — `AddMemoryCache` / `AddOptions` / `AddHttpClient` принимают `Action<T>`, не фабрику; `IConfiguration` для чтения настроек в `Startup`. **v1.8.2 Шаг 1.2** (ToolResultCache). |
| 2026-09-30 | **1.4.26** | § 4.50 — `params` + именованный аргумент = CS8323; флаг-параметр всегда позиционный первый. **v1.8.2 Шаг 1.3** (ToolResultCacheTests). |
| 2026-10-01 | **1.4.27** | § 4.51 — `ITool`, зависящий от `IToolRegistry` / `ISubAgentService`, → `Func<T>` (ADR-002); симптом «A circular dependency was detected» виден только при `dotnet run`. **Прецедент:** `CodeAgentWithReviewTool` (KI-126, Шаг 1D-fix2). |
| 2026-10-03 | **1.4.28** | § 7 — актуализация KI-выжимки после релиза v1.11.0 + регистрации KI-131/137/138/139 (Planned, v1.12.0/v1.12.x). KI-129 → Fixed (v1.11.0). **Docs-only.** |

---

## 9. Как использовать

**При работе над задачей:**

1. Открыть этот файл, вспомнить применимые правила.
2. Создать ветку (если не `main`).
3. Мелкими шагами, коммитить каждый шаг.
4. **В том же коммите** — обновить `CHANGELOG.md` (и `README.md`/`KNOWN_ISSUES.md` при необходимости).
5. Проверить `dotnet build` + `dotnet test` локально.
6. Push → проверить CI → merge.

**При code review:**

1. Проверить по пунктам раздела 1-2 (базовое + документация).
2. Технические замечания — по разделу 4.
3. Ссылаться на конкретный пункт: «нарушение 1.4 (нет `Async`-суффикса)».

**При онбординге нового разработчика:**

1. Прочитать этот файл **целиком**.
2. Прочитать `docs/KNOWN_ISSUES.md` (последние 5 записей).
3. Прочитать `CHANGELOG.md` (последний релиз).
4. Клонировать репо, настроить User Secrets, запустить `dotnet test`.
