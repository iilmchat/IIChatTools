# DESIGN v1.10 — Google Gemini

**Версия:** 1.0
**Дата:** 2026-10-01
**Автор:** IIChatTools Team
**Статус:** **Implemented (v1.10.0, 2026-10-01)**
**Связанные KI:** KI-110b (Google Gemini — Fixed в v1.10.0), KI-110a (Anthropic Claude — Fixed v1.9.0), KI-109 (External-LLM Agent — база)
**Целевой релиз:** v1.10.0 ✅ Done

---

## § 1. Контекст

### § 1.1. Текущее состояние (после v1.9.0)

- **`external_llm_agent`** работает: агент + 3 tools (`ask_external_llm`, `list_external_providers`, `check_internet_connection`).
- **6 провайдеров:** DeepSeek, OpenAI, Groq, Together AI, Ollama (OpenAI-формат) + Anthropic Claude (свой формат).
- **`ProviderFormat` enum:** `OpenAI = 0` / `Anthropic = 1` / `Gemini = 2`.
- **`ExternalLlmClient.CompleteAsync`** — switch по `Format`:
  - `OpenAI` → `CompleteOpenAiAsync` (работает);
  - `Anthropic` → `CompleteAnthropicAsync` (работает);
  - `Gemini` → `NotSupportedException` (заглушка, ждёт v1.10.0).
- **Тесты:** 559/559 (4 Skip).
- **Конфиг:** секция `gemini` в `appsettings.json` уже **валидна** для `ExternalProviderRegistry`, но функциональность отсутствует.

### § 1.2. Industry best practices

| Практика | Источник | Применение |
|---|---|---|
| **Adapter pattern** (продолжение) | GoF | Третья ветка `Format` → switch |
| **Stable API** (`v1`, не `v1beta`) | Semantic Versioning + Google | DESIGN выбор: `v1` |
| **x-goog-api-key header** | Google 2024+ | Рекомендуемая аутентификация |
| **Prompt + system отдельно** | Google AI docs | `systemInstruction` — отдельное поле `Content` |
| **Fail-fast на неизвестном Format** | AWS Well-Architected | `NotSupportedException`/`InvalidOperationException` |
| **Privacy-first** (унаследовано) | GDPR | Без PII в логах |

### § 1.3. Цели v1.10.0

| # | Цель | Метрика |
|---|---|---|
| 1 | Поддержка Google Gemini | `ask_external_llm(provider="gemini", ...)` работает |
| 2 | Обратная совместимость | 6 существующих провайдеров без изменений конфига |
| 3 | Минимальный diff | Только `ExternalLlmClient` + 2 новых файла |
| 4 | Тесты по образцу Anthropic | +~25 (builders + parser + client) |
| 5 | Privacy-first | Без PII в логах / audit |

### § 1.4. Что НЕ входит в v1.10.0

- **Streaming** (`streamGenerateContent`) — только non-stream.
- **Function calling / tools** — только prompt → text.
- **Vision** (изображения, audio) — не входит.
- **Cached content** (`cachedContent`) — не входит.
- **Google Search grounding** — не входит.
- **System Instruction через SSE** — не входит.
- **Per-user API keys** — v1.x (по образцу KI-108 для Mail).

---

## § 2. Проблема

### § 2.1. Gemini не OpenAI-совместим

Google Gemini API отличается от OpenAI Chat Completions **по 8 аспектам**:

| Аспект | OpenAI | Gemini |
|---|---|---|
| **Endpoint** | `POST {BaseUrl}/chat/completions` | `POST {BaseUrl}/models/{model}:generateContent` |
| **Auth header** | `Authorization: Bearer <key>` | **`x-goog-api-key: <key>`** |
| **System prompt** | роль `system` в `messages[]` | **отдельное поле** `systemInstruction` (объект `Content`) |
| **`max_tokens` / `maxOutputTokens`** | опционально | **обязателен** (`generationConfig.maxOutputTokens`) |
| **`temperature`** | 0..2 | 0..2 |
| **Request body** | `{model, messages[], temperature, max_tokens}` | `{contents[], systemInstruction?, generationConfig}` |
| **Response text** | `choices[0].message.content` (string) | `candidates[0].content.parts[0].text` (массив частей) |
| **Токены** | `usage.prompt_tokens` / `completion_tokens` | `usageMetadata.promptTokenCount` / `candidatesTokenCount` |
| **Стоп-причина** | `choices[0].finish_reason` | `candidates[0].finishReason` (`STOP`/`MAX_TOKENS`/`SAFETY`/...) |
| **Формат ошибок** | `{error: {message, type, code}}` | `{error: {code, message, status}}` (`code` — числовой HTTP + строковый `status`) |

### § 2.2. Почему нельзя «просто подставить URL»

- Тело запроса — **разное**: system отдельно, `contents[].parts[]` вместо `messages[].content`.
- Заголовок авторизации — **разный** (`x-goog-api-key` вместо `Bearer`).
- Парсинг ответа — **разный**: `candidates[0].content.parts[].text` вместо `choices[0].message.content`.
- **Модель в URL**, а не в body (`/models/{model}:generateContent`).
- Простая подмена `BaseUrl` → 400 Bad Request / 404 Not Found.

### § 2.3. Ограничения текущего `IExternalLlmClient`

- `CompleteGeminiAsync` — заглушка `NotSupportedException`.
- `SendWithRetryAsync` / `SendOnceAsync` — уже параметризованы `IReadOnlyDictionary<string, string> headers` (готово для `x-goog-api-key`).
- `ProviderCostCalculator` — уже per-provider (тарифы Gemini в appsettings).

---

## § 3. Решение

### § 3.1. `GeminiRequestBuilder` (новый)

Файл `Implementation/ExternalLlm/Formats/GeminiRequestBuilder.cs`.

Static helper. Собирает `JObject` для `POST {BaseUrl}/models/{model}:generateContent`:

```json
{
  "contents": [
    {
      "role": "user",
      "parts": [{ "text": "<prompt>" }]
    }
  ],
  "systemInstruction": {
    "parts": [{ "text": "<system>" }]
  },
  "generationConfig": {
    "maxOutputTokens": 8192,
    "temperature": 0.7
  }
}
```

**Правила:**
- `contents` — **всегда** задан, минимум 1 элемент `{role: "user", parts: [{text: prompt}]}`.
- `systemInstruction` — **добавляется, если не пуст**. `role` **не задаём** (Google игнорирует `role` в systemInstruction).
- `generationConfig` — **всегда** задан. `maxOutputTokens` обязателен: `request.MaxTokens ?? provider.MaxTokens`, clamp ≥ 1, default 4096.
- `temperature` — clamp `[0, 2]` (Gemini range). Default 0.7.
- **`model` в URL, не в body** — builder его не возвращает.
- **`stream` — не добавляем** (v1.10.0 — non-stream).
- **URL-шаблон:** `{BaseUrl}/models/{model}:generateContent`. `BaseUrl` = `https://generativelanguage.googleapis.com/v1`.

**Возвращает:** `JObject` (тело) — URL-часть собирает `ExternalLlmClient` (знает про `provider.BaseUrl` и `provider.Model`).

### § 3.2. `GeminiResponseParser` (новый)

Файл `Implementation/ExternalLlm/Formats/GeminiResponseParser.cs`.

Static helper. Парсит `JObject` ответа:

- **`candidates[0].content.parts[]`** — склеиваем все блоки с `text` через `\n`. Пропускаем блоки `thought: true` (v1.10.0 без thinking), `functionCall`, `functionResponse`, `inlineData`, `codeExecutionResult` (не поддерживаются).
- **`usageMetadata.promptTokenCount`** → `promptTokens`.
- **`usageMetadata.candidatesTokenCount`** → `completionTokens`.
- **`candidates[0].finishReason`** → логируется как info (STOP / MAX_TOKENS / SAFETY / RECITATION / OTHER).
- **Пустой `candidates[]`** → возвращаем `("", 0, 0)`. Если `promptFeedback.blockReason` установлен — **не падаем**, возвращаем пустую строку (клиент сам решит, что делать с пустым ответом).
- **Отсутствие `candidates[0].content.parts`** → `("", promptTokens, completionTokens)`.

**Возвращает:** `(string Content, int PromptTokens, int CompletionTokens)`.

### § 3.3. Рефакторинг `ExternalLlmClient.CompleteGeminiAsync`

Заменяем заглушку `NotSupportedException` на реальный метод. Общая обвязка (circuit breaker, budget, cost, audit) — **не меняется**.

```csharp
private async Task<(string Content, int PromptTokens, int CompletionTokens)>
    CompleteGeminiAsync(
        ExternalProviderOptions provider,
        string apiKey,
        ExternalLlmRequest request,
        string providerName,
        CancellationToken ct)
{
    // 1. Собираем тело (helper из § 3.1).
    var payload = GeminiRequestBuilder.Build(provider, request);

    // 2. URL с моделью (в отличие от OpenAI/Anthropic — модель в пути).
    var url = $"{provider.BaseUrl.TrimEnd('/')}/models/{provider.Model}:generateContent";
    var timeoutSec = Math.Clamp(provider.TimeoutSeconds, 1, 600);

    // 3. Заголовки: x-goog-api-key.
    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        headers["x-goog-api-key"] = apiKey;
    }

    var responseJson = await SendWithRetryAsync(url, payload, headers, timeoutSec, ct);

    // 4. Парсер не падает при отсутствии полей (DESIGN § 3.2).
    return GeminiResponseParser.Parse(responseJson);
}
```

**Switch в `CompleteAsync`:** ветка `ProviderFormat.Gemini` → `await CompleteGeminiAsync(...)`. `NotSupportedException` удаляется.

### § 3.4. Обработка `finishReason` / `promptFeedback`

v1.10.0 — **не бросаем исключения** на `SAFETY` / `RECITATION` / `blockReason`. Возвращаем то, что пришло (возможно пустая строка), клиент (`AskExternalLlmTool`) сам решит. Поведение как в Anthropic-ветке (DESIGN v1.9 § 3.5): «не падать при отсутствии полей».

Опционально (в v1.10.x) — логировать `finishReason` и `promptFeedback.blockReason` через `_logger.LogInformation` с `providerName`.

### § 3.5. Что НЕ трогаем

- `ExternalProviderRegistry` — формат не валидирует (уже работает для `Format=Gemini`).
- `ExternalLlmCircuitBreaker` — per-provider, формат не важен.
- `ExternalLlmBudgetTracker` — per-user, формат не важен.
- `AskExternalLlmTool`, `ListExternalProvidersTool`, `CheckInternetConnectionTool` — формат не важен (работают через `IExternalLlmClient`).
- `ExternalLlmAgentTool` — обёртка над агентом.
- `ProviderCostCalculator` — уже works по тарифам из `provider.CostPer1k*`.
- `ChatStreamService`, `Startup.cs`, `Program.cs` — не трогаем.
- `SendWithRetryAsync` / `SendOnceAsync` — уже параметризованы заголовками (готово в v1.9.0 Фаза 3).

---

## § 4. Архитектура

### § 4.1. Новые файлы

```
IIChatTools.Services/
  └── Implementation/ExternalLlm/
      └── Formats/                          (существующая папка)
          ├── GeminiRequestBuilder.cs        — static helper (новый)
          └── GeminiResponseParser.cs        — static helper (новый)

IIChatTools.Tests/
  └── UnitTests/ExternalLlm/Formats/         (существующая папка)
      ├── GeminiRequestBuilderTests.cs       — ~8 тестов
      └── GeminiResponseParserTests.cs       — ~8 тестов
```

### § 4.2. Изменения в существующих файлах

| Файл | Что | Строк diff |
|---|---|---|
| `Implementation/ExternalLlm/ExternalLlmClient.cs` | Заменить заглушку `NotSupportedException` на вызов `CompleteGeminiAsync` + новый приватный метод | ~50 |
| `IIChatTools.Tests/UnitTests/ExternalLlm/ExternalLlmClientTests.cs` | Заменить тест `CompleteAsync_GeminiFormat_ThrowsNotSupported` на позитивный `..._UsesXGoogApiKeyHeader` + `..._UsesGenerateContentEndpoint` + `..._ParsesCandidates` | ~120 |
| `IIChatTools.Tests/IntegrationTests/ExternalLlm/ExternalLlmIntegrationTests.cs` | +1 `[Fact(Skip=...)]` для Gemini | ~45 |
| `Directory.Build.props` | `<Version>1.9.0 → 1.10.0` (при релизе) | ~5 |
| `CHANGELOG.md` | Релизная секция (при релизе) | — |
| `KNOWN_ISSUES.md` | KI-110b → Fixed (v1.10.0) | — |

### § 4.3. Sequence — `CompleteGeminiAsync`

```
1. Проверка circuit breaker        (общая обвязка, как v1.8.1)
2. Проверка бюджета (per-user)     (общая обвязка)
3. Резолв API-ключа                 (общая обвязка)
4. GeminiRequestBuilder.Build(provider, request)
   → JObject {contents[], systemInstruction?, generationConfig}
5. HTTP POST {BaseUrl}/models/{Model}:generateContent:
     Headers:
       x-goog-api-key: <apiKey>
       content-type: application/json
     Body: см. шаг 4
   Retry 1× при 5xx/429 (та же SendWithRetryAsync)
   Timeout — CancellationTokenSource.CancelAfter (RULES § 4.48)
6. GeminiResponseParser.Parse(responseJson)
   → (content, promptTokens, completionTokens)
7. RecordSuccess + RecordUsage   (общая обвязка)
8. Return ExternalLlmResponse {
     Provider, Content, PromptTokens, CompletionTokens, CostUsd, DurationMs
   }
```

---

## § 5. Конфигурация

### § 5.1. `appsettings.json` — секция `gemini` (уже есть после v1.9.0 Фаза 4)

```jsonc
"gemini": {
  "DisplayName": "Google Gemini (v1.10.0)",
  "Format": "Gemini",
  "BaseUrl": "https://generativelanguage.googleapis.com/v1",   // v1 (stable)
  "Model": "gemini-2.0-flash",
  "ApiKeySecretName": "ExternalLlm:Gemini:ApiKey",
  "CostPer1kInputUsd": 0.0001,    // $0.10 / 1M tokens (input)
  "CostPer1kOutputUsd": 0.0004,   // $0.40 / 1M tokens (output)
  "MaxTokens": 8192,
  "TimeoutSeconds": 60
}
```

**Изменение от v1.9.0:** `BaseUrl` содержит `/v1` (было `/v1beta`). Остальное — без изменений.

### § 5.2. User Secrets

```
cd C:\Projects\AI\IIChatTools\IIChatTools.API

# Google Gemini (требует VPN из РФ для регистрации; запросы с российских IP блокируются)
dotnet user-secrets set "ExternalLlm:Gemini:ApiKey" "AIza..."
```

**`ExternalLlm:Enabled`** — уже есть (v1.8.1). Не трогаем.

### § 5.3. Как получить API key (RU-специфика)

**Google Gemini:**

1. **VPN обязателен** — регистрация и API-запросы блокируются по IP РФ.
2. https://aistudio.google.com/app/apikey — регистрация через Google-аккаунт.
3. **Free tier доступен** (15 RPM, 1500 req/day для `gemini-2.0-flash`).
4. API key: «Create API key» → формат `AIza...`.
5. **Заголовок `x-goog-api-key`** — задаётся в коде (не в конфиге).

**Куда записать:** README (раздел «External-LLM Agent» → «Получить ключи»),
+ этот DESIGN § 5.3.

---

## § 6. Безопасность

### § 6.1. API key — `x-goog-api-key` header

Gemini API ожидает ключ в заголовке `x-goog-api-key` (рекомендовано Google с mid-2024),
а не в query-параметре `?key=`. Реализация — в `CompleteGeminiAsync`:

```csharp
headers["x-goog-api-key"] = apiKey;
```

**Не логируется** (как v1.8.1). `ILogger` — только метаданные.

**Альтернатива (не используем):** `?key=<apiKey>` в URL. Deprecated, ключ попадает в логи прокси / историю браузера / access-логи.

### § 6.2. HTTPS — уже enforced

`ExternalProviderRegistry.ValidateProviderOrThrow` требует `https://` для
не-localhost. Gemini — публичный домен → `https://` обязателен.

### § 6.3. Privacy — без изменений

- **Не логируется** prompt / content (уже v1.8.1).
- **AuditLog** — только метаданные (`provider`, `promptLength`, `tokens`, `cost`).
- **`include_context: false` по умолчанию** — Gemini тоже.

### § 6.4. Сводная таблица угроз

| Угроза | Защита | Обходится? |
|---|---|---|
| Утечка API key | User Secrets / env + `x-goog-api-key` (не в URL) | ❌ |
| Превышение бюджета | `DailyBudgetUsd` + `DailyTokensLimit` | ❌ |
| Утечка PII | `include_context: false` по умолчанию | ⚠️ при `true` |
| Man-in-the-middle | HTTPS only | ❌ |
| Зависание при недоступности | Circuit breaker + Timeout | ❌ |
| Неверный формат | Fail-fast в `CompleteAsync` switch | ❌ |

---

## § 7. План фаз (0–5)

**Оценка:** ~5 ч.

| Фаза | Что | Оценка | Статус |
|:---:|---|:---:|:---:|
| **0** | DESIGN (этот документ) | 1 ч | ✅ Done (`bba6390`) |
| **1** | `GeminiRequestBuilder` + тесты | 1 ч | ✅ Done (`28d156a`) |
| **2** | `GeminiResponseParser` + тесты | 1 ч | ✅ Done (`08d36b9`) |
| **3** | Рефакторинг `ExternalLlmClient` (switch → `CompleteGeminiAsync`) + тесты | 1 ч | ✅ Done (`e448406`) |
| **4** | `appsettings.json` (BaseUrl v1beta → v1) + README + `.Development.json` | 0.5 ч | ✅ Done (`edf13f5`) |
| **5** | Релиз v1.10.0 (CHANGELOG, KNOWN_ISSUES, tag, Release) | 0.5 ч | ✅ Done |

### § 7.1. Фаза 1 — `GeminiRequestBuilder` (1 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 1.1 | `GeminiRequestBuilder.cs` | `GeminiRequestBuilderTests` (~8) |

**DoD:** `dotnet build` 0/0, тесты зелёные.

### § 7.2. Фаза 2 — `GeminiResponseParser` (1 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 2.1 | `GeminiResponseParser.cs` | `GeminiResponseParserTests` (~8) |

**DoD:** `dotnet build` 0/0, тесты зелёные.

### § 7.3. Фаза 3 — Client (1 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 3.1 | `ExternalLlmClient.cs` — switch: `Gemini` → `CompleteGeminiAsync` | `ExternalLlmClientTests` — обновить Gemini-тест |
| 3.2 | `CompleteGeminiAsync` — новый метод | +4 новых теста |
| 3.3 | `ExternalLlmIntegrationTests` — +1 `[Fact(Skip=...)]` для Gemini | — |

**DoD:** `dotnet build` 0/0, `dotnet test` **563 → ~576** (4 Skip → 5 Skip).

### § 7.4. Фаза 4 — Config + README (0.5 ч)

| Шаг | Что |
|---|---|
| 4.1 | `appsettings.json` — `BaseUrl` `v1beta` → `v1` |
| 4.2 | `appsettings.Development.json` — то же |
| 4.3 | README — раздел «External-LLM Agent» → +Gemini (получение ключа, free tier, RU-специфика) |

### § 7.5. Фаза 5 — Релиз v1.10.0 (0.5 ч)

| Шаг | Что |
|---|---|
| 5.1 | CHANGELOG `[Unreleased]` → `[1.10.0] — 2026-MM-DD` |
| 5.2 | KNOWN_ISSUES: KI-110b → Fixed (v1.10.0) |
| 5.3 | `Directory.Build.props` — `<Version>1.10.0</Version>` + `Copyright` |
| 5.4 | `docs/development/v1.9/DESIGN_GEMINI.md` → **Implemented** |
| 5.5 | Tag `v1.10.0` + GitHub Release |

---

## § 8. Definition of Done (v1.10.0)

### § 8.1. Функциональные требования

- [x] `GeminiRequestBuilder` — собирает Gemini-body.
- [x] `GeminiResponseParser` — парсит `candidates[].content.parts[]` + `usageMetadata`.
- [x] `ExternalLlmClient.CompleteGeminiAsync` — `POST /models/{model}:generateContent`.
- [x] `x-goog-api-key` header (не Bearer).
- [x] `NotSupportedException` заглушка удалена из switch.
- [x] `appsettings.json` / `.Development.json` — `BaseUrl` → `v1`.
- [x] 6 существующих провайдеров работают **без изменений конфига**.

### § 8.2. Нефункциональные

- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — **~576/576** (+13).
- [ ] CI + Docker Publish — зелёные.
- [ ] RULES § 8 — обновлён (если есть новые уроки).
- [ ] CHANGELOG `[1.10.0] — YYYY-MM-DD`.
- [ ] README — раздел «External-LLM Agent» + Gemini.
- [ ] DESIGN → **Implemented**.

### § 8.3. Smoke (5 сценариев)

| # | Сценарий | Ожидание |
|---|---|---|
| 1 | `list_external_providers` | Gemini в списке; `available: true` |
| 2 | `ask_external_llm(provider="gemini", prompt="2+2")` | Ответ от Gemini |
| 3 | `ask_external_llm(provider="deepseek", compare_with="gemini", ...)` | 2 ответа, `totalCostUsd` |
| 4 | Отключить интернет → `ask_external_llm(provider="gemini")` | Circuit breaker + Fail «временно недоступен» |
| 5 | `ask_external_llm(provider="gemini")` без `ExternalLlm:Gemini:ApiKey` | `ToolResult.Fail` «API-ключ не найден» |

### § 8.4. Документация

- [ ] README.md — Gemini в разделе External-LLM.
- [ ] CHANGELOG.md — `[1.10.0]`.
- [ ] KNOWN_ISSUES.md — KI-110b → Fixed.
- [ ] RULES.md — § 8 (если есть новые уроки).

---

## § 9. Ссылки

### § 9.1. KI

- **KI-110b** — Google Gemini (этот документ, v1.10.0).
- **KI-110a** — Anthropic Claude (v1.9.0, Fixed).
- **KI-109** — External-LLM Agent (v1.8.1, база).

### § 9.2. Внешние источники

- [Gemini API: Generating content](https://ai.google.dev/api/generate-content) — эталон (`generateContent`).
- [Gemini API: Authentication](https://ai.google.dev/gemini-api/docs/api-key) — `x-goog-api-key`.
- [Gemini API: Pricing](https://ai.google.dev/pricing) — тарифы.
- [Gemini API: Errors](https://ai.google.dev/gemini-api/docs/troubleshooting) — формат ошибок.

### § 9.3. Внутренние документы

- `docs/development/v1.9/DESIGN_ANTHROPIC_GEMINI.md` — база (v1.9.0, Anthropic + Gemini-заглушка).
- `docs/development/v1.8/DESIGN_EXTERNAL_LLM.md` — оригинальный External-LLM Agent.
- `docs/development/RULES.md` — правила (v1.4.26).
- `docs/KNOWN_ISSUES.md` — реестр.

---

## § 10. Приложения

### Приложение A — пример Gemini-запроса / ответа

**Запрос:**

```http
POST https://generativelanguage.googleapis.com/v1/models/gemini-2.0-flash:generateContent
x-goog-api-key: AIza...
content-type: application/json

{
  "contents": [
    {
      "role": "user",
      "parts": [{ "text": "Сколько будет 2+2? Ответь одним словом." }]
    }
  ],
  "systemInstruction": {
    "parts": [{ "text": "Ты — лаконичный ассистент." }]
  },
  "generationConfig": {
    "maxOutputTokens": 8192,
    "temperature": 0.7
  }
}
```

**Ответ:**

```json
{
  "candidates": [
    {
      "content": {
        "role": "model",
        "parts": [{ "text": "Четыре" }]
      },
      "finishReason": "STOP",
      "index": 0
    }
  ],
  "usageMetadata": {
    "promptTokenCount": 20,
    "candidatesTokenCount": 3,
    "totalTokenCount": 23
  },
  "modelVersion": "gemini-2.0-flash-001",
  "responseId": "..."
}
```

**Парсинг:**
- `candidates[0].content.parts[0].text` → `"Четыре"`.
- `usageMetadata.promptTokenCount` → `promptTokens = 20`.
- `usageMetadata.candidatesTokenCount` → `completionTokens = 3`.
- `CostUsd = (20/1000)*0.0001 + (3/1000)*0.0004 = 0.000002 + 0.0000012 = 0.0000032`.

### Приложение B — пример `compare_with="gemini"` через LLM

Идентично Anthropic (DESIGN v1.9 Приложение B). `AskExternalLlmTool.ExecuteComparisonAsync` делает 2 параллельных запроса (`Task.WhenAll`) — один OpenAI-совместимый, второй Gemini.

### Приложение C — расчёт стоимости (gemini-2.0-flash)

| Токенов (in) | Токенов (out) | CostUsd |
|---:|---:|---:|
| 100 | 200 | $0.00001 + $0.00008 = **$0.00009** |
| 245 | 512 | $0.0000245 + $0.0002048 = **$0.0002293** |
| 1000 | 2000 | $0.0001 + $0.0008 = **$0.0009** |

При дневном бюджете $5 = **~21800 запросов** (avg 245/512). Gemini дешевле Anthropic Haiku в ~15 раз для output.

---

**Конец DESIGN_GEMINI.md**