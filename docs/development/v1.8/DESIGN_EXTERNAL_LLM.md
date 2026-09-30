# DESIGN v1.8 — External-LLM Agent

**Версия:** 1.1
**Дата:** 2026-09-30
**Автор:** IIChatTools Team
**Статус:** **✅ Implemented (v1.8.1, 2026-09-30)**
**Связанные KI:** KI-109 (External-LLM Agent — Fixed в v1.8.1), KI-110 (Anthropic/Gemini — Planned, v1.9+), KI-120 (галлюцинация числа — Documented)
**Целевой релиз:** v1.8.1 (OpenAI-совместимые: DeepSeek, OpenAI, Groq, Together AI, Ollama)

---

## § 1. Контекст

### § 1.1. Текущее состояние (после v1.7.1)

- **50 инструментов** в `ToolRegistry`: 40 raw + 6 агентов + 3 RAG + `database_agent`.
- **Chat видит 11 инструментов**: 7 агентов + 3 RAG + `database_agent`.
- **LLM работает только локально** (LM Studio + qwen3-4b).
- **Нет механизма** для обращения к внешним моделям (DeepSeek, OpenAI, Claude, Gemini, ...).

### § 1.2. Industry best practices

| Практика | Источник | Применение в проекте |
|---|---|---|
| **OpenAI-совместимые API** — единый формат `/v1/chat/completions` | OpenAI, DeepSeek, Groq, Together | Один клиент на 5+ провайдеров |
| **Circuit breaker** — защита от зависаний | Resilience4j, Polly | По образцу KI-094 (Wikipedia) |
| **Budget guardrails** — дневные лимиты | OpenAI Usage Limits, Anthropic Admin | `ExternalLlm:DailyBudgetUsd` |
| **Privacy-first** — без PII в логах | GDPR, RULES § 5.x | Только метаданные в AuditLogs |
| **Fail-safe** — не блокировать работу при недоступности | AWS Well-Architected | Fallback на локальную модель |

### § 1.3. Цели v1.8.0

| # | Цель | Метрика |
|---|---|---|
| 1 | LLM может **спросить внешнюю модель** | Tool `ask_external_llm(provider, prompt, include_context?)` |
| 2 | **Оркестратор** — выбор провайдера под задачу | 4 сценария: Fallback / Специализация / Разные знания / Сравнение |
| 3 | **OpenAI-совместимые** провайдеры | DeepSeek, OpenAI, Groq, Together AI, Ollama |
| 4 | **Circuit breaker** — при недоступности | 3 fail подряд → skip на 5 мин |
| 5 | **Дневной лимит** — защита от $1000 за ночь | `DailyBudgetUsd` + `DailyTokensLimit` |
| 6 | **Privacy-first** — без PII в логах | AuditLogs — только метаданные |
| 7 | **`include_context: false`** по умолчанию | Только `prompt`, не весь чат |

### § 1.4. Что НЕ входит в v1.8.0

- **Anthropic Claude** (`/v1/messages`, свой формат) — v1.9+ (KI-110).
- **Google Gemini** (`/v1beta/models`, свой формат) — v1.9+ (KI-110).
- **Streaming с внешних API** — только non-stream (проще, дешевле, без SSE-переделки).
- **Function calling** на внешних API — только обычный prompt → text (без tools).
- **OAuth2 / delegated auth** — только API keys в User Secrets.
- **Per-user API keys** — v1.8.x (по образцу KI-108 для Mail).

---

## § 2. Проблема

### § 2.1. Локальная модель не справляется со сложными задачами

**Примеры:**
- «Напиши production-ready async pipeline с cancellation + retry + circuit breaker».
  → qwen3-4b: галлюцинирует, путает API, забывает про `CancellationToken`.
- «Проанализируй этот SQL — есть ли индексы, которые надо добавить?»
  → qwen3-4b: поверхностный ответ, не учитывает cardinality.
- «Что нового в .NET 10, что вышло после августа 2026?»
  → qwen3-4b: knowledge cutoff — не знает.

**Хочется:** LLM сама решает, когда спросить внешнюю модель — **DeepSeek-V3** для русского/кода,
**GPT-4o-mini** для свежих данных, **Groq** для быстрых итераций.

### § 2.2. Пользователь хочет сравнить ответы

**Пример:** «Сравни, как ChatGPT и DeepSeek решают задачу X».

**Сейчас:** невозможно без ручного копирования в браузер.

**Хочется:** LLM вызывает `ask_external_llm(provider="openai", compare_with="deepseek", prompt="...")`
→ получает 2 ответа → анализирует и выдаёт единый вывод.

### § 2.3. Ограничения существующих инструментов

| Инструмент | Что делает | Чего не хватает |
|---|---|---|
| `web_search` | Ищет в интернете | Не LLM |
| `fetch_web_content` | Загружает страницу | Не LLM |
| `consult_secondary_agent` | Спрашивает **ту же локальную** модель | Не внешний провайдер |
| `execute_command` + `curl` | Теоретически можно | Нет валидации, нет лимитов, нет circuit breaker |

External-LLM Agent заполняет пробел: **«безопасный и бюджетно-ограниченный доступ к внешним LLM»**.

### § 2.4. RU-специфика доступа к провайдерам

| Провайдер | Работает в РФ | Как оплатить | Комментарий |
|---|:---:|---|---|
| **DeepSeek** | ✅ Да | Крипта / ЮMoney через посредников | Лучший вариант для РФ |
| **OpenAI** | ❌ Нет | VPN + зарубежная карта | API тоже требует VPN |
| **Groq** | ⚠️ Через VPN | Free tier (без оплаты) | Ограниченный лимит, но работает |
| **Together AI** | ⚠️ Через VPN | Зарубежная карта | Много OSS-моделей |
| **Ollama** | ✅ Локально | — | Не требует интернета вообще |

**Вывод:** **DeepSeek — рекомендуемый провайдер для РФ** (работает без VPN, оплата доступна).

---

## § 3. Решение

### § 3.1. Один агент `external_llm_agent` (не 5 top-level tools)

По аналогии с `mail_agent`, `database_agent` — Chat видит **один инструмент `external_llm_agent`**,
внутри которого **1 tool** (`ask_external_llm`) + **опциональный helper** (`list_external_providers`).

**Почему так:**
- Chat видит **11 инструментов** в v1.7.1. Добавление 5 провайдеров как top-level tools раздуло бы список до 16 (KI-052).
- Один агент — один system prompt с оркестрацией (Fallback / Специализация / Сравнение).
- `external_llm_agent` наследуется от `AgentToolBase` — получает multi-turn loop, `MaxSteps`.

**Имя агента:** `external_llm_agent`
**DisplayName:** «Агент внешних LLM»
**Модель:** `qwen/qwen3-4b-2507` (агент сам не пишет — делегирует).
**MaxSteps:** 5.
**RequiresApproval:** `false` — не mutating (только читает внешние API).

> **Важно:** `RequiresApprovalByDefault = false`, но `ask_external_llm` **может** тратить деньги ($$$).
> Поэтому — **дневной лимит** (§ 6.4) + **логирование в AuditLogs** (§ 6.5). Approval на каждый вызов
> не нужен — иначе пользователь будет кликать 10 раз подряд.

### § 3.2. Оркестратор — 4 сценария

Агент внутри решает, **как** использовать внешние модели. 4 сценария:

**Сценарий A — Fallback.**
Локальная модель не справляется → пользователь говорит «попробуй по-другому» / «это не точно».
→ LLM вызывает `ask_external_llm(provider="deepseek", prompt="<тот же вопрос>")`.

**Сценарий B — Специализация.**
Для разных задач — разные модели. Примеры:
- **Код** → DeepSeek-Coder / DeepSeek-V3.
- **Русский язык** → DeepSeek (хорошо знает RU).
- **Английский** → OpenAI GPT-4o-mini.
- **Свежие данные** → OpenAI (знания до 2024-10).
- **Быстрые итерации** → Groq (LPU, 500+ tok/s).

**Сценарий C — Разные знания.**
Спросить 2 модели, потому что у них разные тренировочные данные.
Пример: «Спроси и DeepSeek, и OpenAI — что они думают про X».

**Сценарий D — Сравнение.**
Получить 2 ответа → сравнить → выдать единый вывод.
Реализуется через **`compare_with`**: `ask_external_llm(provider="openai", compare_with="deepseek", prompt="...")`
→ возвращает `{ primary: "...", secondary: "...", comparison: "..." }`.

**Промпт агента** (§ 5.2) инструктирует модель: какой сценарий когда применять.

### § 3.3. OpenAI-совместимые провайдеры (v1.8.0)

Все провайдеры ниже используют **один формат** запроса:
`POST /v1/chat/completions` с JSON `{ model, messages, temperature, max_tokens }`.

| Провайдер | BaseUrl | Модель (по умолчанию) | Специфика |
|---|---|---|---|
| **DeepSeek** | `https://api.deepseek.com/v1` | `deepseek-chat` | Работает в РФ без VPN |
| **OpenAI** | `https://api.openai.com/v1` | `gpt-4o-mini` | VPN + зарубежная карта |
| **Groq** | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile` | Free tier, очень быстрый |
| **Together AI** | `https://api.together.xyz/v1` | `meta-llama/Llama-3.3-70B-Instruct-Turbo` | Много OSS-моделей |
| **Ollama (remote)** | `http://<host>:11434/v1` | `qwen3:4b` | Self-hosted, без оплаты |

**Один клиент** — `IExternalLlmClient` — для всех. Провайдер выбирается по `provider` из config.

### § 3.4. `include_context: false` по умолчанию

**Что это значит:**
- **По умолчанию** — в внешнюю модель уходит **только `prompt`** (изолированный запрос).
- **Флаг `include_context: true`** — уходит **последние N сообщений** из чата (для опытных).
- **Не уходит:** system prompt проекта, история tools, RAG-контекст, attachments.

**Почему:**
- **Стоимость:** вся история чата × токены × $ → дорого.
- **Privacy:** PII (адреса, имена, номера) утекает в OpenAI / DeepSeek.
- **GDPR:** пользователь не соглашался на передачу данных третьим лицам.

**При `include_context: true`:**
- Уходит последние 3 пары (user + assistant) — не весь чат.
- Отфильтровываются `tool_result` / `tool_call` (не нужны внешней модели).
- В лог пишется warning: `"External LLM: context included, N messages, PII risk"`.

### § 3.5. Privacy-first (без PII в логах)

**Жёсткие правила (RULES § 5.x):**
- **Не логировать prompt** — может содержать PII / коммерческую тайну.
- **Не логировать ответ** внешней модели.
- **AuditLog.ParametersJson** — только: `{ provider, promptLength, includeContext, compareWith }`.
- **AuditLog.ResultJson** — только: `{ success, promptTokens, completionTokens, costUsd, durationMs }`.
- **ILogger** — только: `LogInformation("External: {Provider} promptLen={Len} tokens={Tokens}")`.
- **Не сохранять** в `ChatMessage.MetadataJson`.

### § 3.6. Обнаружение интернет-доступа (fail-safe)

**Проблема:** интернет может быть недоступен (корпоративный прокси, отключён Wi-Fi, упал DNS).
Без обработки — LLM будет звать `ask_external_llm` 10 раз подряд → все fail → waste времени.

**Решение:** **3 уровня защиты.**

**Уровень 1 — Health check при старте (опционально):**
- Если `ExternalLlm:HealthCheckOnStartup = true` → при старте запрос к `DefaultProvider` (`/v1/models`).
- Если недоступен → **лог Warning** + `external_llm_agent` **не регистрируется** в DI.
- LLM не видит агента → не тратит попытки.

**Уровень 2 — Circuit breaker (в runtime):**
- Per-provider: **3 fail подряд** → skip на **5 минут**.
- В течение skip — `ask_external_llm` возвращает `ToolResult.Fail("Провайдер X недоступен, повторите через N мин.")`.
- По образцу KI-094 (Wikipedia timeout).
- Реализация — `IExternalLlmCircuitBreaker` (Singleton, `ConcurrentDictionary<string, CircuitState>`).

**Уровень 3 — Fail-safe в агенте:**
- Если `ask_external_llm` вернул Fail → агент **не падает**, возвращает `ToolResult.Ok` с сообщением.
- LLM внутри `external_llm_agent` **продолжает** работу: может попробовать другого провайдера.
- Если все недоступны → возвращает `ToolResult.Fail("Все провайдеры недоступны")`.
- Chat (верхний уровень) видит Fail → LLM отвечает пользователю без внешней модели.

**Дополнительно — tool `check_internet_connection`** (опциональный):
- Read-only, без approval.
- Проверяет доступность `DefaultProvider` (легкий HEAD-запрос).
- LLM может сама решить, есть ли интернет, перед тем как звать `ask_external_llm`.

**Итог:** если интернета нет — система **не падает**, а быстро деградирует до локальной модели.

---

## § 4. Архитектура

### § 4.1. Слои (`IIChatTools.Services`)

    IIChatTools.Services/
      ├── DTO/ExternalLlm/
      │   ├── ExternalLlmOptions.cs            — bind из appsettings:ExternalLlm
      │   ├── ExternalProviderOptions.cs       — подсекция Providers[*]
      │   ├── ExternalLlmRequest.cs            — { provider, prompt, includeContext, compareWith, maxTokens, temperature }
      │   ├── ExternalLlmResponse.cs           — { provider, content, promptTokens, completionTokens, costUsd, durationMs }
      │   ├── ExternalLlmComparisonDto.cs      — { primary, secondary, comparison }
      │   └── ProviderHealthStatus.cs          — { provider, available, lastError, lastCheckAt }
      │
      ├── Interfaces/
      │   ├── IExternalLlmClient.cs            — обёртка над OpenAI-совместимыми API
      │   ├── IExternalLlmCircuitBreaker.cs    — circuit breaker
      │   ├── IExternalLlmBudgetTracker.cs     — дневной лимит
      │   └── IExternalProviderRegistry.cs     — реестр провайдеров
      │
      ├── Implementation/ExternalLlm/
      │   ├── ExternalLlmClient.cs             — Singleton, HttpClient per provider
      │   ├── ExternalLlmCircuitBreaker.cs     — Singleton
      │   ├── ExternalLlmBudgetTracker.cs      — Singleton
      │   ├── ExternalProviderRegistry.cs      — Singleton
      │   └── ProviderCostCalculator.cs        — static helper (USD по токенам)
      │
      └── Implementation/Tools/ExternalLlm/
        ├── AskExternalLlmTool.cs              — основной tool
        └── ListExternalProvidersTool.cs       — helper (список провайдеров)
        └── CheckInternetConnectionTool.cs     — опционально (см. § 3.6)

**`IExternalLlmClient`** — Singleton, обёртка:

    public interface IExternalLlmClient
    {
        Task<ExternalLlmResponse> CompleteAsync(
            ExternalLlmRequest request,
            CancellationToken ct = default);

        Task<bool> TestConnectionAsync(
            string providerName,
            CancellationToken ct = default);
    }

**HttpClient per provider:**
- Один `IHttpClientFactory` — общий.
- Именованные клиенты: `ExternalLlm:deepseek`, `ExternalLlm:openai`, `ExternalLlm:groq`, ...
- Timeout 60 сек (большие модели думают долго).
- Retry 1 раз при 5xx/429 (с экспоненциальной задержкой 1s).

**Thread-safety:**
- `IExternalLlmClient` — Singleton, stateless (кроме HttpClient factory).
- `ExternalLlmBudgetTracker` — `ConcurrentDictionary<int, DailyUsage>` (per-user).

### § 4.2. DI-регистрация (`Startup.cs`)

    // 1. Опции
    services.Configure<ExternalLlmOptions>(Configuration.GetSection("ExternalLlm"));

    // 2. Инфраструктура
    services.AddSingleton<IExternalProviderRegistry, ExternalProviderRegistry>();
    services.AddSingleton<IExternalLlmCircuitBreaker, ExternalLlmCircuitBreaker>();
    services.AddSingleton<IExternalLlmBudgetTracker, ExternalLlmBudgetTracker>();
    services.AddSingleton<IExternalLlmClient, ExternalLlmClient>();

    // 3. Tools (только если ExternalLlm:Enabled = true)
    if (Configuration.GetValue<bool>("ExternalLlm:Enabled"))
    {
        RegisterExternalLlmTools(services);
    }

**`RegisterExternalLlmTools(services)`:**

    private static void RegisterExternalLlmTools(IServiceCollection services)
    {
        services.AddScoped<ITool, AskExternalLlmTool>();
        services.AddScoped<ITool, ListExternalProvidersTool>();
        services.AddScoped<ITool, CheckInternetConnectionTool>();
    }

**`external_llm_agent`** — в `SubAgents` секции `appsettings.json` (см. § 5.2).

### § 4.3. Tools (2-3 штуки внутри агента)

| Tool | Approval | Назначение | Параметры |
|---|:---:|---|---|
| `ask_external_llm` | ❌ | Спросить внешнюю модель | `provider`, `prompt`, `include_context?=false`, `compare_with?`, `max_tokens?`, `temperature?` |
| `list_external_providers` | ❌ | Список провайдеров + статус | — |
| `check_internet_connection` | ❌ | Проверить доступность | `provider?` (по умолчанию `DefaultProvider`) |

**Формат ответа `ask_external_llm` (без compare):**

    {
      "success": true,
      "data": {
        "provider": "deepseek",
        "content": "Ответ внешней модели...",
        "promptTokens": 245,
        "completionTokens": 512,
        "costUsd": 0.0008,
        "durationMs": 3200
      }
    }

**Формат ответа `ask_external_llm` (с compare):**

    {
      "success": true,
      "data": {
        "primary": {
          "provider": "openai",
          "content": "...",
          "promptTokens": 245,
          "completionTokens": 512,
          "costUsd": 0.0032,
          "durationMs": 2800
        },
        "secondary": {
          "provider": "deepseek",
          "content": "...",
          "promptTokens": 245,
          "completionTokens": 480,
          "costUsd": 0.0008,
          "durationMs": 3100
        },
        "totalCostUsd": 0.004
      }
    }

**Формат ответа `list_external_providers`:**

    {
      "success": true,
      "data": {
        "default": "deepseek",
        "providers": [
          {
            "name": "deepseek",
            "displayName": "DeepSeek",
            "available": true,
            "costPer1kInputUsd": 0.00014,
            "costPer1kOutputUsd": 0.00028
          },
          {
            "name": "openai",
            "displayName": "OpenAI",
            "available": false,
            "lastError": "Circuit breaker open (retry in 3m)",
            "costPer1kInputUsd": 0.00015,
            "costPer1kOutputUsd": 0.0006
          }
        ]
      }
    }

---

## § 5. Конфигурация

### § 5.1. `appsettings.json` — секция `ExternalLlm`

    "ExternalLlm": {
      "Enabled": false,
      "DefaultProvider": "deepseek",
      "HealthCheckOnStartup": true,

      "DailyBudgetUsd": 5.0,
      "DailyTokensLimit": 500000,

      "CircuitBreaker": {
        "FailureThreshold": 3,
        "BreakDurationSeconds": 300
      },

      "Providers": {
        "deepseek": {
          "DisplayName": "DeepSeek",
          "BaseUrl": "https://api.deepseek.com/v1",
          "Model": "deepseek-chat",
          "ApiKeySecretName": "ExternalLlm:DeepSeek:ApiKey",
          "CostPer1kInputUsd": 0.00014,
          "CostPer1kOutputUsd": 0.00028,
          "MaxTokens": 8192,
          "TimeoutSeconds": 60
        },
        "openai": {
          "DisplayName": "OpenAI",
          "BaseUrl": "https://api.openai.com/v1",
          "Model": "gpt-4o-mini",
          "ApiKeySecretName": "ExternalLlm:OpenAI:ApiKey",
          "CostPer1kInputUsd": 0.00015,
          "CostPer1kOutputUsd": 0.0006,
          "MaxTokens": 4096,
          "TimeoutSeconds": 60
        },
        "groq": {
          "DisplayName": "Groq",
          "BaseUrl": "https://api.groq.com/openai/v1",
          "Model": "llama-3.3-70b-versatile",
          "ApiKeySecretName": "ExternalLlm:Groq:ApiKey",
          "CostPer1kInputUsd": 0.0,
          "CostPer1kOutputUsd": 0.0,
          "MaxTokens": 8192,
          "TimeoutSeconds": 30
        },
        "together": {
          "DisplayName": "Together AI",
          "BaseUrl": "https://api.together.xyz/v1",
          "Model": "meta-llama/Llama-3.3-70B-Instruct-Turbo",
          "ApiKeySecretName": "ExternalLlm:Together:ApiKey",
          "CostPer1kInputUsd": 0.00088,
          "CostPer1kOutputUsd": 0.00088,
          "MaxTokens": 8192,
          "TimeoutSeconds": 60
        },
        "ollama": {
          "DisplayName": "Ollama (remote)",
          "BaseUrl": "http://localhost:11434/v1",
          "Model": "qwen3:4b",
          "ApiKeySecretName": "ExternalLlm:Ollama:ApiKey",
          "CostPer1kInputUsd": 0.0,
          "CostPer1kOutputUsd": 0.0,
          "MaxTokens": 4096,
          "TimeoutSeconds": 120
        }
      }
    }

### § 5.2. `SubAgents` — `external_llm_agent`

Добавить в `SubAgents` секцию (по образцу v1.4.0):

    "external_llm_agent": {
      "Enabled": true,
      "DisplayName": "Агент внешних LLM",
      "Description": "Обращение к внешним LLM (DeepSeek, OpenAI, Groq, Together AI, Ollama) для сложных задач, свежих данных и сравнения ответов.",
      "Model": "qwen/qwen3-4b-2507",
      "MaxSteps": 5,
      "RequiresApproval": false,
      "SystemPrompt": "Ты — агент для работы с внешними LLM. Твоя задача — обращаться к внешним моделям (DeepSeek, OpenAI, Groq, Together AI, Ollama), когда локальная модель не справляется.\n\nСЦЕНАРИИ ИСПОЛЬЗОВАНИЯ:\n1. FALLBACK — если пользователь говорит 'это неточно', 'попробуй по-другому', 'я не уверен'.\n2. СПЕЦИАЛИЗАЦИЯ — подбирай провайдера под задачу:\n   - Код / русский язык → deepseek\n   - Свежие данные / английский → openai\n   - Быстрые итерации → groq\n3. РАЗНЫЕ ЗНАНИЯ — если пользователь просит 'спроси несколько моделей'.\n4. СРАВНЕНИЕ — если пользователь просит 'сравни ответы'.\n\nКРИТИЧЕСКИЕ ПРАВИЛА:\n1. По умолчанию include_context = false (только prompt, без истории чата).\n2. Если пользователь явно просит 'учитывай контекст' — include_context = true.\n3. Не передавай в prompt PII (номера, адреса, пароли). Если пользователь просит — предупреди его.\n4. Всегда указывай в финальном ответе, какой провайдер использован.\n5. Отвечай на русском языке.",
      "AllowedTools": [
        "ask_external_llm",
        "list_external_providers",
        "check_internet_connection"
      ]
    }

### § 5.3. User Secrets — API keys

**Dev (DeepSeek — работает в РФ без VPN):**

    cd C:\Projects\AI\IIChatTools\IIChatTools.API

    dotnet user-secrets set "ExternalLlm:DeepSeek:ApiKey" "sk-..."
    dotnet user-secrets set "ExternalLlm:OpenAI:ApiKey" "sk-..."        # если есть
    dotnet user-secrets set "ExternalLlm:Groq:ApiKey" "gsk_..."        # free tier
    dotnet user-secrets set "ExternalLlm:Enabled" "true"

### § 5.4. Как получить API keys (RU-специфика)

**DeepSeek (рекомендуется в РФ):**
1. https://platform.deepseek.com/
2. Регистрация (нужен email / телефон).
3. Пополнить баланс: $2 хватит на ~500 запросов `deepseek-chat`.
4. Оплата из РФ:
   - **Крипта** (USDT TRC-20 — принять на бирже).
   - **ЮMoney через посредников** (`@ChatGPT_Bot` в TG или аналоги).
   - **Карта Мир** — не везде, зависит от посредника.
5. API key: https://platform.deepseek.com/api_keys → «Create new key» → скопировать.

**OpenAI (требует VPN):**
1. **VPN обязателен** (иначе 403 при регистрации / оплате).
2. https://platform.openai.com/signup — регистрация.
3. Пополнить: зарубежная карта (Visa / Mastercard, не РФ).
4. API key: https://platform.openai.com/api-keys → «Create new secret key».
5. **При использовании API из РФ** — тоже нужен VPN (OpenAI блокирует по IP).

**Groq (free tier):**
1. https://console.groq.com/ — регистрация через Google.
2. Free tier: 30 req/min, 14400 req/day.
3. API key: https://console.groq.com/keys → «Create API Key».
4. **Работает из РФ через VPN** (или через прокси — `IICHATTOOLS_PROXY`).

**Together AI:**
1. https://api.together.xyz/ — регистрация.
2. Free tier: $5 кредит при регистрации.
3. API key: https://api.together.xyz/settings/api-keys.

**Ollama (локально / self-hosted):**
1. Установить Ollama: https://ollama.com/download.
2. `ollama pull qwen3:4b`.
3. `ollama serve` (порт 11434).
4. `ExternalLlm:Providers:ollama:BaseUrl = "http://localhost:11434/v1"`.
5. `ApiKey` — не нужен (Ollama игнорирует), но для совместимости можно задать `"none"`.

### § 5.5. Admin override через AppSettings

По аналогии с `SubAgents.*` и `SqlAgent.*`:
- Ключи `ExternalLlm.*` — редактируются через `/admin → External LLM` (новая вкладка, v1.8.x).
- **API keys в БД не хранятся** — только в User Secrets / env.
- Override возможен для: `Enabled`, `DefaultProvider`, `DailyBudgetUsd`, `DailyTokensLimit`, `CircuitBreaker:*`, `Providers:*.Model`, `Providers:*.TimeoutSeconds`.

### § 5.6. Валидация конфигурации

При старте (если `ExternalLlm:Enabled = true`):

| Проверка | Действие |
|---|---|
| `DefaultProvider` ∈ Providers | `InvalidOperationException` |
| `Providers` не пуст | `InvalidOperationException` |
| У `DefaultProvider` задан `ApiKeySecretName` и разрешается | `InvalidOperationException` |
| `BaseUrl` — валидный URL | `InvalidOperationException` |
| `DailyBudgetUsd` ∈ [0, 1000] | Clamp + warning |
| `DailyTokensLimit` ∈ [0, 10_000_000] | Clamp + warning |
| `CircuitBreaker:FailureThreshold` ∈ [1, 10] | Clamp + warning |
| `CircuitBreaker:BreakDurationSeconds` ∈ [30, 3600] | Clamp + warning |
| `CostPer1k*` ≥ 0 | Warning (если < 0 — clamp) |

**Принцип:** критичные (нет API key / нет BaseUrl) → fail fast. Некритичные → clamp + warning.

---

## § 6. Безопасность

### § 6.1. Approval

| Tool | `RequiresApprovalForCall` | Обоснование |
|---|:---:|---|
| `ask_external_llm` | ❌ | Read-only, но тратит деньги → **DailyBudgetUsd** (см. § 6.4) |
| `list_external_providers` | ❌ | Read-only, не тратит |
| `check_internet_connection` | ❌ | Read-only, лёгкий HEAD-запрос |

**Почему без approval:**
- `ask_external_llm` — read-only для системы (не пишет в БД, не меняет workspace).
- Approval на каждый запрос = плохой UX (пользователь кликает 10 раз).
- Защита от «$1000 за ночь» — **дневной лимит** (§ 6.4), а не approval.

**Если пользователь хочет полного контроля:** можно в v1.8.x добавить `ExternalLlm:RequireApproval = true` — тогда approval появится.

### § 6.2. Privacy (PII)

**Жёсткие правила:**
- **Не логировать prompt** — может содержать PII / коммерческую тайну.
- **Не логировать ответ** внешней модели.
- **AuditLog.ParametersJson** — только: `{ provider, promptLength, includeContext, compareWith }`.
- **AuditLog.ResultJson** — только: `{ success, promptTokens, completionTokens, costUsd, durationMs }`.
- **ILogger** — только: `LogInformation("External: {Provider} promptLen={Len} tokens={Tokens}")`.
- **В `ChatMessage.MetadataJson`** — не сохранять.

**Warning в UI:**
- При первом использовании `ask_external_llm` с `include_context: true` — показать предупреждение:
  «Вы передаёте контекст чата во внешний сервис (OpenAI / DeepSeek). Это может раскрыть PII. Продолжить?»

### § 6.3. Вложения

**Не поддерживается в v1.8.0.**
- `ask_external_llm` принимает **только текст** (prompt).
- Вложения (файлы) — не отправляются.
- Если пользователь просит «отправь файл в GPT» — LLM честно отвечает «невозможно».

**Причина:**
- Стоимость резко растёт (изображения → токены).
- Privacy-риски выше.
- Формат вложений в OpenAI-совместимых API сложнее (multipart / base64).

### § 6.4. Биллинг / tracking

**Три уровня защиты от «$1000 за ночь»:**

**Уровень 1 — Дневной бюджет (USD):**
- `ExternalLlm:DailyBudgetUsd = 5.0` (по умолчанию).
- При превышении — `ToolResult.Fail("Превышен дневной бюджет: $5.00. Повторите завтра.")`.
- Обнуляется в 00:00 UTC.

**Уровень 2 — Дневной лимит токенов:**
- `ExternalLlm:DailyTokensLimit = 500000` (по умолчанию).
- При превышении — Fail.

**Уровень 3 — Per-request лимит:**
- `ExternalLlm:Providers:*.MaxTokens = 8192` — максимальная длина ответа.
- Защита от «ответ на 100k токенов».

**Tracking (в `/admin → Status`):**
- Счётчик запросов (за день, за всё время).
- Расход токенов (prompt / completion).
- Расход USD (за день, за всё время).
- Разбивка по провайдерам.

**Реализация:** `ExternalLlmBudgetTracker` (Singleton, `ConcurrentDictionary<int, DailyUsage>`).
Cleanup — `Timer` каждые 30 минут (удаляет устаревшие записи).

### § 6.5. Circuit breaker

**Проблема:** провайдер недоступен (сеть / API down) → каждый запрос висит 60 сек.

**Решение:**
- Per-provider: **3 fail подряд** → skip на **5 минут**.
- В течение skip — `ToolResult.Fail("Провайдер недоступен, повторите через N мин.")`.
- Reset при успешном запросе.
- По образцу KI-094 (Wikipedia timeout).

**Реализация:** `IExternalLlmCircuitBreaker` (Singleton, `ConcurrentDictionary<string, CircuitState>`).

**`CircuitState`:**
- `ConsecutiveFailures` (int).
- `OpenedAt` (DateTime?).
- `IsOpen` (bool) — если `ConsecutiveFailures >= Threshold` и `OpenedAt + Duration > now`.

**Cleanup:** Timer каждые 5 минут (сбрасывает устаревшие состояния).

### § 6.6. Сводная таблица угроз и защит

| Угроза | Защита | Обходится? |
|---|---|---|
| Утечка API key | User Secrets / env (не в git) | ❌ |
| Превышение бюджета | DailyBudgetUsd + DailyTokensLimit + MaxTokens | ❌ |
| Утечка PII во внешний API | include_context: false по умолчанию + Warning | ⚠️ При include_context: true |
| Логирование PII | Только метаданные в audit | ❌ |
| Зависание при недоступности | Circuit breaker + Timeout 60s | ❌ |
| Рекурсивный вызов (LLM зациклился) | MaxSteps (5) + Circuit breaker | ❌ |
| Подмена провайдера | Провайдер выбирается из config, не из user input | ❌ |
| API key в command line | Только через HttpClient (header) | ❌ |
| Man-in-the-middle | HTTPS only (BaseUrl должен быть https://) | ❌ |
| Спам запросов (DoS) | DailyBudgetUsd + Circuit breaker | ❌ |

**Открытые риски (осознанные):**
- **`include_context: true`** → PII утекает в OpenAI / DeepSeek. Митигация: warning при первом использовании.
- **Provider-specific модели** — `deepseek-chat` может быть deprecated без предупреждения. Митигация: логирование ошибок + fallback на `DefaultProvider`.

---

## § 7. План фаз (0–6)

**Оценка:** ~12–15 ч (≈2 рабочих дня).

| Фаза | Что | Оценка | Зависимости |
|:---:|---|:---:|---|
| **0** | DESIGN (этот документ) | — | ✅ **Done (2026-09-29)** |
| **1** | NuGet (нет — HttpClient встроен) + DTO + скелет | 2 ч | ✅ **Done (`de7e3eb`)** |
| **2** | ExternalLlmClient + CircuitBreaker + BudgetTracker | 3 ч | ✅ **Done (`8b245bd`, `725e720`)** |
| **3** | 3 tools (ask/list/check) | 2.5 ч | ✅ **Done (`b5d8d40`)** |
| **4** | external_llm_agent + Chat integration | 1 ч | ✅ **Done (`4407aba`)** |
| **5** | Тесты (unit + integration) | 2.5 ч | ✅ **Done (`3f0f7ad`)** |
| **6** | Документация + релиз v1.8.1 | 2 ч | ✅ **Done (этот коммит)** |

### § 7.1. Фаза 1 — DTO + скелет (2 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 1.1 | `DTO/ExternalLlm/` — 6 файлов | — |
| 1.2 | `Interfaces/` — 4 интерфейса | — |
| 1.3 | `Startup.cs`: пустые регистрации (закомментированы) | — |

**DoD:** `dotnet build` 0/0. Все DTO/интерфейсы компилируются.

### § 7.2. Фаза 2 — Client + CircuitBreaker + BudgetTracker (3 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 2.1 | `ExternalProviderRegistry.cs` — чтение config + маппинг | `ExternalProviderRegistryTests` (3) |
| 2.2 | `ExternalLlmCircuitBreaker.cs` | `ExternalLlmCircuitBreakerTests` (5) |
| 2.3 | `ExternalLlmBudgetTracker.cs` | `ExternalLlmBudgetTrackerTests` (5) |
| 2.4 | `ProviderCostCalculator.cs` (static helper) | `ProviderCostCalculatorTests` (4) |
| 2.5 | `ExternalLlmClient.cs` — HttpClient per provider | `ExternalLlmClientSmokeTests` (mock HTTP) (3) |
| 2.6 | `Startup.cs` — DI-регистрация | — |
| 2.7 | `appsettings.json` + `.Development.json` — секция `ExternalLlm` | — |

**DoD:** `IExternalLlmClient.CompleteAsync` работает с mock HTTP. Circuit breaker открывается после 3 fail.

### § 7.3. Фаза 3 — 3 tools (2.5 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 3.1 | `AskExternalLlmTool.cs` | 6 тестов (валидация + успех + compare + budget exceeded + circuit open) |
| 3.2 | `ListExternalProvidersTool.cs` | 3 теста |
| 3.3 | `CheckInternetConnectionTool.cs` | 2 теста |
| 3.4 | `RegisterExternalLlmTools(services)` в Startup.cs | — |

**DoD:** 11+ тестов на 3 tools. Все зелёные.

### § 7.4. Фаза 4 — external_llm_agent (1 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 4.1 | `SubAgents:external_llm_agent` в `appsettings.json` | — |
| 4.2 | `ChatStreamService`: `external_llm_agent` в `allowedNames` (RULES § 4.44!) | — |
| 4.3 | Smoke: `curl /api/tools/execute` с агентом | — |
| 4.4 | Smoke через Chat UI | — |

**DoD:** Chat видит **12 инструментов** (после v1.8.0 — вместе с `mail_agent` → **13**).

### § 7.5. Фаза 5 — Тесты (2.5 ч)

| Шаг | Что | Кол-во |
|---|---|:---:|
| 5.1 | Unit: DTO + Registry + CircuitBreaker + Budget + Cost | ~17 |
| 5.2 | Unit: Tools (mock HTTP) | ~11 |
| 5.3 | Integration: ExternalLlmClient → реальный Ollama (skip если нет) | 2 |

**DoD:** `dotnet test` — зелёные. Общее: 375 → ~405.

### § 7.6. Фаза 6 — Документация + релиз (2 ч)

| Шаг | Что |
|---|---|
| 6.1 | README: раздел «External-LLM Agent» |
| 6.2 | CHANGELOG `[1.8.0]` — закрыть вместе с Mail Agent |
| 6.3 | KNOWN_ISSUES: KI-109 → Fixed; KI-110 → Planned |
| 6.4 | RULES § 7 (KI-выжимка) + § 8 |
| 6.5 | DESIGN.md → статус **Implemented** |
| 6.6 | Tag `v1.8.0` + GitHub Release |

---

## § 8. Definition of Done (v1.8.0 — External-LLM)

### § 8.1. Функциональные требования

- [ ] `IExternalLlmClient` + `ExternalLlmClient` (Singleton, HttpClientFactory).
- [ ] `IExternalProviderRegistry` + `ExternalProviderRegistry` (Singleton).
- [ ] `IExternalLlmCircuitBreaker` + `ExternalLlmCircuitBreaker` (Singleton).
- [ ] `IExternalLlmBudgetTracker` + `ExternalLlmBudgetTracker` (Singleton).
- [ ] 3 tools: `ask_external_llm` / `list_external_providers` / `check_internet_connection`.
- [ ] `external_llm_agent` в `SubAgents` + в `allowedNames` Chat.
- [ ] Chat видит **13 инструментов** (после Mail + External: 6 агентов + consult + 3 RAG + `database_agent` + `mail_agent` + `external_llm_agent`).
- [ ] Support 5 провайдеров: DeepSeek / OpenAI / Groq / Together AI / Ollama.
- [ ] `include_context: false` по умолчанию (только prompt).
- [ ] Daily budget ($5) + Daily tokens (500k) + Per-request max (8192).
- [ ] Circuit breaker (3 fail → 5 мин skip).
- [ ] Privacy: без PII в логах / audit.

### § 8.2. Нефункциональные

- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — **~405/405** (+~30 новых).
- [ ] CI + Docker Publish — зелёные.
- [ ] RULES § 7 + § 8 обновлены.
- [ ] CHANGELOG `[1.8.0]`.
- [ ] README раздел «External-LLM Agent».
- [ ] DESIGN.md статус **Implemented**.

### § 8.3. Smoke (8 сценариев)

| # | Сценарий | Ожидание |
|---|---|---|
| 1 | `ExternalLlm:Enabled = false` → перезапуск | `external_llm_agent` не виден в Chat, `/api/tools` без него |
| 2 | `curl /api/tools/execute list_external_providers` | JSON со списком + статус |
| 3 | `curl /api/tools/execute check_internet_connection` | `{ success: true }` (DeepSeek доступен) |
| 4 | Chat: «Спроси DeepSeek, что нового в .NET 10» | `external_llm_agent` → `ask_external_llm(provider="deepseek")` → ответ |
| 5 | Chat: «Сравни ответы ChatGPT и DeepSeek про X» | `ask_external_llm(compare_with="deepseek")` → 2 ответа |
| 6 | Отключить интернет → Chat: «Спроси DeepSeek» | `check_internet_connection` → false; `ask_external_llm` → Fail |
| 7 | 3 fail подряд (несуществующий API key) | Circuit breaker открывается → `list_external_providers` показывает `available: false` |
| 8 | Превышение daily budget ($5) | `ask_external_llm` → Fail «Превышен дневной бюджет» |

### § 8.4. Документация

- [ ] README.md — раздел «External-LLM Agent».
- [ ] CHANGELOG.md — `[1.8.0]`.
- [ ] KNOWN_ISSUES.md — KI-109 → Fixed; KI-110 → Planned.
- [ ] TESTING.md — smoke-сценарии.
- [ ] RULES.md — обновлён (если есть новые уроки).

---

## § 9. Ссылки

### § 9.1. KI

- **KI-109** — External-LLM Agent (этот документ, v1.8.0).
- **KI-110** — Anthropic / Gemini providers (v1.9+).
- **KI-052** — Multi-Agent (эталон SubAgentDescriptor).
- **KI-094** — Wikipedia timeout (эталон circuit breaker).

### § 9.2. Правила (RULES.md)

- § 1.9 — `PathHelper` (не применимо — нет файлов, но правило).
- § 1.14 — локализация (RU + EN).
- § 1.15 — новый инструмент = 1 класс + 1 строка регистрации.
- § 4.17 — JS-локализация через `data-*`.
- § 4.28 — Timeout + Retry для HTTP (эталон для ExternalLlmClient).
- § 4.44 — новый top-level ITool → `allowedNames`.
- § 5.x — User Secrets, без PII в логах.

### § 9.3. Внешние источники

- [OpenAI Chat Completions API](https://platform.openai.com/docs/api-reference/chat) — эталон формата.
- [DeepSeek API](https://platform.deepseek.com/api-docs/) — OpenAI-совместимый.
- [Groq API](https://console.groq.com/docs) — OpenAI-совместимый.
- [Together AI API](https://docs.together.ai/) — OpenAI-совместимый.
- [Ollama OpenAI-compat](https://github.com/ollama/ollama/blob/main/docs/openai.md).
- [Polly Circuit Breaker](https://github.com/App-vNext/Polly) — эталон (не используем, своя реализация).
- [Anthropic Messages API](https://docs.anthropic.com/en/api/messages) — v1.9+.

### § 9.4. Внутренние документы

- `docs/development/v1.4/DESIGN.md` — Multi-Agent (эталон SubAgentDescriptor).
- `docs/development/v1.7/DESIGN_DB_AGENT.md` — Database Agent (эталон approvals + admin UI).
- `docs/development/v1.8/DESIGN_MAIL_AGENT.md` — Mail Agent (симметричный документ).
- `docs/development/RULES.md` — правила (v1.4.20).
- `docs/KNOWN_ISSUES.md` — реестр проблем.

---

## § 10. Приложения

### Приложение A — пример `ask_external_llm` через LLM

**Задача пользователя:** «Локальная модель не справилась — спроси DeepSeek, как реализовать retry с exponential backoff в .NET 10».

**Что делает LLM:**
1. Вызывает `external_llm_agent(task="Спросить DeepSeek про retry с exponential backoff в .NET 10")`.
2. Внутри агента:
   - LLM вызывает `ask_external_llm(provider="deepseek", prompt="Напиши пример retry с exponential backoff в .NET 10 (Polly или custom). Учти CancellationToken, jitter.", include_context=false)`.
   - `ExternalLlmClient`:
     - `IExternalLlmBudgetTracker.CheckAndReserve(userId, estimatedCost)` → OK.
     - `IExternalLlmCircuitBreaker.IsOpen("deepseek")` → false.
     - HTTP POST на `https://api.deepseek.com/v1/chat/completions` (Bearer `sk-...`).
   - Получает ответ + `promptTokens=245, completionTokens=512, costUsd=0.0008`.
   - Логирует в AuditLogs: `{ provider: "deepseek", promptLength: 145, tokens: 757, costUsd: 0.0008 }`.
3. Агент возвращает `ToolResult.Ok` с ответом.
4. Chat формулирует финальный ответ с указанием: «Источник: DeepSeek (deepseek-chat)».

### Приложение B — пример `compare_with`

**Задача:** «Сравни, как ChatGPT и DeepSeek отвечают на вопрос "Что нового в .NET 10?"».

**Что делает LLM:**
1. Вызывает `external_llm_agent(task="Сравнить ответы OpenAI и DeepSeek про .NET 10")`.
2. Внутри:
   - LLM вызывает `ask_external_llm(provider="openai", compare_with="deepseek", prompt="Что нового в .NET 10 LTS? Топ-5 изменений.")`.
   - `ExternalLlmClient` делает **2 параллельных** запроса (`Task.WhenAll`):
     - OpenAI (`gpt-4o-mini`).
     - DeepSeek (`deepseek-chat`).
   - Возвращает `ExternalLlmComparisonDto`: `{ primary: { provider: "openai", content: "..." }, secondary: { provider: "deepseek", content: "..." }, totalCostUsd: 0.004 }`.
3. Chat формирует итог: «OpenAI дал ответ X. DeepSeek дал ответ Y. Общее: …».

### Приложение C — пример `list_external_providers`

**Формат ответа:**

    {
      "success": true,
      "data": {
        "default": "deepseek",
        "providers": [
          {
            "name": "deepseek",
            "displayName": "DeepSeek",
            "available": true,
            "costPer1kInputUsd": 0.00014,
            "costPer1kOutputUsd": 0.00028
          },
          {
            "name": "openai",
            "displayName": "OpenAI",
            "available": false,
            "lastError": "Circuit breaker open (retry in 3m)",
            "costPer1kInputUsd": 0.00015,
            "costPer1kOutputUsd": 0.0006
          },
          {
            "name": "groq",
            "displayName": "Groq",
            "available": true,
            "costPer1kInputUsd": 0.0,
            "costPer1kOutputUsd": 0.0
          }
        ]
      }
    }

### Приложение D — пример статистики в `/admin → Status`

**Панель «External LLM»:**
- Сегодня: 23 запроса, 45 680 токенов, $0.18 / $5.00.
- Всё время: 412 запросов, 890 000 токенов, $3.47.
- По провайдерам:
  - DeepSeek: 18 запросов, $0.12 (работает).
  - OpenAI: 5 запросов, $0.06 (circuit breaker open — 3 fail подряд).
  - Groq: 0 запросов (не настроен).

### Приложение E — RU-специфика получения API keys

**DeepSeek (рекомендуется в РФ):**
1. https://platform.deepseek.com/
2. Регистрация по email (можно Yandex / Mail.ru).
3. **Оплата из РФ:**
   - **Крипта (USDT TRC-20)** — самый простой путь. Принимается через Binance / OKX.
   - **ЮMoney через посредников** — например, `@ChatGPT_Bot` в Telegram.
   - **Карта Мир** — не везде, зависит от посредника.
4. `$2` хватит на ~500 запросов `deepseek-chat` (при avg 800 токенов/запрос).
5. API key: https://platform.deepseek.com/api_keys → «Create new key» → скопировать.

**Groq (free tier, работает через VPN):**
1. https://console.groq.com/ — регистрация через Google.
2. Free tier: 30 req/min, 14400 req/day — хватает для личного использования.
3. API key: https://console.groq.com/keys → «Create API Key».
4. В РФ — **обязательно VPN** (или корпоративный прокси через `IICHATTOOLS_PROXY`).

**OpenAI (требует VPN + зарубежная карта):**
1. **VPN обязателен** — регистрация и API-запросы блокируются по IP РФ.
2. https://platform.openai.com/signup.
3. Карта: Visa / Mastercard не РФ (Казахстан / Грузия / Кипр и т.д.).
4. Минимум $5 на баланс.
5. API key: https://platform.openai.com/api-keys.

**Ollama (локально, без оплаты):**
1. Скачать: https://ollama.com/download.
2. `ollama pull qwen3:4b` (или `deepseek-r1:7b`).
3. `ollama serve` — порт 11434.
4. Работает **без интернета** вообще (после скачивания модели).

