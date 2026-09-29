# PROMPT_V2.md — Стартовый промпт для нового чата

**Версия промпта:** v2.8
**Дата:** 2026-09-29
**Актуальный релиз проекта:** v1.8.0 (2026-09-29)
**Статус:** Mail Agent реализован; в очереди — External-LLM Agent + переработка docs.

---

## § 0. Главное правило форматирования

**Не более одного уровня code fence'ов в твоём ответе.**

- Обычный код — тройной бэктик, **не вкладывать внутрь другого**.
- Пример внутри markdown-блока — **4-пробельный отступ**, не тройной бэктик.
- Команда (inline) — `dotnet build`.

**Почему:** 5+ инцидентов с развалившейся разметкой (v1.5.0 → v1.7.x). DeepSeek-парсер ломает вложенные fence'ы — внутренние слипаются с внешними, превращаясь в литерал «text».

**Большие MD-файлы (README, RULES, CHANGELOG, KNOWN_ISSUES, RELEASES, PROMPT_V2):**
только **точечный diff** («Найти X / Заменить на Y») или **отдельная секция**. Никогда — целиком в одном ответе.

Если риск поломки критичен — **порциями**: сначала 1-3 правки, после подтверждения — следующие.

---

## § 1. Ссылка на репозиторий

- **GitHub:** https://github.com/iilmchat/IIChatTools
- **Ветка по умолчанию:** `main`
- **Текущий релиз:** v1.8.0 (2026-09-29)
- **В работе:** External-LLM Agent (DESIGN готов, реализация в очереди); переработка docs (PROMPT_V2 layout — DONE).

---

## § 2. Правила оформления (ОБЯЗАТЕЛЬНО)

**`docs/development/RULES.md` — v1.4.21 (2026-09-29).**

Ключевые разделы:

- **§ 1** — базовые правила (naming, XML-doc, локализация, DI).
- **§ 2** — документация (CHANGELOG, README, KNOWN_ISSUES — в том же коммите).
- **§ 3** — workflow (маленькие шаги, `git add -A`, build 0/0).
- **§ 4** — технические правила C# / .NET 10 (**47 правил**, включая свежие 4.44–4.47).
- **§ 5** — безопасность (User Secrets, PathHelper, ArgumentList).
- **§ 6** — git (commit message, `--force-with-lease`).
- **§ 7** — актуальная KI-выжимка (после v1.8.0).
- **§ 8** — история изменений правил.

**Самое важное для работы:**

- **RULES § 4.30** — cref в XML-doc с перегрузками → используй `<c>...</c>`, не `<see cref="..."/>`.
- **RULES § 4.44** — новый top-level `ITool` → обязательно в `allowedNames` Chat (RULES-применимо **только** к top-level, **не** к наследникам `AgentToolBase`).
- **RULES § 4.45** — `Path.GetFileName` на Linux: нормализуй `\` → `/` перед вызовом.
- **RULES § 4.46** — default interface method (C# 8+) не виден через конкретный тип — используй интерфейсную переменную.
- **RULES § 4.47** — MailKit `IMessageSummary.Attachments` — `IEnumerable<BodyPartBasic>`, `.Count` — extension (LINQ). Используй `.Any()`.

---

## § 3. Текущее состояние (v1.8.0)

### Стек

- **.NET 10 LTS** (SDK 10.0.401).
- **ASP.NET Core** (Razor + JWT + Cookie).
- **EF Core 10** (SqlServer / Sqlite / InMemory).
- **LM Studio** (OpenAI-совместимый API + `/v1/embeddings`).
- **MailKit 4.8.0** + MimeKit (транзитивно) — IMAP/SMTP.
- **PdfPig 0.1.9** + **DocumentFormat.OpenXml 3.1.0** — PDF/DOCX в RAG.
- **Microsoft.Data.Sqlite 10.0.12** + **Microsoft.Data.SqlClient 6.0.2** — SqlAgent.
- **PuppeteerSharp 7.1**, **Prometheus-net**, **Microsoft.ML.Tokenizers** (tiktoken).

### Архитектура — 4 слоя (API → Services → Data + Tests)

- **IIChatTools.API** — Controllers + Views + ES-модули + `Startup.cs` + `Program.cs`.
- **IIChatTools.Services** — бизнес-логика:
  - Core: `ToolRegistry`, `ChatService`, `ChatStreamService`, `LmStudioClient`, `ChatApprovalCoordinator`, `ChatRetentionService`.
  - RAG: `EmbeddingService`, `InMemoryVectorStore`, `DocumentIngestionService`, `RetrievalService`.
  - SqlAgent: `SqlAgentService`, `SqlQueryValidator`, `SqlConnectionProvider`, `AdminSqlAgentService`.
  - Mail: `MailKitClient`, `GlobalMailAccountProvider`, `MailAttachmentService`, `InMemoryMailRateLimiter`.
- **IIChatTools.Data** — EF Entities + миграции (SqlServer).
- **IIChatTools.Tests** — xUnit (**424/424**).

### Метрики

- **58 инструментов** в `ToolRegistry`:
  - 40 raw (включая `consult_secondary_agent`).
  - +6 специализированных агентов.
  - +1 `mail_agent`.
  - +3 RAG-tool.
  - +1 `database_agent`.
  - +7 mail-tools.
- **Chat видит 12 инструментов**: 7 агентов + `consult_secondary_agent` + 3 RAG + `database_agent` + `mail_agent`.
- **KI:** ~77 в реестре (на 2026-09-29); Fixed/Resolved = ~65; In Progress = 0; Deferred = 4; Documented = 12; Planned = 5; Partially Fixed = 1; Implemented = 3. Точные числа — в `docs/KNOWN_ISSUES.md` → «Сводка по статусам».

---

## § 4. Что выпущено (v1.3.0 → v1.8.0)

**v1.8.0 (2026-09-29) — Mail Agent (KI-107):**

- Почтовый агент `mail_agent` + 7 mail-tools (`send_email`, `list_emails`, `read_email`, `search_emails`, `delete_email`, `move_email`, `mark_as_read`).
- IMAP4/SMTP через MailKit 4.8.0.
- Глобальные creds (App Password в User Secrets).
- Rate limiting 20/час, 30 чтений/мин.
- Privacy-first (без PII в логах).
- Вложения в `mail-attachments/{uid}/` ≤ 10 MB.
- Chat видит **12 инструментов**.

**Post-release фиксы v1.8.x (после релиза):**

- **KI-115** (Fixed) — `mail_agent`: Context Length 16384 в LM Studio (было 8192, prompt не влезал).
- **KI-116** (Fixed) — `gemma-4-12b-coder-fable5-composer2.5-v1` **не поддерживает OpenAI tool calling** — `tool_calls: []` при правильном reasoning. Откат `code_agent`, `planner_agent`, `mail_agent` на `qwen/qwen3-4b-2507`.
- **KI-117** (Documented) — Требование к LM Studio: **Context Length ≥ 16384** для всех агентов.
- **KI-118** (Documented) — Chat LLM не вызывает `code_agent` для простых задач («2+2 через Python»).
- **KI-112** (Documented) — Docker-образ lightweight, без `git`/`gh`/`python3`/`node`.

**v1.7.1 (2026-09-29) — патч-релиз:**

- KI-104 — `PdfParser` (PdfPig) + `DocxParser` (OpenXml). RAG: 28 → 30 форматов.
- KI-103 — локализация `/status`.
- KI-105 — транзитивная уязвимость `System.IO.Packaging`.
- KI-106 — оригинальное имя в источниках RAG для attachments.

**v1.7.0 (2026-09-29) — Database Agent (KI-097):**

- `database_agent` (top-level `ITool`): 4 action (`list_databases`, `list_tables`, `describe_table`, `execute_query`).
- 5 уровней безопасности (read-only роль, валидатор SQL, whitelist, timeout, approval+audit).
- Admin UI `/admin → SQL Agent`.

**v1.6.1 (2026-09-28) — Web-tools sources:**

- `wikipedia_search`, `web_search`, `fetch_web_content` возвращают citations.
- Sources через агентов (`SubAgentTaskResult.Sources`).
- 4-полевой ключ дедупа `(Type|DocumentPath|Url|ChunkIndex)`.

**v1.6.0 (2026-09-28) — Sources / citations:**

- Блок «📚 Источники» под ответом ассистента.
- `ChatMessage.MetadataJson`, `ChatSourceDto`.
- Побочный fix: RAG-tools не попадали в `allowedNames` Chat с v1.5.0.

**v1.5.0 (2026-09-28) — RAG / Knowledge Base:**

- 4 индекса (`project_docs`, `my_rag_docs`, `chat_history`, `workspace`).
- 3 RAG-tool + вложения в чат (📎).
- Admin KB UI, Profile Workspace UI.

**v1.4.x — Chat UX polish:**

- Per-user retention (KI-067), статистика агентов (KI-076), tiktoken (KI-049).
- Search (Ctrl+F / Ctrl+K), collapse sidebar (Ctrl+B), DeepSeek-style input.
- Логотип (KI-081).

**v1.4.0 — Multi-Agent (KI-052):**

- 6 специализированных суб-агентов.
- `SubAgentRegistry`, `AgentToolBase`, `SubAgentDescriptor`.

**v1.3.x — Chat UI:**

- Sidebar, SSE-стриминг, tool calling, approvals, Markdown + code blocks.
- Approvals в чате (v1.3.0), inline-edit, AI-title.

---

## § 5. Roadmap

**v1.8.x (текущая ветка):**

- **KI-108** — Per-user mail accounts (свой ящик у каждого пользователя). Таблица `UserMailAccount` + шифрование через `IDataProtector`.
- Сохранение вложений при `read_email` (требует переделки `IMailClient`).
- Прикрепление вложений к `send_email` (привязка к `MimeMessage`).
- **PROMPT_V2 layout** — переработка разметки → **DONE** (v2.8, 2026-09-29).

**v1.8.x / v1.9+ — External-LLM Agent (KI-109):**

- Агент `external_llm_agent` + 3 инструмента (`ask_external_llm`, `list_external_providers`, `check_internet_connection`).
- OpenAI-совместимые провайдеры (DeepSeek, OpenAI, Groq, Together AI, Ollama).
- 4 сценария-оркестратора (Fallback / Специализация / Разные знания / Сравнение).
- Circuit breaker, дневной лимит ($5/день, 500k токенов), privacy-first.
- **DESIGN готов** — `docs/development/v1.8/DESIGN_EXTERNAL_LLM.md`.

**v1.9+ — Anthropic / Gemini (KI-110):**

- Свои форматы запросов (`/v1/messages`, `/v1beta/models`).
- Расширение `IExternalLlmClient` (ветвление по `Format`).

**Инфраструктура (v1.8.x):**

- **KI-107-fix** — сохранение вложений при `read_email` (переделка `IMailClient`).
- **KI-091** — SqlServer цепочка миграций (обязательно перед prod-SqlServer).
- **KI-070** — миграции Sqlite.
- **Qdrant** — замена `InMemoryVectorStore` (если перерастём 10k чанков).
- **KI-057** — config-driven exclusion patterns моделей LM Studio.

**Deferred:**

- KI-047 — Fallback PATCH/DELETE через POST.
- KI-053 — Multi-user approvals (роли approver).
- KI-082 — Модалка-редактор длинных user-сообщений.
- KI-096 — GitHub Wiki.
- KI-099 — Внешние БД для Database Agent.

---

## § 6. Формат работы

1. **Полные файлы** с XML-документацией на русском.
2. **Путь к файлу** в начале каждого блока кода.
3. **Правка существующего файла** — точечный diff (Найти / Заменить на).
4. **Новый файл** — выводить целиком.
5. **Несколько правок в одном файле** — нумеровать: Правка 4.1, Правка 4.2.
6. **Новые NuGet-пакеты** — с версиями и указанием проекта.
7. **Сводка в конце**: что сделано / что проверить.
8. **Новые проблемы** → `KI-XXX` в `docs/KNOWN_ISSUES.md`.
9. **Новые UI-строки** → оба `.resx` (RU + EN) — правило 1.14.
10. **Новый инструмент** → 1 класс + 1 строка регистрации в `Startup.cs`.
11. **Обновление `AppVersion.Current`** — только при релизе (правило 2.7).
12. **Большие MD-файлы** — только точечный diff или отдельная секция (RULES § 2.11).

### Формат вывода кода

Каждый файл — отдельным блоком с заголовком и путём. Пример структуры:

    📄 Файл N — название (новый | правка)
    Путь: <repo-root>/.../File.cs
    [открывающий fence с языком, например csharp]
        код
    [закрывающий fence]

При правке существующего файла — пошагово:

    Найти: (полный фрагмент, который заменяем)
    Заменить на: (новый фрагмент)

Несколько правок в одном файле — нумеровать: Правка 4.1, Правка 4.2.

Новый файл — выводить целиком (с XML-doc).

**Единый уровень fence'ов!** Если файл сам содержит тройной бэктик (например, README.md с примерами кода) — оборачивай его в 4 бэктика. НО внутри 4 бэктиков не должно быть ещё одного слоя 3 бэктиков, вложенных в 3 — если такое случается, отдавай файл как plain text без обёртки.

Когда риск поломки критичен (большой MD, вложенные fence'ы, 5+ уровней структуры) — отдавай порциями: сначала правки 1-3, потом (после подтверждения) — 4-6. Дешевле, чем переписывать сломанную разметку вручную.

### Обязательные секции в конце ответа

После всех правок — строго эти разделы, в этом порядке:

- 🔨 **Build + test** — команды + ожидание (0 warnings, 0 errors; N/N тестов).
- 🚀 **Commit** — here-string commit message + `git add -A` + `push`.
- 🧪 **Smoke** — что проверить после коммита (сценарии / DevTools / SQL).
- 📊 **Сводка** — статус + что жду (логи / скрины / `git log`).
- 🎯 **Что дальше** — предложение следующего шага (с оценкой).

### Если чего-то не хватает

**Не выдумывай.** Если нужен файл, которого нет в контексте:

- Скажи явно: «Нужен файл X».
- Дождись, пока пользователь его пришлёт.
- Не предлагай «примерно так».

Если непонятно требование — **задай вопрос до кода**.

### Перед началом работы — дождись «ДА»

Не начинай писать код, пока пользователь не подтвердил план / DESIGN / предыдущий шаг.
Исключение: прямое «делай» / «приступай».

### Рабочий путь

- **Основной (Windows):** `C:\Projects\AI\IIChatTools`.
- Есть копии на других машинах (например, `D:\Projects\IIChatTools`) — обязательно уточнять путь при переключении.
- В примерах команд используй `<repo-root>` как плейсхолдер.

### Стандартные команды

**Остановить приложение** (RULES § 3.14):

    Get-Process IIChatTools.API -ErrorAction SilentlyContinue | Stop-Process -Force

**Build + test:**

    cd <repo-root>
    dotnet build IIChatTools.sln
    dotnet test IIChatTools.sln --no-build

**Чистая сборка:**

    cd <repo-root>
    Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force
    dotnet restore IIChatTools.sln --configfile NuGet.Config.online --force --verbosity minimal
    dotnet build IIChatTools.sln --no-restore
    dotnet test IIChatTools.sln

**Запуск (dev):**

    cd <repo-root>/IIChatTools.API
    dotnet run

UI: `https://localhost:5001`; Метрики: `https://localhost:5001/metrics`.

**Commit** (here-string — избежать проблем с PowerShell-экранированием):

    git add -A
    git status

    @'
    <type>(<scope>): <subject>

    пункт 1
    пункт 2

    Build 0/0. Tests N/N.
    '@ | Out-File -FilePath .commit-msg.txt -Encoding utf8NoBOM
    git commit -F .commit-msg.txt
    Remove-Item .commit-msg.txt

    git push origin main

### Что сделать сейчас (первое действие в новом чате)

1. Прочитай `RULES.md` целиком (v1.4.21) — 9 разделов.
2. Спроси, что делаем: v1.8.x / v1.9.0 (External-LLM) / новая задача / фикс / KI.
3. **Не начинай код,** пока не поймёшь задачу.
4. Формат — полные файлы, путь в начале блока.
5. Большие MD — только diff (RULES § 2.11).

---

## § 7. Известные подводные камни

- **`Path.GetFileName` кросс-платформенный** (RULES § 4.45): на Linux распознаёт только `/`. Абсолютный Windows-путь `C:\...\RULES.md` на Linux-CI вернётся целиком. Нормализуй `\` → `/` перед вызовом.
- **Кэш браузера** после правок `chat.js` / `chat.css`: `Ctrl+Shift+R` + в DevTools Network — галка «Disable cache».
- **RAG-tools в `allowedNames` Chat** (RULES § 4.44): при добавлении нового top-level `ITool` в DI — обязательно добавить его имя в `allowedNames` в `ChatStreamService.StreamAsync`. **НЕ применяется** к наследникам `AgentToolBase` — они попадают через `SubAgentRegistry.GetEnabled()`.
- **Sources: 4-полевой ключ дедупликации** (v1.6.1): `(Type|DocumentPath|Url|ChunkIndex)`. Без `Url` web/wiki-источники схлопываются в один.
- **Sources через агентов** (v1.6.1): `SubAgentTaskResult.Sources` (`IReadOnlyList<ChatSourceDto>`). `SubAgentService` аккумулирует.
- **`wikipedia_search` intermittent timeout** (KI-094): SSL через прокси. Fallback `web_search` работает.
- **`.resx` ключи case-insensitive** — коллизия → MSB3568. Новые — camelCase.
- **`git commit -m "..."` в PowerShell** — экранирование ломается на кавычках. Используй here-string + `-F .commit-msg.txt`.
- **Локализация JS** — только через `data-*`-атрибуты (RULES § 4.17).
- **CancellationToken требует `using System.Threading;`** в контроллерах.
- **`[ApiController]` не подходит для View-контроллеров** — возвращает ProblemDetails 400 вместо формы.
- **SqlServer vs Sqlite** — миграции применяются только для SqlServer. Для Sqlite — `EnsureCreatedAsync` (RULES § 4.25).
- **`yield return` + scope переменных** — объявлять до `try-catch`, иначе CS0103 (RULES § 4.33).
- **`ExecuteDeleteAsync` не поддерживается InMemory** (RULES § 4.29) — fallback `ToList` + `RemoveRange`.
- **`Microsoft.ML.Tokenizers` — два пакета**: API + Data.Cl100kBase (RULES § 4.32).
- **`cref` в XML-doc с перегрузками** — CS0419 (RULES § 4.30).
- **Перед `dotnet build` — останови приложение**, иначе MSB3027 (RULES § 3.14).
- **После изменения `chat.js` / `site.css`** — `Ctrl+F5` (кэш браузера).
- **`ChatStreamService.StreamAsync` — `yield return` запрещён в try-catch** (CS1631).
- **Sqlite + открытый DB Browser** — `database is locked` (KI-085). Открывать в Read Only.
- **Расширение интерфейса** — grep по ВСЕМ fake-заглушкам (RULES § 4.34).
- **InMemory + AddDbContext** — явный `InMemoryDatabaseRoot` + имя БД до лямбды (RULES § 4.42).
- **`JToken.GetValue(name, comparison)`** не существует — перебирать `obj.Properties()` (RULES § 4.43).
- **Перед `dotnet ef migrations add`** — проверить `Database:Provider` в `appsettings.Development.json` (RULES § 3.15).
- **`IMessageSummary.Attachments`** — `IEnumerable`, `.Count` — extension (RULES § 4.47).
- **`ConcurrentDictionary.TryRemove(key, out _)`** — CS1503 в C# 13. Явная переменная.
- **Тесты `Reason`/`Message`** — проверяй **фактический** текст (обычно русский), не идентификаторы.

### PDF / DOCX (KI-104, v1.7.1)

- **`.doc` (старый формат Word) — не поддерживается.** OpenXml работает только с `.docx`.
- **OCR сканов PDF — не поддерживается.** PdfPig читает только текстовый слой.
- **Шифрованные PDF** — `PdfDocument.Open` бросает исключение.
- **Сложная вёрстка** (таблицы, multi-column) — текст склеивается.
- **`accept` для `<input type="file">` — хардкод в `Views/Chat/Index.cshtml`.** При добавлении нового парсера — не забыть добавить расширение в `accept`.

### Mail Agent (KI-107, v1.8.0)

- **Yandex: App Password ≠ включение IMAP.** Это две разные настройки. `Login invalid credentials or IMAP is disabled` — в 90% случаев IMAP не включён в веб-интерфейсе (`Настройки → Почтовые программы`).
- **App Password — 16 символов без пробелов.** Yandex показывает группами для читаемости, но пробелы надо убрать.
- **Логин — полный email** (`user@yandex.ru`), не просто `user`.
- **`mail_agent` — НЕ требует правок `ChatStreamService`.** Наследник `AgentToolBase` → попадает в `allowedNames` через `SubAgentRegistry.GetEnabled()`.
- **Вложения при `read_email` НЕ сохраняются** в workspace (v1.8.0) — метаданные отдаются, файлы нет.
- **Прикрепление вложений к `send_email` НЕ реализовано** в v1.8.0.

---

## § 8. Известные факты про LM Studio

- Модель `qwen/qwen3-4b-2507` — плохо следует сложным инструкциям, иногда галлюцинирует.
- `GET /v1/models` возвращает embedding-модели (`text-embedding-*`) — фильтруются в `/api/models`.
- SSE-режим не отдаёт usage — токены через tiktoken (KI-049a).
- `wikipedia_search` — intermittent SSL-обрывы (KI-064 Fixed, KI-094 Documented).
- Embedding-модель: `text-embedding-nomic-embed-text-v1.5`, 768 dim, через `POST /v1/embeddings`.
- Tool calling — поддерживается, но модель путается при >15 инструментах (отсюда Multi-Agent).

---

## § 9. Начни с вопроса

Прочитай правила и это сообщение. Затем задай мне вопросы:

1. Что делаем сегодня — v1.8.x (инфраструктура) / v1.9.0 (External-LLM) / новая задача / фикс / KI?
2. Есть ли специфичные требования?
3. Нужны ли файлы, которых у тебя нет?
4. Какой путь к проекту (уточнить — `C:\Projects\...` или `D:\Projects\...`)?

**Готов? Приступаем.**
