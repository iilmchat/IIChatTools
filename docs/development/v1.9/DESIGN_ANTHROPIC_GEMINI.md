# DESIGN v1.9 — Anthropic Claude

**Версия:** 1.0
**Дата:** 2026-09-30
**Автор:** IIChatTools Team
**Статус:** **Implemented (v1.9.0, 2026-10-01)**
**Связанные KI:** KI-110a (Anthropic Claude — Fixed в v1.9.0), KI-110b (Google Gemini — Planned, v1.9.x), KI-109 (External-LLM Agent — база)
**Целевой релиз:** v1.9.0 (только Anthropic, ✅ Done) → v1.9.x (Gemini)

---

## § 1. Контекст

### § 1.1. Текущее состояние (после v1.8.1)

- **`external_llm_agent`** работает: агент + 3 tools (`ask_external_llm`,
  `list_external_providers`, `check_internet_connection`).
- **5 OpenAI-совместимых провайдеров:** DeepSeek, OpenAI, Groq, Together AI, Ollama.
- **Всё в `ExternalLlmClient`** заточено под OpenAI-формат:
  `POST {BaseUrl}/chat/completions`, `Authorization: Bearer <key>`,
  тело `{model, messages[], temperature, max_tokens, stream}`.
- **Тесты:** 496/496. `ExternalLlmClientTests` — эталон (mock handler + Moq).
- **`ProviderFormat` отсутствует** — все провайдеры неявно OpenAI.

### § 1.2. Industry best practices

| Практика | Источник | Применение |
|---|---|---|
| **Adapter pattern** — один интерфейс, разные форматы | GoF | `ProviderFormat` + switch в `ExternalLlmClient` |
| **Fail-fast на неизвестном Format** | AWS Well-Architected | `NotSupportedException` для Gemini (v1.9.x) |
| **Обратная совместимость** | Semantic Versioning | `Format = OpenAI` по умолчанию — 5 провайдеров не меняются |
| **Минимум изменений в существующем коде** | KISS | Рефакторинг `CompleteAsync` → 3 приватных метода |
| **Privacy-first** (унаследовано от v1.8.1) | GDPR | Без PII в логах / audit / MetadataJson |

### § 1.3. Цели v1.9.0

| # | Цель | Метрика |
|---|---|---|
| 1 | **Поддержка Anthropic Claude** | `ask_external_llm(provider="anthropic", ...)` работает |
| 2 | **Обратная совместимость** | 5 существующих провайдеров работают без изменений конфига |
| 3 | **Минимальный diff** | `ExternalLlmClient.cs` — рефакторинг, не переписывание |
| 4 | **Готовность к Gemini** | `ProviderFormat.Gemini` в enum, `NotSupportedException` |
| 5 | **Тесты по образцу v1.8.1** | +~20 тестов (builders + parser + client) |
| 6 | **Privacy-first** (как v1.8.1) | Без PII в логах / audit |

### § 1.4. Что НЕ входит в v1.9.0

- **Google Gemini** — v1.9.x (KI-110b). В `appsettings.json` запись **есть**
  (Format=Gemini), но при вызове — `NotSupportedException`.
- **Streaming с Anthropic** — только non-stream (как v1.8.1).
- **Function calling / tools на Anthropic** — только prompt → text.
- **Vision** (изображения) — не входит.
- **Prompt caching** (`cache_control`) — не входит.
- **Extended thinking** (`thinking`) — не входит.
- **Per-user API keys** — v1.9.x (по образцу KI-108).
- **OAuth2** — только API keys в User Secrets.

---

## § 2. Проблема

### § 2.1. Anthropic не OpenAI-совместим

Anthropic Messages API отличается от OpenAI Chat Completions **по 6 аспектам**:

| Аспект | OpenAI | Anthropic |
|---|---|---|
| **Endpoint** | `POST {BaseUrl}/chat/completions` | `POST {BaseUrl}/messages` |
| **Auth header** | `Authorization: Bearer <key>` | `x-api-key: <key>` |
| **Обязательный header** | — | `anthropic-version: 2023-06-01` |
| **System prompt** | роль `system` в `messages[]` | **отдельное поле** `system` (string) |
| **`max_tokens`** | опционально | **обязательно** |
| **`temperature`** | 0..2 | 0..1 |
| **Response text** | `choices[0].message.content` (string) | `content[]` — массив блоков, склеиваем `text` |
| **Токены** | `usage.prompt_tokens` / `completion_tokens` | `usage.input_tokens` / `output_tokens` |
| **Стоп-причина** | `choices[0].finish_reason` | `stop_reason` |
| **Формат ошибок** | `{error: {message, type, code}}` | `{type: "error", error: {type, message}}` |

### § 2.2. Почему нельзя «просто подставить другой URL»

- Тело запроса — **разное** (system отдельно, max_tokens обязателен).
- Заголовок авторизации — **разный** (`x-api-key` вместо Bearer).
- Парсинг ответа — **разный** (`content[]` вместо `choices[]`).
- Простая подмена `BaseUrl` → 400 Bad Request / 401 Unauthorized.

### § 2.3. Ограничения текущего `IExternalLlmClient`

- Метод `CompleteAsync` жёстко собирает OpenAI-body.
- Нет ветвления по формату — все провайдеры идут одним путём.
- `ProviderCostCalculator` — уже per-provider (не меняется).

**`External-LLM Agent` заполняет пробел:** «Claude доступен как один из
провайдеров в существующем агенте — без нового UI, без новых tools».

---

## § 3. Решение

### § 3.1. `ProviderFormat` enum (новый)

Файл `DTO/ExternalLlm/ProviderFormat.cs`:

```csharp
namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Формат API внешнего провайдера LLM (v1.9.0, KI-110a).
    ///
    /// <para>
    /// <b>OpenAI</b> — <c>POST {BaseUrl}/chat/completions</c>,
    /// <c>Authorization: Bearer</c>, тело <c>{model, messages[]}</c>.
    /// Провайдеры: DeepSeek, OpenAI, Groq, Together AI, Ollama.
    /// </para>
    ///
    /// <para>
    /// <b>Anthropic</b> — <c>POST {BaseUrl}/messages</c>,
    /// <c>x-api-key</c> + <c>anthropic-version</c>, system отдельно,
    /// <c>max_tokens</c> обязателен. Ответ — <c>content[]</c> (блоки).
    /// </para>
    ///
    /// <para>
    /// <b>Gemini</b> — зарезервировано на v1.9.x (KI-110b).
    /// При вызове — <see cref="System.NotSupportedException"/>.
    /// </para>
    /// </summary>
    public enum ProviderFormat
    {
        /// <summary>OpenAI-совместимый формат (по умолчанию).</summary>
        OpenAI = 0,

        /// <summary>Anthropic Messages API (<c>/v1/messages</c>).</summary>
        Anthropic = 1,

        /// <summary>Google Gemini (v1.9.x, KI-110b). Пока не реализовано.</summary>
        Gemini = 2
    }
}
```

### § 3.2. `ExternalProviderOptions` — +1 свойство

Диff:

```csharp
public class ExternalProviderOptions
{
    // ... существующие поля ...

    /// <summary>
    /// Формат API. По умолчанию <see cref="ProviderFormat.OpenAI"/> —
    /// 5 существующих провайдеров не задают это поле в appsettings
    /// и продолжают работать без изменений.
    /// </summary>
    public ProviderFormat Format { get; set; } = ProviderFormat.OpenAI;
}
```

**`ExternalProviderRegistry`** — **не меняется.** Валидация `BaseUrl` / `Model` /
`ApiKeySecretName` от `Format` не зависит.

### § 3.3. Рефакторинг `ExternalLlmClient.CompleteAsync`

Тело метода режется на 3 части:

```csharp
public async Task<ExternalLlmResponse> CompleteAsync(
    int userId, ExternalLlmRequest request, CancellationToken ct = default)
{
    // 1. Валидация + резолв провайдера (как v1.8.1)
    // 2. Circuit breaker (как v1.8.1)
    // 3. Бюджет (как v1.8.1)
    // 4. API-ключ (как v1.8.1)
    // 5. NEW: switch по provider.Format:
    var (content, promptTokens, completionTokens) = provider.Format switch
    {
        ProviderFormat.OpenAI    => await CompleteOpenAiAsync(provider, apiKey, request, ct),
        ProviderFormat.Anthropic => await CompleteAnthropicAsync(provider, apiKey, request, ct),
        ProviderFormat.Gemini    => throw new NotSupportedException(
            "Провайдер Gemini запланирован на v1.9.x (KI-110b). " +
            "Используйте OpenAI-совместимый провайдер (DeepSeek, OpenAI, Groq, ...)."),
        _ => throw new InvalidOperationException(
            $"Неизвестный ProviderFormat: {provider.Format}.")
    };

    // 6. Расчёт стоимости (как v1.8.1 — ProviderCostCalculator)
    // 7. Учёты: circuit breaker + budget tracker (как v1.8.1)
    // 8. Return ExternalLlmResponse (как v1.8.1)
}
```

**`CompleteOpenAiAsync`** — существующее тело без верхней обвязки.
Возвращает `(string content, int promptTokens, int completionTokens)`.

**`CompleteAnthropicAsync`** — новый метод (см. § 4.1).

**Общая обвязка (circuit breaker, budget, cost, logging)** — единая для всех форматов.

### § 3.4. `AnthropicRequestBuilder` (новый)

Файл `Implementation/ExternalLlm/Formats/AnthropicRequestBuilder.cs`.

Static helper. Собирает `JObject` для `POST {BaseUrl}/messages`:

```json
{
  "model": "claude-haiku-4-5",
  "max_tokens": 4096,
  "system": "опционально",
  "messages": [{"role": "user", "content": "<prompt>"}],
  "temperature": 0.7
}
```

Правила:
- `max_tokens` — **всегда** задан: `request.MaxTokens ?? provider.MaxTokens`.
- `system` — добавляется, если не пуст.
- `temperature` — clamp `[0, 1]` (не `[0, 2]`, как в OpenAI).
- `stream` — **не добавляем** (v1.9.0 non-stream).

### § 3.5. `AnthropicResponseParser` (новый)

Файл `Implementation/ExternalLlm/Formats/AnthropicResponseParser.cs`.

Static helper. Парсит `JObject` ответа:

- **`content[]`** — склеиваем все блоки `type == "text"` через `\n`.
  Блоки `type == "tool_use"` — **игнорируем** (v1.9.0 без tools).
- **`usage.input_tokens`** → `promptTokens`.
- **`usage.output_tokens`** → `completionTokens`.
- **`stop_reason`** → логируется как `finish_reason` (в info-лог).
- **Пустой `content[]`** → возвращаем пустую строку (не падаем).

Возвращает `(string content, int promptTokens, int completionTokens)`.

### § 3.6. Gemini — заглушка

- `ProviderFormat.Gemini` — в enum **есть**.
- В `appsettings.json` секция `gemini` — **есть** (Format=Gemini).
- `ExternalProviderRegistry` — **валиден** (BaseUrl, Model, ApiKeySecretName в порядке).
- При вызове `ask_external_llm(provider="gemini")` — **`NotSupportedException`**
  с текстом «Gemini запланирован на v1.9.x (KI-110b)».
- Tool `ask_external_llm` ловит исключение → `ToolResult.Fail` с сообщением
  пользователю (существующее поведение).

**Почему так:** запись в конфиге документирует намерение (можно сразу
добавить `ApiKeySecretName` в User Secrets), но функциональность честно
отсутствует. **Альтернатива (b)** — fail-fast на старте — заблокировала бы
всё приложение, если ключ Gemini не задан, что неприемлемо.

---

## § 4. Архитектура

### § 4.1. Новые файлы

    IIChatTools.Services/
      ├── DTO/ExternalLlm/
      │   └── ProviderFormat.cs                    — новый enum
      │
      └── Implementation/ExternalLlm/
          └── Formats/                              — новая папка
              ├── AnthropicRequestBuilder.cs        — static helper
              └── AnthropicResponseParser.cs        — static helper

    IIChatTools.Tests/
      └── UnitTests/ExternalLlm/Formats/            — новая папка
          ├── AnthropicRequestBuilderTests.cs       — ~7 тестов
          └── AnthropicResponseParserTests.cs       — ~7 тестов

### § 4.2. Изменения в существующих файлах

| Файл | Что | Строк diff |
|---|---|---|
| `DTO/ExternalLlm/ExternalProviderOptions.cs` | +1 свойство `Format` | +8 |
| `Implementation/ExternalLlm/ExternalLlmClient.cs` | Рефакторинг `CompleteAsync` → 3 метода | ~150 |
| `appsettings.json` | +2 провайдера (anthropic, gemini) | +30 |
| `appsettings.Development.json` | То же | +30 |
| `IIChatTools.Tests/UnitTests/ExternalLlm/ExternalLlmClientTests.cs` | +4 теста на Anthropic-ветку | ~120 |
| `IIChatTools.Tests/IntegrationTests/ExternalLlm/ExternalLlmIntegrationTests.cs` | +1 `[Fact(Skip=...)]` | ~40 |
| `IIChatTools.Tests/UnitTests/ExternalLlm/ExternalProviderRegistryTests.cs` | +1 тест на Format | ~25 |

**Ничего не трогаем:**
- `ExternalProviderRegistry` — формат не валидирует.
- `ExternalLlmCircuitBreaker` — per-provider, формат не важен.
- `ExternalLlmBudgetTracker` — per-user, формат не важен.
- `AskExternalLlmTool`, `ListExternalProvidersTool`, `CheckInternetConnectionTool` — формат не важен.
- `ExternalLlmAgentTool` — обёртка над агентом, формат не важен.
- `ProviderCostCalculator` — уже работает по тарифам провайдера.
- `ChatStreamService`, `Startup.cs`, `Program.cs` — не трогаем.

### § 4.3. Sequence — `CompleteAnthropicAsync`

    1. Проверка circuit breaker        (общая обвязка, как v1.8.1)
    2. Проверка бюджета (per-user)     (общая обвязка)
    3. Резолв API-ключа из конфига      (общая обвязка)
    4. AnthropicRequestBuilder.Build(provider, request)
       → JObject { model, max_tokens, system?, messages[], temperature? }
    5. HTTP POST {BaseUrl}/messages:
         Headers:
           x-api-key: <apiKey>
           anthropic-version: 2023-06-01
           content-type: application/json
         Body: (см. шаг 4)
       Retry 1× при 5xx/429 (та же SendWithRetryAsync с параметром)
       Timeout — CancellationTokenSource.CancelAfter (RULES § 4.48)
    6. AnthropicResponseParser.Parse(responseJson)
       → (content, inputTokens, outputTokens)
    7. RecordSuccess + RecordUsage   (общая обвязка)
    8. Return ExternalLlmResponse {
         Provider, Content, PromptTokens, CompletionTokens, CostUsd, DurationMs
       }

---

## § 5. Конфигурация

### § 5.1. `appsettings.json` — секция `ExternalLlm:Providers`

Добавить **2 новых записи** к 5 существующим:

```jsonc
"anthropic": {
  "DisplayName": "Anthropic Claude",
  "Format": "Anthropic",
  "BaseUrl": "https://api.anthropic.com/v1",
  "Model": "claude-haiku-4-5",
  "ApiKeySecretName": "ExternalLlm:Anthropic:ApiKey",
  "CostPer1kInputUsd": 0.001,
  "CostPer1kOutputUsd": 0.005,
  "MaxTokens": 8192,
  "TimeoutSeconds": 60
},
"gemini": {
  "DisplayName": "Google Gemini (v1.9.x, KI-110b)",
  "Format": "Gemini",
  "BaseUrl": "https://generativelanguage.googleapis.com/v1beta",
  "Model": "gemini-2.0-flash",
  "ApiKeySecretName": "ExternalLlm:Gemini:ApiKey",
  "CostPer1kInputUsd": 0.0,
  "CostPer1kOutputUsd": 0.0,
  "MaxTokens": 8192,
  "TimeoutSeconds": 60
}
```

**Тарифы Anthropic Haiku** — placeholder (проверить на
https://www.anthropic.com/pricing перед релизом v1.9.0). Если цифры
изменятся — обновить только `appsettings.json` (код не трогаем).

### § 5.2. User Secrets

**Только Anthropic:**

    cd C:\Projects\AI\IIChatTools\IIChatTools.API

    # Anthropic (требует VPN из РФ)
    dotnet user-secrets set "ExternalLlm:Anthropic:ApiKey" "sk-ant-..."

**Gemini** — отложено до v1.9.x. Можно задать сейчас (пригодится):

    dotnet user-secrets set "ExternalLlm:Gemini:ApiKey" "AIza..."

**`ExternalLlm:Enabled`** — уже есть (v1.8.1). Не трогаем.

### § 5.3. Как получить API key (RU-специфика)

**Anthropic Claude:**

1. **VPN обязателен** — регистрация и API-запросы блокируются по IP РФ.
2. https://console.anthropic.com/ — регистрация.
3. Пополнить баланс: зарубежная карта (Visa / Mastercard, не РФ).
   Минимум $5 на старте, Claude Haiku — $0.001/$0.005 за 1k токенов.
4. API key: https://console.anthropic.com/settings/keys → «Create Key».
5. **Заголовок `anthropic-version: 2023-06-01`** — обязателен, задаётся
   в коде (не в конфиге).

**Куда записать:** README (раздел «External-LLM Agent» → «Получить ключи»),
+ этот DESIGN § 5.3.

**Google Gemini (v1.9.x, для справки):**

1. https://aistudio.google.com/app/apikey — регистрация через Google.
2. **Free tier** доступен (rate limits).
3. Из РФ — **требует VPN** (Google блокирует по IP).

---

## § 6. Безопасность

### § 6.1. API key — `x-api-key` (не Bearer)

Anthropic ожидает ключ в заголовке `x-api-key`, а не `Authorization: Bearer`.
Реализация — в `CompleteAnthropicAsync`:

```csharp
httpRequest.Headers.Add("x-api-key", apiKey);
httpRequest.Headers.Add("anthropic-version", "2023-06-01");
```

**Не логируется** (как v1.8.1). `ILogger` — только метаданные.

### § 6.2. HTTPS — уже enforced

`ExternalProviderRegistry.ValidateProviderOrThrow` требует `https://` для
не-localhost. Anthropic и Gemini — публичные домены → `https://` обязателен.

### § 6.3. Privacy — без изменений

- **Не логируется** prompt / content (уже v1.8.1).
- **AuditLog** — только метаданные (`provider`, `promptLength`, `tokens`, `cost`).
- **`include_context: false` по умолчанию** — Anthropic тоже.

### § 6.4. Сводная таблица угроз

| Угроза | Защита | Обходится? |
|---|---|---|
| Утечка API key | User Secrets / env (не в git) | ❌ |
| Превышение бюджета | `DailyBudgetUsd` + `DailyTokensLimit` | ❌ |
| Утечка PII | `include_context: false` по умолчанию | ⚠️ при `true` |
| Man-in-the-middle | HTTPS only | ❌ |
| Зависание при недоступности | Circuit breaker + Timeout | ❌ |
| Неверный формат | Fail-fast в `CompleteAsync` switch | ❌ |

---

## § 7. План фаз (0–5)

**Оценка:** ~9.5 ч (≈1 рабочий день + запас).

| Фаза | Что | Оценка | Статус |
|:---:|---|:---:|:---:|
| **0** | DESIGN (этот документ) + KI-110 → разбить на 110a/110b | 1.5 ч | ✅ Done |
| **1** | `ProviderFormat` enum + `ExternalProviderOptions.Format` + тесты | 1 ч | ✅ Done (`f324a13`) |
| **2** | `AnthropicRequestBuilder` + `AnthropicResponseParser` + тесты | 2 ч | ✅ Done (`06ab722`) |
| **3** | Рефакторинг `ExternalLlmClient` (3 метода + switch) + тесты | 2.5 ч | ✅ Done (`3524b90`) |
| **4** | `appsettings.json` / `.Development.json` (anthropic + gemini) + README | 1 ч | ✅ Done (`46487fc`) |
| **5** | Релиз v1.9.0 (CHANGELOG, KNOWN_ISSUES, RULES § 8, bump, tag) | 1.5 ч | ✅ Done |

### § 7.1. Фаза 1 — `ProviderFormat` + тесты (1 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 1.1 | `ProviderFormat.cs` — enum | — |
| 1.2 | `ExternalProviderOptions.cs` — +1 свойство | `ExternalProviderRegistryTests` +1 (Format не влияет на валидацию) |

**DoD:** `dotnet build` 0/0. Все 5 существующих провайдеров работают
без изменений конфига (Format = OpenAI по дефолту).

### § 7.2. Фаза 2 — Builders (2 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 2.1 | `AnthropicRequestBuilder.cs` | `AnthropicRequestBuilderTests` (~7) |
| 2.2 | `AnthropicResponseParser.cs` | `AnthropicResponseParserTests` (~7) |

**DoD:** `dotnet build` 0/0. Тесты на builders — зелёные.

### § 7.3. Фаза 3 — Client (2.5 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 3.1 | `ExternalLlmClient.cs` — рефакторинг | `ExternalLlmClientTests` — все существующие должны пройти без изменений |
| 3.2 | `CompleteAnthropicAsync` | +4 новых теста (`x-api-key`, `anthropic-version`, парсинг, 400 без retry) |
| 3.3 | `CompleteGeminiAsync` → `NotSupportedException` | +1 тест |
| 3.4 | `ExternalLlmIntegrationTests` — +1 `[Fact(Skip=...)]` для Claude | — |

**DoD:** `dotnet build` 0/0, `dotnet test` — 496 → ~517/517.

### § 7.4. Фаза 4 — Config + README (1 ч)

| Шаг | Что |
|---|---|
| 4.1 | `appsettings.json` — +2 провайдера (anthropic, gemini) |
| 4.2 | `appsettings.Development.json` — то же |
| 4.3 | README — раздел «External-LLM Agent»: +Anthropic (получение ключа, VPN, RU-специфика) |

### § 7.5. Фаза 5 — Релиз v1.9.0 (1.5 ч)

| Шаг | Что |
|---|---|
| 5.1 | CHANGELOG `[Unreleased]` → `[1.9.0] — 2026-MM-DD` |
| 5.2 | KNOWN_ISSUES: KI-110a → Fixed (v1.9.0); KI-110b → Planned (v1.9.x) |
| 5.3 | RULES § 8 — новая строка (если есть новые уроки) |
| 5.4 | `Directory.Build.props` — `<Version>1.9.0</Version>` + `Copyright` |
| 5.5 | DESIGN → статус **Implemented** |
| 5.6 | Tag `v1.9.0` + GitHub Release |

---

## § 8. Definition of Done (v1.9.0 — Anthropic)

### § 8.1. Функциональные требования

- [x] `ProviderFormat` enum (OpenAI / Anthropic / Gemini).
- [x] `ExternalProviderOptions.Format` (default = OpenAI).
- [x] `AnthropicRequestBuilder` — собирает Anthropic-body.
- [x] `AnthropicResponseParser` — парсит `content[]` + `usage`.
- [x] `ExternalLlmClient.CompleteAsync` — switch по `Format`.
- [x] `CompleteOpenAiAsync` — существующее поведение (все 5 провайдеров работают).
- [x] `CompleteAnthropicAsync` — новый путь через `/messages`.
- [x] `CompleteGeminiAsync` — `NotSupportedException` (KI-110b).
- [x] `appsettings.json` + `.Development.json` — `anthropic` + `gemini`.
- [x] 5 существующих провайдеров работают **без изменений конфига**.

### § 8.2. Нефункциональные

- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — **~517/517** (+~21).
- [ ] CI + Docker Publish — зелёные.
- [ ] RULES § 8 — обновлён (если есть новые уроки).
- [ ] CHANGELOG `[1.9.0] — YYYY-MM-DD`.
- [ ] README — раздел «External-LLM Agent» + Anthropic.
- [ ] DESIGN → **Implemented**.

### § 8.3. Smoke (5 сценариев)

| # | Сценарий | Ожидание |
|---|---|---|
| 1 | `list_external_providers` | Anthropic в списке; Gemini с `available: false` |
| 2 | `ask_external_llm(provider="anthropic", prompt="2+2")` | Ответ от Claude |
| 3 | `ask_external_llm(provider="deepseek", compare_with="anthropic", ...)` | 2 ответа, `totalCostUsd` |
| 4 | `ask_external_llm(provider="gemini", ...)` | `ToolResult.Fail` с «Gemini — v1.9.x (KI-110b)» |
| 5 | Отключить интернет → `ask_external_llm(provider="anthropic")` | Circuit breaker + Fail «временно недоступен» |

### § 8.4. Документация

- [ ] README.md — Anthropic в разделе External-LLM.
- [ ] CHANGELOG.md — `[1.9.0]`.
- [ ] KNOWN_ISSUES.md — KI-110a → Fixed; KI-110b → Planned.
- [ ] RULES.md — § 8 (история).

---

## § 9. Ссылки

### § 9.1. KI

- **KI-110a** — Anthropic Claude (этот документ, v1.9.0).
- **KI-110b** — Google Gemini (v1.9.x).
- **KI-109** — External-LLM Agent (v1.8.1, база).

### § 9.2. Внешние источники

- [Anthropic Messages API](https://docs.anthropic.com/en/api/messages) — эталон.
- [Anthropic API versioning](https://docs.anthropic.com/en/api/versioning) — `anthropic-version: 2023-06-01`.
- [Anthropic Pricing](https://www.anthropic.com/pricing) — тарифы (проверить при релизе).
- [Google Gemini API](https://ai.google.dev/api) — v1.9.x.

### § 9.3. Внутренние документы

- `docs/development/v1.8/DESIGN_EXTERNAL_LLM.md` — база (v1.8.1).
- `docs/development/v1.8/DESIGN_MAIL_AGENT.md` — симметричный документ.
- `docs/development/RULES.md` — правила (v1.4.24).
- `docs/KNOWN_ISSUES.md` — реестр.

---

## § 10. Приложения

### Приложение A — пример Anthropic-запроса / ответа

**Запрос:**

```http
POST https://api.anthropic.com/v1/messages
x-api-key: sk-ant-...
anthropic-version: 2023-06-01
content-type: application/json

{
  "model": "claude-haiku-4-5",
  "max_tokens": 4096,
  "messages": [
    {"role": "user", "content": "Сколько будет 2+2? Ответь одним словом."}
  ],
  "temperature": 0.7
}
```

**Ответ:**

```json
{
  "id": "msg_01ABC...",
  "type": "message",
  "role": "assistant",
  "content": [
    {"type": "text", "text": "Четыре"}
  ],
  "model": "claude-haiku-4-5",
  "stop_reason": "end_turn",
  "usage": {
    "input_tokens": 20,
    "output_tokens": 3
  }
}
```

**Парсинг:**
- `content[0].text` → `"Четыре"`.
- `usage.input_tokens` → `promptTokens = 20`.
- `usage.output_tokens` → `completionTokens = 3`.
- `CostUsd = (20/1000)*0.001 + (3/1000)*0.005 = 0.00002 + 0.000015 = 0.000035`.

### Приложение B — пример `compare_with="anthropic"` через LLM

**Задача:** «Сравни ответы DeepSeek и Claude на вопрос X».

**Что делает Chat LLM:**
1. Вызывает `external_llm_agent(task="Сравнить ответы DeepSeek и Claude про X")`.
2. Внутри агента:
   - LLM вызывает `ask_external_llm(provider="deepseek",
     compare_with="anthropic", prompt="Что такое X?")`.
   - `AskExternalLlmTool.ExecuteComparisonAsync` делает **2 параллельных**
     запроса (`Task.WhenAll`):
     - DeepSeek (OpenAI-формат).
     - Anthropic (Anthropic-формат).
   - Возвращает `ExternalLlmComparisonDto`:
     `{ primary: {provider: "deepseek", ...}, secondary: {provider: "anthropic", ...},
        totalCostUsd: 0.002 }`.
3. Chat формулирует: «DeepSeek: …. Claude: …. Общее: …».

### Приложение C — расчёт стоимости (Haiku)

| Токенов (in) | Токенов (out) | CostUsd |
|---:|---:|---:|
| 100 | 200 | $0.0001 + $0.001 = **$0.0011** |
| 245 | 512 | $0.000245 + $0.00256 = **$0.002805** |
| 1000 | 2000 | $0.001 + $0.01 = **$0.011** |

При дневном бюджете $5 = **~1780 запросов** (avg 245/512) → исчерпать
сложно, но защита есть.