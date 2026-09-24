# Правила разработки IIChatTools

**Версия:** 1.4.8
**Обновлено:** 2026-09-25
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

**Краткая выжимка Open/Deferred (после релиза v1.3.0):**

| KI | Приоритет | Статус | Суть | План |
|----|-----------|--------|------|------|
| KI-043 | 🟡 | Fixed (v1.3.x) | Утечка памяти в `RateLimitingMiddleware` | ✅ |
| KI-044 | 🟢 | Documented | `iichattools_audit_entries_total` / `lmstudio_requests_total` не инкрементируются | v1.5.0 |
| KI-047 | 🟡 | Deferred | Fallback PATCH/DELETE через POST (для старых сетей) | v1.4.2 |
| KI-049 | 🟢 | Fixed (v1.4.1) | `tokensIn`/`tokensOut` = null в SSE | ✅ (tiktoken) |
| KI-052 | 🟡 | Fixed (v1.4.0) | Специализированные суб-агенты | ✅ |
| KI-053 | 🟡 | Deferred | Multi-user approvals (роли approver, уведомления) | v1.4.2+ |
| KI-057 | 🟢 | Partially Fixed | Config-driven exclusion patterns моделей | v1.5.0 |
| KI-064 | 🟢 | Fixed (v1.3.x) | SSL-обрыв к `ru.wikipedia.org` | ✅ |
| KI-067 | 🟢 | Fixed (v1.4.1) | Per-user retention чатов | ✅ |
| KI-068 | 🟢 | Fixed (v1.3.x) | Поиск по содержимому сообщений | ✅ |
| KI-069 | 🟢 | Fixed (v1.3.x) | Inline-edit названия чата в sidebar | ✅ |
| KI-076 | 🟢 | Fixed (v1.4.1) | Статистика по агентам в админке | ✅ |
| KI-077 | 🟢 | Documented | `model: null` при PUT агента = «сброс» | — |
| KI-078 | 🟢 | Fixed (v1.4.1) | Поиск (A: внутричатовый, B: ⌘K) | ✅ |
| KI-079 | 🟢 | Fixed (v1.4.1) | Свернуть/развернуть sidebar | ✅ |
| KI-080 | 🟢 | Fixed (v1.4.1) | Поле ввода на всю ширину | ✅ |
| KI-081 | 🟢 | Fixed (v1.4.1) | Логотип IIChatTools | ✅ |
| KI-082 | 🟢 | Deferred | Модалка-редактор длинных user-сообщений | v1.5.0+ |
| KI-083 | 🟡 | Planned | RAG / embeddings (Qdrant) | v1.5.0 |
| KI-084 | 🟢 | Fixed (v1.4.1) | Расширенная статистика (tok/s, duration) | ✅ |
| KI-085 | 🟢 | Documented | SQLite `database is locked` (внешний клиент) | — |
| KI-086 | 🟢 | Deferred | Sources / citations под ответом | v1.6.0 |

**Всего в реестре:** 60+ KI. **Fixed/Resolved:** 55+ (v1.0.x–v1.4.1). **Deferred:** 5. **Documented:** 5.

> KI-068 (поиск по содержимому) исправлен **дважды**: первая версия использовала `LOWER() LIKE`, не работала с кириллицей на SQLite. Итоговое решение — фильтрация в памяти (см. § 4.26).

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