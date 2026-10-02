# PROMPT_V3.md — Стартовый промпт для нового чата

**Версия промпта:** v3.2
**Дата:** 2026-10-02
**Актуальный релиз проекта:** v1.10.1 (2026-10-01)
**Статус:** v1.10.1 выпущен. В работе — **KI-126 (Actor-Critic мультиагенты, Ф1: Шаги 1A-1G Done, осталось 1H-1J)** / KI-108 (per-user mail) / KI-091 (SqlServer миграции).

---

## § 0. Главное правило форматирования

**Не более одного уровня code fence'ов в твоём ответе.**

- Обычный код — тройной бэктик, **не вкладывать внутрь другого**.
- Пример внутри markdown-блока — **4-пробельный отступ**, не тройной бэктик.
- Команда (inline) — `dotnet build`.

**Почему:** 6+ инцидентов с развалившейся разметкой (v1.5.0 → v1.8.2). DeepSeek-парсер ломает вложенные fence'ы — внутренние слипаются с внешними, превращаясь в литерал «text».

**Большие MD-файлы (README, RULES, CHANGELOG, KNOWN_ISSUES, RELEASES, PROMPT_V3):**
только **точечный diff** («Найти X / Заменить на Y») или **отдельная секция**. Никогда — целиком в одном ответе.

Если риск поломки критичен — **порциями**: сначала 1-3 правки, после подтверждения — следующие.

---

## § 1. Ссылка на репозиторий

- **GitHub:** https://github.com/iilmchat/IIChatTools
- **Ветка по умолчанию:** `main`
- **Текущий релиз:** v1.10.1 (2026-10-01)
- **В работе:** KI-126 (Actor-Critic мультиагенты, Ф1 Шаги 1A-1G Done — DESIGN: `docs/development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md`) / KI-108 (per-user mail) / KI-091 (SqlServer миграции).

---

## § 2. Правила оформления (ОБЯЗАТЕЛЬНО)

**`docs/development/RULES.md` — v1.4.26 (2026-10-01).**

Ключевые разделы:

- **§ 1** — базовые правила (naming, XML-doc, локализация, DI).
- **§ 2** — документация (CHANGELOG, README, KNOWN_ISSUES — в том же коммите).
- **§ 3** — workflow (маленькие шаги, **`git add -A`**, build 0/0).
- **§ 4** — технические правила C# / .NET 10 (**50 правил**, включая свежие 4.49–4.50).
- **§ 5** — безопасность (User Secrets, PathHelper, ArgumentList).
- **§ 6** — git (commit message, `--force-with-lease`).
- **§ 7** — актуальная KI-выжимка.
- **§ 8** — история изменений правил.

**Самое важное для работы:**

- **RULES § 3.3** — **`git add -A`** вместо selective add. Инцидент 0bd5eb6: selective `git add` → 4 файла не попали в коммит, CI упал.
- **RULES § 4.30** — cref в XML-doc с перегрузками → используй `<c>...</c>`, не `<see cref="..."/>`.
- **RULES § 4.44** — новый top-level `ITool` → обязательно в `allowedNames` Chat (RULES-применимо **только** к top-level, **не** к наследникам `AgentToolBase`).
- **RULES § 4.45** — `Path.GetFileName` на Linux: нормализуй `\` → `/` перед вызовом.
- **RULES § 4.46** — default interface method (C# 8+) не виден через конкретный тип — используй интерфейсную переменную.
- **RULES § 4.47** — MailKit `IMessageSummary.Attachments` — `IEnumerable<BodyPartBasic>`, `.Count` — extension (LINQ). Используй `.Any()`.
- **RULES § 4.48** — `HttpClient.Timeout` нельзя менять после первого `SendAsync` — используй `CancellationTokenSource.CancelAfter`.
- **RULES § 4.49** — `AddMemoryCache` / `AddOptions` / `AddHttpClient` принимают `Action<T>`, **не** `Func<IServiceProvider, T>`. Для чтения конфига — `Configuration.GetValue<T>` в `Startup`.
- **RULES § 4.50** — `params` + именованный аргумент = **CS8323**. Флаг-параметр всегда позиционный первый.

---

## § 3. Текущее состояние (v1.10.1)

### Стек

- **.NET 10 LTS** (SDK 10.0.401).
- **ASP.NET Core** (Razor + JWT + Cookie).
- **EF Core 10** (SqlServer / Sqlite / InMemory).
- **LM Studio** (OpenAI-совместимый API + `/v1/embeddings`).
- **MailKit 4.8.0** + MimeKit — IMAP/SMTP.
- **PdfPig 0.1.9** + **DocumentFormat.OpenXml 3.1.0** — PDF/DOCX в RAG.
- **Microsoft.Data.Sqlite 10.0.12** + **Microsoft.Data.SqlClient 6.1.6** — SqlAgent.
- **PuppeteerSharp 7.1**, **Prometheus-net**, **Microsoft.ML.Tokenizers** (tiktoken).
- **IMemoryCache** — Tool result cache (v1.8.2).

### Архитектура — 4 слоя (API → Services → Data + Tests)

- **IIChatTools.API** — Controllers + Views + ES-модули + `Startup.cs` + `Program.cs`.
- **IIChatTools.Services** — бизнес-логика:
  - Core: `ToolRegistry`, `ChatService`, `ChatStreamService`, `LmStudioClient`, `ChatApprovalCoordinator`, `ChatRetentionService`.
  - RAG: `EmbeddingService`, `InMemoryVectorStore`, `DocumentIngestionService`, `RetrievalService`.
  - SqlAgent: `SqlAgentService`, `SqlQueryValidator`, `SqlConnectionProvider`, `AdminSqlAgentService`.
  - Mail: `MailKitClient`, `GlobalMailAccountProvider`, `MailAttachmentService`, `InMemoryMailRateLimiter`.
  - ExternalLlm: `ExternalLlmClient`, `ExternalProviderRegistry`, `ExternalLlmCircuitBreaker`, `ExternalLlmBudgetTracker`.
  - Cache: `ToolResultCache`, `CanonicalJsonHelper`.
- **IIChatTools.Data** — EF Entities + миграции (SqlServer).
- **IIChatTools.Tests** — xUnit (**592/592**, 5 Skip).

### Метрики

- **60 инструментов** в `ToolRegistry`:
  - 40 raw (включая `consult_secondary_agent`).
  - +8 специализированных агентов (`file_system`, `code`, `web`, `git`, `github`, `planner`, `mail`, `external_llm`).
  - +3 RAG-tool.
  - +1 `database_agent`.
  - +7 mail-tools.
  - +3 external-llm-tools.
- **Chat видит 15 инструментов** (после KI-126 Шаг 1C+1D): 9 агентов + `consult_secondary_agent` + 3 RAG + `database_agent` + `code_agent_with_review`.
- **Тесты:** 629/629 (624 pass, 5 Skip — реальные внешние провайдеры).
- **KI:** 87 в реестре; Fixed ≈ 82 (включая v1.11.0 Unreleased); Deferred = 5; Documented = 12; In Progress = 1 (KI-126); Planned = 5 (KI-108, KI-111, KI-113, KI-128, KI-129); Partially Fixed = 1; Implemented = 3.
- **Релизы после v1.8.2:** v1.9.0 (Anthropic, KI-110a) · v1.10.0 (Gemini, KI-110b) · v1.10.1 (темы UI, KI-122 + KI-092). Детали — `CHANGELOG.md`.

---

## § 4. Что выпущено (v1.3.0 → v1.8.2)

**v1.8.2 (2026-10-01) — Tool result cache + prefix stability + KI-121:**

- **Слой 2 (кэш результатов инструментов):** whitelist из 6 инструментов
  (`wikipedia_search`, `web_search`, `fetch_web_content`, 3 RAG-tool).
  `IMemoryCache` + `ToolResultCache` (Singleton) + `CanonicalJsonHelper`
  (канонизация JSON + SHA-256). Ключ `tool:{name}:u{userId}:{sha256}`.
  Интеграция в `ToolRegistry.ExecuteAsync` (опциональный параметр). 2 метрики
  Prometheus (`tool_cache_hits_total` / `misses_total`).
- **Слой 1 (prefix stability):** RAG-контекст в `ChatStreamService.BuildMessagesAsync`
  теперь **отдельным** system-сообщением **ПОСЛЕ** основного `chat.SystemPrompt`
  (раньше склеивались). KV-cache LM Studio не инвалидируется → TTFT ↓ 30-60%.
- **KI-121 (Fixed):** `external_llm_agent` — правило 4 в SystemPrompt.
- Тесты: **496 → 529** (+33, 3 Skip).
- RULES: +§ 4.49, +§ 4.50.
- +2 KI (Deferred, v1.9.x): KI-122 (темы оформления), KI-123 (spinner загрузки чатов).

**v1.8.1 (2026-09-30) — External-LLM Agent (KI-109):**

- Агент `external_llm_agent` + 3 инструмента (`ask_external_llm` с `compare_with`,
  `list_external_providers`, `check_internet_connection`).
- 5 OpenAI-совместимых провайдеров (DeepSeek / OpenAI / Groq / Together AI / Ollama).
- Budget guardrails ($5/день, 500k токенов), circuit breaker (3 fail → 5 мин skip).
- Тесты: 424 → 496 (+72).

**v1.8.0 (2026-09-29) — Mail Agent (KI-107):**

- `mail_agent` + 7 mail-tools (IMAP/SMTP через MailKit 4.8.0).
- Rate limiting, privacy-first, вложения в `mail-attachments/{uid}/`.
- Тесты: 409 → 424.

**Post-release фиксы v1.8.x:**

- **KI-115** (Fixed) — `mail_agent`: Context Length 16384 в LM Studio.
- **KI-116** (Fixed) — `gemma-4-12b` не поддерживает OpenAI tool calling. Откат `code_agent`, `planner_agent`, `mail_agent` на `qwen/qwen3-4b-2507`.
- **KI-117** (Documented) — LM Studio: Context Length ≥ 16384 для агентов.
- **KI-118** (Documented) — Chat LLM не вызывает `code_agent` для простых задач.
- **KI-120** (Documented) — Chat LLM галлюцинирует число инструментов.

**v1.7.x (2026-09-29) — Database Agent (KI-097) + PDF/DOCX в RAG (KI-104):**

- `database_agent` (4 action: `list_databases`, `list_tables`, `describe_table`, `execute_query`).
- 5 уровней безопасности (read-only роль, валидатор SQL, whitelist, timeout, approval+audit).
- `PdfParser` (PdfPig) + `DocxParser` (OpenXml). RAG: 28 → 30 форматов.

**v1.6.x (2026-09-28) — Sources / citations:**

- Web-tools (`wikipedia_search`, `web_search`, `fetch_web_content`) возвращают citations.
- Sources через агентов (`SubAgentTaskResult.Sources`).
- 4-полевой ключ дедупа `(Type|DocumentPath|Url|ChunkIndex)`.

**v1.5.0 (2026-09-28) — RAG / Knowledge Base (KI-083):**

- 4 индекса (`project_docs`, `my_rag_docs`, `chat_history`, `workspace`).
- 3 RAG-tool + вложения в чат (📎).
- Admin KB UI, Profile Workspace UI.

**v1.4.x — Chat UX + Multi-Agent (KI-052):**

- 6 суб-агентов + `consult_secondary_agent`.
- Per-user retention, статистика агентов, tiktoken.
- Search, collapse sidebar, логотип.

**v1.3.x — Chat UI:**

- Sidebar, SSE-стриминг, tool calling, approvals.
- Approvals в чате, inline-edit, AI-title.

---

## § 5. Roadmap

### v1.11.0 (🚧 В работе) — Actor-Critic мультиагенты (KI-126, Фаза 1)

**DESIGN:** `docs/development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md` (Draft).

**Прогресс Ф1 (2026-10-02):**
- ✅ **1A** — Entities `AgentDebateSession` + `AgentDebateRound` + миграция.
- ✅ **1B** — `IAgentDebateSessionService` + state machine.
- ✅ **1C** — агент `code_reviewer_agent` (Critic).
- ✅ **1D** — top-level `ITool` `code_agent_with_review`.
- ✅ **1E** — SSE + persistence (сессии в БД) + `DefaultSystemPrompt` (fix KI-127).
- ✅ **1F** — эскалация на `ask_external_llm` при `Uncertain`.
- ✅ **1G** — UI: селектор вида, диалоговый / свёрнутый рендер, feedback между раундами.
- 🟡 **Осталось:** 1H (финальная локализация), 1I (тесты), 1J (релиз v1.11.0).

**Известные ограничения (v1.11.0):** KI-129 (F5 не восстанавливает блок дебатов — Planned, v1.11.x).

Автономное взаимодействие суб-агентов с ролями «Исполнитель» (Actor)
и «Критик» (Critic). Actor-Critic для `code_agent` — новый агент
`code_reviewer_agent` + top-level tool `code_agent_with_review`.

- **Новые сущности:** `AgentDebateSession`, `AgentDebateRound`.
- **SSE-события:** `debate_started` / `debate_round` / `debate_escalated` /
  `debate_completed`.
- **Цикл:** до 3 раундов, консенсус — раньше.
- **Эскалация:** при `critic.verdict == Uncertain` → `ask_external_llm`.
- **UI:** селектор между «диалог» и «сворачиваемый».
- **План:** 3 фазы (~12-15 ч). Ф1 — Actor-Critic (~5-6 ч);
  Ф2 — Debate для `planner_agent` (v1.11.x); Ф3 — Orchestrator-Worker +
  Blackboard (v2.0).
- **Скоуп Ф1:** шаги 1A-1J (entities → state machine → reviewer →
  tool → SSE → escalation → UI → localization → tests → release).

### v1.10.1 (✅ Done, 2026-10-01) — Темы оформления UI (KI-122)

5 тем: Light / Dark / Dimmed / Solarized Light / High Contrast.
Переключатель в navbar. Рефакторинг CSS на Bootstrap-переменные.
Попутно закрыт **KI-092** (Bootstrap `aria-hidden` warning).

### v1.10.0 (✅ Done, 2026-10-01) — Google Gemini (KI-110b)

**DESIGN:** `docs/development/v1.9/DESIGN_GEMINI.md` (Implemented).
Третий формат API в `External-LLM Agent` — `ProviderFormat.Gemini`.
`CompleteGeminiAsync` — `POST /models/{model}:generateContent`, `x-goog-api-key`.
Builder + Parser + Client + Config. Тесты: **559 → 592** (+33).

### v1.9.0 (✅ Done, 2026-10-01) — Anthropic Claude (KI-110a)

**DESIGN:** `docs/development/v1.9/DESIGN_ANTHROPIC_GEMINI.md` (Implemented).
Клиент `ExternalLlmClient` — switch по `ProviderFormat` (OpenAI / Anthropic / Gemini).
Anthropic-ветка: `POST /messages`, `x-api-key`, `anthropic-version: 2023-06-01`.

### v1.11.x+ / v2.0 (запланировано)

- **KI-126 Фаза 2** — Debate для `planner_agent` (Pro / Contra / Judge).
- **KI-126 Фаза 3** — Orchestrator-Worker + Blackboard.
- **KI-108** — Per-user mail accounts (таблица `UserMailAccount` + `IDataProtector`).
- **KI-091** — SqlServer цепочка миграций (обязательно перед prod-SqlServer).
- **KI-111 / KI-113** — улучшения `mail_agent` (stateless + честность ответов).
- **KI-082** — модалка-редактор длинных сообщений.
- **Per-user External-LLM API keys** (аналог KI-108).
- Сохранение вложений при `read_email` + прикрепление к `send_email`.

### Done ранее (справка)

- v1.10.1 — Темы UI (KI-122, KI-092).
- v1.10.0 — Gemini (KI-110b).
- v1.9.0 — Anthropic (KI-110a).
- v1.8.2 — Tool result cache + KI-121.
- v1.8.1 — External-LLM Agent (KI-109).
- v1.8.0 — Mail Agent (KI-107).
- v1.7.0 — Database Agent (KI-097).
- v1.5.0 — RAG / Knowledge Base (KI-083).
- v1.4.0 — Multi-Agent (KI-052).
- v1.3.x — Chat UI.

### v1.10+ / инфраструктура

- **KI-091** — SqlServer цепочка миграций (обязательно перед prod-SqlServer).
- **KI-070** — миграции Sqlite.
- **Qdrant** — замена `InMemoryVectorStore` (если перерастём 10k чанков).
- **KI-057** — config-driven exclusion patterns моделей LM Studio.

### Deferred (7)

- **KI-047** — Fallback PATCH/DELETE через POST.
- **KI-053** — Multi-user approvals (роли approver).
- **KI-082** — Модалка-редактор длинных user-сообщений.
- **KI-096** — GitHub Wiki.
- **KI-099** — Внешние БД для Database Agent.
- **KI-122** — Темы оформления UI.
- **KI-123** — Индикатор загрузки списка чатов.

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
11. **`CHANGELOG.md` — в КАЖДОМ коммите с кодом** (RULES § 2.1 + § 4.37).
    Записи — в `[Unreleased]` секцию: `### Added` / `### Changed` / `### Fixed`
    / `### Security`. Формат — `<scope> (<KI-XXX>): суть`. Секцию `[X.Y.Z]`
    (с датой) создаём **только при релизе** (Фаза 5).
    Проверять перед `git commit`: «CHANGELOG обновлён?».
12. **Обновление `AppVersion.Current`** — только при релизе (правило 2.7).
13. **Большие MD-файлы** — только точечный diff или отдельная секция (RULES § 2.11).

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

Когда риск поломки критичен (большой MD, вложенные fence'ы, 5+ уровней структуры) — отдавай порциями: сначала правки 1-3, потом (после подтверждения) — 4-6.

### Обязательные секции в конце ответа

После всех правок — строго эти разделы, в этом порядке:

- 🔨 **Build + test** — команды + ожидание (0 warnings, 0 errors; N/N тестов).
- 🚀 **Commit** — here-string commit message + `git add -A` + `push`.
- 🧪 **Smoke** — что проверить после коммита (сценарии / DevTools / SQL).
- 📊 **Сводка** — статус + что жду (логи / скрины / `git log`).
  **Явно указывать:** «CHANGELOG.md обновлён ✅» / «CHANGELOG.md не требуется
  (чистая документация)».
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
- Есть копии на других машинах — обязательно уточнять путь при переключении.
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
    git status --short

    @'
    <type>(<scope>): <subject>

    пункт 1
    пункт 2

    Build 0/0. Tests N/N.
    '@ | Out-File -FilePath .commit-msg.txt -Encoding utf8NoBOM
    git commit -F .commit-msg.txt
    Remove-Item .commit-msg.txt

    git push origin main

**ВАЖНО (RULES § 3.3):** всегда `git add -A`, не selective `git add <file>`. Инцидент 0bd5eb6: selective add → 4 файла не в коммите, CI упал.

### Что сделать сейчас (первое действие в новом чате)

1. Прочитай `RULES.md` целиком (v1.4.26) — 9 разделов.
2. Прочитай `docs/development/v1.9/DESIGN_ANTHROPIC_GEMINI.md` — план v1.9.0.
3. Спроси, что делаем: v1.9.0 (Anthropic) / v1.8.x hotfix / новая задача / KI.
4. **Не начинай код,** пока не поймёшь задачу.
5. Формат — полные файлы, путь в начале блока.
6. Большие MD — только diff (RULES § 2.11).

---

## § 7. Известные подводные камни

- **`Path.GetFileName` кросс-платформенный** (RULES § 4.45): на Linux распознаёт только `/`. Нормализуй `\` → `/`.
- **Кэш браузера** после правок `chat.js` / `chat.css`: `Ctrl+Shift+R` + в DevTools Network — «Disable cache».
- **RAG-tools в `allowedNames` Chat** (RULES § 4.44): при добавлении нового top-level `ITool` — обязательно в `allowedNames` в `ChatStreamService.StreamAsync`. **НЕ применяется** к наследникам `AgentToolBase`.
- **Sources: 4-полевой ключ дедупликации** (v1.6.1): `(Type|DocumentPath|Url|ChunkIndex)`.
- **Sources через агентов** (v1.6.1): `SubAgentTaskResult.Sources`. `SubAgentService` аккумулирует.
- **`wikipedia_search` intermittent timeout** (KI-094): SSL через прокси. Fallback `web_search`.
- **`.resx` ключи case-insensitive** — коллизия → MSB3568. Новые — camelCase.
- **`git commit -m "..."` в PowerShell** — экранирование ломается. Here-string + `-F .commit-msg.txt`.
- **Локализация JS** — только через `data-*`-атрибуты (RULES § 4.17).
- **SqlServer vs Sqlite** — миграции только для SqlServer. Для Sqlite — `EnsureCreatedAsync` (RULES § 4.25).
- **`yield return` + scope переменных** — объявлять до `try-catch`, иначе CS0103 (RULES § 4.33).
- **`ExecuteDeleteAsync` не поддерживается InMemory** (RULES § 4.29).
- **`cref` в XML-doc с перегрузками** — CS0419 (RULES § 4.30).
- **Перед `dotnet build` — останови приложение** (RULES § 3.14).
- **После изменения `chat.js` / `site.css`** — `Ctrl+F5`.
- **`ChatStreamService.StreamAsync` — `yield return` запрещён в try-catch** (CS1631).
- **Sqlite + открытый DB Browser** — `database is locked` (KI-085). Read Only.
- **Расширение интерфейса** — grep по ВСЕМ fake-заглушкам (RULES § 4.34).
- **InMemory + AddDbContext** — явный `InMemoryDatabaseRoot` + имя БД до лямбды (RULES § 4.42).
- **`JToken.GetValue(name, comparison)`** не существует — перебирать `obj.Properties()` (RULES § 4.43).
- **Перед `dotnet ef migrations add`** — проверить `Database:Provider` (RULES § 3.15).
- **`IMessageSummary.Attachments`** — `IEnumerable`, `.Count` — extension (RULES § 4.47).
- **`ConcurrentDictionary.TryRemove(key, out _)`** — CS1503 в C# 13. Явная переменная.
- **Тесты `Reason`/`Message`** — проверяй **фактический** текст (обычно русский).
- **`AddMemoryCache` / `AddOptions` / `AddHttpClient`** — принимают `Action<T>`, не фабрику (RULES § 4.49).
- **`params` + именованный аргумент** — CS8323 (RULES § 4.50).

### PDF / DOCX (KI-104, v1.7.1)

- **`.doc` (старый Word) — не поддерживается.** Только `.docx`.
- **OCR сканов PDF — не поддерживается.** PdfPig читает только текстовый слой.
- **Шифрованные PDF** — `PdfDocument.Open` бросает исключение.
- **`accept` для `<input type="file">` — хардкод в `Views/Chat/Index.cshtml`.** При добавлении парсера — расширить.

### Mail Agent (KI-107, v1.8.0)

- **Yandex: App Password ≠ IMAP.** Это две разные настройки. `Login invalid credentials or IMAP is disabled` — в 90% случаев IMAP не включён в веб-интерфейсе.
- **App Password — 16 символов без пробелов.** Пробелы убрать.
- **Логин — полный email** (`user@yandex.ru`), не просто `user`.
- **`mail_agent` — НЕ требует правок `ChatStreamService`.** Наследник `AgentToolBase`.
- **Вложения при `read_email` НЕ сохраняются** в workspace (v1.8.0) — метаданные отдаются, файлы нет.
- **Прикрепление вложений к `send_email` НЕ реализовано** в v1.8.0.

### External-LLM Agent (KI-109, v1.8.1)

- **Anthropic / OpenAI — требуют VPN из РФ.** DeepSeek — работает без VPN.
- **Groq — free tier, но через VPN.**
- **`include_context: false` по умолчанию** — во внешнюю модель уходит только prompt.
- **Circuit breaker: 3 fail → 5 мин skip** (per-provider).
- **`DailyBudgetUsd = $5/день`** — per-user.

### Tool result cache (KI-121-followup, v1.8.2)

- **`ask_external_llm` НЕ кэшируется** — приватность + стоимость.
- **Ключ включает `userId`** — per-user изоляция (`search_chat_history` / `search_workspace`).
- **`InvalidateAll("search_knowledge_base")`** — после reindex RAG.
- **`SizeLimit = 10000`** — LRU-вытеснение.

---

## § 8. Известные факты про LM Studio

- Модель `qwen/qwen3-4b-2507` — плохо следует сложным инструкциям, иногда галлюцинирует.
- **Context Length ≥ 16384** для всех агентов (KI-117). При 8192 — обрезка ответа.
- `GET /v1/models` возвращает embedding-модели — фильтруются в `/api/models`.
- SSE-режим не отдаёт usage — токены через tiktoken (KI-049a).
- `wikipedia_search` — intermittent SSL-обрывы (KI-064 Fixed, KI-094 Documented).
- Embedding-модель: `text-embedding-nomic-embed-text-v1.5`, 768 dim, `POST /v1/embeddings`.
- **`gemma-4-12b-coder-fable5-composer2.5-v1` НЕ поддерживает tool calling** (KI-116).
- **Model по умолчанию:** `qwen/qwen3-4b-2507` для всех агентов.

---

## § 9. Начни с вопроса

Прочитай правила и это сообщение. Затем задай мне вопросы:

1. Что делаем сегодня — **KI-126 Фаза 1 (Actor-Critic мультиагенты)**
   / KI-108 (per-user mail) / KI-091 (SqlServer миграции) / новая задача?
2. Есть ли специфичные требования?
3. Нужны ли файлы, которых у тебя нет?
4. Какой путь к проекту — `C:\Projects\AI\IIChatTools` или другой?

**Контекст KI-126 Фаза 1 (если её делаем):**
- DESIGN — `docs/development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md`.
- Согласовано: Actor-Critic для `code_agent` (не Debate).
- Цикл: max 3 раунда + консенсус; настройка через `appsettings.json`.
- Эскалация: `critic.verdict == Uncertain` → `ask_external_llm` (DeepSeek).
- UI: селектор «диалог / сворачиваемый».
- План Ф1: шаги 1A-1J (~5-6 ч).

**Готов? Приступаем.**