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
- **Documented** — Задокументировано багов

---

## [Unreleased]

### Added
- **KI-123 — индикатор загрузки списка чатов** (Deferred → Fixed):
  spinner (Bootstrap `.spinner-border`) + локализованный текст
  «Идёт загрузка списка чатов…» в sidebar `/chat`.
  Появляется на время `GET /api/chats` (включая поиск с `?search=`).
  Локализация через `data-label-loading` на `#chat-list` (RULES § 4.17),
  +2 ключа в `.resx` (RU + EN). `chat.js` — новый helper
  `renderChatListLoading(listEl)`, вызывается из `loadChats()` вместо
  hardcoded «Загрузка…».

### Fixed
- **KI-124 — Интеграционный тест Anthropic Claude** (v1.9.0-followup):
  добавлен `Anthropic_RealRequest_ReturnsResponse` в
  `ExternalLlmIntegrationTests.cs` — `[Fact(Skip=...)]`, env
  `EXTERNALLLM__ANTHROPIC__APIKEY`, требует VPN из РФ. Проверяет путь
  `CompleteAnthropicAsync` (`POST /v1/messages`, `x-api-key`,
  `anthropic-version: 2023-06-01`).
  `CreateRealClient` — +опциональный `ProviderFormat format = OpenAI`
  (обратно совместимо).
  **562 → 563 tests** (559 pass, **4** skip).

---

## [1.9.0] — 2026-10-01

**Anthropic Claude (KI-110a).** DESIGN согласован 2026-09-30
(`docs/development/v1.9/DESIGN_ANTHROPIC_GEMINI.md`). План: 5 фаз
(ProviderFormat → Builders → Client → Config → Релиз). Обратная
совместимость: 5 существующих OpenAI-совместимых провайдеров не меняются.

### Added
- **v1.9.0 Фаза 1 (KI-110a): `ProviderFormat` enum + `ExternalProviderOptions.Format`**:
  - `ProviderFormat` (`DTO/ExternalLlm/ProviderFormat.cs`) — enum:
    `OpenAI = 0` (default) / `Anthropic = 1` / `Gemini = 2` (v1.9.x, KI-110b).
    При вызове `Gemini` — `NotSupportedException` (заглушка).
  - `ExternalProviderOptions.Format` — новое свойство, default = `OpenAI`.
    5 существующих провайдеров (DeepSeek, OpenAI, Groq, Together AI, Ollama)
    не задают `Format` в `appsettings.json` — работают без изменений конфига.
  - Тесты: `ExternalProviderRegistryTests` +2 (Anthropic проходит валидацию;
    `Default = OpenAI` для backward compat). **529 → 531**.

- **v1.9.0 Фаза 2 (KI-110a): Anthropic builders**:
  - `AnthropicRequestBuilder` (`Implementation/ExternalLlm/Formats/`) — static helper,
    собирает `JObject` для `POST {BaseUrl}/messages`. Отличия от OpenAI:
    `max_tokens` обязателен, `system` — отдельным полем (не роль в `messages[]`),
    `temperature` clamp [0, 1] (не [0, 2]), `stream` не добавляется (v1.9.0 non-stream).
  - `AnthropicResponseParser` (`Implementation/ExternalLlm/Formats/`) — static helper,
    извлекает `content[]` (склейка блоков `type=="text"` через `\n`),
    `usage.input_tokens` / `usage.output_tokens`. Блоки `type=="tool_use"`
    игнорируются (v1.9.0 — без function calling). Не падает при отсутствии полей.
  - Тесты: `AnthropicRequestBuilderTests` (12) + `AnthropicResponseParserTests` (8).
    **531 → 551** (3 Skip внешних).

### Added
- **v1.9.0 Фаза 3 (KI-110a): рефакторинг `ExternalLlmClient`**:
  - `CompleteAsync` — switch по `ProviderFormat` → приватные методы:
    - `CompleteOpenAiAsync` — существующее поведение (DeepSeek, OpenAI, Groq,
      Together, Ollama), вынесено без изменений логики;
    - `CompleteAnthropicAsync` — новый путь (`POST /messages`, `x-api-key` +
      `anthropic-version: 2023-06-01`, вызов `AnthropicRequestBuilder` /
      `AnthropicResponseParser` из Фазы 2);
    - `Gemini` → `NotSupportedException` (v1.9.x, KI-110b). Не увеличивает
      fail-счётчик circuit breaker (не сетевая ошибка);
    - неизвестный `Format` → `InvalidOperationException` (fail-fast).
  - `SendWithRetryAsync` / `SendOnceAsync` — параметризованы
    `IReadOnlyDictionary<string, string> headers` вместо хардкоженного
    Bearer. Поддержка `Authorization` (типизировано в `Headers.Authorization`),
    `x-api-key`, `anthropic-version` (через `TryAddWithoutValidation`).
  - Общая обвязка (`circuit breaker` / `budget` / cost-calc / audit) —
    без изменений, применяется ко всем форматам единообразно.
  - Тесты: `ExternalLlmClientTests` +7 (x-api-key, anthropic-version,
    `/messages` endpoint, парсинг content[], multi-block, 400 без retry,
    Gemini NotSupported, unknown format). **551 → 558** (3 Skip).

### Added
- **v1.9.0 Фаза 4 (KI-110a): конфигурация Anthropic / Gemini + README**:
  - `appsettings.json` / `appsettings.Development.json` — **+2 провайдера**
    в `ExternalLlm:Providers`:
    - `anthropic` — `Format: "Anthropic"`, `claude-haiku-4-5`,
      BaseUrl `https://api.anthropic.com/v1`, тарифы $0.001 / $0.005
      за 1k токенов (input / output);
    - `gemini` — `Format: "Gemini"`, `gemini-2.0-flash`,
      BaseUrl `https://generativelanguage.googleapis.com/v1beta`.
      **Заглушка** — при вызове `NotSupportedException` (KI-110b, v1.9.x).
  - README («External-LLM Agent») — Anthropic в intro; инструкция получения
    ключа (`VPN обязателен`, `sk-ant-...`, `dotnet user-secrets`); отдельный
    блок про Gemini-заглушку; обновлён раздел «Ограничения» (Anthropic
    поддержан; Gemini — v1.9.x).
  - **Обратная совместимость:** 5 существующих провайдеров (DeepSeek, OpenAI,
    Groq, Together, Ollama) не задают `Format` — продолжают работать
    с дефолтом `ProviderFormat.OpenAI`.

### Changed
- **v1.9.0 Фаза 2 (KI-110a): `ExternalLlmRequest.System`** (nullable) — нужно
  для правила «`system` добавляется, если не пуст» (DESIGN § 3.4).
  В OpenAI-ветке поле игнорируется (system сейчас не поддерживается).
  Backward-compatible: все существующие инициализаторы работают.

---

## [1.8.2] — 2026-10-01

**Tool result cache + prefix stability + KI-121.**
Whitelist-кэш для «дорогих» инструментов (Wikipedia, web_search, RAG-поиск)
на базе `IMemoryCache`: повторный вызов с теми же аргументами и от того же
пользователя → мгновенный ответ без внешнего запроса. RAG-контекст в
`ChatStreamService` теперь отдельным system-сообщением **после** основного —
KV-cache LM Studio не инвалидируется при добавлении attachments (TTFT ↓
на 30-60%). KI-121: LLM `external_llm_agent` больше не галлюцинирует
«использованные провайдеры», когда все вернули Fail.

### Added
- **Tool result cache (Слой 2 многоуровневого кэширования, v1.8.2)**:
  - `ToolResultCacheOptions` + `ToolCacheEntryOptions` — настройки whitelist
    (per-tool `Enabled` + `TtlSeconds`).
  - `IToolResultCache` + `ToolResultCache` (Singleton, `IDisposable`) —
    обёртка над `IMemoryCache`.
  - `CanonicalJsonHelper` — канонизация JSON (рекурсивная сортировка ключей
    `JObject` по `Ordinal`, порядок `JArray` сохраняется) + SHA-256 + формат
    ключа `tool:{name}:u{userId}:{sha256}`.
  - Интеграция в `ToolRegistry.ExecuteAsync` — проверка кэша до вызова
    инструмента, сохранение после (только при `Success == true`).
    Опциональный параметр `IToolResultCache` в конструкторе (default `null` —
    обратная совместимость с существующими тестами).
  - **Whitelist (6 инструментов):** `wikipedia_search` (TTL 1 ч),
    `web_search` (15 мин), `fetch_web_content` (1 ч),
    `search_knowledge_base` (1 ч), `search_chat_history` (5 мин),
    `search_workspace` (5 мин).
  - **Не кэшируется:** `ask_external_llm` (приватность + стоимость),
    `send_email`, `save_file`, `run_python`, `execute_query`,
    `list_directory`, `read_file` — побочные эффекты / дешевизна.
  - **Ключ включает `userId`** — per-user изоляция (для `search_chat_history` /
    `search_workspace` / `search_knowledge_base`, чтобы не было утечки между
    пользователями).
  - **Инвалидация по префиксу** через `CancellationChangeToken`:
    `IMemoryCache` не поддерживает prefix-eviction, поэтому per-tool `CTS`,
    `InvalidateAll(tool)` отменяет все записи инструмента. Пригодится после
    reindex RAG.
  - **Настройка** — `appsettings.json` → секция `ToolCache`
    (`Enabled`, `SizeLimit`, `Tools[*].TtlSeconds`). `SizeLimit` clamp
    `[100, 1_000_000]`.
  - **Метрики Prometheus:** `iichattools_tool_cache_hits_total` /
    `iichattools_tool_cache_misses_total` (label `tool_name`).
  - **Тесты:** +33 (496 → 529) — `CanonicalJsonHelperTests` (10),
    `ToolResultCacheTests` (14+ после фикса CS8323),
    `ToolRegistryCacheTests` (6).

### Changed
- **Chat — prefix stability (Слой 1, v1.8.2)**: RAG-контекст из `my_rag_docs`
  теперь добавляется **отдельным** system-сообщением **ПОСЛЕ** основного
  `chat.SystemPrompt` (раньше склеивались в одно через `\n\n`). Эффект:
  стабильный префикс не меняется при добавлении / удалении attachments →
  KV-cache LM Studio не инвалидируется → TTFT ↓ на 30-60% при наличии RAG.
  Без attachments поведение не меняется.

### Fixed
- **KI-121 — `external_llm_agent`: галлюцинация «использованные провайдеры»**:
  уточнено правило 4 в `SystemPrompt` (appsettings + .Development) — указывать
  ТОЛЬКО успешно ответивших провайдеров; при полном отказе явно писать
  «Ни один провайдер не доступен» и перечислять причины. Раньше LLM трактовала
  Fail как «была попытка = использован».

### Documented
- **KI-122** — смена темы оформления UI (Deferred, v1.9.x). Флаг: текущий
  Bootstrap 5.2 → нужен 5.3+ (`data-bs-theme`).
- **KI-123** — индикатор загрузки списка чатов («Идёт загрузка» + spinner).
  Deferred, v1.9.x.

### Docs / rules
- **RULES § 4.49** — `AddMemoryCache` / `AddOptions` / `AddHttpClient`
  принимают `Action<T>`, не `Func<IServiceProvider, T>`; читать конфиг
  через `Configuration.GetValue<T>` в `Startup`.
- **RULES § 4.50** — `params` + именованный аргумент = CS8323; флаг-параметр
  всегда позиционный первый.

---

## [1.8.1] — 2026-09-30

**External-LLM Agent (KI-109).** Агент `external_llm_agent` + 3 инструмента внутри:
`ask_external_llm` (с опциональным `compare_with`), `list_external_providers`,
`check_internet_connection`. 5 OpenAI-совместимых провайдеров
(DeepSeek / OpenAI / Groq / Together AI / Ollama). Оркестратор с 4 сценариями
(Fallback / Специализация / Разные знания / Сравнение). Budget guardrails
($5/день, 500k токенов), circuit breaker (3 fail → 5 мин skip), privacy-first
(без PII в логах). Chat видит **13 инструментов** (было 12).
Тесты: **424 → 496** (+72, из них 3 Skip — реальные провайдеры).

### Added
- **External-LLM Agent — Фаза 5: integration-тесты (v1.8.1, KI-109)**:
  - `ExternalLlmIntegrationTests` (`Tests/IntegrationTests/ExternalLlm/`) — 4 теста:
    - `CompleteAsync_UnknownProvider_FailsBeforeHttp` — **без Skip** (fail-fast без сети).
    - `DeepSeek_RealRequest_ReturnsResponse` — `[Fact(Skip=...)]`.
    - `OpenAI_RealRequest_ReturnsResponse` — `[Fact(Skip=...)]`.
    - `Ollama_RealRequest_ReturnsResponse` — `[Fact(Skip=...)]`.
  - Для запуска — env-переменная `EXTERNALLLM__{PROVIDER}__APIKEY` + убрать Skip вручную.
  - **DoD:** `dotnet build` 0/0, `dotnet test` 495 → **496/496** (1 fail-fast + 3 Skip).

### Documented
- **KI-120 (new)** — Chat LLM галлюцинирует количество инструментов: перечисляет 13
  корректно, потом пишет «правильно: 10». Ограничение qwen3-4b (аналогично KI-118).
  **Зашитого числа в `ChatStreamService` НЕТ** (проверено). Workaround для smoke:
  спрашивать «перечисли» вместо «сколько».

### Added
- **External-LLM Agent — Фаза 4: агент `external_llm_agent` (v1.8.1, KI-109)**:
  - `ExternalLlmAgentTool` (`Implementation/Tools/SubAgent/`) — наследник
    `AgentToolBase`, `Name = AgentName = "external_llm_agent"`.
    `RequiresApprovalByDefault` резолвится из дескриптора
    (`SubAgents:external_llm_agent:RequiresApproval = false`).
  - `Startup.cs` — 1 строка в `RegisterSpecializedAgentTools`:
    `services.AddScoped<ITool, ExternalLlmAgentTool>()`.
  - `appsettings.json` + `.Development.json` — секция `SubAgents:external_llm_agent`
    (`Enabled: true`, `Model: qwen/qwen3-4b-2507`, `MaxSteps: 5`,
    `RequiresApproval: false`, `AllowedTools: [ask_external_llm,
    list_external_providers, check_internet_connection]`, SystemPrompt с 4 сценариями).
  - **ChatStreamService — НЕ требует правок:** `external_llm_agent` — наследник
    `AgentToolBase`, попадает в `allowedNames` через `SubAgentRegistry.GetEnabled()`
    (RULES § 4.44 здесь **не** применим — только для top-level `ITool` в Chat).
  - **Chat видит 13 инструментов** (было 12): 8 агентов + consult + 3 RAG +
    `database_agent` + `mail_agent` + `external_llm_agent`.
  - **Тесты:** без unit (всё через `SubAgentRegistry` — конфигурация).
  - **DoD:** `dotnet build` 0/0. `dotnet test` 495/495 (без изменений).
- **External-LLM Agent — Фаза 3: 3 tools + регистрация (v1.8.1, KI-109)**:
  - `AskExternalLlmTool` (`Implementation/Tools/ExternalLlm/`) — `ask_external_llm`.
    Одиночный режим (`provider` + `prompt`) и сравнение (`compare_with` — 2 параллельных
    запроса через `Task.WhenAll`, возврат `{ primary, secondary, totalCostUsd }`).
    Параметры: `prompt` (required), `provider?`, `compare_with?`, `include_context?`
    (Фаза 3: принимается, но не инжектится — реальный контекст в Фазе 4), `max_tokens?`,
    `temperature?` (clamp [0, 2]). Approval не требуется (защита — DailyBudgetUsd).
  - `ListExternalProvidersTool` — `list_external_providers`. Возвращает `{ default,
    providers[] }` с именем, DisplayName, моделью, доступностью (circuit breaker),
    тарифами, последней ошибкой. Без параметров.
  - `CheckInternetConnectionTool` — `check_internet_connection`. Лёгкий `GET /models`
    через `IExternalLlmClient.TestConnectionAsync`. Параметр `provider?`
    (по умолчанию `DefaultProvider`).
  - `Startup.cs`: `RegisterExternalLlmTools(services)` — 3 `Scoped<ITool>`, только при
    `ExternalLlm:Enabled = true`. Chat их **не видит** напрямую — только через
    `external_llm_agent` (Фаза 4, `AllowedTools` в `SubAgents:*`). **ChatStreamService
    не правится** (RULES § 4.44 применим только к top-level `ITool` в Chat).
  - **Тесты:** +16 (`AskExternalLlmToolTests` ×10, `ListExternalProvidersToolTests` ×3,
    `CheckInternetConnectionToolTests` ×3).
  - **DoD:** `dotnet build` 0/0, `dotnet test` 479 → **495/495**.
- **External-LLM Agent — Фаза 2.5–2.6: Client + wire-up (v1.8.1, KI-109)**:
  - `ExternalLlmClient` (`Implementation/ExternalLlm/`) — Singleton, `IExternalLlmClient`.
    OpenAI-совместимый POST `{BaseUrl}/chat/completions` с Bearer-токеном. Retry 1×
    при 5xx / 429 (не при 4xx и timeout). Circuit breaker + Budget tracker +
    Cost calculator интегрированы. Privacy: без prompt/content в логах.
  - `IExternalLlmClient.CompleteAsync(int userId, ...)` — расширена сигнатура:
    `userId` нужен для per-user budget tracker (DESIGN § 6.4). `TestConnectionAsync`
    без `userId` (не тратит бюджет).
  - `Startup.cs` — раскомментированы 4 регистрации (`Configure<ExternalLlmOptions>` +
    3 Singleton + Client).
  - `appsettings.json` / `.Development.json` — секция `ExternalLlm` (`Enabled = false`
    по умолчанию, 5 провайдеров: deepseek / openai / groq / together / ollama).
  - `Program.cs` — fail-fast блок: resolve `IExternalProviderRegistry` при
    `ExternalLlm:Enabled = true` (иначе валидация сработала бы только на первом вызове).
  - **Тесты:** +14 (`ExternalLlmClientTests`).
  - **Fix (в том же коммите):** `HttpClient.Timeout` нельзя менять после первого
    `SendAsync` (`InvalidOperationException: This instance has already started...`).
    В retry-цикле с переиспользованием `HttpClient` (mock, DI-контейнер) это ломается.
    Заменено на `CancellationTokenSource.CancelAfter` — таймаут привязан к запросу,
    а не к клиенту (см. RULES § 4.48).
  - **DoD:** `dotnet build` 0/0, `dotnet test` 465 → **479/479**.
- **External-LLM Agent — Фаза 2.1–2.4: Infrastructure (v1.8.1, KI-109)**:
  - `ExternalProviderRegistry` (`Implementation/ExternalLlm/`) — Singleton, читает
    `ExternalLlm:Providers` из конфигурации. Fail-fast валидация при `Enabled = true`
    (DESIGN_EXTERNAL_LLM § 5.6).
  - `ExternalLlmCircuitBreaker` (`Implementation/ExternalLlm/`) — Singleton, `IDisposable`.
    Per-provider, N подряд fail → skip на `BreakDurationSeconds`. Cleanup Timer 5 мин (KI-043).
  - `ExternalLlmBudgetTracker` (`Implementation/ExternalLlm/`) — Singleton, `IDisposable`.
    Per-user, daily budget + tokens. Lazy-reset при смене дня UTC. Cleanup Timer 30 мин.
  - `ProviderCostCalculator` (`Implementation/ExternalLlm/`) — static helper: USD по токенам.
  - **Тесты:** +41 (Registry ×10, CircuitBreaker ×11, BudgetTracker ×11, CostCalculator ×9).
  - **DoD:** `dotnet build` 0/0, `dotnet test` 424 → **465/465**.
- **External-LLM Agent — Фаза 1: DTO + интерфейсы (v1.8.1, KI-109)**:
  - `DTO/ExternalLlm/` — 7 файлов: `ExternalLlmOptions`, `ExternalLlmCircuitBreakerOptions`,
    `ExternalProviderOptions`, `ExternalLlmRequest`, `ExternalLlmResponse`,
    `ExternalLlmComparisonDto`, `ProviderHealthStatus`.
  - `Interfaces/` — 4 файла: `IExternalLlmClient`, `IExternalLlmCircuitBreaker`,
    `IExternalLlmBudgetTracker`, `IExternalProviderRegistry`.
  - `Startup.cs` — закомментированный блок будущих регистраций (раскомментируется в Фазе 2.6).
  - **DoD:** `dotnet build` 0/0.
- **README — раздел «Docker — что работает, что нет» (v1.8.x, KI-112)**:
  - Таблица: Chat UI, Mail Agent, RAG, SqlAgent, file_system_agent, web_agent —
    работают; code_agent, git_agent, github_agent — нет (нет утилит в lightweight-образе).
  - DataProtection volume `iichattools-keys:/home/app/.aspnet/DataProtection-Keys`.
  - LM Studio через `host.docker.internal` (+ `--add-host` на Linux).
  - Mail Agent credentials через env-переменные с префиксом `Mail__`.
  - HTTPS redirect note + `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.

### Fixed
- **KI-116 — `code_agent` и `planner_agent` тоже откачены на qwen3-4b (v1.8.x)**:
  - **Подтверждение gemma не tool-calling:** логи LM Studio для `planner_agent`
    показали `"tool_calls": []` при правильном `reasoning_content` с
    запланированными `save_memory(...)`. `save_memory` **не вызывался**.
  - **Fix:** `SubAgents:code_agent:Model` и `SubAgents:planner_agent:Model`
    → `qwen/qwen3-4b-2507`.
  - **KI-116** → **Fixed** (v1.8.x).
- **KI-118 — Chat LLM не вызывает `code_agent` для простых задач (new, Documented)**:
  - Запрос «Через Python посчитай 2+2» → Chat LLM вывела код, но **не
    вызвала `code_agent`**. Требует усиления Description.
- **Mail Agent — успех: qwen3-4b + Context Length 16384 (v1.8.x, KI-115 → Fixed)**:
  - **Диагноз (лог LM Studio):** `prompt_tokens: 8145, completion_tokens: 47,
    finish_reason: "length"` — упор в дефолтный Context Length 8192. Агент
    физически не мог дописать ответ.
  - **Fix:**
    - `SubAgents:mail_agent:Model` → `qwen/qwen3-4b-2507` (после отката gemma).
    - **LM Studio: Context Length 8192 → 16384** для qwen3-4b.
  - **Результат:** `prompt_tokens: 10194, completion_tokens: 295, finish_reason: "stop"` —
    письмо прочитано **полностью** (тело, ключевые моменты, детали).
  - **KI-115** → **Fixed** (v1.8.x).
  - **KI-117** (new, Documented): требование к LM Studio — `Context Length ≥ 16384`
    для агентов. README обновлён (новый раздел «Требования к LM Studio»).
- **Mail Agent — откат модели на qwen3-4b + KI-116 (v1.8.x)**:
  - **Диагноз:** в KI-115 попытались заменить модель `mail_agent` на
    `gemma-4-12b-coder-fable5-composer2.5-v1` (как у `code_agent`), но
    **gemma не генерирует `tool_calls[]`** — она пишет вызов функции как
    plain text в `content` (см. лог LM Studio, KI-116).
  - **Fix:** откат `SubAgents:mail_agent:Model` обратно на
    `qwen/qwen3-4b-2507` (умеет tool calling). Few-shot промпт оставлен.
  - **KI-116** — новый документированный баг: gemma-4-12b не
    tool-calling-совместима. Требует проверки `code_agent` / `planner_agent`.
  - **KI-115** — статус `Planned` → `In Progress` (требуется архитектурный
    фикс, см. план ниже).

### Changed
- **Mail Agent — увеличены `MaxSteps` (10 → 15) + усилен SystemPrompt
  (v1.8.x, KI-111 / KI-113)**:
  - `MaxSteps: 10 → 15` в `SubAgents:mail_agent` (`appsettings.json` +
    `appsettings.Development.json`). Причина: Chat LLM передаёт `maxSteps=5`
    вместо дефолта, чего не хватает для list_emails + read_email.
  - SystemPrompt: добавлены **ПРАВИЛА ЭФФЕКТИВНОСТИ** (без промежуточных
    разведок, типовые задачи в 1-2 вызова) и **ПРАВИЛА ЧЕСТНОСТИ**
    (не говорить «успешно», если не выполнено — см. KI-113).

### Added
- **KI-111..114 — 4 новые записи в KNOWN_ISSUES**:
  - **KI-111** (Planned) — `mail_agent` не помнит контекст между вызовами.
  - **KI-112** (Documented) — Docker-образ без `git`/`gh`/`python3`/`node`.
  - **KI-113** (Planned) — `mail_agent`: qwen3-4b галлюцинирует успех.
  - **KI-114** (Documented) — `AgentToolBase` возвращает `Ok` при `Completed=false`.
- **Mail Agent — Troubleshooting в README (v1.8.x, KI-107-follow)**:
  подраздел «Troubleshooting (Yandex и другие)» в `README.md`.
  Разбор типичных ошибок `MailKit.Security.AuthenticationException: LOGIN
  invalid credentials or IMAP is disabled` — 3 причины (IMAP не включён
  в веб-интерфейсе, пароль не App Password, Username не полный email),
  проверка через внешний IMAP-клиент, troubleshooting timeout,
  `Workspace:RootPath`, rate limit.

---

## [1.8.0] — 2026-09-29

**Mail Agent (IMAP/SMTP через MailKit 4.8.0, KI-107).**
Почтовый агент `mail_agent` + 7 инструментов внутри: `send_email` (approval),
`list_emails`, `read_email`, `search_emails`, `delete_email` (approval),
`move_email` (approval), `mark_as_read`. Rate limiting 20 писем/час, 30 чтений/мин.
Privacy-first (без PII в логах). Вложения в `mail-attachments/{uid}/`, ≤ 10 MB.
Глобальные credentials (App Password в User Secrets). Chat видит **12 инструментов**
(было 11). Реализовано в 5 фазах + релизная документация. Тесты: **409 → 424**.

### Added
- **DESIGN v1.8 — Mail Agent (Draft)** (`docs/development/v1.8/DESIGN_MAIL_AGENT.md`):
  дизайн-документ для почтового агента (IMAP/SMTP через MailKit 4.8.0).
  7 инструментов внутри агента `mail_agent`: `send_email` (approval),
  `list_emails`, `read_email`, `search_emails`, `delete_email` (approval),
  `move_email` (approval), `mark_as_read`. Глобальные credentials (App Password
  в User Secrets). Rate limiting 20 писем/час. Privacy-first (без PII в логах).
  Вложения в `Workspace/users/{id}/mail-attachments/{uid}/`, ≤ 10 MB.
  Целевой релиз — v1.8.0.
- **DESIGN v1.8 — External-LLM Agent (Draft)** (`docs/development/v1.8/DESIGN_EXTERNAL_LLM.md`):
  дизайн-документ для агента внешних LLM (DeepSeek / OpenAI / Groq / Together AI / Ollama).
  3 инструмента внутри агента `external_llm_agent`: `ask_external_llm` (с опциональным
  `compare_with` для сценария «сравнение»), `list_external_providers`,
  `check_internet_connection`. Оркестратор с 4 сценариями (Fallback / Специализация /
  Разные знания / Сравнение). `include_context: false` по умолчанию.
  Budget guardrails: `DailyBudgetUsd = $5`, `DailyTokensLimit = 500k`, `MaxTokens` per request.
  Circuit breaker (3 fail → skip 5 мин). Целевой релиз — v1.8.0.
- **KI — заведены 4 записи (Planned, v1.8.0/v1.8.x/v1.9+)**:
  - **KI-107** — Mail Agent (IMAP/SMTP через MailKit). v1.8.0.
  - **KI-108** — Per-user mail accounts (свой ящик у каждого пользователя). v1.8.x.
  - **KI-109** — External-LLM Agent (OpenAI-совместимые провайдеры). v1.8.0.
  - **KI-110** — Anthropic Claude + Google Gemini (свои форматы запросов). v1.9+.
- **Mail Agent — Фаза 1 (v1.8.0, KI-107)**: NuGet (MailKit 4.8.0) + DTO + интерфейсы.
  - `Directory.Build.props`: `<MailKitVersion>4.8.0</MailKitVersion>`.
  - `IIChatTools.Services.csproj`: `<PackageReference Include="MailKit" ... />`.
  - `DTO/Mail/` — 12 файлов: `MailOptions`, `MailEndpointOptions`, `MailAttachmentsOptions`,
    `MailSearchOptions`, `MailRateLimitOptions`, `MailAccountCredentials`,
    `MailMessageSummaryDto`, `MailMessageDto`, `MailAttachmentDto`, `SendMailRequest`,
    `SearchMailRequest`, `RateLimitResult`.
  - `Interfaces/` — 4 файла: `IMailClient`, `IMailAccountProvider`,
    `IMailAttachmentService`, `IMailRateLimiter`.
  - **DoD:** `dotnet build` 0/0. Все типы компилируются, но пока не используются.
- **Mail Agent — Фаза 2 (v1.8.0, KI-107)**: MailKitClient + GlobalMailAccountProvider.
  - `Implementation/Mail/GlobalMailAccountProvider.cs` — Singleton, читает `IOptions<MailOptions>`,
    возвращает `MailAccountCredentials`. Fail-fast при `Enabled=false` / пустых Host / отсутствии creds.
  - `Implementation/Mail/MailKitClient.cs` — Singleton, `IDisposable`. Реализация
    `IMailClient`: 8 методов (list/read/search/send/delete/move/mark/test).
    Per-call connect → operation → disconnect. IMAP-пул с TTL — отложен (KI-107-more).
    Privacy: в логах — только метаданные (uid, count, bytes). Вложения — Фаза 4.
  - `Startup.cs` — DI: `Configure<MailOptions>` + `Configure<MailRateLimitOptions>` +
    `AddSingleton<IMailAccountProvider, GlobalMailAccountProvider>` +
    `AddSingleton<IMailClient, MailKitClient>`.
  - `appsettings.json` / `.Development.json` — секция `Mail` (`Enabled=false` по умолчанию,
    dev — placeholder `CHANGE_ME_VIA_USER_SECRETS` для creds).
  - **Тесты:** +3 smoke (`MailKitClientSmokeTests`).
  - **DoD:** `dotnet build` 0/0. `dotnet test` — 375 → **378/378**.
- **Mail Agent — Фаза 3A (v1.8.0, KI-107)**: 3 read/mutating tool'а.
  - `Implementation/Tools/Mail/ListEmailsTool.cs` — `list_emails` (read-only).
    Параметры: `mailbox?="INBOX"`, `count=20` (clamp 1..100), `unseenOnly=false`.
  - `Implementation/Tools/Mail/ReadEmailTool.cs` — `read_email` (read-only).
    Параметры: `uid` (required), `mailbox?="INBOX"`, `saveAttachments=true`.
  - `Implementation/Tools/Mail/SendEmailTool.cs` — `send_email` (**approval**).
    Параметры: `to[]`, `cc[]?`, `bcc[]?`, `subject`, `body`, `isHtml=false`,
    `attachments[]?`. Валидация: email, ≤ 10 получателей, непустое body.
    Rate limiting (20/час) + attachments — Фаза 4.
  - `Startup.cs` — `RegisterMailTools(services, Configuration)`: 3 инструмента
    только при `Mail:Enabled = true`.
  - **Privacy:** в логах — только количество (recipients, attachments, bodyLen).
  - **Тесты:** +16 (`MailToolsTests`).
  - **DoD:** `dotnet test` — 378 → **394/394**.
- **Mail Agent — Фаза 3B (v1.8.0, KI-107)**: 4 tool'а (search / delete / move / mark_as_read).
  - `Implementation/Tools/Mail/SearchEmailsTool.cs` — `search_emails` (read-only).
    Параметры: `from?`, `subject?`, `since?` (YYYY-MM-DD), `before?`, `unseenOnly?`,
    `mailbox?="INBOX"`, `limit=20` (clamp 1..100). Требуется хотя бы 1 фильтр.
  - `Implementation/Tools/Mail/DeleteEmailTool.cs` — `delete_email` (**approval**).
    Параметры: `uid` (required), `mailbox?="INBOX"`. Перемещает в Trash.
  - `Implementation/Tools/Mail/MoveEmailTool.cs` — `move_email` (**approval**).
    Параметры: `uid` (required), `from?="INBOX"`, `to` (required).
  - `Implementation/Tools/Mail/MarkAsReadTool.cs` — `mark_as_read` (**без approval** —
    мелкое действие, approval уже был на уровне агента).
  - `Startup.cs` — `RegisterMailTools` +4 регистрации.
  - **Тесты:** +15 (`MailTools3BTests`).
  - **DoD:** `dotnet test` — 394 → **409/409**.
- **Mail Agent — Фаза 4 (v1.8.0, KI-107)**: MailAttachmentService + InMemoryMailRateLimiter.
  - `Implementation/Mail/MailAttachmentService.cs` — `IMailAttachmentService`.
    - `SaveIncomingAsync` — сохранение вложения в `{workspace}/mail-attachments/{uid}/{sha256}.ext`
      (дедупликация по SHA256, защита от path-traversal через `PathHelper`).
    - `ResolveForSendAsync` — валидация относительных путей + лимиты
      (MaxFileSize / MaxTotalSize / MaxFilesPerMessage).
  - `Implementation/Mail/InMemoryMailRateLimiter.cs` — `IMailRateLimiter`, Singleton, `IDisposable`.
    Per-user лимиты: `SendsPerHour`, `SendsPerMinute`, `ReadsPerMinute`, `MaxHourlyBytesPerUser`.
    Timer cleanup каждые 5 минут (по образцу KI-043).
  - `SendEmailTool` — интеграция `IMailRateLimiter.CheckSend` перед отправкой
    + `RecordBytesSent` после. При превышении — `ToolResult.Fail` с RetryAfter.
  - `Startup.cs` — DI: `AddSingleton<IMailRateLimiter, InMemoryMailRateLimiter>` +
    `AddScoped<IMailAttachmentService, MailAttachmentService>`.
  - **Тесты:** +11 (`MailAttachmentServiceTests` 7 + `InMemoryMailRateLimiterTests` 6).
    Плюс 7 тестов SendEmailTool обновлены (rate limiter в конструкторе) + 2 новых.
  - **DoD:** `dotnet test` — 409 → **420/420**.
  - **Отложено:** сохранение вложений при `read_email` (требует переделки `IMailClient` —
    Фаза 5+).
- **Mail Agent — Фаза 5 (v1.8.0, KI-107)**: агент `mail_agent` в Chat.
  - `Implementation/Tools/SubAgent/MailAgentTool.cs` — наследник `AgentToolBase`
    (по образцу 6 других агентов). `Name = AgentName = "mail_agent"`.
    `RequiresApprovalByDefault` резолвится из дескриптора
    (`SubAgents:mail_agent:RequiresApproval = true`).
  - `Startup.cs` — 1 строка в `RegisterSpecializedAgentTools`:
    `services.AddScoped<ITool, MailAgentTool>()`.
  - `appsettings.json` + `.Development.json` — секция `SubAgents:mail_agent`
    (`Enabled: true`, `Model: qwen3-4b`, `MaxSteps: 10`, `RequiresApproval: true`,
    `AllowedTools: [7 mail-tools]`, system prompt про «никогда не отправляй без просьбы»).
  - **ChatStreamService — НЕ требует правок:** `mail_agent` — наследник
    `AgentToolBase`, попадает в `allowedNames` через
    `SubAgentRegistry.GetEnabled()` (как 6 других агентов). RULES § 4.44
    здесь не применим (в отличие от `database_agent` — он не наследник).
  - **Chat видит 12 инструментов** (было 11): 6 агентов + consult + 3 RAG +
    `database_agent` + `mail_agent`.
  - **Тесты:** без unit (3 override'а — нечего тестировать). Smoke —
    через `/api/tools` + Chat UI.
  - **DoD:** `dotnet build` 0/0. `dotnet test` — 424/424 (без изменений).

---

## [1.7.1] — 2026-09-29

**PDF / DOCX в RAG + локализация `/status` + оригинальное имя в источниках (KI-103, KI-104, KI-105, KI-106).**
PdfParser (PdfPig 0.1.9) + DocxParser (OpenXml 3.1.0) — RAG расширен с 28 → 30 форматов.
Fix accept для `<input type="file">` в `/chat` (.pdf, .docx). Fix локализации `/status`
(4 hardcoded RU). Замена `System.IO.Packaging` 8.0.0 → 10.0.0 (транзитивная уязвимость,
KI-105). Оригинальное имя в источниках RAG для attachments (KI-106). Тесты: **341 → 375** (+34).

### Fixed
- **RAG — оригинальное имя файла в источниках для attachments (v1.7.1, KI-106)**:
  - **Симптом:** в UI-блоке «📚 Источники» под ответом ассистента для
    приложенных к чату файлов показывалось `a2a41a01…docx` (GUID) вместо
    оригинального `Договор.docx`.
  - **Причина:** `ChatAttachmentService.UploadAsync` сохраняет файл на диск
    как `{guid}.ext` (by design, KI-083 Шаг 6A), а в `IngestionRequest.Source`
    передавался `StoragePath` (= `chat-attachments/{chatId}/{guid}.ext`).
    `RagSourceBuilder.BuildLabel` берёт имя из `DocumentPath` → GUID.
  - **Fix:**
    - `ChatAttachmentService` — helper `BuildRagDocumentPath(chatId, fileName, subfolder)`
      → `"chat-attachments/{chatId}/{fileName}"`.
    - `UploadAsync`: в `IngestionRequest.Source` передаётся RAG-путь
      (оригинальное имя), не `StoragePath`.
    - `DeleteAsync`: сначала удаляет по новому пути; при 0 — fallback на
      `entity.StoragePath` (для записей до v1.7.1).
    - **Физический файл** на диске — по-прежнему `{guid}.ext` (без изменений).
  - **Старые записи** (до v1.7.1) остаются с GUID в `DocumentPath` —
    одноразовая миграция не делается (косметика).
  - **Тесты:** +1 (`UploadAsync_StoresOriginalFileName_InRagDocumentPath`).
    Тест `DeleteAsync_LegacyAttachment_FallsBackToStoragePath` — не добавлен
    (нужен `FakeIngestionService.DeleteReturnValues`; в следующий KI).
  - **Связанные:** KI-083, KI-086.
- **Admin UI — `/status` локализация badge'ей (v1.7.1, KI-103)**:
  - **Симптом:** при переключении языка на EN badge'и БД и зависимостей
    оставались на русском («Онлайн», «Установлено», «Не установлено»).
  - **Причина:** hardcoded RU-строки в `status.js` (функция `render`).
    Остальные строки `Status.cshtml` уже были через `@Localizer[...]`.
  - **Fix:**
    - `Status.cshtml` (`#status-root`) — `data-label-online`,
      `data-label-offline`, `data-label-installed`, `data-label-not-installed`.
    - `status.js` — helper `pageLabels()` (читает `data-*` → camelCase) +
      замена 4 hardcoded RU-строк на `labels.*`.
    - `.resx` (RU + EN) — **+4 ключа** (`StatusOnline`, `StatusOffline`,
      `StatusInstalled`, `StatusNotInstalled`). Синхронизация через
      `LocalizationSyncTests`.
  - **Не трогал:** `statusBadge()` — возвращает `Success`/`Error`/`Pending`/
    `Cancelled` — это **данные из API** (`AuditLog.LogStatus`), не UI.
  - **DoD:** badge'и переводятся RU/EN.

### Security
- **KI-105 — транзитивная уязвимость `System.IO.Packaging 8.0.0` (v1.7.1)**:
  - Обнаружено `dotnet restore` после добавления `DocumentFormat.OpenXml 3.1.0`
    (KI-104). **2 high-severity** advisory: `GHSA-f32c-w444-8ppv` +
    `GHSA-qj66-m88j-hmgj` (DoS) на транзитивный `System.IO.Packaging 8.0.0`
    — 6 warnings NU1903 в 3 проектах.
  - **Fix:** явный `PackageReference Include="System.IO.Packaging" Version="10.0.0"`
    в `IIChatTools.Services.csproj` — перебивает транзитивную 8.0.0.
    Прецедент — KI-022 (SQLitePCLRaw).
  - Версия в `Directory.Build.props` (`$(SystemIOPackagingVersion)`).
  - **Профилактика** (в том же коммите): скрипт
    scripts/setup/check-vulnerabilities.ps1 — обёртка над
    dotnet list package --vulnerable --include-transitive с exit-кодом
    1 при обнаружении уязвимостей. README — раздел «Проверка уязвимостей».  

### Added
- **RAG — PDF / DOCX парсеры (v1.7.1, KI-104)**:
  - **`PdfParser`** (`Implementation/Rag/Parsers/`) — Singleton, `Name = "PdfPig"`,
    расширение `.pdf`. PdfPig 0.1.9 (Apache 2.0). Извлекает текстовый слой
    через `PdfDocument.Open(bytes)` → `GetPages()` → `page.Text`. Метаданные:
    `format`, `parser`, `pageCount`. `ParsedDocument.PageCount = doc.NumberOfPages`.
    Corrupt/encrypted PDF → `InvalidDataException`.
  - **`DocxParser`** (`Implementation/Rag/Parsers/`) — Singleton, `Name = "OpenXml"`,
    расширение `.docx`. DocumentFormat.OpenXml 3.1.0 (MIT). Извлекает текст
    через `WordprocessingDocument.Open(ms, isEditable: false)` →
    `MainDocumentPart.Document.Body.Descendants<Paragraph>().InnerText`.
    Метаданные: `format`, `parser`, `paragraphCount`. `PageCount = null`.
    Corrupt / `.doc` (старый формат) → `InvalidDataException`.
  - **`Startup.cs`** — DI: `services.AddSingleton<IRagDocumentParser, PdfParser>()` +
    `services.AddSingleton<IRagDocumentParser, DocxParser>()` (после PlainText).
    Реестр (`RagDocumentParserRegistry`) подхватывает через `IEnumerable<T>`.
  - **`appsettings.json`** — `Rag:Ingestion:AllowedExtensions` + `.pdf`, `.docx`.
  - **NuGet:** `Directory.Build.props` — `<PdfPigVersion>0.1.9</PdfPigVersion>`,
    `<OpenXmlVersion>3.1.0</OpenXmlVersion>`, `<SystemIOPackagingVersion>10.0.0</SystemIOPackagingVersion>`.
  - **Тесты:** `PdfParserTests` (**16** — CanParse_T ×4, CanParse_F ×6,
    Name, Extensions, 4 `ThrowsAsync`) + `DocxParserTests` (**17** —
    CanParse_T ×4, CanParse_F ×6, Name, Extensions, 3 `ThrowsAsync`,
    ValidDocx, EmptyDocx). Реальный PDF-контент не проверяется
    (PdfPig read-only); DOCX генерируется самим OpenXml SDK.
    **Всего: 341 → 374**.

---

## [1.7.0] — 2026-09-29

**Database Agent — read-only SQL-доступ LLM к БД приложения (KI-097).**
4 действия инструмента `database_agent` (`list_databases` / `list_tables` /
`describe_table` / `execute_query`), 5 уровней безопасности (read-only роль,
валидатор, whitelist, timeout+auto-LIMIT, approval+audit), admin UI
`/admin → SQL Agent`, per-action approval (KI-101), fix локализации `/admin`
(KI-102). Тесты: **312 → 341**.

### Added
- **Database Agent — Фаза 1: контракты + DTO (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.1)**:
  - **DTO (`DTO/SqlAgent/`)** — 5 файлов: `DatabaseConnectionInfoDto`,
    `SqlTableInfoDto`, `SqlColumnInfoDto`, `SqlQueryRequest`, `SqlQueryResultDto`.
  - **Interfaces (`Interfaces/`)** — 3 файла: `ISqlAgentService` (оркестратор
    list / describe / execute), `ISqlQueryValidator` (валидация SQL),
    `ISqlConnectionProvider` (фабрика `DbConnection`).
  - **DTO (`DTO/SqlAgent/`)** — расширено: +3 файла `ValidationResult`
    (результат валидации), `SqlAgentOptions` (bind из `appsettings:SqlAgent`),
    `SqlAgentConnectionOptions` (подсекция `Connections[*]`).
    <br/>**Почему DTO, а не Implementation:** `SqlAgentConnectionOptions`
    используется в сигнатуре `ISqlQueryValidator.Validate(...)`, значит
    является частью **контракта**. `Implementation → Interfaces` — да,
    `Interfaces → Implementation` — **нет** (нарушение слоистости).
    Аналогично `ISubAgentRegistry` → `DTO/SubAgent/SubAgentDescriptor`
    и `IChunkingStrategy` → `ChunkingOptions`.
  - **DoD Фазы 1:** `dotnet build` 0/0, `dotnet test` 241/241.
    Все типы компилируются, но пока нигде не используются.
    Реализация — Фазы 2-6.
  - **Правка DESIGN_DB_AGENT § 4.1 и § 7.1:** 3 типа перенесены
    из `Implementation/SqlAgent/` в `DTO/SqlAgent/` (фикс слоистости
    до первого использования).
- **Database Agent — Фаза 2: SqlConnectionProvider + SqlAgentOptionsProvider (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.2)**:
  - **`SqlConnectionProvider`** (`Implementation/SqlAgent/`) — Singleton,
    реализация `ISqlConnectionProvider`. **Switch по `Provider`** вместо
    рефлексии: compile-time проверка, скорость, AOT-совместимость.
    Connection string резолвится из `IConfiguration` при каждом вызове
    (User Secrets / env), без кэша.
  - **`SqlAgentOptionsProvider`** (`Implementation/SqlAgent/`) — Singleton,
    хранит baseline из `IOptions<SqlAgentOptions>` + runtime-overrides
    (для будущей админки, Шаг 6). Потокобезопасен (`lock`). Валидация
    при старте (DESIGN § 5.5): `DefaultConnection` обязателен, `MaxRows` /
    `StatementTimeoutSeconds` — clamp + warning.
  - **`Startup.cs`** — DI-регистрация: `services.Configure<SqlAgentOptions>(...)`
    + `SqlAgentOptionsProvider` (Singleton) + `ISqlConnectionProvider`
    (Singleton).
  - **`Program.cs`** — новый метод `LoadSqlAgentOverrides` (без Async —
    вызывается один раз при старте): читает `AppSettings` с ключами
    `SqlAgent.{name}.{field}`, применяет к провайдеру.
  - **`appsettings.json`** / **`appsettings.Development.json`** — секция
    `SqlAgent` (Connection internal, AllowedTables / DeniedTables,
    QueryValidation). Dev: `Provider=Sqlite`, `MaxRows=50`;
    Prod: `Provider=SqlServer`, `MaxRows=100`.
  - **NuGet** (`Directory.Build.props` + `IIChatTools.Services.csproj`):
    `Microsoft.Data.Sqlite` 10.0.12 + `Microsoft.Data.SqlClient` 6.0.2 —
    ADO.NET-провайдеры для `SqliteConnection` / `SqlConnection`.
  - **`README.md`** — инструкция User Secrets для
    `SqlAgent:Internal:ConnectionString` (dev — Sqlite `Mode=ReadOnly`,
    prod — SqlServer `ApplicationIntent=ReadOnly`).
  - **Тесты** — `SqlConnectionProviderSmokeTests` (4, включая
    DoD-тест «connection string разрешается из config»). Полный набор —
    Фаза 7 (`SqlConnectionProviderTests` + `SqlQueryValidatorTests`).
  - **DoD Фазы 2:** `SqlConnectionProvider` открывает соединение `internal`
    (Sqlite), резолвит connection string из `IConfiguration`.
- **Database Agent — Фаза 3: SqlQueryValidator (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.3)**:
  - **`SqlQueryValidator`** (`Implementation/SqlAgent/`) — Singleton (stateless),
    реализация `ISqlQueryValidator`. **7 шагов валидации** (DESIGN § 6.2):
    <list type="number">
      1. Базовые проверки (пустой, длина, первый токен — SELECT/WITH, multi-statement);
      2. Токенизация (литералы и комментарии **исключаются** из проверок);
      3. Запрет ключевых слов (INSERT, DELETE, DROP, ...);
      4. Запрет функций (load_extension, readfile, ...);
      5. Извлечение таблиц (FROM/JOIN, поддержка schema.table и CTE);
      6. Whitelist / blacklist (Sqlite — case-sensitive, SqlServer — insensitive);
      7. Auto-LIMIT (перед `;`, если LIMIT отсутствует).
    </list>
  - **`SqlToken` / `SqlTokenKind`** (`Implementation/SqlAgent/`, internal) —
    внутренние типы токенизатора. Классификация: Identifier, StringLiteral,
    QuotedIdentifier, Comment, Number, Punctuation. Escape-последовательности
    (`''` в строках, `""` в quoted identifier) обрабатываются.
  - **Startup.cs** — DI-регистрация: `services.AddSingleton<ISqlQueryValidator, SqlQueryValidator>()`.
  - **Тесты** — `SqlQueryValidatorTests` (**27 тестов**):
    базовые (5), keyword-denial (5), function-denial (3), whitelist (10),
    auto-LIMIT (4), комплексный пример из DESIGN § 6.2 (Приложение A).
    Покрытие `SqlQueryValidator` — **> 90%**.
  - **DoD Фазы 3:** валидатор отклоняет `DELETE` / `DROP` / `AspNetUsers` /
    multi-statement, пропускает `SELECT 'DROP TABLE Chats' AS x` (литерал не команда),
    `WITH cte AS (...) SELECT ...` (CTE не таблица). Auto-LIMIT работает.
  - **Fix (в том же коммите, №1):** CS1503 — `HashSet<string>` требует
    `StringComparer` (`IEqualityComparer<string>`), а не `StringComparison`
    (enum для `string.Equals`). Одна строка в `SqlQueryValidator.Validate` (шаг 6).
  - **Fix (в том же коммите, №2):** тестовый хелпер `SqliteProvider` в
    `SqlQueryValidatorTests` создавал `SqlAgentOptions` с пустым
    `Connections`, из-за чего конструктор `SqlAgentOptionsProvider` падал
    с `InvalidOperationException: DefaultConnection='internal' отсутствует
    в Connections` (baseline-валидация DESIGN § 5.5). 29 тестов не
    доходили до валидатора SQL. Fix: добавили минимальный `internal` в
    `Connections` — реальные опции подключения всё равно передаются в
    `Validate(sql, options)` отдельным аргументом.

- **Database Agent — Фаза 4: SqlAgentService + fix KI-100 (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.4)**:
  - **`SqlAgentService`** (`Implementation/SqlAgent/`) — Scoped, реализация
    `ISqlAgentService`. 4 операции: `ListConnectionsAsync`, `ListTablesAsync`,
    `DescribeTableAsync`, `ExecuteQueryAsync`.
    - `ListConnectionsAsync` — метаданные из `SqlAgentOptionsProvider` (без connection string).
    - `ListTablesAsync` — whitelist минус DeniedTables + `SELECT COUNT(*)` (best-effort, -1 при ошибке).
    - `DescribeTableAsync` — провайдер-специфичный запрос (`PRAGMA table_info` для Sqlite,
      `INFORMATION_SCHEMA.COLUMNS` для SqlServer) + пример значения (первая непустая, ≤200 символов).
    - `ExecuteQueryAsync` — валидатор → соединение → `CommandTimeout` →
      `DataReader` с защитой от UNION-обхода (читаем до `MaxRows`, флаг `truncated`).
    - Динамические идентификаторы через `QuoteIdentifier` (`[Name]` с escape `]]`).
    - **Провайдер-специфичные whitelist-сравнения**: Sqlite — `Ordinal` (case-sensitive),
      SqlServer — `OrdinalIgnoreCase` (DESIGN § 6.3).
  - **KI-100 Fix:** `SqlConnectionProvider` резолвит **относительный** `Data Source` Sqlite
    относительно `IWebHostEnvironment.ContentRootPath` (через новый `IAppPathProvider`).
    Без этого фикса первый `execute_query` упал бы с `SQLite Error 14: unable to open
    database file` при запуске не из `IIChatTools.API`.
    - Новый интерфейс `IAppPathProvider` (`Interfaces/`) + `AppPathProvider` (`Implementation/`).
    - Регистрация: `services.AddSingleton<IAppPathProvider>(...)` на основе `IWebHostEnvironment`.
    - `:memory:` и абсолютные пути — не трогаются. Префикс `file:` — тоже.
  - **`Startup.cs`** — DI: `IAppPathProvider` (Singleton) + `ISqlAgentService` (Scoped).
  - **Тесты** — `SqlAgentServiceTests` (**11**), `SqlConnectionProviderSmokeTests` (**+1** на KI-100).
    Всего: **274 → 286**.
  - **DoD Фазы 4:** `SELECT COUNT(*) FROM Chats` возвращает число через
    `ISqlAgentService`; Auto-LIMIT работает; невалидный SQL бросает `ArgumentException`.
  - **Fix (в том же коммите, №1):** `IReadOnlyList<T>` не имеет `.Find` (это instance-метод
    `List<T>`) — в `SqlAgentServiceTests` заменено на LINQ `FirstOrDefault` (×3, +`using System.Linq;`).
    Плюс `Assert.Equal(1, ...Count)` → `Assert.Single(...)` (xUnit2013).
- **Database Agent — Фаза 5: DatabaseAgentTool + интеграция в Chat (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.5)**:
  - **`DatabaseAgentTool : ITool`** (`Implementation/Tools/SqlAgent/`) —
    top-level инструмент (**не** `AgentToolBase` — DESIGN § 4.1).
    Диспетчеризует 4 действия через `ISqlAgentService`:
    `list_databases` / `list_tables` / `describe_table` / `execute_query`.
    Параметры: `action` (required), `connection` (optional),
    `table` (optional), `sql` (optional), `maxRows` (optional).
  - **`Startup.cs`** — новый метод `RegisterSqlAgentTools(services, configuration)`:
    регистрирует `ITool → DatabaseAgentTool` (Scoped), но **только если
    `SqlAgent:Enabled = true`** (DESIGN § 3.4).
  - **`ChatStreamService`** — константа `DatabaseAgentToolName = "database_agent"`
    + блок в `allowedNames` (RULES § 4.44). Без этого LLM не увидел бы tool в `tools[]`.
  - **Тесты** — `DatabaseAgentToolTests` (**13**, включая `ThrowOnExecute`
    для проверки обработки ошибок валидатора). Всего: **286 → 299**.
  - **Approval:** `RequiresApprovalByDefault = true` (все 4 действия).
    **Отклонение от DESIGN § 3.3** (там был per-action approval):
    текущая архитектура `ChatStreamService` не поддерживает per-action —
    флаг `RequiresApprovalByDefault` один на tool. Заведена **KI-101** (Deferred, v1.7.x).
  - **DoD Фазы 5:** Chat видит **11 инструментов** (было 10).
    LLM может вызвать `database_agent` и получить ответ на вопрос про БД.
- **Database Agent — Фаза 5.5: per-action approval (v1.7.0, KI-101 — Fixed)**:
  - **Архитектурный паттерн:** в `ITool` добавлен **default-метод**
    `RequiresApprovalForCall(JObject arguments)` с fallback на
    `RequiresApprovalByDefault`. Все 46 существующих инструментов работают
    без изменений (C# 8+ default interface method).
  - **`IToolRegistry.GetTool(string name)`** — новый метод + реализация
    в `ToolRegistry`.
  - **`ChatStreamService`** — блок `requiresApproval` переписан:
    `toolInstance?.RequiresApprovalForCall(args) ?? true`.
  - **`DatabaseAgentTool.RequiresApprovalForCall`** — override:
    только `execute_query` требует approval. Метаданные
    (`list_databases` / `list_tables` / `describe_table`) — без approval.
  - **`Description` усилен:** явно просит LLM для вопросов про количество
    использовать **сразу** `execute_query` (`SELECT COUNT(*)`), не делать
    «разведку» через `list_databases` / `list_tables`.
  - **Тесты:** +6 (в `DatabaseAgentToolTests`) + 4 (в `ToolRegistryTests`).
    Всего: **300 → 310**.
  - **Fix (в том же коммите, CS0535 ×2):** после расширения `IToolRegistry.GetTool`
    два fake-класса в `ChatStreamServiceTests` (`FakeToolRegistry`, `EmptyToolRegistry`)
    не реализовали новый член. RULES § 4.34 — расширение интерфейса требует
    grep по **всем** fake-заглушкам. `FakeToolDef` расширен до `: ITool` (для
    корректного `RequiresApprovalForCall`), добавлен `GetTool` в оба fake-класса.
  - **Fix (в том же коммите, CS1061):** в `ToolRegistryTests` тест
    `RequiresApprovalForCall_DefaultImplementation_ReturnsRequiresApprovalByDefault`
    объявлял переменные типом конкретного класса (`FakeToolWithApproval`),
    который не переопределяет метод. **Default interface method (C# 8+)**
    доступен только через интерфейсную переменную — иначе CS1061.
    Fix: `ITool toolFalse = new FakeToolWithApproval()`. Заведено
    **RULES § 4.46** (default interface method не виден через конкретный тип).
  - **DoD Фазы 5.5:** при вопросе «Сколько чатов?» — **1 модалка** approval
    (на `execute_query`), а не 3 (как в smoke Фазы 5).
- **Database Agent — Фаза 6A: Admin-сервис (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6)**:
  - **3 DTO** (`DTO/Admin/`): `SqlAgentConnectionItemDto` (состояние подключения
    для UI, включая `IsOverridden`), `UpdateSqlAgentConnectionRequest` (все поля
    опциональны — null = не менять), `SqlAgentTestResultDto` (Success/Message/DurationMs).
  - **`IAdminSqlAgentService` + `AdminSqlAgentService`** (Scoped):
    - `GetAllConnectionsAsync` — актуальные опции (override + baseline) + флаг `IsOverridden`.
    - `UpdateConnectionAsync` — валидация (MaxRows 1–10000, Timeout 1–300),
      upsert override в `AppSettings` (ключи `SqlAgent.{name}.{field}`),
      применение к `SqlAgentOptionsProvider` в runtime, аудит.
    - `TestConnectionAsync` — открывает соединение (даже для `Enabled=false`)
      + `SELECT 1`; все ошибки возвращаются в DTO, не бросаются.
    - `ResetConnectionAsync` — удаляет все ключи `SqlAgent.{name}.*` из AppSettings,
      вызывает `SqlAgentOptionsProvider.Reset(name)`, аудит.
  - **`ISqlConnectionProvider.CreateConnectionAsync`** — новый параметр
    `bool ignoreEnabled = false` (default — прежнее поведение). Используется
    только admin-сервисом для теста отключённых подключений.
  - **`SqlConnectionProvider`** + fake в `SqlAgentServiceTests` — обновлены
    под новую сигнатуру.
  - **`Startup.cs`** — DI: `services.AddScoped<IAdminSqlAgentService, AdminSqlAgentService>()`.
  - **DoD Фазы 6A:** сервис собирается, baseline + override работают,
    `TestConnectionAsync` возвращает Success/Message/DurationMs.
  - **Fix (в том же коммите, CS1503 ×3):** после добавления параметра
    `bool ignoreEnabled = false` в середину `ISqlConnectionProvider.CreateConnectionAsync`
    три **позиционных** вызова в `SqlAgentService` (`ListTablesAsync` /
    `DescribeTableAsync` / `ExecuteQueryAsync`) стали передавать `cancellationToken`
    в слот `ignoreEnabled`. Fix: именованные аргументы
    `CreateConnectionAsync(connection, cancellationToken: cancellationToken)`.
    RULES § 4.34 — уточнён: «изменение сигнатуры метода → grep по вызовам,
    не только по реализациям» (случай б).
- **Database Agent — Фаза 6B: AdminSqlAgentController (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6)**:
  - **`AdminSqlAgentController`** (`IIChatTools.API/Controllers/`) — 4 endpoint'а:
    <list type="bullet">
      <item><description><c>GET /api/admin/sql-agent/connections</c> — список подключений;</description></item>
      <item><description><c>PUT /api/admin/sql-agent/connections/{name}</c> — обновить override-настройки;</description></item>
      <item><description><c>POST /api/admin/sql-agent/connections/{name}/test</c> — SELECT 1 (работает и для Enabled=false);</description></item>
      <item><description><c>POST /api/admin/sql-agent/connections/{name}/reset</c> — сбросить к baseline.</description></item>
    </list>
  - Формат ответа `{ success, data }` / `{ success: false, message }` (RULES § 1.6).
  - `[Authorize(Policy = "AdminOnly")]` — только администраторы.
  - `IStringLocalizer<SharedResources>` — общие сообщения (ошибки сервера).
  - Аудит действий — внутри `AdminSqlAgentService` (не дублируется).
  - **DoD Фазы 6B:** все 4 endpoint'а доступны администратору,
    изменения whitelist применяются в runtime без рестарта.
- **Database Agent — Фаза 6C+6D: UI + локализация (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6)**:
  - **9-я вкладка «SQL Agent»** в `Admin.cshtml` (после «База знаний»).
    Таблица: name (с бейджем «изменено» при override) / displayName / provider /
    enabled / allowed tables / maxRows / timeout / actions.
  - **`admin-sql-agent.js`** (новый модуль, ~250 строк):
    - `loadConnections` / `renderConnectionsTable` — таблица подключений;
    - `openEditModal` — модалка (переиспользует `showModal` из `admin.js`);
      редактирование whitelist/blacklist (textarea построчно), MaxRows, Timeout, Enabled;
    - `testConnection` — `SELECT 1` через `POST .../test`;
    - `resetConnection` — сброс к baseline через `POST .../reset`;
    - локализация — через `data-*` на `#pane-sql-agent` (RULES § 4.17).
  - **`.resx` (RU + EN)** — **22 ключа** (`AdminTabSqlAgent`,
    `SqlAgentColumn*`, `SqlAgentEditTitle`, `SqlAgentReset*`, `SqlAgentTest*`,
    `SqlAgentEnabledLabel`, `SqlAgentAllowedTables`, `SqlAgentDeniedTables`,
    `SqlAgentMaxRowsLabel`, `SqlAgentTimeoutLabel`, `SqlAgentNoConnections`,
    `SqlAgentOverriddenBadge`). Синхронизированы через `LocalizationSyncTests`.
  - **DoD Фазы 6C+6D:** через `/admin → SQL Agent` можно:
    (а) видеть список подключений с бейджем override,
    (б) редактировать whitelist/MaxRows/Timeout/Enabled в модалке,
    (в) проверить подключение (`SELECT 1`),
    (г) сбросить к значениям из appsettings.json. Все изменения — в runtime.
- **Admin UI — Фаза 6E: Fix локализации (v1.7.0, KI-102)**:
  - **Баг** в `admin.js` (`loadWhitelist`, empty-state): literal
    `@Localizer["Убрать из белого списка"]` — синтаксис Razor **не работает**
    в `.js`-файлах. Проявлялся как raw-текст при добавлении инструмента
    в whitelist. Устранён.
  - **Hardcoded RU-строки** в `admin.js` (12), `admin-agents.js` (15),
    `admin-sql-agent.js` (3) — заменены на `data-label-*` (RULES § 4.17).
  - **`Admin.cshtml`** — добавлены `data-label-*` на 4 панели
    (`pane-users`, `pane-settings`, `pane-whitelist`, `pane-agents`).
  - **`.resx` (RU + EN)** — **+30 ключей** (`AdminUser*`, `AdminSettings*`,
    `AdminWhitelist*`, `AdminAgent*`, `AdminAgentModal*`, `AdminTime*`,
    `SqlAgentMaxRowsValidation`, `SqlAgentTimeoutValidation`,
    `SqlAgentConnectionNotFound`). Синхронизация проверяется
    `LocalizationSyncTests`.
  - **Helpers:** `paneLabels(paneId)` в `admin.js` (читает `data-*` →
    camelCase) + локальный `paneLabels()` в `admin-agents.js`.
  - **DoD Фазы 6E:** при переключении RU/EN весь UI `/admin` переводится
    (users / settings / whitelist / agents / SQL Agent).
- **Database Agent — Фаза 7A+7B: тесты admin-слоя (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.7)**:
  - **`AdminSqlAgentServiceTests`** (**13 тестов**):
    `GetAllConnectionsAsync` (2), `UpdateConnectionAsync` — валидация (4),
    persist в AppSettings (2), runtime-применение (1), аудит (1),
    `TestConnectionAsync` (3), `ResetConnectionAsync` (3).
    Fake `IAppSettingsService` (in-memory CRUD) + `Mock<IAuditService>` +
    реальный `SqlAgentOptionsProvider` + `SqliteConnection(":memory:")`.
  - **`AdminSqlAgentControllerTests`** (**7 тестов**):
    `GetConnectionsAsync` (1), `UpdateConnectionAsync` (3 — valid / null / ArgumentException),
    `TestConnectionAsync` (1), `ResetConnectionAsync` (2 — valid / throw).
    Fake `IAdminSqlAgentService` + `FakeStringLocalizer` (IStringLocalizer<T>).
    Прямой вызов контроллера (без `WebApplicationFactory`) — консистентно
    с `AdminKnowledgeControllerTests`.
  - **DoD Фазы 7A+7B:** все admin-endpoints Database Agent покрыты unit-тестами.
    Всего: **312 → 332**.
  - **KI-103** (Documented, план — v1.7.x): на `/status` часть UI — hardcoded RU
    в `status.js` (не входит в 6E — там только `/admin`).
- **Database Agent — Фаза 7C: интеграционные тесты + документация (v1.7.0, KI-097)**:
  - **`SqlAgentIntegrationTests`** (**3 теста**): сквозной путь
    `DatabaseAgentTool → ISqlAgentService → ISqlQueryValidator → ISqlConnectionProvider`
    → реальная Sqlite-БД (временный файл). Проверяет:
    - `ExecuteQuery_ValidSelect_ReturnsRowCount` — успешный SELECT; проверяет
      `Data.Rows[0]["Total"] == 3` (не `Message` — там «Возвращено 1 строк»,
      т.к. это 1 строка с COUNT(*)).
    - `ExecuteQuery_DeniedTable_ViaTool_ReturnsFail` — валидатор отбивает `AspNetUsers`.
    - `ListDatabases_ViaTool_ReturnsInternalConnection`.
  - **README.md** — обновлён:
    - Chat видит **11** инструментов (было 10).
    - Таблица «Инструменты»: +3 RAG +1 Database Agent = **50**.
    - **Новый раздел «Database Agent (v1.7.0)»**: 4 действия, 5 уровней
      безопасности, примеры вопросов, конфигурация, ограничения.
    - API endpoints: +4 строки (`/api/admin/sql-agent/...`).
    - Счётчик тестов: 241 → **341**.
    - **Fix разметки**: восстановлен блок «Миграции и запуск»
      (незакрытый `markdown` code-block из прошлого diff'а).
  - **KNOWN_ISSUES.md:**
    - **KI-097** → **Fixed (v1.7.0)**. Тело: 13 коммитов, 51 тест, все фазы 0-7.
    - **KI-098** → **Fixed (v1.7.0, Фазы 6A-6E)**.
    - **Сводка по статусам:** `Fixed (v1.7.0) = 4`, `Planned = 1`, `Documented = 9`.
      Всего: **62**.
  - **DoD Фазы 7C:** сквозной путь Database Agent проверен integration-тестами,
    документация (README + KNOWN_ISSUES) обновлена. **Всего тестов: 341**.
  - **Fix (в том же коммите, №1):** интеграционный тест проверял `result.Message`
    на contains «3» — но там количество **строк результата** (1 строка с COUNT(*)),
    а не значение COUNT(*). Fix: проверять `Data.Rows[0]["Total"] == 3L`.
  - **Fix (в том же коммите, №2):** README — секция «Миграции и запуск»
    содержала служебную копию diff'а («Заменить на:» + незакрытый
    ` ```markdown `). Восстановлена: два code-блока (`SqlServer` + `Sqlite`)
    + цитата про `EnsureCreatedAsync`.
- **Database Agent — Фаза 7D: TESTING.md (v1.7.0, KI-097, KI-088)**:
  - **`docs/TESTING.md`** — версия 1.5.0 → **1.7.0**:
    - Шапка: связанные DESIGN + KI-097.
    - § 1: 199/199 → **341/341**; ~15 → ~20 минут.
    - § 2 (Smoke): 10 → **16 сценариев** (+5 для Database Agent: #12–16).
    - **§ 3.5 (новый)** — Database Agent (7 сценариев): whitelist, reset,
      runtime-применение, отказы валидатора, `Enabled=false`.
    - § 4 (UI/UX): +2 проверки локализации (`/admin → SQL Agent` + остальные
      admin-вкладки, KI-102).
    - § 5: обновлён счётчик автотестов + добавлены 5.11 (DB Agent end-to-end)
      и 5.12 (runtime-применение overrides).
    - § 6: ссылка на DESIGN v1.7.
    - § 7: строки v1.6.0 и v1.7.0.
  - **DoD Фазы 7D:** TESTING.md полностью отражает v1.7.0.
    KI-097 → Fixed (Фаза 7 полностью закрыта).
  - **Fix (в том же коммите, №2):** `ExecuteQueryAsync` не выставлял `Truncated = true`,
    когда auto-LIMIT был добавлен валидатором. Причина: SQLite/SqlServer **сам** обрезает
    результат по `LIMIT`, reader возвращает ровно `MaxRows` строк, лишней итерации цикла нет,
    и `if (rows.Count >= maxRows)` не срабатывает. Добавлена post-loop проверка:
    `if (!truncated && validation.LimitAdded && rows.Count >= maxRows) truncated = true;`
    (консервативно — false positive возможен, если в таблице ровно `MaxRows` строк;
    принимается как меньшая из зол).
  - **KI-100 (Documented, план — Фаза 4):** относительный путь Sqlite
    connection string (`Data Source=Data/iichattools-dev.db`) резолвится от
    `Environment.CurrentDirectory`, а не от `ContentRootPath`. Проявится в
    Фазе 4 при первом `execute_query`. План: резолвить относительно
    `IWebHostEnvironment.ContentRootPath` в `SqlConnectionProvider`.

### Changed
- **Docs — PROMPT_V2.md v2.4 → v2.5 (post-release v1.6.1)**:
  - Шапка: актуальный релиз `v1.6.0` → **v1.6.1**.
  - Метрики: 216 → **241** тестов.
  - «Что выпущено» — расширено до v1.6.1 (Sources / citations для Web-tools).
  - Roadmap: **v1.6.1 → Done**, v1.6.x → **v1.6.2** (KI-094, KI-095)
    и **v1.7.0** (KI-096 GitHub Wiki + инфраструктура).
  - «Подводные камни» — добавлены: 4-полевой ключ дедупа sources,
    проброс sources через агентов, KI-094 (Wikipedia timeout).
  - «Известные факты про LM Studio» — обновлено: KI-064 + KI-094.

---

## [1.6.1] — 2026-09-28

**Sources / citations для Web-tools (KI-086-post).** Продолжение v1.6.0:
`wikipedia_search`, `web_search`, `fetch_web_content` теперь возвращают
citations → блок «📚 Источники» в UI показывает **все** источники (RAG + web + wiki).
Sources пробрасываются через агентов (`SubAgentTaskResult.Sources`).
Кросс-платформенный fix `DocumentPath` (относительные пути вместо абсолютных).

**Тесты:** 219 → **241** (+22).

### Added
- **Sources — `fetch_web_content` возвращает citation (v1.6.1, KI-086-post, Шаг A4)**:
  - `FetchWebContentTool.ExecuteAsync` — `ToolResult.Ok(data, message, sources)`,
    где `sources` = `[WebSourceBuilder.BuildSingle(title, url, text, "web")]`.
  - `label = <title>` страницы (fallback → `url`), `url = запрошенный URL`,
    `snippet` — первые 200 символов очищенного текста.
  - **Не покрыто (осознанно):** если `extractText = false` (HTML-режим) —
    `sources` = `null` (для отладки, snippet не имеет смысла).

### Added
- **Sources — тесты `WebSourceBuilder` (v1.6.1, KI-086-post, Шаг A5)**:
  - **22** теста (`WebSourceBuilderTests.cs`, включая `[Theory]`-наборы):
    - `Build`: null / empty / valid / limit / skip-empty / dedup-by-url /
      normalize-type ×5 / truncate-snippet.
    - `BuildSingle`: empty-url ×3 / valid-url.
    - `BuildLabel`: fallback-chain ×6.
  - **Тесты:** 219 → **241** (+22).

### Added
- **Sources / KI — записи в KNOWN_ISSUES (v1.6.1, KI-086-post, Шаг A5)**:
  - **KI-094** — `wikipedia_search` intermittent timeout (SSL через прокси).
    Documented, план v1.6.2. Fallback `web_search` закрывает UX.
  - **KI-095** — snippet `fetch_web_content` может дублировать label
    (h1 = title на некоторых страницах). Documented, не баг.
  - **KI-096** — GitHub Wiki для проекта (roadmap v1.7+).
    Scope: публичная wiki / RAG-индексация / автосинхронизация.

### Added
- **Sources — `web_search` возвращает citations (v1.6.1, KI-086-post, Шаг A3)**:
  - `WebSearchTool.ExecuteAsync` — `ToolResult.Ok(data, message, sources)`.
  - `sources` — `WebSourceBuilder.Build(retrieved, "web", maxCount: 5)`:
    `type = "web"`, `label = title`, `url` — распакованный DuckDuckGo-редирект,
    `snippet` — HTML-очищенный (≤ 200 символов).
  - **Fallback-эффект:** даже если `wikipedia_search` упал по timeout (KI-064),
    в `web_agent` отработает `web_search` → citations придут в UI.

### Added
- **Sources — проброс citations через агентов (v1.6.1, KI-086-post, Шаг B)**:
  - **Проблема:** LLM в Chat вызывает `web_agent` (не `wikipedia_search` напрямую).
    Внутри `web_agent` — `wikipedia_search` возвращает `ToolResult.Sources`,
    но `SubAgentService` их **отбрасывал**, отдавая наружу только `finalAnswer`.
  - `SubAgentTaskResult.Sources` (`IReadOnlyList<ChatSourceDto>`) — новое поле.
    Собирается в `SubAgentService` из `ToolResult.Sources` всех inner-вызовов
    с дедупликацией по ключу `(Type|DocumentPath|Url|ChunkIndex)`.
  - `AgentToolBase.ExecuteAsync` — проброс `result.Sources` в `ToolResult.Ok(...)`.
  - `ConsultSecondaryAgentTool.ExecuteAsync` — то же.
  - **Тесты:** +3 в `AgentToolBaseTests` (sources / null / empty).

### Added
- **Sources — `wikipedia_search` возвращает citations (v1.6.1, KI-086-post, Шаг A2)**:
  - `WikipediaSearchTool.ExecuteAsync` возвращает `ToolResult.Ok(data, message, sources)`.
  - `sources` — `WebSourceBuilder.Build(retrieved, "wiki", maxCount: limit)`:
    `type = "wiki"`, `label = title`, `url = https://{lang}.wikipedia.org/?curid={pageId}`,
    `snippet` — HTML-очищенный extract из search API (≤ 200 символов).

### Added
- **Sources — Web-tools: `RetrievedWebResult` + `WebSourceBuilder` (v1.6.1, KI-086-post, Шаг A1)**:
  - `RetrievedWebResult` (`DTO/Rag/`) — унифицированный результат веб-поиска
    (`Title`, `Url`, `Snippet`).
  - `WebSourceBuilder` (`Implementation/Rag/`) — статический хелпер:
    `Build` / `BuildSingle` / `BuildLabel`; переиспользует
    `RagSourceBuilder.TruncateSnippet` (≤ 200 символов с «…»).

### Fixed
- **Sources — snippet `fetch_web_content` дублировал `<title>` (v1.6.1, KI-086-post, Шаг A4.fix / A4.fix2)**:
  - **Симптом:** `snippet` начинался с `Example DomainExample DomainThis domain is...`.
  - **Причина:** `FetchWebContentTool` вырезал `<script>`, `<style>`, `<noscript>`,
    но **не** `<head>` (где живёт `<title>`).
  - **Fix:** добавил `//head` в список удаляемых узлов.
  - **Регрессия A4.fix:** удаление `//head` ДО `SelectSingleNode("//title")` →
    `title=null` → `label=url` вместо `Example Domain`.
  - **Fix A4.fix2:** сначала читаем `//title`, потом удаляем узлы.
  - **Остаток** («Example Domain» в snippet) — это `<h1>` в `<body>`, реальный
    контент, не дубль. См. KI-095.

### Fixed
- **Sources — дедупликация схлопывала web/wiki-источники в один (v1.6.1, KI-086-post, Шаг B.fix)**:
  - **Симптом:** `web_agent` возвращает 7 sources (в curl), но в UI-блоке
    «📚 Источники» отображается только **1**.
  - **Причина:** в `ChatStreamService.AddSourcesToAccumulator` (Шаг 3 v1.6.0)
    ключ дедупликации был `(Type|DocumentPath|ChunkIndex)` — **без `Url`**.
    Для RAG-чанков работало, для web/wiki — все источники схлопывались в один.
  - **Fix:**
    - `ChatStreamService.AddSourcesToAccumulator` — ключ `(Type|DocumentPath|Url|ChunkIndex)`
      с `?? string.Empty` (симметрично `SubAgentService`).
    - `WebSourceBuilder.Build` — внутренняя дедупликация по `Url`.

### Fixed
- **Sources — относительные пути в `DocumentChunk.DocumentPath` (v1.6.1, KI-086-post)**:
  - **Симптом:** в `sources[i].documentPath` уходил абсолютный путь
    `C:\Projects\AI\IIChatTools\docs\development\RULES.md`.
  - **Причина:** `DocumentIngestionService.GetTextFromSourceAsync` для
    `SourceType.File` возвращал `request.FilePath` (абсолютный), игнорируя
    `request.Source` (относительный), который передают все три caller'а.
  - **Fix:** `request.Source` (если задан) → `DocumentPath`; fallback на
    `request.FilePath` для обратной совместимости.
  - **Сопутствующий fix:** `ChatAttachmentService.DeleteAsync` — путь для
    `DeleteDocumentAsync` берётся из `entity.StoragePath`.

### Added
- **Sources — тесты `WebSourceBuilder` (v1.6.1, KI-086-post, Шаг A5)**:
  - 15 тестов (`WebSourceBuilderTests.cs`):
    - `Build`: null / empty / valid / limit / skip-empty / dedup-by-url /
      normalize-type ×5 / truncate-snippet.
    - `BuildSingle`: empty-url ×3 / valid-url.
    - `BuildLabel`: fallback-chain ×6.
  - **Тесты:** 219 → **234** (+15).

### Added
- **Sources / KI — записи в KNOWN_ISSUES (v1.6.1, KI-086-post, Шаг A5)**:
  - **KI-094** — `wikipedia_search` intermittent timeout (SSL через прокси).
    Documented, план v1.6.2. Fallback `web_search` закрывает UX.
  - **KI-095** — snippet `fetch_web_content` может дублировать label
    (h1 = title на некоторых страницах). Documented, не баг.
  - **KI-096** — GitHub Wiki для проекта (roadmap v1.7+).
    Scope: публичная wiki / RAG-индексация / автосинхронизация.

### Fixed
- **Sources — snippet `fetch_web_content` дублировал `<title>` (v1.6.1, KI-086-post, Шаг A4.fix)**:
  - **Симптом:** `snippet` начинался с `<title>` (`Example Domain...`),
    label = URL вместо «Example Domain».
  - **Причина:** первая правка удаляла `<head>` (где живёт `<title>`)
    **до** `SelectSingleNode("//title")` — title → null → fallback на url.
  - **Fix:** сначала извлекаем `//title`, потом удаляем `<script>|<style>|<noscript>|<head>`,
    потом `InnerText`. Остаток «Example Domain» в snippet — это `<h1>`
    внутри `<body>` (реальный контент страницы, не дубль `<title>`).

### Added
- **Sources — `fetch_web_content` возвращает citation (v1.6.1, KI-086-post, Шаг A4)**:
  - `FetchWebContentTool.ExecuteAsync` — `ToolResult.Ok(data, message, sources)`,
    где `sources` = `[WebSourceBuilder.BuildSingle(title, url, text, "web")]`.
  - `label = <title>` страницы (fallback → `url`), `url = запрошенный URL`,
    `snippet` — первые 200 символов очищенного текста.
  - **Ожидаемый эффект:** при вызове `fetch_web_content` в UI-блоке
    «📚 Источники» появляется кликабельная ссылка на загруженную страницу.
  - **Не покрыто (осознанно):** если `extractText = false` (HTML-режим) —
    `sources` = `null` (для отладки, snippet не имеет смысла).

### Fixed
- **Sources — дедупликация схлопывала web/wiki-источники в один (v1.6.1, KI-086-post, Шаг B.fix)**:
  - **Симптом:** `web_agent` возвращает 7 sources (в curl), но в UI-блоке
    «📚 Источники» отображается только **1**.
  - **Причина:** в `ChatStreamService.AddSourcesToAccumulator` (Шаг 3 v1.6.0)
    ключ дедупликации был `(Type|DocumentPath|ChunkIndex)` — **без `Url`**.
    Для RAG-чанков работало (у них есть DocumentPath/ChunkIndex), но для
    web/wiki (оба поля `null`) ключ получался одинаковым — `"wiki||"` — и
    все источники схлопывались в один (первый добавленный).
  - **Fix:**
    - `ChatStreamService.AddSourcesToAccumulator` — ключ `(Type|DocumentPath|Url|ChunkIndex)`
      с `?? string.Empty` (симметрично `SubAgentService`, Шаг B).
    - `WebSourceBuilder.Build` — внутренняя дедупликация по `Url`
      (case-insensitive). DuckDuckGo HTML иногда отдаёт 4 `<div class="result">`
      с одинаковой ссылкой → после фикса в списке 1 запись, а не 4.
  - **Побочный эффект:** старые записи в `DocumentChunks` с `Url = null` —
      ключ для RAG-чанков не изменился (`null` → `""`).

### Added
- **Sources — `web_search` возвращает citations (v1.6.1, KI-086-post, Шаг A3)**:
  - `WebSearchTool.ExecuteAsync` — `ToolResult.Ok(data, message, sources)`.
  - `sources` — `WebSourceBuilder.Build(retrieved, "web", maxCount: 5)`:
    `type = "web"`, `label = title`, `url` — распакованный DuckDuckGo-редирект,
    `snippet` — HTML-очищенный (≤ 200 символов).
  - **Fallback-эффект:** даже если `wikipedia_search` упал по timeout (KI-064),
    в `web_agent` отработает `web_search` → citations придут в UI.

### Added
- **Sources — проброс citations через агентов (v1.6.1, KI-086-post, Шаг B)**:
  - **Проблема:** LLM в Chat вызывает `web_agent` (не `wikipedia_search` напрямую).
    Внутри `web_agent` — `wikipedia_search` возвращает `ToolResult.Sources`,
    но `SubAgentService` их **отбрасывал**, отдавая наружу только `finalAnswer`
    (текст). В UI-блоке «📚 Источники» citations от Web-инструментов не появлялись.
  - `SubAgentTaskResult.Sources` (`IReadOnlyList<ChatSourceDto>`) — новое поле.
    Собирается в `SubAgentService` из `ToolResult.Sources` всех inner-вызовов
    с дедупликацией по ключу `(Type|DocumentPath|Url|ChunkIndex)`.
  - `AgentToolBase.ExecuteAsync` — проброс `result.Sources` в `ToolResult.Ok(...)`.
  - `ConsultSecondaryAgentTool.ExecuteAsync` — то же (для `consult_secondary_agent`,
    который не наследуется от `AgentToolBase`).
  - **Ожидаемый эффект:** citations от `wikipedia_search` (после A2) и
    `web_search` / `fetch_web_content` (после A3/A4) появляются в UI-блоке
    «📚 Источники» под ответом ассистента через `ChatStreamService` accumulator
    (Шаг 3 v1.6.0).
  - **Тесты:** +3 в `AgentToolBaseTests` (sources / null / empty). **216 → 219**.

### Added
- **Sources — `wikipedia_search` возвращает citations (v1.6.1, KI-086-post, Шаг A2)**:
  - `WikipediaSearchTool.ExecuteAsync` возвращает `ToolResult.Ok(data, message, sources)`.
  - `sources` — `WebSourceBuilder.Build(retrieved, "wiki", maxCount: limit)`:
    `type = "wiki"`, `label = title`, `url = https://{lang}.wikipedia.org/?curid={pageId}`,
    `snippet` — HTML-очищенный extract из search API (≤ 200 символов).
  - **Ожидаемый эффект:** в UI-блоке «📚 Источники» под ответом ассистента
    появляются кликабельные ссылки на статьи Wikipedia (после вызова `wikipedia_search`).

### Added
- **Sources — Web-tools: `RetrievedWebResult` + `WebSourceBuilder` (v1.6.1, KI-086-post, Шаг A1)**:
  - `RetrievedWebResult` (`DTO/Rag/`) — унифицированный результат веб-поиска
    (`Title`, `Url`, `Snippet`) для трёх Web-инструментов.
  - `WebSourceBuilder` (`Implementation/Rag/`) — статический хелпер:
    - `Build(results, type, maxCount = 5)` — список результатов → список citations
      (`type` = `wiki` / `web` / ...); фильтрует пустые результаты; ограничивает top-N.
    - `BuildSingle(title, url, snippet, type)` — один citation
      (для `fetch_web_content` и одиночных Wikipedia); возвращает `null`,
      если URL пуст.
    - `BuildLabel(title, url)` — приоритет: `title` → `url` → `"(unknown)"`.
    - Переиспользует `RagSourceBuilder.TruncateSnippet` (≤ 200 символов с «…»).
  - Тесты и подключение в tools — следующие шаги (A2-A5).

### Fixed
- **Sources — относительные пути в `DocumentChunk.DocumentPath` (v1.6.1, KI-086-post)**:
  - **Симптом:** в `sources[i].documentPath` (API `/api/tools/execute`, SSE `done`,
    `ChatMessageDto.Sources`) уходил абсолютный путь `C:\Projects\AI\IIChatTools\docs\development\RULES.md`.
    Некрасиво в API + утечка структуры ФС для мульти-юзера.
  - **Причина:** `DocumentIngestionService.GetTextFromSourceAsync` для
    `SourceType.File` возвращал `request.FilePath` (абсолютный) как `documentPath`,
    **игнорируя** `request.Source` (относительный), который передают **все три**
    caller'а (`AdminKnowledgeService`, `ChatAttachmentService`, `WorkspaceIndexService`).
  - **Fix:** использовать `request.Source`, если задан. Fallback на `request.FilePath`
    для обратной совместимости (прямые вызовы без Source).
  - **Сопутствующий fix:** `ChatAttachmentService.DeleteAsync` — путь для
    `DeleteDocumentAsync` теперь берётся из `entity.StoragePath` (относительный),
    а не из абсолютного `fullPath`. Согласовано с новым форматом в БД.
  - **Миграция данных:** старые записи в `DocumentChunks` остаются с абсолютными
    путями. Для обновления — вручную `/admin → База знаний → Обновить индекс проекта`
    (для `project_docs`). Новые записи (upload вложений, workspace-индексация)
    сразу идут с относительными путями.

### Changed
- **Docs — PROMPT_V2.md v2.3 → v2.4 (post-release v1.6.0)**:
  - Шапка: «Актуальный релиз проекта» v1.5.0 → **v1.6.0**.
  - Метрики: 199 → **216** тестов.
  - «Что выпущено» — расширено до v1.6.0, добавлен раздел Sources / citations.
  - Roadmap: **v1.6.0 — Sources ✅ Done**, v1.5.x → **v1.6.x**
    (Web-tools sources, absolute paths fix, KI-091, KI-070, PDF/DOCX, Qdrant).
  - «Известные подводные камни» — добавлены 3 пункта:
    Path.GetFileName (RULES § 4.45), кэш браузера JS/CSS, RAG-tools в `allowedNames`
    (RULES § 4.44).
  - Ссылка на RULES: v1.4.17 → **v1.4.18**.

_(пусто — новые изменения вносятся сюда)._

---

## [1.6.0] — 2026-09-28

**Sources / citations под ответом ассистента (KI-086).** Блок «📚 Источники»
с RAG-чанками, реально использованными LLM: live через SSE `done` и при
F5-загрузке из `ChatMessageDto.Sources`. 13 коммитов, 17 новых тестов
(199 → 216).

**Известные ограничения v1.6.0:** см. `docs/development/RELEASES.md` § 1a.
Ключевое: Web-tools (Wikipedia, WebSearch, FetchWebContent) sources **не отдают**
в v1.6.0 — план на v1.6.1.

### Changed
- **Sources / citations — Шаг 5.3: KI-086 → Fixed, RULES v1.4.18 (v1.6.0, KI-086)**:
  - **`docs/KNOWN_ISSUES.md`:** KI-086 → **Fixed (v1.6.0)**. Дописано тело —
    13 коммитов, 17 новых тестов, все ключевые файлы. Сводка: +`Fixed (v1.6.0) = 1`,
    `Deferred = 3 → 2`.
  - **`docs/development/RULES.md`:**
    - § 7 (KI-выжимка) — добавлена строка KI-086 → Fixed (v1.6.0).
    - § 8 (история) — новая строка **1.4.18**: правила 4.44 (top-level ITool →
      `allowedNames`), 4.45 (`Path.GetFileName` — кросс-платформенные грабли).
    - Шапка: версия 1.4.17 → **1.4.18**.

### Added
- **Sources / citations — Шаг 5.2: README + TESTING (v1.6.0, KI-086)**:
  - **README.md:**
    - «Chat UI» — новый пункт «📚 Источники (Sources)» (live + F5-режим).
    - «RAG / Knowledge Base → Как работает» — пункт 5 про citations.
    - «Ограничения (осознанные, MVP)» — убран пункт про Sources
      (реализовано в v1.6.0).
  - **docs/TESTING.md:**
    - Smoke § 2 — 11-й сценарий (блок «Источники» после ответа + F5).
      Счётчик сценариев: 10 → 11.
    - Full regression § 3.2 — 3.2.12 (search_knowledge_base + блок
      «Источники» + проверка SSE `done` через DevTools).

### Fixed
- **Sources / citations — Шаг 5.1.fix2: кросс-платформенный BuildLabel (v1.6.0, KI-086)**:
  - **Симптом:** CI падал на `ubuntu-latest` (тест `BuildLabel_AbsoluteWindowsPath_ReturnsFileName`),
    локально на Windows — 213/213.
  - **Причина:** `Path.GetFileName` на Linux распознаёт только `/` как
    разделитель пути. Абсолютный Windows-путь `C:\Projects\...\RULES.md`
    на Linux возвращается целиком, а не `RULES.md`.
  - **Fix:** нормализация разделителей (`\` → `/`) перед `Path.GetFileName`
    в `RagSourceBuilder.BuildLabel`. Тот же паттерн, что в `PathHelper`
    (KI-040 — Linux traversal через backslash).
  - **Тесты:** +3 `[Theory]` `BuildLabel_MixedSeparators_ReturnsFileName`
    (backslash / forward slash / смешанные). **213 → 216**.
  - **Урок (RULES § 4.45, добавлено ниже):** при работе с путями
    через `Path.*` — нормализовать разделители, если вход может прийти
    из Windows-контекста в Linux-CI.
    
- **Sources / citations — Шаг 5.1: unit-тесты RagSourceBuilder (v1.6.0, KI-086)**:
  - **14** тестов в `IIChatTools.Tests/UnitTests/Rag/RagSourceBuilderTests.cs`
    (4 `[Fact]` + `3×[Theory]` + 2 `[Fact]` + 3 `[Fact]` + `2×[Theory]`):
    - `Build`: null / empty / valid / multiple (порядок сохранён).
    - `BuildLabel`: null / empty / absolute Windows path / relative path.
    - `TruncateSnippet`: short / exactly-max / long (с «…») / null.
  - **Тесты:** 199 → **213** (+14). В тексте коммита `86ead5d` указано
    «209» — поправка (я не учёл, что `[Theory]` + `[InlineData]`
    разворачивается в N отдельных тестов).
  - Fix CS1574: `<see cref="ChatSourceDto"/>` — добавлен `using IIChatTools.Services.DTO.Chat;`,
    убран префикс `DTO.Chat.` в cref.

- **Sources / citations — Шаг 4: UI-блок «Источники» (v1.6.0, KI-086)**:
  - **`chat.js`** — `renderSourcesBlock(sources)`:
    - рендерит `<ol>` под ответом ассистента с иконкой 📚;
    - формат элемента: `{label} (фрагмент {N}, score {S})`;
    - `chunkIndex + 1` — 1-based (user-friendly);
    - snippet — в `title` (tooltip при hover);
    - `type: "rag"` — простой текст (файл в workspace/репо);
    - `type: "web"` / `"wiki"` — `<a href>` (задел на v1.6.1).
  - **Live-режим:** `finalizeAssistantBubble` вставляет блок из SSE `done` → `data.sources`.
  - **F5-режим:** `renderMessage` рендерит блок из `msg.sources`
    (структурный массив, `ChatMessageDto.Sources` — Шаг 3.5d).
  - **`.resx` (RU + EN):** +2 ключа — `ChatSourcesHeader` («Источники» / «Sources»),
    `ChatSourceChunkMeta` («фрагмент {0}» / «chunk {0}»).
  - **`chat.css`:** `.chat-message-sources` (компактный список с левой полосой),
    `.chat-message-sources-header`, `.chat-message-sources-item`,
    `.chat-message-sources-meta`. На мобильных meta переносится на новую строку.

### Fixed
- **Sources / citations — Шаг 3.5e: warning CS1574 в ChatDtos.cs (v1.6.0, KI-086)**:
  - `<see cref="IIChatTools.API.Controllers.ChatController.GetChatAsync"/>` →
    `<c>ChatController.GetChatAsync</c>`. Services не ссылается на API
    (ADR-001), cref не резолвится. Тот же паттерн, что уже применён для
    `DocumentChunk` → `IVectorStore` (RULES § 4.30).
  - Причина: шаг 3.5d нарушил RULES § 3.6 «0 warnings».

### Fixed
- **Sources / citations — Шаг 3.5d: camelCase + структурный Sources в ChatMessageDto (v1.6.0, KI-086)**:
  - `ChatMessageDto.Sources` (`IReadOnlyList<ChatSourceDto>`) — структурированный
    массив источников (вместо сырого `MetadataJson`). Парсится на бэкенде
    в `ChatController.ParseSources` (Newtonsoft case-insensitive → работает
    и с camelCase, и с PascalCase записями).
  - `ChatStreamService.SerializeSources` — сериализация в **camelCase**
    (единый формат с SSE). Новые записи в БД — camelCase, старые
    (созданные до этого шага) остаются PascalCase, но парсятся без потерь.
  - `ChatMessageDto.MetadataJson` — оставлено для отладки / API-совместимости.
  - **Причина:** `JsonConvert.SerializeObject` по умолчанию писал PascalCase
    (`{"sources":[{"Type":"rag",...}]}`), а фронт (chat.js) ожидает
    `sources[0].type` (camelCase, как в SSE `done`). Без унификации блок
    «Источники» работал бы в live-режиме, но не после F5.

### Fixed
- **Sources / citations — Шаг 3.5c: RAG-tools не попадали в tools[] Chat (v1.6.0, KI-086)**:
  - **Баг:** в `ChatStreamService.StreamAsync` список `allowedNames` строился
    только из `SubAgentRegistry.GetEnabled()` (6 агентов) + `consult_secondary_agent`.
    RAG-инструменты (`search_knowledge_base`, `search_chat_history`,
    `search_workspace`) были зарегистрированы в DI как `ITool`, но **отфильтровывались** —
    Chat видел 7 инструментов вместо ожидаемых 10.
  - **Симптом:** LLM в чате не могла вызвать `search_knowledge_base` (его не было
    в `tools[]`) и выбирала `file_system_agent` как fallback — тот искал
    `RULES.md` в workspace пользователя и не находил.
  - **Fix:** явное добавление 3 RAG-tools в `allowedNames` через константу
    `RagToolNames` (v1.6.0, KI-086, Шаг 3.5c).
  - **Урок (RULES § 4.44, добавлено ниже):** при добавлении нового top-level
    `ITool` в DI — проверить, что он попал в `allowedNames` для Chat.
    `[ToolRegistry]` — это **все** инструменты, а Chat видит только
    **подмножество** (6 агентов + consult + 3 RAG).

### Added
- **Sources / citations — Шаг 3.5b: DefaultSystemPrompt в чате (v1.6.0, KI-086)**:
  - `ChatStreamService.DefaultSystemPrompt` — базовый system prompt проекта,
    добавляется к **каждому** чату (перед RAG-контекстом и пользовательским
    `Chat.SystemPrompt`). Явно указывает модели использовать
    `search_knowledge_base` для вопросов о проекте и `file_system_agent` —
    для файлов пользователя.
  - Причина: усиления `Description` в Шаге 3.5 недостаточно — qwen3-4b
    по-прежнему выбирала `file_system_agent` при вопросе «Что у нас в RULES.md…».
  - Пользовательские `Chat.SystemPrompt` не перезаписываются — идут после
    базового через пустую строку.

### Fixed
- **Sources / citations — Шаг 3.5: LLM не выбирала search_knowledge_base (v1.6.0, KI-086)**:
  - Усилен `Description` у `search_knowledge_base`: явно перечислены имена файлов
    (`README.md`, `RULES.md`, `KNOWN_ISSUES.md`, `CHANGELOG.md`, `RELEASES.md`,
    `ARCHITECTURE.md`, `DESIGN.md`) + явный запрет искать эти файлы через
    `file_system_agent` / `read_file`.
  - В `appsettings*.json` в `Description` агента `file_system_agent` добавлено
    уточнение «В WORKSPACE ПОЛЬЗОВАТЕЛЯ» и предупреждение: для документации
    проекта использовать `search_knowledge_base`.
  - **Причина:** qwen3-4b при вопросе «Что у нас в RULES.MD про yield return?»
    выбирала `file_system_agent` (воспринимала RULES.MD как файл в workspace),
    а не RAG-tool. LLM внутри агента не находила файл → отвечала «не найдено».
  - **Не блокер Шага 3** — Шаг 3 работает корректно; фикс улучшает выбор
    инструмента моделью.

### Added
- **Sources / citations — Шаг 3: агрегация и сохранение (v1.6.0, KI-086)**:
  - `ChatStreamService` накапливает `sources` за весь stream из двух мест:
    (а) auto-inject RAG-чанки (Шаг 6C — `BuildRagContextAsync` теперь
    возвращает их вместе с текстом блока); (б) `tool_result` от RAG-tools.
  - **Дедупликация** по ключу `(Type|DocumentPath|ChunkIndex)`:
    один чанк из auto-inject и tool_result попадёт в список единожды.
  - **Сохранение** в `ChatMessage.MetadataJson` финального assistant-сообщения
    (и в limit-message при исчерпании 5 итераций tool calling'а) —
    JSON-формат `{ "sources": [...] }`. Пустой список → `null` (поле не пишем).
  - **SSE `done`** получил опциональный параметр `sources` — UI рендерит
    «Источники» сразу без F5 (используется в Шаге 4).
  - `ChatStreamEvent.Done` расширен параметром `IReadOnlyList<ChatSourceDto> sources`
    (обратносовместимо: вызовы без sources работают как раньше).
  - Вспомогательные приватные методы `AddSourcesToAccumulator` / `SerializeSources`
    (в `ChatStreamService`).

### Fixed
- **Sources / citations — Шаг 2.fix: проброс Sources до HTTP/SSE (v1.6.0, KI-086)**:
  - `ToolsController.ExecuteAsync`: проброс `result.Sources` в JSON-ответ
    `/api/tools/execute` (поле `sources`). Без этого фикса RAG-tools возвращали
    sources внутри `ToolResult`, но HTTP-ответ терял их.
  - `ChatStreamService`: проброс `toolResult.Sources` в `ChatToolResultDto`
    SSE-события `tool_result` — UI Шага 4 сможет рендерить «Источники» live.
  - `ChatController.GetChatAsync`: проброс `ChatMessage.MetadataJson` в
    `ChatMessageDto` — источники подтянутся при F5-загрузке истории чата.

### Added
- **Sources / citations — Шаг 2: ToolResult.Sources + RAG-tools (v1.6.0, KI-086)**:
  - `ToolResult.Sources` (`IReadOnlyList<ChatSourceDto>?`) — новый опциональный
    параметр в `Ok(data, message, sources)`. Обратносовместимо: 46 существующих
    инструментов продолжают вызывать `Ok(data, message)` — `Sources` остаётся
    `null`.
  - `ChatToolResultDto.Sources` — проброс в SSE-событие `tool_result`.
  - `RagSourceBuilder` (`IIChatTools.Services/Implementation/Rag/`) — хелпер
    сборки `RetrievedChunkDto → ChatSourceDto`: тип `rag`, label = имя файла
    из `DocumentPath`, snippet обрезается до 200 символов. Используется
    всеми тремя RAG-tool (без дублирования логики).
  - 3 RAG-tool (`search_knowledge_base`, `search_chat_history`,
    `search_workspace`) возвращают `ToolResult.Ok(data, message, sources)`.
  - **Ожидаемый эффект:** после выполнения RAG-tool SSE-событие `tool_result`
    несёт `sources` — UI-блок «Источники» появится в Шаге 4.

### Added
- **Sources / citations — Шаг 1: DTO + ChatMessage.MetadataJson (v1.6.0, KI-086)**:
  - `ChatSourceDto` (`IIChatTools.Services/DTO/Chat/ChatSourceDto.cs`):
    единый DTO источника для блока «Источники» под ответом ассистента.
    Поля: `Type` (`rag`/`web`/`wiki`), `Label`, `Url`, `DocumentPath`,
    `ChunkIndex?`, `Score?`, `Snippet`.
  - `ChatMessage.MetadataJson` (`string`, nvarchar(max)) — метаданные
    сообщения. Пока хранит `{ "sources": [...] }`. Поле рассчитано
    на будущие расширения (страница PDF, timestamp и т.п.).
  - `ChatMessageDto.MetadataJson` — проброс в API (сырой JSON,
    разбор — на клиенте).
  - Миграция `AddChatMessageMetadata` (SqlServer).

---

## [1.5.0] — 2026-09-28

**RAG / Knowledge Base — семантический поиск по документам проекта, приложенным
файлам и истории чатов (KI-083).** 4 индекса (`project_docs`, `my_rag_docs`,
`chat_history`, `workspace`), 3 tool для LLM, вложения в чате (📎),
админка `/admin → База знаний`, opt-in Workspace-индекс в `/profile`.

**Тесты:** 191 → **199** (8 новых).

### Added
- **Docs — `docs/TESTING.md` (KI-088, Шаг 8.6, v1.5.0)**:
  - Чек-лист ручной приёмки: Smoke (10 сценариев, ~15 мин),
    Full regression (35 сценариев по Chat / RAG / Multi-Agent / Система, ~40 мин),
    UI/UX (9 проверок), «Что НЕ покрыто автотестами» (10 пунктов).
  - Дефекты → `KNOWN_ISSUES.md` с `KI-XXX`.
  - Раздел «Что НЕ покрыто» явно перечисляет SSE-стриминг, tool calling loop,
    approvals end-to-end, индексацию workspace, RAG с реальным LM Studio,
    rate limiting, retention, Docker-образ — то, что нельзя проверить
    автотестами.

### Changed
- **Release — финализация v1.5.0: KI / RULES / DESIGN / ARCHITECTURE (v1.5.0, KI-083, Шаг 8.5)**:
  - **`docs/KNOWN_ISSUES.md`:**
    - KI-083 (RAG) → **Fixed (v1.5.0)**; в тело дописана сводка Фазы 8 (8.1 → 8.7).
    - **Дубликат `KI-086`** (SQLite locked) переименован в **`KI-093`**
      с пометкой «дубликат KI-085, оставлен для истории». KI-086 остаётся
      за sources/citations (v1.6.0).
    - **Сводка по статусам** обновлена: `Fixed (v1.5.0) = 1`, `In Progress = 0`.
  - **`docs/development/RULES.md`** — версия 1.4.16 → **1.4.17**:
    - § 7 (KI-выжимка) актуализирована после релиза v1.5.0.
    - § 8 (История): новая строка 1.4.17.
  - **`docs/development/v1.5/DESIGN.md`** — статус `Implemented (Фазы 0-7)` →
    **`Implemented (v1.5.0)`** (релиз 2026-09-28).
  - **`docs/development/ARCHITECTURE.md`** — версия 1.4.1 → **1.5.0**:
    - § 3.3 (RAG entities) — убран тег «(в работе)».
    - § 3.5 (миграции) — `AddDocumentChunks` / `AddChatAttachments` — убран «(план)».
    - § 6.3 (RAG-tool) — `Chat видит 10` (было «+3 инструмента (после v1.5)»).
    - Футер: `v1.4.1` → `v1.5.0`.

### Added
- **Docs — README: раздел «RAG / Knowledge Base» (v1.5.0, KI-083, Шаг 8.1)**:
  - «Ключевые возможности»: RAG-пункт обновлён (Фаза 7 закрыта, ссылка на DESIGN).
- **Release — версия 1.4.1 → 1.5.0 (v1.5.0, KI-083, Шаг 8.3)**:
  - `Directory.Build.props`: `<Version>1.5.0</Version>` + `<Copyright>` + шапка-комментарий.
  - `README.md`: заголовок, Copyright, ghcr-теги (`v1.5.0` / `1.5.0` / `1.5` / `1`).
  - **Версия в UI/логах подхватится автоматически** — `AppVersion.Current` читается
    из `AssemblyInformationalVersionAttribute` (MSBuild формирует из `<Version>`).
  - `CHANGELOG.md` — секция `[Unreleased]` пока **не закрыта** (это Шаг 8.4).

### Added
- **Docs — RELEASES.md: § 1a «Известные ограничения v1.5.0» (v1.5.0, KI-083, Шаг 8.2)**:
  - **§ 1a** — отдельная секция про SqlServer-миграции (KI-091): **не применяются**
    в v1.5.0; dev — Sqlite; prod-SqlServer — после v1.5.x. Инструкция «что делать
    / чего не делать».
  - **§ 1** — расширен чек-лист (ссылка на § 1a).
  - **§ 3** — добавлена проверка известных ограничений (Sqlite + RAG-индексы).
  - **§ 10** — добавлены ссылки на RULES § 3.15 и KI-091.
  - Прочие ограничения (Sources/citations, PDF/DOCX, re-ranking, Qdrant,
    KI-088, KI-092) — зафиксированы в § 1a.

### Added
- **Docs — README: раздел «RAG / Knowledge Base» (v1.5.0, KI-083, Шаг 8.1)**:
  - «Ключевые возможности»: RAG-пункт обновлён (Фаза 7 закрыта, ссылка на DESIGN).
  - «API (основные endpoints)»: +14 endpoint'ов + убран дубликат `generate-title`.
  - «Chat UI»: +1 пункт — RAG-вложения (📎 + чипы + auto-inject).
  - **Новый раздел «RAG / Knowledge Base»**: 4 индекса, как работает, форматы
    и лимиты, UI, конфигурация (таблица параметров), ограничения MVP.
  - «Сборка и тесты»: счётчик 188/188 → 199/199.
  
- **RAG / Knowledge Base — Фаза 7 закрыта: Admin KB UI + Profile Workspace UI (v1.5.0, KI-083)**:
  - **Сводка фазы** (7A → 7D.3, всё запушено 2026-09-25 → 2026-09-28):
    - **7A** — backend: DTO + `IAdminKnowledgeService` + `AdminKnowledgeController` (6 endpoints).
    - **7B** — UI админки: 8-я вкладка «База знаний» + `admin-knowledge.js` + модалка чанков.
    - **7C.1** — backend Workspace Index: `IWorkspaceIndexService` + `ProfileWorkspaceController` (5 endpoints).
    - **7C.2** — UI `/profile`: карточка Workspace index + `profile-workspace.js` (polling 2 с).
    - **7D.1** — 5 unit-тестов `WorkspaceIndexServiceTests`.
    - **7D.2** — 3 unit-теста `AdminKnowledgeControllerTests`.
    - **7D.3** — RULES v1.4.16 (§ 4.42, § 4.43).
  - **Тесты:** 191 → **199** (5 + 3 новых).
  - **Осталось:** Фаза 8 — релиз v1.5.0 (README, KNOWN_ISSUES, RELEASES, tag).

### Added
- **Docs — RULES v1.4.16: +2 правила (v1.5.0, KI-083, Шаг 7D.3)**:
  - **§ 4.42** — `AddDbContext` + `UseInMemoryDatabase(name)` в тестах требует
    явного `InMemoryDatabaseRoot` + вычисления имени БД **до** лямбды (лямбда
    выполняется на каждый scope; без этого данные между scope не шарятся).
  - **§ 4.43** — у `JToken` нет `GetValue(string, StringComparison)`; для
    case-insensitive чтения свойства `JObject` — перебирать `obj.Properties()`
    вручную (реальный MVC сериализует в camelCase, `JsonConvert` в тесте — в
    PascalCase).
  - **Уроки** из Шагов 7D.1 (WorkspaceIndexServiceTests) и 7D.2
    (AdminKnowledgeControllerTests).
  - **§ 8 (История):** версия 1.4.16, дата 2026-09-28.

### Added
- **RAG / Knowledge Base — Шаг 7D.2: Unit-тесты AdminKnowledgeController (v1.5.0, KI-083)**:
  - **`AdminKnowledgeControllerTests`** (3 теста):
    - `GetIndexesAsync_ReturnsFourIndexes` — GET `/indexes` → 200 + 4 индекса
      (`project_docs`, `my_rag_docs`, `chat_history`, `workspace`).
    - `ReindexProjectDocsAsync_ReturnsResult` — POST `/reindex` → 200 +
      `data.documentChunksCreated`, `data.durationMs`.
    - `UpdateSettingsAsync_InvalidStrategy_ReturnsFail` — PUT `/settings`
      с невалидным `ChunkingStrategy` → сервис бросает `ArgumentException`
      → контроллер возвращает `success=false` + сообщение.
  - **Fake-зависимость:** `FakeAdminKnowledgeService` (запоминает вызовы,
    настраиваемые результат / исключение для каждого метода).
  - **Паттерн:** прямой вызов контроллера (без `WebApplicationFactory`),
    консистентно с `ChatAttachmentsControllerTests`. Авторизация
    (`[Authorize(Policy = "AdminOnly")]`) — не проверяется (middleware,
    полная HTTP-интеграция — Шаг 8).

### Added
- **RAG / Knowledge Base — Шаг 7D.1: Unit-тесты WorkspaceIndexService (v1.5.0, KI-083)**:
  - **`WorkspaceIndexServiceTests`** (5 тестов):
    - `GetStatusAsync_Disabled_ReturnsEnabledFalse` — флаг не задан →
      `Enabled=false, ChunkCount=0, IsIndexing=false`.
    - `EnableAsync_SetsFlagAndStartsIndexing` — флаг устанавливается, фоновая
      задача обрабатывает 2 файла (.txt + .md); служебные подпапки
      (`chat-attachments/`) и неподдерживаемые расширения (.png) игнорируются.
    - `DisableAsync_ClearsChunksAndFlag` — флаг сброшен + вызов
      `ClearIndexForUserAsync("workspace", 1)`.
    - `ReindexAsync_WhenDisabled_Throws` — `InvalidOperationException`.
    - `ReindexAsync_WhenEnabled_ClearsAndStarts` — очищает чанки + запускает
      второй прогон, `IngestAsync` вызван повторно.
  - **Инфраструктура тестов:** реальный `ServiceCollection` + InMemory-DB +
    реальный `UserSettingsService`; fake — `FakeIngestionService` (расширен
    `ClearForUserCalls` + `NextClearForUserRemoved`), `FakeWorkspaceResolver`.
    Polling-loop до 5 с для ожидания завершения фоновой задачи.
  - **`FakeIngestionService`:** реализация `ClearIndexForUserAsync` теперь
    записывает вызовы в `ClearForUserCalls` (для ассертов).

### Added
- **RAG / Knowledge Base — Шаг 7C.2: Workspace Index UI (v1.5.0, KI-083)**:
  - **`Views/Profile/Index.cshtml`:** карточка `#profile-workspace-card` под
    карточкой «Хранение чатов»:
    - checkbox «Включить семантический поиск по файлам workspace» + hint;
    - строка статуса (Отключён / Индексация… / Включён · N файлов · M чанков);
    - progress-bar (Bootstrap `.progress`) с процентом — только при `isIndexing`;
    - плашка последней ошибки (`lastError`);
    - кнопка «Переиндексировать» (disabled, если не enabled или идёт индексация).
  - **`wwwroot/js/modules/profile-workspace.js`** (новый модуль):
    - `loadStatus()` — `GET /api/profile/workspace-index` при загрузке страницы.
    - `onToggleEnable` — POST `/enable` (без confirm) или POST `/disable`
      (с `confirm()`, т.к. удаляются все чанки).
    - `onReindex` — POST `/reindex` с confirm-свободным путём (сервер сам
      проверяет `enabled`).
    - **Polling 2 с через `setTimeout`** (не `setInterval`) — гарантирует,
      что следующий запрос не стартует до завершения предыдущего. Автостоп,
      как только `isIndexing = false`.
    - Все строки — через `data-*` атрибуты карточки (RULES § 4.17).
  - **`.resx` (RU + EN):** +14 ключей (camelCase): `ProfileWorkspaceSection`,
    `ProfileWorkspaceEnable`, `ProfileWorkspaceEnableHint`,
    `ProfileWorkspaceStatusLabel`, `ProfileWorkspaceStatusDisabled`,
    `ProfileWorkspaceStatusEnabled`, `ProfileWorkspaceStatusIndexing`,
    `ProfileWorkspaceProgress`, `ProfileWorkspaceReindex`,
    `ProfileWorkspaceEnableSuccess`, `ProfileWorkspaceDisableSuccess`,
    `ProfileWorkspaceReindexSuccess`, `ProfileWorkspaceDisableConfirm`,
    `ProfileWorkspaceLastErrorPrefix`.
  - **`site.css`:** `.profile-workspace-progress` (height 14px) +
    `#profile-workspace-error` (жёлтая левая полоса).
  - **KNOWN_ISSUES:** KI-092 (Bootstrap `aria-hidden` warning при закрытии
    вложенных модалок — задокументировано, не блокер).
  - **Фаза 7 (Admin Knowledge Base UI + Profile Workspace UI) закрыта.**

### Added
- **RAG / Knowledge Base — Шаг 7C.1: Workspace Index backend (v1.5.0, KI-083)**:
  - **DTO (`DTO/Rag/`):**
    - `WorkspaceIndexStatusDto` — Enabled, FilesIndexed, ChunkCount, LastIndexedAt,
      IsIndexing, TotalFiles, FilesProcessed, ChunksCreated, LastError.
  - **`IWorkspaceIndexService` + `WorkspaceIndexService` (Singleton):**
    - `GetStatusAsync` — флаг `Workspace.Index.Enabled` из `UserSettings` +
      метрики из `DocumentChunks` (`COUNT`, `DISTINCT DocumentPath`, `MAX(CreatedAt)`)
      + прогресс in-memory.
    - `EnableAsync` — устанавливает флаг enabled=true, запускает фоновую индексацию.
    - `DisableAsync` — сбрасывает флаг, очищает чанки (через новый
      `ClearIndexForUserAsync`), сбрасывает прогресс.
    - `ReindexAsync` — clear + restart (только если enabled; no-op, если
      индексация уже идёт).
    - Фоновая индексация — `Task.Run` + `IServiceScopeFactory.CreateScope()`
      (Singleton не держит Scoped-зависимости).
    - Обход workspace: фильтр по `IRagDocumentParserRegistry.GetAllSupportedExtensions()`,
      пропуск служебных подпапок (`chat-attachments`, `logs`, `bin`, `obj`,
      `.git`, `node_modules`, `.vs`, `.idea`).
    - Прогресс — `ConcurrentDictionary<int, WorkspaceIndexProgress>` (per-user,
      in-memory; не переживает рестарт).
  - **`IDocumentIngestionService.ClearIndexForUserAsync(indexName, userId, ct)`** —
    расширение контракта Шага 4C.1: селективная очистка per-user индекса
    (только чанки указанного пользователя).
  - **`ProfileWorkspaceController`** (5 endpoints, `[ApiController]`, `[Authorize]`):
    - `GET  /api/profile/workspace-index` — статус (initial load);
    - `GET  /api/profile/workspace-index/status` — статус (polling 2 с);
    - `POST /api/profile/workspace-index/enable`;
    - `POST /api/profile/workspace-index/disable`;
    - `POST /api/profile/workspace-index/reindex`.
  - **DI (`Startup.cs`):** `IWorkspaceIndexService` → `WorkspaceIndexService` (Singleton).
  - **Тесты:** запланированы на Шаг 7D (интеграционные).
  - **UI (`/profile`)** — Шаг 7C.2.

### Added
- **RAG / Knowledge Base — Шаг 7B: Admin Knowledge Base UI (v1.5.0, KI-083)**:
  - **`Admin.cshtml`:** 8-я вкладка «База знаний» (`#tab-knowledge` / `#pane-knowledge`) +
    модалка `#ragChunksModal` для просмотра чанков (pagination, delete).
  - **`admin-knowledge.js`** (новый модуль): таблица 4 индексов (`name`/`docCount`/`chunkCount`/`lastIndexedAt`),
    кнопка «Обновить индекс проекта» (POST reindex + toast с количеством чанков и длительностью),
    кнопка «Настройки RAG» (модалка на базе `showModal` из `admin.js` — 8 полей),
    просмотр чанков с пагинацией (20/стр.), удаление отдельного чанка (с `confirm`).
  - **`.resx` (RU + EN):** +29 ключей (camelCase) — `AdminTabKnowledgeBase`,
    `RagReindex*`, `RagIndexColumn*`, `RagChunk*`, `RagSettings*`, `RagViewChunks`,
    `RagNoIndexes`, `RagDeleteChunk*`.
  - **`site.css`:** стили `.rag-chunk-path` (моноширинный, word-break) и
    `.rag-chunk-preview` (однострочный ellipsis, max-width 500px).
  - **Локализация через `data-*`** (RULES § 4.17): 19 атрибутов на `#pane-knowledge`.
  - **Тесты:** UI-тестов нет (как и для других админ-вкладок; smoke — DevTools).
  - **Зависимости:** `IAdminKnowledgeService` (Шаг 7A, commit `2a05ad2`).

### Added
- **RAG / Knowledge Base — Шаг 7A: Admin Knowledge Base backend (v1.5.0, KI-083)**:
  - **DTO (`DTO/Rag/`):**
    - `RagIndexDto` — Name, Description, ChunkCount, DocumentCount, LastIndexedAt.
    - `RagSettingsDto` — ChunkingStrategy, ChunkSize, ChunkOverlap, MinChunkSize,
      DefaultTopK, MinScore, EmbeddingModel, AutoIndexProjectDocs.
    - `RagChunkDto` — Id, IndexName, DocumentPath, ChatId, UserId, ChunkIndex,
      Tokens, TextPreview, CreatedAt.
  - **`IAdminKnowledgeService` + `AdminKnowledgeService` (Scoped):**
    - `GetIndexesAsync` — 4 индекса (`GROUP BY IndexName`, всегда все 4).
    - `ReindexProjectDocsAsync` — чтение `Rag:Ingestion:ProjectDocsPaths`,
      auto-detect project root (по `IIChatTools.sln`), `ForceReindex=true`, `UserId=0`.
    - `GetChunksAsync` — пагинация (1-based, max pageSize=100).
    - `DeleteChunkAsync` — БД + VectorStore (best-effort).
    - `GetSettingsAsync` — override из AppSettings + fallback на appsettings.json.
    - `UpdateSettingsAsync` — валидация + upsert override'ов в `AppSettings` (Category «RAG»).
  - **`AdminKnowledgeController`** (6 endpoints, `[Authorize(Policy = "AdminOnly")]`):
    - `GET /api/admin/knowledge/indexes`
    - `POST /api/admin/knowledge/indexes/project-docs/reindex`
    - `GET /api/admin/knowledge/chunks?index=&page=&pageSize=`
    - `DELETE /api/admin/knowledge/chunks/{id}`
    - `GET /api/admin/knowledge/settings`
    - `PUT /api/admin/knowledge/settings`
  - **DI (`Startup.cs`):** `IAdminKnowledgeService` → `AdminKnowledgeService` (Scoped).
  - **Config (`appsettings.Development.json`):** полная секция `Rag` —
    `Embedding`, `Chunking`, `Ingestion` (включая `ProjectDocsPaths`), `Retrieval`, `Attachments`.
  - **Тесты:** запланированы на Шаг 7D (интеграционные — 3 шт).
  - **Runtime-эффект override'ов** (применение настроек RAG без restart) — отложено
    на отдельный шаг (аналогично `LoadSubAgentOverridesAsync` в Program.cs).

### Added
- **RAG / Knowledge Base — Шаг 6D: UI вложений чата (v1.5.0, KI-083)**:
  - **Chat UI (`Index.cshtml`):**
    - 📎-кнопка в `.chat-input-box` слева от textarea (DeepSeek-style, SVG-icon).
    - `<input type="file" id="chat-file-input" multiple hidden>` — accept: 26 расширений PlainTextParser.
    - Панель `.chat-attachments-bar` над полем ввода: строка «RAG: N чанков [Очистить RAG]» + chips-контейнер.
    - `data-*`-атрибуты для локализации (8 ключей на панели).
  - **JS (`chat.js`):**
    - State: `attachments` (ChatAttachmentDto[]), `ragChunkCount` (число чанков).
    - `loadAttachments()` — GET вложений активного чата (вызывается в `selectChat`).
    - `uploadFiles(files)` — параллельная загрузка (POST multipart, по одному запросу на файл); toast на каждый файл; ошибка одного не отменяет остальные.
    - `deleteAttachment(id)` — DELETE.
    - `clearAttachments()` — POST clear + `confirm()`.
    - `renderAttachmentsBar()` — рендерит chips (📄 {name} ({size} · {N чанков}) [×]) и summary.
    - `formatFileSize(bytes)` — «N KB» / «N.N MB».
    - `formatChipChunks(count)` — «1 чанк» / «N чанков» через `data-label-*`.
    - `enableInput` теперь управляет также 📎-кнопкой.
    - `showEmptyState` сбрасывает `attachments`/`ragChunkCount`.
  - **CSS (`chat.css`):** секция `.chat-attachments-bar` / `.chat-attachment-chip` / `.chat-attachment-chip-remove` / `.chat-input-action-attach` (+ mobile: meta-chip скрыт).
  - **Локализация (12 ключей × 2 = 24 записи):**
    `RagAttachButton`, `RagAttachButtonTooltip`, `RagClearButton`,
    `RagChunksCountOne`, `RagChunksCountMany`,
    `RagUploadSuccess`, `RagUploadTooLarge`, `RagUploadUnsupportedFormat`,
    `RagUploadExceedsMaxFiles`, `RagUploadExceedsTotalSize`,
    `RagClearConfirm`, `RagClearSuccess`.
  - **Фаза 6 (Attached Files) закрыта.** UI: 6A + 6B + 6C + 6D — все Done.

### Added
- **RAG / Knowledge Base — Шаг 6C: auto-inject top-K в system prompt (v1.5.0, KI-083)**:
  - `ChatStreamService` инжектит `AppDbContext` + `IRetrievalService` (оба Scoped).
  - Новый приватный метод `BuildRagContextAsync(chatId, userId, startUserMessageId, ct)`:
    - Быстрая проверка `AnyAsync` — есть ли чанки в `my_rag_docs` для чата (если нет — retrieval не вызывается).
    - Берёт текст последнего user-сообщения из БД (по `startUserMessageId` — совместимо с regenerate).
    - Вызывает `IRetrievalService.SearchAsync(query, "my_rag_docs", topK, chatId, userId)`.
    - Фильтрует по `Rag:Attachments:AutoInjectMinScore` (default 0.35).
    - Формирует блок по DESIGN § 4.7.5: «Ниже — релевантные фрагменты… [1] source (фрагмент N): текст».
    - При ошибке — лог Warning, продолжаем без RAG.
  - `BuildMessagesAsync`: system prompt = RAG-блок + оригинальный `chat.SystemPrompt` (через `\n\n`).
  - Конфиг: `Rag:Attachments:{AutoInjectTopK=5, AutoInjectMinScore=0.35}` (fallback в коде).
  - Тесты: `ChatStreamServiceTests` +3:
    - `StreamAsync_AutoInject_RagContextInSystemPrompt` — чанки есть → блок в system prompt.
    - `StreamAsync_AutoInject_NoRagChunks_NoInjection` — нет чанков → retrieval не вызывается.
    - `StreamAsync_AutoInject_LowScoreFiltered` — score < MinScore → блок не вставляется.
  - Fake `FakeLmStudioClient` расширен: `CapturedMessages` (все входящие JArray).
  - Fake `FakeRetrievalService` добавлен (записывает вызовы, настраиваемый результат).

### Added
- **Docs — синхронизация README/RULES с RAG-прогрессом (v1.5.0, KI-083)**:
  - README: статус тестов 124/124 → **188/188**; Chat видит 10 инструментов (было 7).
  - README: раздел RAG — прогресс фаз 1-5, 6A/6B.
  - RULES 1.4.14 → 1.4.15: § 4.40 (GUID-имена файлов в тестах),
    § 4.41 (`JToken.Value<T>()` без key → CS7036).

### Added
- **RAG / Knowledge Base — Шаг 6B: ChatAttachmentsController + лимиты multipart (v1.5.0, KI-083)**:
  - `ChatAttachmentsController` (4 endpoints):
    - `POST /api/chat/{chatId}/attachments` — загрузка (multipart, `[FromForm] IFormFile`), `[RequestSizeLimit(40 MB)]`.
    - `GET /api/chat/{chatId}/attachments` — список вложений.
    - `DELETE /api/chat/{chatId}/attachments/{attachmentId}` — удалить одно.
    - `POST /api/chat/{chatId}/attachments/clear` — очистить все.
  - Все endpoints: проверка владения чатом через `IChatService.GetChatAsync`.
  - Формат ответов: `{ success, data }` / `{ success: false, message }` — единый с проектом (без ProblemDetails).
  - `Startup.cs`: подняты лимиты multipart до 40 MB
    (`FormOptions.MultipartBodyLengthLimit` + `KestrelServerOptions.Limits.MaxRequestBodySize`).
    Причина: Kestrel по умолчанию 30 MB → 32 MB файл обрезался бы до нашей валидации.
  - Тесты: `ChatAttachmentsControllerTests` (9, unit через fake `IChatAttachmentService`).
  - HTTP-smoke через `WebApplicationFactory` — запланирован на Шаг 8 (полная integration-серия).

### Added
- **RAG / Knowledge Base — Шаг 6A-тесты: ChatAttachmentServiceTests (v1.5.0, KI-083)**:
  - 11 тестов `ChatAttachmentServiceTests`:
    - Upload: ValidFile + ChunksCount, TooLarge, UnsupportedFormat,
      ExceedsFilesPerChat, ExceedsTotalSize, SameHash_Dedup, ChatNotOwned,
      SavesFileInUserWorkspace.
    - Delete: RemovesFileAndChunks (файл + ingestion + БД).
    - ClearForChat: RemovesAll (+ папка chatId удалена).
    - GetForChat: ReturnsOnlyOwnAttachments.
  - Fake: `FakeWorkspaceResolver` (temp-root + `users/{userId}`),
    `FakeIngestionService` (записывает вызовы, настраиваемый результат).
  - Реальные: `RagDocumentParserRegistry` + `PlainTextParser`,
    `AppDbContext` через `TestDbContextFactory`.

### Added
- **RAG / Knowledge Base — Шаг 6A: ChatAttachment entity + сервис (v1.5.0, KI-083)**:
  - Entity `ChatAttachment` (`IIChatTools.Data/Entities/ChatAttachment.cs`).
  - `AppDbContext`: `DbSet<ChatAttachment>` + конфигурация (FK на `Chat` Cascade, 2 индекса).
  - DTO `ChatAttachmentDto` (`Id, ChatId, UserId, FileName, ContentType, SizeBytes, ChunksCount, UploadedAt`).
  - `IChatAttachmentService` — контракт: Upload / GetForChat / Delete / ClearForChat.
  - `ChatAttachmentService` (Scoped):
    - **Upload:** валидация размера/формата/лимитов; SHA256-дедупликация (тот же файл в том же чате → возвращает существующий); сохранение файла в `{UserWorkspace}/chat-attachments/{chatId}/{guid}.ext`; `IngestionAsync(my_rag_docs, ChatId, UserId)`; при ошибке ingestion — attachment остаётся (ChunksCount=0).
    - **Delete:** файл + чанки (`my_rag_docs`) + запись БД (best-effort, не падаем на ошибках).
    - **ClearForChat:** всё то же, но bulk + удаление пустой папки `chat-attachments/{chatId}`.
  - Конфиг: `Rag:Attachments:{MaxFileSizeBytes, MaxFilesPerChat, MaxTotalSizePerChat, StorageSubfolder}`.
  - **DI (`Startup.cs`):** `IChatAttachmentService` → `ChatAttachmentService` (Scoped).
  - Тесты: отдельный Шаг 6A-тесты.
  - Миграция: `AddChatAttachments`.

### Added
- **RAG / Knowledge Base — Шаг 5C: search_workspace (v1.5.0, KI-083)**:
  - `SearchWorkspaceTool : ITool` (`search_workspace`) — read-only.
    - Индекс `workspace` (per-user, **opt-in**).
    - Параметры: `query` (required), `topK` (optional, 1–20), `filePattern` (optional, substring).
    - **UserId обязателен из context**: `UserId ≤ 0` → Fail.
    - **Opt-in check**: читает `Workspace.Index.Enabled` из `IUserSettingsService`
      (per-user). Если `false` → Fail «Workspace index отключён» (без вызова Retrieval).
    - `filePattern` — post-filter по `DocumentPath` (Contains, OrdinalIgnoreCase),
      с over-fetch (`topK × 3`) для компенсации потерь.
  - **DI (`Startup.cs`):** `RegisterRagTools` — 3 RAG-tool (было 2), все Scoped.
  - Тесты: `SearchWorkspaceToolTests` (5) — Name/Params, Disabled_Fail,
    Enabled_Searches, UsesUserIdFromContext, FilePatternFilters.
  - **Фаза 5 (Retrieval + 3 Tools) закрыта. Chat видит 10 инструментов.**

### Added
- **RAG / Knowledge Base — Шаг 5B: search_knowledge_base + search_chat_history (v1.5.0, KI-083)**:
  - `SearchKnowledgeBaseTool : ITool` (`search_knowledge_base`) — read-only.
    - Индекс `project_docs` (глобальный).
    - Параметры: `query` (required), `topK` (optional, 1–20, default 5, clamp).
  - `SearchChatHistoryTool : ITool` (`search_chat_history`) — read-only.
    - Индекс `chat_history` (per-user).
    - Параметры: `query` (required), `topK` (optional), `chatId` (optional).
    - **UserId обязателен из context**: `UserId ≤ 0` → Fail (защита от утечки).
  - Формат ответа (для LLM): `{ query, count, results: [{rank, score, source, chunkIndex, text}] }`.
  - **DI (`Startup.cs`):** новый метод `RegisterRagTools`; 2 инструмента зарегистрированы как Scoped `ITool`.
  - Тесты: `SearchKnowledgeBaseToolTests` (5) + `SearchChatHistoryToolTests` (5) = 10.

### Added
- **RAG / Knowledge Base — Шаг 5A: IRetrievalService + RetrievalService (v1.5.0, KI-083)**:
  - `RetrievedChunkDto` — ChunkId, Text, Score, DocumentPath, ChunkIndex, IndexName, Metadata.
  - `IRetrievalService` — контракт `SearchAsync(query, indexName, topK?, chatId?, userId?, ct)`.
  - `RetrievalService` (Scoped) — оркестрация:
    - `IEmbeddingService.GetEmbeddingAsync(query)` — эмбеддинг запроса.
    - `IVectorStore.Search` с over-fetch (`topK × OverFetchMultiplier`).
    - Фильтрация по метаданным (chatId / userId).
    - Фильтрация по `Rag:Retrieval:MinScore` (default 0.3).
    - Enrichment из БД: `DocumentChunk.Text` + `MetadataJson` (парсится в `Dictionary<string,string>`).
  - Конфиг: `Rag:Retrieval:{DefaultTopK, OverFetchMultiplier, MinScore}`.
  - **DI (`Startup.cs`):** `IRetrievalService` → `RetrievalService` (Scoped).
  - **Fake-helper:** `IIChatTools.Tests.Fakes.FakeEmbeddingService` (детерминированные векторы +
    `SetVector` для точного контроля score) — переиспользуется в 5A/5B/5C.
  - Тесты: `RetrievalServiceTests` (7) — EmptyQuery, NoVectors, TopK/Sorted,
    MinScore, FiltersByChatId, FiltersByUserId, EnrichesMetadataFromDb.

### Added
- **RAG / Knowledge Base — Шаг 4C.3: DocumentIngestionServiceTests (v1.5.0, KI-083)**:
  - 11 тестов: IngestAsync Text/File/UnsupportedFormat/FileTooLarge,
    SameHash Skips/Reindexes, UpdatesVectorStore,
    DeleteDocumentAsync DB/VectorStore, ClearIndexAsync, ChatId-изоляция.
  - Fake-зависимости: `FakeEmbeddingService` (детерминированные 3-компонентные векторы, без HTTP).
  - Реальные: `PlainTextParser`, `RagDocumentParserRegistry`, `RecursiveChunkingStrategy`,
    `ChunkingStrategyResolver`, `InMemoryVectorStore`, `TokenCounter`.
  - `AppDbContext` — InMemory через `TestDbContextFactory`.
  - **Фаза 4 (Parser + Ingestion) закрыта.**

### Added
- **RAG / Knowledge Base — Шаг 4C.2a: MaxFileSizeBytes проверка (v1.5.0, KI-083)**:
  - `DocumentIngestionService`: при `SourceType = File` — проверка размера файла.
  - Лимит: `Rag:Ingestion:MaxFileSizeBytes` (default **32 MB** = `33_554_432` байт).
  - Превышение → `ArgumentException` с указанием фактического размера и лимита.
  - Проверка **до** `ParseAsync` — не тратим ресурсы на парсинг слишком больших файлов.

### Added
- **RAG / Knowledge Base — Шаг 4C.2: DocumentIngestionService (v1.5.0, KI-083)**:
  - `DocumentIngestionService : IDocumentIngestionService` (Scoped).
  - Оркестрация: `parse → chunk → embed → store`.
    - parse: `IRagDocumentParserRegistry.Resolve(filePath)` → `ParseAsync`.
    - chunk: `IChunkingStrategyResolver.Resolve(config)` + `ChunkingOptions` из `Rag:Chunking`.
    - embed: `IEmbeddingService.GetEmbeddingsAsync(chunks)`.
    - store: `INSERT DocumentChunks` (EF Core) + `IVectorStore.Add` по каждому чанку.
  - Идемпотентность: SHA256 содержимого → skip при совпадении hash (без `ForceReindex`).
  - Источники: File (парсинг), Text (inline, path = `text://{hash12}`), Url — NotSupportedException (v1.5.x).
  - `DeleteDocumentAsync` / `ClearIndexAsync` — чистка БД + `IVectorStore`.
  - **DI (`Startup.cs`):** `IDocumentIngestionService` → `DocumentIngestionService` (Scoped).
  - Тесты — Шаг 4C.3.

### Added
- **RAG / Knowledge Base — Шаг 4C.1: IDocumentIngestionService контракты (v1.5.0, KI-083)**:
  - `IngestionSourceType` (enum) — File | Text | Url.
  - `IngestionRequest` DTO — IndexName, FilePath/Text/Url, ChatId?, UserId, SourceType, ForceReindex, Source.
  - `IngestionResultDto` — IndexName, DocumentChunksCreated, TokensTotal, DurationMs,
    DocumentHash, DocumentPath, Skipped, SkipReason.
  - `IDocumentIngestionService` — IngestAsync / DeleteDocumentAsync / ClearIndexAsync.
  - Реализация — Шаг 4C.2, тесты — Шаг 4C.3.

### Added
- **RAG / Knowledge Base — Шаг 4B: IRagDocumentParserRegistry + DI (v1.5.0, KI-083)**:
  - `IRagDocumentParserRegistry` — контракт (Resolve, GetAllSupportedExtensions).
  - `RagDocumentParserRegistry` — Singleton, получает `IEnumerable<IRagDocumentParser>` через DI.
    - `Resolve` — линейный обход (первый матч по расширению побеждает).
    - `GetAllSupportedExtensions` — union расширений всех парсеров (case-insensitive).
  - **DI (`Startup.cs`):** `IRagDocumentParser` → `PlainTextParser` (Singleton);
    `IRagDocumentParserRegistry` → `RagDocumentParserRegistry` (Singleton).
  - Тесты: `RagDocumentParserRegistryTests` (3) — Resolve Txt, UnknownExt, Union.

### Added
- **RAG / Knowledge Base — Шаг 4A: IRagDocumentParser + PlainTextParser (v1.5.0, KI-083)**:
  - DTO `ParsedDocument` (Text, Metadata, OriginalSizeBytes, PageCount).
  - `IRagDocumentParser` — контракт (Name, SupportedExtensions, CanParse, ParseAsync).
  - `PlainTextParser` — 28 расширений (текст, разметка, код, конфиги).
    - **Чтение байтами** + ручной BOM-детект (`EF BB BF` / `FF FE` / `FE FF`).
    - Strict UTF-8 → fallback Windows-1251 (`CodePagesEncodingProvider`).
    - Defensive strip **всех** ведущих `\uFEFF` (BOM-символов).
    - Нормализация переносов `\r\n` / `\r` → `\n`.
    - Strip Markdown frontmatter (`--- ... ---`).
  - Тесты: `PlainTextParserTests` (8) — CanParse T/F, UTF-8, BOM, CP1251, CRLF, MD, empty.

### Added
- **RAG / Knowledge Base — Шаг 3C: Sentence/Fixed стратегии + Resolver (v1.5.0, KI-083)**:
  - `SentenceChunkingStrategy` (Name="sentence") — split по `. ! ? \n`, группировка предложений до `ChunkSize`.
  - `FixedChunkingStrategy` (Name="fixed") — жёсткий разрез по токенам (`Encode` → срез → `Decode`).
  - `IChunkingStrategyResolver` + `ChunkingStrategyResolver` — выбор по имени (case-insensitive, fallback на recursive).
  - `Startup.cs`: 3 стратегии + resolver (Singleton).
  - Тесты: `SentenceChunkingStrategyTests` (2) + `FixedChunkingStrategyTests` (2) + `ChunkingStrategyResolverTests` (2).
- **RAG / Knowledge Base — Шаг 3B: RecursiveChunkingStrategy (v1.5.0, KI-083)**:
  - `RecursiveChunkingStrategy` (Name="recursive", default) — рекурсивное разбиение: `\n\n` → `\n` → `. ` → … → ` ` → token-fallback.
  - `ChunkOverlap` при склейке (`TakeLastTokens`), `MergeSmallChunks` (MinChunkSize), `SplitByTokens` (fallback).
  - Тесты: `RecursiveChunkingStrategyTests` (6).
- **RAG / Knowledge Base — Шаг 3A: IChunkingStrategy + ChunkingOptions (v1.5.0, KI-083)**:
  - DTO `ChunkingOptions` (ChunkSize=500, ChunkOverlap=64, MinChunkSize=100, Separators, Strategy=recursive).
  - `IChunkingStrategy` — контракт (`Name` + `Chunk`).
  - `ITokenCounter.Encode(string) → IReadOnlyList<int>` + `Decode(IReadOnlyList<int>) → string`.
  - `TokenCounter`: реализация через `TiktokenTokenizer.EncodeToIds/Decode`.
  - Тесты: `TokenCounterTests` +2 (`EncodeDecode_RoundTrip`, `EncodeDecode_Empty`).
- **RAG / Knowledge Base — Шаг 2C: InMemoryVectorStore + DI (v1.5.0, KI-083)**:
  - `InMemoryVectorStore : IVectorStore, IDisposable` — Singleton.
  - `ConcurrentDictionary<string, IndexData>` + `Dictionary<int, VectorEntry>` + `ReaderWriterLockSlim`.
  - Add: write-lock, L2-нормализация, overwrite по chunkId. Search: read-lock, dot product.
  - `Startup.cs`: `services.AddSingleton<IVectorStore, InMemoryVectorStore>()`.
  - Тесты: `InMemoryVectorStoreTests` (11, включая `ConcurrentAdds_ThreadSafe` ×1000).
- **RAG / Knowledge Base — Шаг 2B: IVectorStore + VectorMath + DTO (v1.5.0, KI-083)**:
  - DTO `ChunkMetadata` (DocumentChunkId, IndexName, ChatId?, UserId, DocumentPath, ChunkIndex, Source).
  - DTO `VectorSearchResult` (ChunkId, Score, Metadata).
  - `IVectorStore` — интерфейс (Add, Search, Remove, Clear, Count, GetIndexNames).
  - `VectorMath` — helper (L2Normalize, DotProduct, CosineSimilarity). double-аккумулятор в DotProduct, ZeroNormThreshold=1e-12.
  - Тесты: `VectorMathTests` (6).
- **RAG / Knowledge Base — fix: warning CS1574 + KI-091 (v1.5.0, KI-083)**:
  - `DocumentChunk.cs`: `<see cref="IVectorStore"/>` → `<c>IVectorStore</c>` (тип в Services, у Data нет ссылки на Services).
  - KI-091 (Deferred): SqlServer цепочка миграций повреждена (snapshot drift). План пересборки — v1.5.0-rc.
  - DESIGN.md § 5.5 — примечание про SqlServer долг.
- **RAG / Knowledge Base — Шаг 2A: DocumentChunk entity + миграция (v1.5.0, KI-083)**:
  - Entity `DocumentChunk` (`IIChatTools.Data/Entities/DocumentChunk.cs`).
  - Конфигурация в `AppDbContext.OnModelCreating`: 3 индекса + FK на `Chat` (Cascade, nullable).
  - **FK на ApplicationUser НЕТ** (UserId=0 — маркер «глобальный чанк»; см. DESIGN § 5.2).
  - Миграция `AddDocumentChunks` (`IIChatTools.Data/Migrations/SqlServer/`).
  - DESIGN.md § 5.2 — уточнение про FK-связи.

### Added
- **RAG / Knowledge Base — Фаза 1: Embedding Service (v1.5.0, KI-083)**:
  - DTO `EmbeddingResponse` / `EmbeddingData` / `EmbeddingUsage` (`IIChatTools.Services/DTO/LmStudio/`).
  - `ILmStudioClient.GetEmbeddingsAsync(inputs, model, ct)` — POST `/v1/embeddings` (LM Studio).
  - `IEmbeddingService` + `EmbeddingService` (Singleton, батч 64, модель `nomic-embed-text-v1.5`, 768 dim).
  - **DI:** `ILmStudioClient` → **Singleton** (для совместимости с `EmbeddingService`); `IEmbeddingService` → Singleton.
  - `appsettings.json` + `.Development.json` — секция `Rag:Embedding` (Model, Dimensions, BatchSize, TimeoutSeconds, CacheEnabled).
  - **Тесты:** `EmbeddingServiceTests` (6) + `LmStudioEmbeddingTests` (6, mock HTTP).
  - Design: `docs/development/v1.5/DESIGN.md` § 4.1.

### Added
- **Docs — финальная чистка legacy, часть 5 (v1.4.x, финал)**:
  - `scripts/migrations/` (2 legacy `.ps1`) → `scripts/migrations/archive/v1.0.x/`.
  - `scripts/migrations/README.md` — новый.
  - `docs/development/ARCHITECTURE.md` § 3.5 — добавлены команды для создания миграций (SqlServer / Sqlite).

### Added
- **Docs — финальная чистка legacy, часть 4 (v1.4.x)**:
  - `scripts/build/` (8 legacy `.bat`/`.ps1`) → `scripts/build/archive/v1.0.x/`.
  - `scripts/build/README.md` — новый.
  - **PROMPT_V2.md**: восстановлена секция «📐 Формат вывода кода и ответа» (пример через 4-пробельный отступ — не ломает MD).

### Added
- **Docs — финальная чистка legacy, часть 3 (v1.4.x)**:
  - `scripts/diagnostics/` (3 legacy в архив, 3 удалены) → `scripts/diagnostics/archive/v1.0.x/`.
  - `scripts/diagnostics/README.md` — новый.
  - **PROMPT_V2.md v2.1 → v2.2**: секция «Формат вывода кода и ответа» (структура блоков, обязательные секции, правило «не выдумывай»).

### Added
- **Docs — финальная чистка legacy, часть 2 (v1.4.x)**:
  - `scripts/git/` (5 legacy-скриптов + 2 обёртки) → `scripts/git/archive/v1.0.x/`.
  - `scripts/git/README.md` — новый (описание почему каталог пуст).
  - Завершена чистка `scripts/setup/` (удалены 4 одноразовые обёртки).
  - **KI-088** — Planned: `docs/TESTING.md` (чек-лист ручной приёмки, v1.5.0).

### Added
- **Docs — финальная чистка legacy (v1.4.x)**:
  - `docs/guides/` (3 файла) + `docs/testing/` (2 файла) → `docs/development/archive/v1.0.x/guides-testing/`.
  - `scripts/setup/archive/v1.0.x/` — 8 legacy-скриптов (downloader*.ps1, check-packages, create_structure).
  - Удалены: 4 обёртки в `scripts/setup/` (одноразовые `.sh`/`.ps1`).
  - `scripts/setup/README.md` — новый (описание актуальных скриптов + порядок offline-restore).
  - Версия в шапках актуальных скриптов: v1.1.1 → v1.4.1.
  - `archive/README.md` — обновлена таблица структуры.

### Added
- **Docs — актуальный обзор архитектуры (KI-087, v1.5.x)**:
  - `docs/development/ARCHITECTURE.md` — сводный документ (9 разделов): слои, схема БД, DI-lifetime, поток Chat (SSE + approvals + regenerate), 46 инструментов, внешние зависимости, 10 ADR-style решений.
  - `docs/architecture/` (12 legacy-файлов эпохи v1.0 → v1.1) перенесены в `docs/development/archive/v1.0.x/architecture/`.
  - `archive/README.md` — обновлена структура.
  - `PROMPT_V2.md` — усилено правило о форматировании MD (в начале).

### Planned
- **v1.4.x**: KI-047 (PATCH/DELETE fallback), KI-053 (multi-user approvals — крупная), KI-077 (model:null при PUT), KI-082 (модалка-редактор).
- **v1.5.0**: RAG / Knowledge Base (KI-083) — **в работе**. Прогресс:
  - Фаза 0 (DESIGN) — ✅
  - Фаза 1 (Embedding Service) — ✅
  - Фаза 2 (Vector Store + DocumentChunk) — ✅
  - Фаза 3 (Chunking) — ✅
  - Фазы 4-8 (Parser+Ingestion, Retrieval+Tools, Attachments, Admin UI, Tests) — впереди.
  - См. [`docs/development/v1.5/DESIGN.md`](docs/development/v1.5/DESIGN.md).
- **v1.5.0-rc**: KI-091 (SqlServer миграции), KI-088 (TESTING.md).
- **v1.6.0**: KI-086 (вывод источников / citations из tool_result).

---

## [1.4.1] — 2026-09-25

**Chat UX polish + per-user retention + tiktoken-статистика.**

### Added
- **Admin UI — статистика запусков агентов (KI-076)**:
  - `AgentStatsDto` (`AgentName`, `DisplayName`, `TotalRuns`, `SuccessRuns`, `ErrorRuns`, `AvgDurationMs`, `LastRunAt`, `SuccessRate`).
  - `IAgentStatsService` + `AgentStatsService` — агрегация `AuditLogs` с `ToolName LIKE 'agent.%'` (один SQL-запрос с `GroupBy`).
  - `AgentToolBase` → инжектит `IAuditService`, пишет запись при каждом запуске агента (`ToolName = "agent.{AgentName}"`, `Status = Success / Error / Cancelled`, `DurationMs`, `ResultJson`).
  - `AdminAgentsController`: `GET /api/admin/agents/stats`.
  - `Admin.cshtml` → вкладка «Агенты»: карточки статистики (`agent-stats-grid`).
  - `admin-agents.js`: `loadAgentStats()`, `formatDuration`, `formatPercent`, `formatLastRun`.
  - `site.css`: стили `.agent-stat-card`.
  - 2 новых ключа `.resx` (`AgentStatsLastRun`, `AgentStatsEmpty`).
  - 3 теста `AgentStatsServiceTests`.

### Added
- **Chat — расширенная статистика генерации (KI-084a, v1.4.x)**:
  - `ChatMessage` +3 nullable-поля: `DurationMs`, `FirstTokenMs`, `FinishReason`.
  - `ChatStreamService` — Stopwatch (общая длительность) + время до первого delta + последний `finish_reason`.
  - `ChatStreamEvent.Done` +3 параметра (`durationMs`, `firstTokenMs`, `finishReason`).
  - `ChatMessageDto` +3 поля; `ChatController.GetChatAsync` — маппинг.
  - `chat.js` — meta: `123 / 45 токенов · 7.4 tok/s · 9.1 с`.
  - `.resx` (RU + EN): +2 ключа (`ChatMessageTokPerSec`, `ChatMessageDuration`).
  - **Требует миграции** `AddChatMessageStats` (SqlServer) / удаления `.db` (Sqlite, EnsureCreated).
- **Chat — токены в SSE-событиях `start` и `done` (KI-084b, v1.4.x)**:
  - `ChatStreamEvent.Start` — опциональный 3-й параметр `userTokens` (tiktoken).
  - `ChatStreamService`: `start` передаёт токены user-сообщения (из tiktoken или из БД при Regenerate); `done` передаёт **посчитанные** `contextTokens`/`completionTokens` (раньше — сырые `usage`, часто null).
  - Limit-message (когда исчерпаны 5 итераций): токены тоже считаются и сохраняются.
  - `chat.js`: `updateBubbleMeta()` — обновление meta-строки live; `formatTokenMetaText()` — вынесена из `formatTokenMeta`.
  - Токены теперь видны **сразу** во время стрима (не только после F5).
  - `appendUserMessage`/`appendAssistantBubble`: сохраняют `dataset.time` и `dataset.roleLabel` для последующего обновления meta.
- **Chat UI — отображение токенов в meta-строке (KI-049b, v1.4.x)**:
  - `ChatMessageDto`: +2 nullable-поля (`TokensIn`, `TokensOut`).
  - `ChatController.GetChatAsync`: маппинг токенов.
  - `chat.js`: `formatTokenMeta()` — user `15 токенов`, assistant `123 / 45 токенов`.
  - `Chat/Index.cshtml`: `data-label-tokens-user` / `data-label-tokens-assistant`.
  - `.resx` (RU + EN): +2 ключа (`ChatTokensUser`, `ChatTokensAssistant`).
  - Токены отображаются только при наличии (не null).
- **Chat — подсчёт токенов через tiktoken (KI-049a, v1.4.x)**:
  - Пакеты `Microsoft.ML.Tokenizers` + `Microsoft.ML.Tokenizers.Data.Cl100kBase` 1.0.0
    (API + BPE-словарь; одно без другого не работает).
  - `ITokenCounter` + `TokenCounter` (Singleton): `CountTokens(string)`, `CountConversation(messages)`.
  - `ChatStreamService`: заполняет `TokensIn`/`TokensOut` для user и assistant сообщений.
    - Если LM Studio отдала `usage` (non-stream) — использует точные значения.
    - Иначе — считает через tiktoken (приближение ±5-10%).
  - 7 unit-тестов (`TokenCounterTests`).
- **RULES v1.4.4**: § 4.31 (Retention:Enabled в Development — намеренно false).

### Fixed
- **KI-049a (fix)**: добавлен пакет `Microsoft.ML.Tokenizers.Data.Cl100kBase` — без него `TiktokenTokenizer.CreateForEncoding("cl100k_base")` падает с `InvalidOperationException: The tokenizer data file ... could not be loaded`.
- **KI-067-2 (fix)**: `ExecuteDeleteAsync` не поддерживается InMemory-провайдером EF Core 10. Решение: fallback — загрузка сущностей в память + `RemoveRange` + `SaveChangesAsync` (для тестов). Для SqlServer/Sqlite — прежний bulk-DELETE.

### Added
- **Users — UI для per-user retention (KI-067-3, v1.4.x)**:
  - **Backend:**
    - `UserSettingsDto` (`RetentionDays`, `DoNotDelete`, `GlobalRetentionDays`, `MaxRetentionDays`).
    - `AdminController`:
      - `GET /api/admin/users/{id}/settings` — текущие настройки.
      - `PUT /api/admin/users/{id}/settings` — сохранение (валидация 1..MaxDays, `null` = сброс override).
    - `ProfileController`:
      - `GET /profile` — Razor-страница профиля.
      - `GET /api/profile/settings` — свои настройки.
      - `PUT /api/profile/settings` — сохранение своих настроек.
  - **Frontend:**
    - `Views/Profile/Index.cshtml` — карточка «Хранение чатов» с полями.
    - `wwwroot/js/modules/profile.js` — загрузка/сохранение, дизейбл поля при `DoNotDelete`.
    - `admin.js`: кнопка ⚙ в строке пользователя → модалка с полями retention.
    - `_Layout.cshtml`: пункт меню «Профиль» (для залогиненных).
    - `_LoginPartial.cshtml`: displayName — ссылка на `/profile`.
  - **Локализация:** +10 ключей (`ProfileTitle`, `ProfileMenu`, `ProfileRetentionSection`, `ProfileGlobalHint`, `ProfileRetentionDays`, `ProfileRetentionDaysPlaceholder`, `ProfileRetentionDaysHint`, `ProfileDoNotDelete`, `ProfileDoNotDeleteHint`, `ProfileSave`).
- **Users — per-user retention чатов (KI-067-2, v1.4.x)**:
  - `IUserSettingsService.GetAllWithKeyPrefixAsync(prefix)` — получить все настройки по префиксу (для фонового сервиса).
  - `IChatService.DeleteOldChatsAsync(retentionDays, excludedUserIds)` — bulk-DELETE с исключением пользователей.
  - `IChatService.DeleteOldChatsForUserAsync(userId, retentionDays)` — bulk-DELETE для одного пользователя.
  - `ChatRetentionService`: чтение per-user overrides `Chat.RetentionDays` (int) и `Chat.DoNotDelete` (bool).
    - `DoNotDelete = true` — пользователь исключается полностью.
    - `RetentionDays = N` — свой срок хранения (clamp к `Chat:Retention:MaxDays`).
    - `DoNotDelete` побеждает `RetentionDays`.
  - 6 новых тестов (2 — `UserSettingsServiceTests`, 4 — `ChatServiceRetentionTests`).
- **UI — фирменный логотип IIChatTools (KI-081, v1.4.x)**:
  - `wwwroot/images/logo-icon.svg` — иконка (шестиугольник + переплетение), inline SVG.
  - `wwwroot/images/logo-full.svg` — иконка + текст «IIRuChating».
  - `wwwroot/site.webmanifest` — PWA-манифест (иконки 192/512).
  - Favicon: SVG + ICO + PNG 96 + apple-touch-icon + manifest (от realfavicongenerator).
  - Navbar-brand: логотип-иконка + «IIChatTools vX.Y.Z».
  - Hero на главной: логотип с текстом (240px).
  - Login / Register: логотип с текстом (200px).
  - Empty state в `/chat` (нет чата / пустой чат): иконка-логотип (64px) вместо 💬.
  - `site.css`: `.navbar-brand-logo`, `.home-hero-logo`, `.auth-logo`.
  - `chat.css`: `.chat-empty-logo`.

### Changed
- **Chat UI — поле ввода на всю ширину (KI-080, DeepSeek-style, v1.4.x)**:
  - `.input-group` → `.chat-input-box`: закруглённое поле (`border-radius: 1.5rem`) на всю ширину `.chat-main`, светлый фон (`#f6f8fa`).
  - Textarea: прозрачная внутри поля, без бордера, растёт вверх (`max-height: 200px`).
  - Кнопки Send/Stop: круглые SVG-иконки (стрелка вверх / квадрат) внутри поля справа.
  - Focus-ring на `.chat-input-box:focus-within` (убраны Bootstrap `box-shadow` на textarea).
  - Удалён мёртвый CSS KI-061a (правила для `.input-group`).
  - `enableInput`/`setStreamingUI` без изменений (те же `#btn-send`/`#btn-stop`).

### Added
- **Tests — KI-078B (v1.4.x)**: 8 тестов для `SearchUserChatsWithSnippetAsync`:
  - `EmptyQuery_ReturnsEmpty` — семантика «пустой запрос → пусто» (в отличие от `SearchUserChatsAsync`).
  - `ByTitle_ReturnsMatchedFieldTitle` — `MatchedField="title"`, `Snippet=null`.
  - `ByContent_ReturnsSnippetWithOffsets` — `MatchedField="content"`, offsets указывают на совпадение.
  - `PrioritizesTitleOverContent` — при совпадении и в title, и в content приоритет у title.
  - `SnippetHasEllipsis` — «…» по краям, когда совпадение в середине.
  - `DoesNotLeakOtherUsersChats` — фильтр по userId.
  - `NoMatches_ReturnsEmpty` — пустой результат.
  - `TruncatesLongQuery` — обрезка > 200 символов.
  - Всего тестов: **47 → 55**.
- **Chat UI — ⌘K-модалка поиска по чатам (KI-078B-2, v1.4.x)**:
  - Модалка по центру (`Ctrl+K` или SVG 🔍 в collapsed sidebar).
  - Input с debounce 200ms → `GET /api/chats?search=` (backend KI-078B-1).
  - Список результатов: title + относительная дата + snippet с подсветкой `<mark>`.
  - Навигация ↑/↓ (циклическая), Enter — открыть чат, Esc / клик вне / ✕ — закрыть.
  - Защита от гонок fetch (`globalSearchRequestId`).
  - Fix: SVG 🔍 в collapsed sidebar теперь открывает ⌘K-модалку (было — разворот sidebar).
  - Блокировка скролла body на время открытой модалки.
  - Fix: кнопка «Очистить поле» (появляется при непустом input; сброс без закрытия модалки).
  - Локализация RU + EN (6 ключей: `ChatSearchGlobal*`).
- **Chat API — расширенный поиск с snippet (KI-078B-1, v1.4.x)**:
  - `ChatSearchResultDto` (`Id`, `Title`, `Model`, `UpdatedAt`, `MatchedField`, `Snippet`, `SnippetMatchStart`, `SnippetMatchLength`).
  - `IChatService.SearchUserChatsWithSnippetAsync(userId, search)` — поиск с превью совпадения (≈30 символов до + 100 после), позиция совпадения для подсветки.
  - Приоритет: сначала title, потом content. Один чат — один результат.
  - `ChatListItemDto` расширен 4 nullable-полями (`MatchedField`, `Snippet`, `SnippetMatchStart`, `SnippetMatchLength`) — заполняются только при `search`.
  - `ChatController.GetChatsAsync`: при `search != null` — использует расширенный метод.
- **Chat UI — внутричатовый поиск (KI-078A, v1.4.x)**:
  - Панель поиска в правом верхнем углу `.chat-main` (Ctrl+F / кнопка 🔍 в header).
  - Подсветка совпадений `<mark class="chat-search-hit">` в ленте активного чата (user + assistant).
  - Активное совпадение — `.chat-search-hit-active` (жёлтый фон + синяя рамка) + авто-скролл.
  - Счётчик «N из M», кнопки ↑ / ↓ (циклический переход), Enter / Shift+Enter.
  - Закрытие: `Esc`, клик вне панели, кнопка ✕.
  - Debounce 150ms; поиск по `.chat-message-content` (tool-блоки не трогаем).
  - Авто-закрытие при переключении чата.
  - Локализация RU + EN (7 ключей: `ChatSearchInChat*`).
- **Chat UI — collapse/expand sidebar (KI-079, DeepSeek-style, v1.4.x)**:
  - Collapsed-состояние — mini-rail 56px с иконками (SVG): «+» (новый чат), 🔍 (поиск), `«`/`»` (toggle).
  - Кнопка «+ Новый чат» — текст в expanded, SVG `+` в collapsed.
  - Кнопка 🔍 — только в collapsed (в expanded используется input `#chat-search`).
  - Кнопка toggle — одна, работает в обоих состояниях (title меняется по data-атрибутам).
  - Анимация `width .2s ease`; collapsed → `.chat-container.chat-sidebar-collapsed` (`width: 0`).
  - Сохранение состояния в `localStorage["chat.sidebarCollapsed"]`, восстановление при загрузке.
  - Горячая клавиша `Ctrl+B` (toggle, `preventDefault` — не конфликтует с закладками браузера).
  - Mobile (< 768px): collapse отключён, sidebar всегда виден.
  - Локализация RU + EN (`ChatSidebarCollapse` / `ChatSidebarExpand`).

### Planned
- **v1.4.x**: KI-047 (PATCH/DELETE fallback), KI-049 (tokensIn/Out через tiktoken), KI-053 (multi-user approvals), KI-067 (per-user retention), KI-076 (статистика по агентам), KI-077 (model:null при PUT), KI-078 (внутричатовый поиск + подсветка), KI-079 (collapse sidebar), KI-080 (поле ввода на всю ширину).
- **v1.5.0**: RAG (Qdrant / embeddings), Knowledge base.

---

## [1.4.0] — 2026-09-24

**Multi-Agent (KI-052)** — Chat работает через 6 специализированных суб-агентов + универсальный `consult_secondary_agent`.

### Added
- **v1.4.0 Фаза 1 (KI-052)**: реестр специализированных суб-агентов.
  - DTO `SubAgentDescriptor` — Name, DisplayName (RU), Description, SystemPrompt, AllowedTools, Model, MaxSteps, RequiresApprovalByDefault, Disabled.
  - `SubAgentTaskRequest.SystemPromptOverride` + `ModelOverride` (обратносовместимо).
  - `ISubAgentRegistry` + `SubAgentRegistry` (singleton, читает `SubAgents:*` из appsettings.json).
  - 6 агентов в `appsettings.json` (+ в Development): `file_system_agent`, `code_agent`, `web_agent`, `git_agent`, `github_agent`, `planner_agent`.
  - 4 unit-теста (`SubAgentRegistryTests`).
  - `docs/development/v1.4/DESIGN.md` — дизайн-документ фазы.
- **v1.4.0 Фаза 6.1-6.4 (KI-052)**: backend админки агентов.
  - DTO: `AgentListItemDto`, `UpdateAgentRequest`.
  - `AdminAgentsController`: `GET /api/admin/agents`, `PUT /api/admin/agents/{name}`, `POST /api/admin/agents/{name}/reset`.
  - Persist override'ов: JSON в `AppSettings` по ключу `SubAgents.{name}` (upsert).
  - Восстановление при старте: `Program.LoadSubAgentOverridesAsync`.
  - Локализация: 15 ключей в `.resx` (RU + EN).
- **v1.4.0 Фаза 6.5-6.6 (KI-052)**: frontend админки агентов.
  - `Admin.cshtml`: 7-я вкладка «Агенты» (таблица: техническое имя, отображаемое, модель, инструментов, approval, вкл/выкл, действия).
  - `admin-agents.js`: модуль вкладки (загрузка, редактирование, сброс); ленивая инициализация через `shown.bs.tab`.
  - `admin.js`: `showModal` экспортирован для переиспользования.
  - Модалка редактирования: DisplayName, Description, Model, MaxSteps, SystemPrompt, AllowedTools (textarea построчно), RequiresApproval, Disabled.
- **v1.4.0 Фаза 7 (KI-052)**: тесты `AgentToolBase` (7 новых).
  - `AgentToolBaseTests` (Integration): пустой/длинный task, unknown/disabled агент, передача дескриптора в `SubAgentTaskRequest`, clamp maxSteps к 30.
  - Всего тестов: **47/47** (было 40).

### Changed
- **v1.4.0 Фаза 2 (KI-052)**: `ModelOverride` + `SystemPromptOverride` в суб-агентах.
  - `ILmStudioClient.CompleteAsync(..., string model = null)` — перегрузка с явной моделью (обратносовместимо).
  - `LmStudioClient`: если `model` передан — используется он; иначе `LmStudio:Model`.
  - `SubAgentService`: `BuildSystemMessage(maxSteps, overridePrompt)` — `SystemPromptOverride` имеет приоритет над `SubAgent:SystemPrompt`.
  - Основной цикл, финальное резюме и reviewer — все используют `request.ModelOverride`.
- **v1.4.0 Фаза 3 (KI-052)**: `AgentToolBase` + 6 инструментов-обёрток.
  - `AgentToolBase` — единый базовый класс (params, resolve дескриптора, вызов `SubAgentService` через `Func<ISubAgentService>` — разрыв DI-цикла).
  - 6 наследников: `FileSystemAgentTool`, `CodeAgentTool`, `WebAgentTool`, `GitAgentTool`, `GitHubAgentTool`, `PlannerAgentTool`.
  - `RequiresApprovalByDefault` резолвится из дескриптора (`SubAgents:X.RequiresApproval`).
  - `SubAgentService`: расширена защита от рекурсии — запрет `consult_secondary_agent` + любого `*_agent` внутри суб-агента.
- **v1.4.0 Фаза 4 (KI-052)**: усилены system-промпты всех 6 агентов.
  - `web_agent`: обязательно использовать инструменты перед ответом, указывать источник.
  - `file_system_agent`: проверять `list_directory` перед операциями.
  - `code_agent`: обязательно проверять код запуском.
  - `git_agent`: начинать с `git_status`, не делать force-push.
  - `github_agent`: начинать с `gh_auth_status`.
  - `planner_agent`: `save_memory` / `get_system_info` по назначению.
- **v1.4.0 Фаза 5 (KI-052)**: Chat использует `SubAgentRegistry` вместо `SubAgent:DefaultAllowedTools`.
  - `ChatStreamService`: в конструктор добавлен `ISubAgentRegistry`.
  - Список tools = `SubAgentRegistry.GetEnabled()` (6 агентов) + `consult_secondary_agent` (fallback).
  - **Эффект:** Chat видит **7 инструментов** вместо 12. LLM вызывает `file_system_agent` вместо `list_directory` + `read_file` + `save_file` по отдельности.

### Fixed
- **KI-073 (Fixed)**: первый `dotnet test` на Windows после cold build занимал ~44-60 с. Диагностика: **Defender — не главная причина** — виноват **testhost boot** (30+ DLL из API, PuppeteerSharp). Решено: `scripts/setup/configure-defender.ps1` + отключение Dev Drive protection + reboot → **1.4 с**.
- **KI-075**: `ToolRegistry` логировал «инициализирован» на `LogInformation` при каждом scope — спам в проде. Понижено до `LogDebug`.

### Documented
- **KI-076** (Deferred, v1.4.x): статистика по агентам (TotalRuns, AvgTime, SuccessRate) в админке.
- **KI-077** (Documented): `model: null` при PUT агента = «сбросить на default», а не «не менять».
- **KI-078, KI-079, KI-080** (Deferred, v1.4.x): по мотивам DeepSeek — внутричатовый поиск + подсветка, collapse sidebar, поле ввода на всю ширину.

---

## [1.3.1] — 2026-09-24

### Added
- **KI-068**: новый метод `IChatService.SearchUserChatsAsync(userId, search)` — регистронезависимый поиск (кросс-провайдерно). Пустой запрос эквивалентен `GetUserChatsAsync`.

### Changed
- **KI-069**: переименование чата в sidebar теперь через **inline-edit** (ChatGPT-style) вместо `prompt()`. Двойной клик по названию → `<input>`, **Enter** = сохранить, **Esc** = отмена, **blur** = сохранить. Кнопка ✏️ вызывает тот же inline-edit. Backend (`PATCH /api/chats/{id}`) без изменений.
- **KI-068**: поиск по чатам теперь **server-side** (по названию + содержимому сообщений). Запрос `GET /api/chats?search={q}`. Frontend — debounce 300ms, убран клиентский фильтр.

### Fixed
- **KI-068 (регрессия)**: поиск по чатам возвращал неполный список при кириллице. Причина: SQLite `LOWER()` не обрабатывает не-ASCII — `LOWER('Привет') = 'Привет'`, поэтому `LIKE '%прив%'` не матчил. Решение: фильтрация в памяти через `string.Contains(term, StringComparison.OrdinalIgnoreCase)`. См. RULES § 4.26.
- **KI-071**: даты в JSON сериализовались без суффикса `Z` (Sqlite + EF Core возвращают `DateTime` с `Kind=Unspecified`). JS `new Date()` парсил их как local → только что созданные чаты показывались как «3 ч назад» (UTC+3). Решение: `AddNewtonsoftJson(o => o.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc)` + то же для `SseJsonSettings`. См. RULES § 4.27.
- **KI-072**: при активном поиске новый чат попадал в отфильтрованный sidebar. Решение: `createChat()` сбрасывает поиск и перезагружает полный список перед созданием.
- **KI-064**: `wikipedia_search` через корпоративный прокси зависал на ~43 секунды. Решение: явный `Timeout = 15s` + retry 1 раз с задержкой 1s. См. RULES § 4.28.
- **KI-043**: утечка памяти в `RateLimitingMiddleware`. Решение: `Timer` каждые 2 минуты удаляет `LimiterEntry` с `LastUsedUtc` > 5 минут. Middleware реализует `IDisposable`.

---

## [1.3.0] — 2026-09-23

### Added
- **Chat UI (v1.3 Фаза 1.1)**: сущности `Chat` и `ChatMessage` + миграция `AddChatAndChatMessages`. Таблицы `Chats`, `ChatMessages` в БД; индексы `IX_Chats_UserId_UpdatedAt`, `IX_ChatMessages_ChatId_CreatedAt`.
- **ChatService (v1.3 Фаза 1.2)**: `IChatService` + `ChatService` — CRUD чатов и сообщений, проверка владения (`userId`), `DeleteOldChatsAsync` для retention.
- **LM Studio SSE (v1.3 Фаза 1.3)**: `ILmStudioClient.ChatStreamAsync` — `IAsyncEnumerable<ChatCompletionChunk>` для стриминга. DTO `ChatCompletionChunk` (DeltaContent, DeltaReasoning, DeltaToolCall, FinishReason, Usage, IsDone). Парсер SSE-формата (`data: {...}`, `[DONE]`, tool_calls частями). Сохранён `CompleteAsync` для `SubAgentService`.
- **Design doc v1.3**: `docs/development/v1.3/DESIGN.md` — Chat UI (sidebar, SSE, инлайн tool calls), API-контракты, схема данных, план работ.
- **Unit-тесты**: 5 новых на парсинг SSE (`LmStudioSseParseTests`). Всего: **24/24**.
- **ChatController (v1.3 Фаза 1.4)**: REST API чатов — `GET /api/chats` (список), `GET /api/chats/{id}` (детали + история), `POST /api/chats` (создать), `PATCH /api/chats/{id}` (обновить title/model/systemPrompt), `DELETE /api/chats/{id}` (удалить). DTO: `ChatListItemDto`, `ChatDetailDto`, `ChatMessageDto`, `CreateChatRequest`, `UpdateChatRequest`. Проверка владения (`userId` из claims), `[Authorize]` на всех методах.
- **Tool calling infrastructure (v1.3 Фаза 1.6.A)**: `ToolDefinitionsBuilder` — единый построитель JSON-схем инструментов в формате OpenAI Function Calling. Используется Chat (10 инструментов из `SubAgent:DefaultAllowedTools`) и SubAgent. Поддерживает белый список и список исключений.
- **Tool calling — SSE events (v1.3 Фаза 1.6.A.2.1)**: `ChatStreamEvent.ToolCall` и `ChatStreamEvent.ToolResult`. DTO `ChatToolCallDto` (`id`, `name`, `arguments`, `requiresApproval`) и `ChatToolResultDto` (`id`, `name`, `success`, `content`, `message`). Готовит почву для multi-turn loop в `ChatStreamService`.
- **Tool calling — аккумулятор (v1.3 Фаза 1.6.A.2.2)**: `ToolCallsAccumulator` — накопление инкрементальных `delta.tool_calls` из SSE-стрима LM Studio. Складывает `arguments` по `index`, собирает завершённые вызовы формата OpenAI.
- **Tool calling — multi-turn loop (v1.3 Фаза 1.6.A.2.3)**: `ChatStreamService.StreamAsync` расширен до multi-turn цикла (до 5 итераций). Стрим → накопление `tool_calls` → выполнение через `IToolRegistry` → SSE-события `tool_call` / `tool_result` → сохранение в БД → повторный стрим. При `Request.UseTools == false` — старое поведение (без tools). Инструменты с `RequiresApprovalByDefault == true` возвращают `ToolResult.Fail` (полная реализация — Фаза 1.7).
- **Тесты tool calling (v1.3 Фаза 1.6.B)**: 2 новых интеграционных теста в `ChatStreamServiceTests` — успешный multi-turn loop (list_directory → результат → финальный ответ) и approval-инструмент (save_file → ToolResult.Fail без выполнения). FakeLmStudioClient расширен — поддержка итераций. Всего: **29/29**.
- **Approvals в чате — каркас (v1.3 Фаза 1.7.1)**: `ChatApprovalDecision` (enum), `ChatApprovalRequiredDto`, `ChatApprovalResolvedDto`, `IChatApprovalCoordinator`. Дополнены `ChatStreamEvent.ToolApprovalRequired` и `.ToolApprovalResolved`. Готовит почву для Singleton-координатора (Шаг 1.7.2).
- **Approvals в чате — REST-endpoints (v1.3 Фаза 1.7.4)**: `POST /api/chat/approvals/{callId}/approve` и `POST /api/chat/approvals/{callId}/reject`. Вызывают `IChatApprovalCoordinator.ResolveAsync`, будят ожидающий SSE-стрим. `[Authorize]` — только аутентифицированные. Если ожидающий не найден (таймаут) — `{ success: false }`.
- **Approvals в чате (v1.3 Фаза 1.7 — полная реализация)**: 
  - `POST /api/chat/approvals/{callId}/approve` — подтвердить вызов инструмента.
  - `POST /api/chat/approvals/{callId}/reject` — отклонить.
  - SSE-события `tool_approval_required` / `tool_approval_resolved`.
  - Координатор Singleton + cleanup (KI-043 учтён).
  - camelCase в SSE-событиях (единый стиль).
  - Smoke-тест end-to-end: `save_file` → approval_required → reject → tool_result(fail) → финал.
- **Фаза 1.7 v1.3 закрыта.** Backend полностью готов к Chat UI.
- **Chat UI — каркас страницы `/chat` (v1.3 Фаза 2.0.1)**: 
  - `ChatViewController` + Razor-страница `Views/Chat/Index.cshtml`.
  - Layout: sidebar (список чатов) + область сообщений + поле ввода.
  - `wwwroot/css/chat.css` — стили чата.
  - `wwwroot/js/modules/chat.js` — заглушка (инициализация без логики).
  - `_Layout.cshtml`: пункт меню «Чат» + `@RenderSection("Styles")`.
  - **Chat UI — локализация (v1.3 Фаза 2.0.1)**: 16 новых ключей в `SharedResources.resx` (EN) и `SharedResources.ru.resx` (RU). Ключи для страницы `/chat`: `Чат`, `Новый чат`, `Удалить чат`, `Выберите чат или создайте новый`, `Введите сообщение…`, `Отправить`. Резерв для Шагов 2.0.2–2.0.4 (sidebar CRUD, SSE-ошибки). Соответствует правилу 1.14.
- **Chat UI — endpoint моделей (v1.3 Фаза 2.0.2a)**: `GET /api/models` — возвращает список моделей LM Studio (`/v1/models`) + модель по умолчанию из `LmStudio:Model` (всегда первая, `isDefault: true`). Fallback: если LM Studio недоступен — возвращается только default (UI работает в offline-режиме). `[Authorize]`. DTO `ModelInfoDto` (`id`, `name`, `isDefault`). Готовит почву для UI-селектора модели (DESIGN.md § 13.1).
- **Chat UI — sidebar (v1.3 Фаза 2.0.2b)**: `chat.js` реализован полностью для sidebar:
  - `loadChats()` / `loadModels()` — загрузка списка чатов и моделей.
  - `createChat()` — создание (модель по умолчанию из `/api/models`).
  - `selectChat(id)` — переключение с загрузкой истории.
  - `deleteChat(id)` — удаление с подтверждением.
  - `renderChatList()` / `renderMessages()` — рендер sidebar и ленты.
  - Синхронизация активного чата с URL через `history.replaceState` (`?chatId=N`).
  - Относительные даты (`только что`, `5 мин назад`, `вчера`, `22.09`).
  - Минимальное отображение tool calls (серые блоки с именем инструмента).
  - Автоскролл к последнему сообщению.
  - `chat.css` дополнен стилями сообщений и tool-блоков.
- **Chat UI — SSE-стриминг (v1.3 Фаза 2.0.3)**: `chat.js` умеет отправлять сообщения и стримить ответ:
  - `sendMessage()` — `POST /api/chat/stream` с `useTools: true`.
  - `readSseStream()` — чтение SSE через `fetch` + `ReadableStream`.
  - `handleSseEvent()` — обработка `start`/`delta`/`done`/`error`/`tool_call`/`tool_result`.
  - `tool_approval_required` / `tool_approval_resolved` — заглушки (полная обработка — Фаза 2.0.4).
  - Оптимистичное отображение user-пузыря, индикатор «Печатает…» (три точки), потоковая отрисовка delta.
  - Блокировка input/кнопки на время стрима, автоскролл (только если пользователь был внизу).
  - Enter — отправка, Shift+Enter — новая строка, автоувеличение textarea.
  - Локальное обновление `UpdatedAt` в sidebar после `done`.
  - `chat.css`: стили `chat-typing-indicator`, `chat-message-streaming`, `chat-message-error`, `chat-tool-result.success/error`.
- **Chat UI — Approvals (v1.3 Фаза 2.0.4)**: интеграция `_ApprovalModal` в чат.
  - `approvals.js`: рефакторинг — общая логика вынесена в `_showApprovalModal({ approveUrl, rejectUrl, askReason })`.
  - Новый экспорт `requestChatApproval(callId, ...)` — для flow `/api/chat/approvals/{callId}/...` (без prompt причины, см. Q6 Фазы 1.7).
  - `requestApproval(actionId, ...)` — сохранён (обратная совместимость с `/test`).
  - `chat.js`: обработка SSE-события `tool_approval_required` — показ модалки (fire-and-forget, не блокирует SSE reader).
  - `chat.js`: `tool_approval_resolved` — логирование (UI-индикатор — v1.3.x).
  - Стрим продолжается автоматически после решения: `tool_approval_resolved` → `tool_result` → `delta` → `done`.
- **Chat UI — переименование + авто-нумерация (v1.3 Фаза 2.0.5a)**:
  - Кнопка ✏️ в sidebar рядом с 🗑 (появляется при hover / на активном чате).
  - `renameChat(id)` — `prompt()` с текущим именем → `PATCH /api/chats/{id}` (`{ title }`). Пустое имя отклоняется; при совпадении с текущим — no-op.
  - Локальное обновление `state.chats` + синхронизация с header для активного чата.
  - `generateNextChatTitle()` — авто-нумерация «Новый чат», «Новый чат 2», «Новый чат 3», … (учитывает максимальный существующий N).
  - `chat.css`: `.chat-list-item-delete` → `.chat-list-item-action` (общий класс для edit/delete), разные цвета hover (edit — синий, delete — красный).
- **Chat UI — ChatGPT-style скроллинг (v1.3 Фаза 2.0.5b)**:
  - `state.autoScroll` — флаг прилипания к низу ленты.
  - Scroll listener на `#chat-messages` — если пользователь отскроллил > 80px от низа → `autoScroll = false`, вернулся к низу → `autoScroll = true`.
  - `scrollToBottom(force)` — при `force === true` скроллит принудительно и включает autoScroll; иначе — только при `autoScroll === true`.
  - Кнопка «↓ Вниз» (`.chat-scroll-down`) — появляется при `autoScroll === false`, клик → принудительный скролл. Создаётся динамически в JS (Razor не тронут).
  - При переключении чата (`selectChat`) `autoScroll` сбрасывается в `true`.
  - `chat.css`: `.chat-main` → `position: relative`, стили `.chat-scroll-down`.
- **Chat UI — Фаза 2.0 закрыта (v1.3)**: Chat UI реализован end-to-end.
  - `/chat` — Razor-страница с sidebar, лентой сообщений, полем ввода.
  - Sidebar: список чатов (относительные даты), создание, удаление, переименование (✏️), авто-нумерация «Новый чат N».
  - SSE-стриминг: `POST /api/chat/stream`, потоковая отрисовка delta с мигающим курсором.
  - Tool calling: до 5 итераций, SSE-события `tool_call` / `tool_result`.
  - Approvals: модалка `_ApprovalModal` с drag-and-drop, X=Reject, countdown 5 минут.
  - ChatGPT-style скроллинг: кнопка «↓ Вниз», автоскролл отключается при ручной прокрутке вверх.
  - Enter — отправка, Shift+Enter — новая строка, автоувеличение textarea.
  - Синхронизация активного чата с URL (`?chatId=N`).
  - Локализация RU/EN.
  - Скриншоты и подробности — `docs/development/v1.3/DESIGN.md` § 14.
- **Chat UI — Copy message (v1.3 Фаза 2.1.1)**: на каждом сообщении (user/assistant) — кнопка 📋 под контентом. Появляется при hover, копирует текст в clipboard. Inline-фидбек (✅ на 1.5 сек). Работает и во время стриминга (dataset.copyText синхронизируется с накопленным текстом). Fallback — `execCommand('copy')` для HTTP / старых браузеров. Tool-блоки не копируются.
- **Chat UI — Markdown + code blocks (v1.3 Фаза 2.1.4)**: рендеринг ответов LLM через `marked` + `DOMPurify` (bundled локально в `wwwroot/lib/marked/` и `wwwroot/lib/dompurify/`).
  - `renderMarkdown(text)` — `DOMPurify.sanitize(marked.parse(text))` (XSS-защита обязательна; LLM-вывод никогда не вставляется без sanitize).
  - Поддерживаются: `**bold**`, `*italic*`, `## headings`, списки, `code`, ` ```code blocks``` `, ссылки, таблицы.
  - Во время стрима — plain-text (без мерцания); после `done` — финальный Markdown-рендер (`finalizeAssistantBubble`).
  - **Code blocks** — ChatGPT-style: wrapper `.chat-code-block` с шапкой (язык + кнопки Copy/Download).
    - Copy — копирует код, inline ✅ на 1.5 сек.
    - Download — скачивает как файл с расширением по языку (`langToExtension`).
- `chat.css`: стили `.chat-markdown` (GitHub-like) и `.chat-code-block`.
- **Chat UI — подсветка синтаксиса (v1.3 Фаза 2.1.4.1)**: `highlight.js` 11.9.0 (UMD, полный бандл) + тема `github.min.css`, bundled локально в `wwwroot/lib/highlight/`. Подсветка срабатывает в `enhanceCodeBlocks()` через `hljs.highlightElement(code)`. Автодетект языка по `class="language-xxx"` или auto-detect. Фон/паддинг темы нейтрализованы, чтобы wrapper `.chat-code-block` (#f6f8fa) оставался единым — работает только палитра токенов. Fallback: если `hljs` не загружен — code block без подсветки, Copy/Download работают.
- **Chat UI — Regenerate endpoint (v1.3 Фаза 2.1.2.2)**:
  - `ChatStreamRequest.Regenerate` (bool) — если `true`, стрим не сохраняет новое user-сообщение, а удаляет последний assistant-exchange (assistant + tool) и генерирует ответ заново от последнего user.
  - `POST /api/chat/regenerate` — принимает `{ chatId }`, эмулирует `StreamAsync` с `Regenerate = true` и `UseTools = true`. Возвращает SSE-поток, идентичный `/api/chat/stream`.
  - `ChatStreamController`: общая логика вынесена в private `StreamInternalAsync` (используется и `stream`, и `regenerate`).
  - `ChatStreamService.StreamAsync`: при `Regenerate = true` вызывает `DeleteLastAssistantExchangeAsync`, находит последний user-message и стартует общий multi-turn loop.
- **Chat UI — Regenerate (v1.3 Фаза 2.1.2.3)**: кнопка 🔄 в `.chat-message-actions` на **последнем** assistant-сообщении (рядом с 📋). Клик: удаляет последний assistant-пузырь из DOM, вызывает `POST /api/chat/regenerate`, стримит новый ответ через существующий `readSseStream`. Кнопка 🔄 автоматически перевешивается на новый bubble в `finalizeAssistantBubble()` и снимается со всех остальных.
- **Chat UI — Stop (v1.3 Фаза 2.1.3.2)**: кнопка ⏹ рядом с input для прерывания стрима.
  - `#btn-stop` — отдельный элемент в `.input-group`; во время стрима `#btn-send` скрыта, `#btn-stop` показана.
  - `state.abortController` + `fetch(..., { signal })` — при Stop рвётся SSE-соединение.
  - `AbortError` — частичный assistant-пузырь удаляется из DOM; **частичный ответ не сохраняется в БД**.
  - Работает для обычного стрима и Regenerate.
  - **Audit при Stop (2.1.3.3)**: при отмене в `AuditLogs` пишется запись `Status = "Cancelled"`, `ToolName = chat_stream | chat_regenerate`, `DurationMs` (Stopwatch), `ClientIp`. Текст сообщения не логируется (без PII).
- **Chat UI — Retry после Stop (v1.3 Фаза 2.1.3.5, KI-065)**:
  - Кнопка «🔄 Повторить» под последним user-сообщением — если ответ был прерван через Stop или не сгенерирован.
  - Backend: `ChatStreamService.StreamAsync` (Regenerate) — убрана жёсткая проверка `deleted == 0`; best-effort удаление; работает при последнем user (когда ответ не сохранён).
  - Frontend: `regenerateLastMessage({ allowNoAssistant: true })` — переиспользован для Retry; `showRetryOnLastUser()` вызывается из `AbortError`; `renderMessages` показывает кнопку при загрузке истории, если последнее — user.
  - CSS: `.chat-message-action-with-text`, `.chat-message-actions-always-visible`. 
- **Chat UI — Audit Stop (v1.3 Фаза 2.1.3.3)**: `ChatStreamController` инжектит `IAuditService`; при `OperationCanceledException` (клиент нажал Stop) пишется запись в `AuditLogs`:
  - `ToolName`: `chat_stream` (обычный) или `chat_regenerate` (Regenerate).
  - `Status`: `Cancelled`.
  - `ParametersJson`: `{ chatId, regenerate, hasMessage }` — **без текста сообщения** (правило 5.x — без PII).
  - `ResultJson`: `{ reason: "user_stop" }`.
  - `DurationMs`: длительность стрима (Stopwatch).
  - `ClientIp`: из `HttpContext.Connection.RemoteIpAddress`.
- **Chat UI — AI-title frontend (v1.3 Фаза 2.2.1b)**:
  - `maybeGenerateTitle()` — фоновый `POST /api/chats/{id}/generate-title` после **первого** успешного ответа в пустом чате.
  - Триггер: `state.activeChatMessageCount === 0` до отправки + `dataset.streamCompleted === '1'` после `done`.
  - Защита от повторного вызова: title должен матчить `/^Новый чат(\s+\d+)?$/` — иначе пропускаем (пользователь переименовал вручную).
  - Race-safe: `chatIdAtRequest` фиксируется — если пользователь переключится, пока идёт запрос, обновится правильный чат.
  - При успехе: `renderChatList()` + `renderChatHeader()` — без F5.
  - `state.activeChatMessageCount` — новый счётчик для активного чата (init в `selectChat`).
- **Chat UI — селектор модели (v1.3 Фаза 2.2.4, DESIGN § 13.1)**: `<select id="chat-model-select">` в header чата (вместо `<small>`).
  - Заполняется из `state.models` (уже загружено в `initChatPage` → `loadModels`).
  - Формат: `<id>` + ` (по умолчанию)` у `isDefault: true`. Если текущей модели нет в списке — disabled option `<id> (недоступна)`.
  - `onChatModelChanged`: `PATCH /api/chats/{id}` с `{ model }`; оптимистично обновляет `state.activeChat.model` + `state.chats[i].model`; при ошибке — откат.
  - Селектор **disabled во время стрима** (`setStreamingUI`). Попытка смены в этот момент игнорируется.
  - `showEmptyState`: очистка селекта (нет активного чата).
  - Локализация: 4 новых ключа (`Модель`, `по умолчанию`, `недоступна`, `Нет моделей`) — передаются в JS через `data-*`-атрибуты.
  - `chat.css`: `.chat-model-select` (моноширинный, компактный, hover/focus/disabled).
- **Chat retention (v1.3 Фаза 2.2.5, DESIGN § 13.2)**: фоновый `ChatRetentionService` (по образцу `AuditRetentionService`).
  - Удаляет чаты старше `Chat:Retention:DefaultDays` (по умолчанию — 30 дней) через `IChatService.DeleteOldChatsAsync` (bulk DELETE через `ExecuteDeleteAsync`).
  - Первый запуск — через 2 минуты после старта; далее раз в `CleanupIntervalHours` (по умолчанию — 24 ч).
  - Защита от опечаток: `DefaultDays` ограничивается сверху `MaxDays` (365).
  - `Chat:Retention:Enabled = false` — полностью отключает retention (рекомендуется в dev).
  - Метрика Prometheus: `iichattools_chat_cleanup_total` (label `reason="retention"`).
  - Логирование: `Retention чатов: удалено {Count} чатов старше {Cutoff}`.
  - Per-user override — отложено в v1.3.x (**KI-067**).
- **Chat UI — поиск по чатам (v1.3 Фаза 2.2.3)**: input `#chat-search` в sidebar (между кнопкой «+ Новый чат» и списком).
  - Клиентский фильтр по `chat.title` (case-insensitive, `includes`).
  - `state.searchQuery` сохраняется при createChat/deleteChat/renameChat — фильтр не сбрасывается.
  - «Ничего не найдено» — если ни один чат не матчит (локализация через `data-label-no-results`).
  - Поиск по содержимому сообщений — отложено в v1.3.x (**KI-068**).
- **Chat UI — Backend edit user-message (v1.3 Фаза 2.2.6a)**:
  - `IChatService.EditUserMessageAsync(messageId, userId, newContent)` — обновляет `Content` user-сообщения + удаляет все сообщения после (ассистент + tool + последующие) + обновляет `Chat.UpdatedAt`.
  - `POST /api/chat/messages/{id}/edit` — принимает `{ content }`. Возвращает `{ success, data: { messageId, deletedCount } }`.
  - Валидация: не пустое, role == "user", владение через `chat.UserId`. Обобщённый ответ «Сообщение не найдено» — не палим чужие чаты.
  - `ChatStreamController`: + `IChatService` в конструктор.
  - DTO: `EditUserMessageRequest` (request), `EditUserMessageResult` (сервис).
- **Chat UI — inline-edit user-message (v1.3 Фаза 2.2.6b)**:
  - Кнопка ✏️ в `.chat-message-actions` на **user**-сообщениях (при hover).
  - Для свежих сообщений ✏️ добавляется в SSE-событии `start` (после optimistic-рендера id ещё не известен). Для истории (F5) — сразу в `renderMessage`.
  - Клик по ✏️ → `.chat-message-content` заменяется на `<textarea>` + кнопки Save/Cancel.
  - **Enter** = Save, **Shift+Enter** = новая строка, **Esc** = Cancel.
  - Save: `POST /api/chat/messages/{id}/edit` (Фаза 2.2.6a) → обновление UI → удаление DOM-сообщений после → `regenerateLastMessage({ allowNoAssistant: true })`.
  - Edit доступен только в `state.isStreaming === false`.
  - `chat.css`: `.chat-message-edit` (textarea), `.chat-message-edit-actions`.
  
### Changed
- **ChatStreamService (v1.3 Фаза 1.6.A.2.4)**: добавлена зависимость `IWorkspaceResolver`. `ToolExecutionContext.WorkspaceRoot` теперь реально резолвится (было `null`) — FS-инструменты в чате работают.
- **Config (v1.3 Фаза 1.6.A.2.4)**: `SubAgent:DefaultAllowedTools` обновлён — только **read-only** инструменты без approval: `read_file`, `find_files`, `get_file_metadata`, `fuzzy_find_local_files`, `git_status`, `git_log`, `git_diff`, `web_search`, `wikipedia_search`, `get_system_info`. Mutating-инструменты (`list_directory`, `save_file`, `replace_text_in_file`, `execute_command`, `git_add`, …) требуют approval и станут доступны в чате после Фазы 1.7.
- **`appsettings.Development.json`**: добавлен `SubAgent:DefaultAllowedTools` (ранее отсутствовал — все 40 инструментов попадали в чат).
- **`ListDirectoryTool` (v1.3 Фаза 1.6.A.2.4)**: `RequiresApprovalByDefault` → `false`. `list_directory` — read-only операция (возвращает список файлов, ничего не меняет), не требует подтверждения. Добавлен обратно в `SubAgent:DefaultAllowedTools`.
- **Документация (v1.3 Фаза 1.6.B)**: обновлены `docs/KNOWN_ISSUES.md` — KI-051 → Resolved, KI-049 дополнен, добавлены KI-054 (approvals в чате, Фаза 1.7) и KI-055 (tool calling реализован). KI-048 дополнен планом автоматизации git config.
- **Тест `StreamAsync_ToolCalling_RequiresApproval_DoesNotExecute` (v1.3 Фаза 1.7.3)**: обновлён под новую логику — проверяет SSE-события `tool_approval_required` и `tool_approval_resolved` + сообщение «Пользователь отклонил вызов инструмента» (ранее — «Требуется подтверждение»).
- **`.gitignore` (v1.3 Фаза 2.0.2b)**: добавлены правила для `cookies.txt` и `.commit-msg.txt` — артефакты локальных smoke-тестов и here-string-коммитов.
- **Chat UI — sidebar (v1.3 Фаза 2.0.2b)**: кнопка удаления чата добавлена в каждый элемент sidebar (появляется при hover / на активном чате). `<a>` → `<div role="button" tabindex="0">` — позволяет вкладывать `<button>` без семантических конфликтов. Кнопка 🗑 в header (`#btn-delete-chat`) сохранена для активного чата.
- **Chat UI — AI-title cleanup (v1.3 Фаза 2.2.1b)**: удалены диагностические `console.log` из `sendMessage`/`maybeGenerateTitle` после подтверждения работы. Оставлен один информативный `[chat] AI-title: "..."` + `console.warn` на реальные проблемы (нет чата, regex не матчит, response failed).

### Fixed
- **v1.3 Фаза 1.1**: warnings CS0108 (Chat.UpdatedAt скрывает BaseEntity.UpdatedAt — намеренно, `new`), CS0618 (HasName → HasDatabaseName в EF Core 10).
- **v1.3 Фаза 1.3**: warnings CS1574 (cref ArgumentNullException и др. — добавлен `using System;`), CA2024 (reader.EndOfStream в async — заменён на проверку `line == null`).
- **KI-057 (v1.3 Фаза 2.0.2a)**: `GET /api/models` — embedding-модели (например, `text-embedding-nomic-embed-text-v1.5`) исключаются из списка heuristic-фильтром по подстроке `embed`. Причина: эти модели не поддерживают `/v1/chat/completions` и приводят к 400 при выборе в чате.
- **Chat UI — approvals UX (v1.3 Фаза 2.0.4)**: две проблемы, выявленные на smoke-тесте:
- **Модалку нельзя было подвинуть** — добавлен drag-and-drop по `.modal-header` (курсор `move`, `position: fixed` на время drag, сброс стилей при `hidden.bs.modal`).
- **Закрытие крестиком (X) подвешивало UI** — модалка закрывалась, но reject на сервер не отправлялся; SSE-стрим ждал 5 минут, input/btn-send оставались заблокированными. Теперь **X = Reject**: при `hidden.bs.modal` без решения автоматически отправляется reject на сервер (в /chat — без причины, в /test — с reason «Закрыто пользователем»).
- Исправлена утечка listener'ов `hidden.bs.modal` — `{ once: true }`.
- `getOrCreateInstance` вместо `new bootstrap.Modal(...)` — нет warning'ов при повторных открытиях.
- **KI-046 (v1.3 Фаза 2.0.6a + hotfix)**: `MessageCount` в `ChatListItemDto` — реализован подсчёт через `IChatService.GetMessageCountsAsync(userId)` (один SQL `GROUP BY ChatId`). Sidebar отображает meta-строку в формате «<дата> · N сообщ.» через `formatChatMeta(chat)` (пустые чаты — только дата).
- **KI-059**: placeholder `CHANGE_ME_VIA_USER_SECRETS` в `Browser:ProxyServer` трактовался как реальный прокси. Из-за этого `web_search` и `wikipedia_search` падали с `SocketException 11001` (хост `change_me_via_user_secrets:80` неизвестен). Исправлено: helper `IsRealProxyUrl()` в `Startup.cs` и `Program.cs` — placeholder / пустые / невалидные URL игнорируются, прокси не устанавливается.
- **KI-060**: user-сообщения рендерились как Markdown (Фаза 2.1.4). Если пользователь писал ` ``` ` или `**bold**` в обычном тексте, они интерпретировались как разметка → пустой code block с шапкой «без» в user-пузыре. Теперь user-сообщения — plain text (`renderUserContent`), Markdown применяется только к assistant.
- **KI-061**: кнопка «Отправить» перекрывала textarea при одном ряде текста. Фикс: `min-height` для textarea (стандарт Bootstrap `.form-control`), `align-items: stretch` в `.input-group`, `.btn { align-self: stretch; height: auto }`.
- **KI-062**: отсутствовал автофокус на поле ввода при создании/выборе чата. Фикс: `input.focus()` в `selectChat()` (покрывает оба сценария, т.к. `createChat` вызывает `selectChat`).
- **KI-061a**: box-shadow фокуса на textarea перекрывал кнопку «Отправить». Focus-ring перенесён на `.input-group:focus-within`; `textarea:focus` и `.btn:focus` → `box-shadow: none`.
- **KI-066**: кнопка Copy (📋) пропадала под ответом ассистента до F5. Причина: `renderMessageActions('')` в `appendAssistantBubble()` не создаёт кнопку при пустом тексте (правка 2.2.1a). Фикс: `finalizeAssistantBubble()` пересоздаёт `.chat-message-actions` с финальным текстом после Markdown-рендера.

### Security
- Н/Д

### Documented
- **KI-064**: `wikipedia_search` иногда падает с `SSL connection could not be established` (SocketException 10054) через корпоративный прокси — внешняя сетевая проблема, не баг приложения. LLM переключается на `web_search`. Планируется уменьшение таймаута + retry в v1.3.x.

---

## [1.2.0] — 2026-09-21

### Added
- **Docker**: `Dockerfile` (multi-stage, SDK 10.0 → ASP.NET Runtime 10.0), `docker-compose.yml` (prod + опциональный SQL Server), `docker-compose.override.yml` (dev), `.dockerignore`, `.env.example`. Опциональный Chromium через build-arg `INSTALL_BROWSER`.
- **CI/CD**: GitHub Actions — `ci.yml` (build + test + coverage artifacts), `docker-publish.yml` (образ в ghcr.io на main и теги v*), `dependabot.yml` (авто-обновления NuGet + Actions).
- **Health checks**: `/health/live` (liveness), `/health/ready` (БД + Workspace), `/health` (полный JSON-отчёт). Анонимные endpoints.
- **Rate limiting**: собственный `RateLimitingMiddleware` на `System.Threading.RateLimiting`. Политики: per-user (100/min), tools-execute (30/min), auth (5/min). JSON-ответ 429 с `retryAfterSeconds`. `/health/*` без лимита.
- **Prometheus метрики**: `/metrics` (анонимный). Стандартные `http_requests_received_total`, `http_request_duration_seconds`, `dotnet_collection_count_total`, `process_*`. Кастомные: `iichattools_tool_executions_total`, `iichattools_tool_execution_duration_seconds`, `iichattools_pending_approvals`, `iichattools_active_users`, `iichattools_audit_entries_total`, `iichattools_lmstudio_requests_total`, `iichattools_audit_cleanup_total`.
- **Audit retention**: `AuditRetentionService` — фоновый BackgroundService чистит `AuditLogs` (ExecuteDeleteAsync) и `logs/audit/*.jsonl` по retention policy. Настраивается через `Audit:CleanupIntervalHours`, `Audit:DatabaseRetentionDays`, `Audit:FileRetentionDays`.
- **MSSQL migrations**: `IIChatTools.Data/Migrations/SqlServer/20260921102238_InitialSqlServer` — версионирование схемы для SQL Server. `__EFMigrationsHistory` в БД.
- **Скрипты setup**: `scripts/setup/enable-online-restore.ps1` (создаёт `NuGet.Config.online`), `scripts/setup/fill-local-packages.ps1` (наполняет `LocalPackages`).
- **Config**: `NuGet.Config.example.online` — шаблон онлайн-конфига.

### Changed
- **Directory.Build.props**: версии Microsoft-пакетов синхронизированы с ref-pack SDK 10.0.401 → **10.0.12**.
- **`AppMetrics`** перенесён в `IIChatTools.Services/Metrics` — восстановлена слоистость (Data → Services → API).
- **`MetricsRefreshBackgroundService`** перенесён в `IIChatTools.API/BackgroundServices`.
- **`Startup.cs`**: `UseMiddleware<RateLimitingMiddleware>()` вместо `UseRateLimiter()` (см. KI-042).

### Fixed
- **KI-042**: `Microsoft.AspNetCore.RateLimiting` недоступен в SDK 10.0.401 — реализован собственный `RateLimitingMiddleware`.
- **KI-045**: HELP-описания метрик переведены на английский (устранены кракозябры в PowerShell с CP866).
- **Auth**: `[FromForm]` для `LoginAsync` и `RegisterAsync` — устранён HTTP 415 при работе с Razor-формой (побочный эффект `[ApiController]`).

### Documented
- **KI-043**: утечка памяти в `RateLimitingMiddleware` (лимитеры не очищаются при истечении окна) — запланировано на v1.2.x.
- **KI-044**: `iichattools_audit_entries_total` и `iichattools_lmstudio_requests_total` пока не инкрементируются — запланировано на v1.2.x.

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
- **ChatStream DTO (v1.3 Фаза 1.5)**: `ChatStreamRequest` и `ChatStreamEvent` (start/delta/done/error) — типы для SSE-стриминга ответов LLM.
- **ChatStreamService (v1.3 Фаза 1.5)**: `IChatStreamService` + `ChatStreamService` — координатор SSE-стриминга. Сохраняет user message → строит историю (JArray, OpenAI-формат) → вызывает `ILmStudioClient.ChatStreamAsync` → сохраняет assistant message с токенами → возвращает `IAsyncEnumerable<ChatStreamEvent>`.
- **ChatStreamController (v1.3 Фаза 1.5)**: `POST /api/chat/stream` — SSE-эндпоинт. Заголовки `text/event-stream`, `X-Accel-Buffering: no` (обход прокси/nginx), flush после каждого события. События: `start`, `delta`, `done`, `error`.
- **Unit-тесты ChatStreamService (v1.3 Фаза 1.5)**: 3 теста с `FakeLmStudioClient` — успешный стрим (start/delta/done + сохранение), отсутствие чата (error), пустое сообщение (error). Всего: **27/27**.

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
- **KI-050**: rate limiting на HTML-эндпоинтах (`/auth/login`, `/auth/register`) — теперь возвращает **303 redirect** с query `error=ratelimit&retryAfter=N` вместо сырого JSON. `Login.cshtml` / `Register.cshtml` показывают красный banner «Слишком много попыток входа. Попробуйте снова через N секунд». Для API-клиентов (`Accept: application/json`) сохранён прежний 429 JSON.
- **`AuthController`**: удалён атрибут `[ApiController]` — это UI-контроллер (возвращает Razor-Views), и автоматическая модель-валидация возвращала `ProblemDetails` 400 вместо формы с ошибками.

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
