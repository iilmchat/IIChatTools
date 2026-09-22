# Дизайн-документ v1.3 «Conversational»

**Статус**: Draft для утверждения
**Дата**: 2026-09-21
**Автор**: IIChatTools Development
**Базовая версия**: v1.2.0 «Foundation»

---

## 1. Цель релиза

Превратить IIChatTools из «инструмента для отладки через /test» в **полноценный чат-интерфейс с LLM**, где пользователь общается в браузере, а LLM сама вызывает инструменты по необходимости.

**Killer features**:
- 🚀 Chat UI с историей диалогов
- 🌊 Стриминг ответов LM Studio (мгновенный отклик)
- 🛠 Автоматический tool calling (LLM сама вызывает инструменты)
- 📜 Персистентная история (в БД)

**Не входит в v1.3** (отложено в v1.4 «Knowledge»):
- RAG / embeddings
- Multi-agent orchestration
- Qdrant

---

## 2. Use Cases (сценарии пользователя)

### UC-1: Простой вопрос
1. Пользователь открывает `/chat`.
2. Вводит: «Какая погода в Москве?».
3. LLM отвечает стримингом.
4. Диалог сохранён в истории.

### UC-2: Вопрос с tool calling
1. Пользователь: «Покажи файлы в workspace».
2. LLM: «Я вызову list_directory...» → инструмент → результат → финальный ответ.
3. Пользователь видит **вызов инструмента** в UI (сворачиваемый блок).
4. LLM продолжает диалог.

### UC-3: Tool с подтверждением
1. Пользователь: «Закоммить изменения с сообщением "test"».
2. LLM вызывает `git_commit` → требует approval.
3. Появляется **модалка подтверждения** (уже есть в v1.2 через `_ApprovalModal`).
4. Пользователь подтверждает → инструмент выполняется.
5. LLM завершает ответ.

### UC-4: История диалогов
1. Пользователь возвращается через день.
2. Видит список чатов в sidebar.
3. Клик → загружается история + контекст.
4. Продолжает диалог.

### UC-5: Multi-turn с инструментами
1. Пользователь: «Прочитай README.md, найди упоминания .NET и посчитай их».
2. LLM: `read_file` → анализ → `execute_command` (grep) → ответ.
3. Всё в одном диалоге с видимыми tool calls.

---

## 3. Архитектура

### 3.1. Схема

```
┌──────────────────────────────────────────────────────────────┐
│                    Browser (Chat UI)                          │
│  ┌────────────────────┐  ┌──────────────────────────────────┐ │
│  │  Sidebar (чаты)    │  │  Chat area                       │ │
│  │  - список чатов    │  │  - лента сообщений               │ │
│  │  - "Новый чат"     │  │  - tool calls (сворачиваемые)    │ │
│  │                    │  │  - streaming text                 │ │
│  │                    │  │  - input + send                  │ │
│  └────────────────────┘  └──────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
                              ↕ HTTP + SSE
┌──────────────────────────────────────────────────────────────┐
│                    IIChatTools.API                            │
│  ┌────────────────────┐  ┌──────────────────────────────────┐ │
│  │  ChatController    │  │  ChatStreamController (SSE)      │ │
│  │  - GET /api/chats  │  │  - POST /api/chat/stream         │ │
│  │  - GET /api/chats/ │  │    (Server-Sent Events)          │ │
│  │       {id}/messages│  │                                  │ │
│  │  - DELETE /api/... │  │                                  │ │
│  └────────────────────┘  └──────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
                              ↕ DI
┌──────────────────────────────────────────────────────────────┐
│                    IIChatTools.Services                       │
│  ┌────────────────────┐  ┌──────────────────────────────────┐ │
│  │  ChatService       │  │  LmStudioClient (расширенный)    │ │
│  │  - CreateChatAsync │  │  - ChatStreamAsync (SSE)         │ │
│  │  - AddMessageAsync │  │  - ChatWithToolsAsync            │ │
│  │  - GetHistoryAsync │  │                                  │ │
│  │  - SendMessageAsync│  │                                  │ │
│  └────────────────────┘  └──────────────────────────────────┘ │
│                              ↓                                 │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │  ToolRegistry (уже есть)                                 │ │
│  │  Используется для tool calling в чате                    │ │
│  └──────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
                              ↕ EF Core
┌──────────────────────────────────────────────────────────────┐
│                    IIChatTools.Data                           │
│  ┌────────────────────┐  ┌──────────────────────────────────┐ │
│  │  Chat (новая)      │  │  ChatMessage (новая)             │ │
│  │  - Id              │  │  - Id                            │ │
│  │  - UserId          │  │  - ChatId                        │ │
│  │  - Title           │  │  - Role (user/assistant/tool)    │ │
│  │  - CreatedAt       │  │  - Content                       │ │
│  │  - UpdatedAt       │  │  - ToolCallsJson                 │ │
│  │  - Model           │  │  - ToolCallId                    │ │
│  └────────────────────┘  │  - CreatedAt                     │ │
│                          └──────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 3.2. Потоки данных

#### Поток 1: Отправка сообщения (SSE)

```
1. User вводит "Какая погода в Москве?" → JS POST /api/chat/stream
2. ChatController.SendStreamAsync:
   a. Сохраняет user message в БД
   b. Загружает историю чата
   c. Открывает SSE response
   d. Вызывает LmStudioClient.ChatStreamAsync(history, tools)
3. LmStudioClient отправляет запрос к LM Studio /v1/chat/completions (stream=true)
4. LM Studio стримит tokens → LmStudioClient парсит SSE → ChatController
5. ChatController отправляет SSE events в браузер:
   - event: message → data: { delta: "Погода" }
   - event: message → data: { delta: " в Москве" }
   - event: tool_call → data: { name: "web_search", args: {...} }
   - event: tool_result → data: { name: "web_search", result: {...} }
   - event: done → data: { messageId: 123 }
6. JS в браузере:
   - Стримит токены в UI
   - При tool_call — визуализирует блок
   - При done — добавляет message в ленту
7. ChatController сохраняет финальный assistant message в БД
```

#### Поток 2: Tool calling

```
1. LM Studio (при стриминге или в ответе) отдаёт tool_calls:
   { "tool_calls": [{ "id": "call_1", "function": { "name": "read_file", "arguments": "{...}" } }] }
2. ChatService:
   a. Парсит tool_calls
   b. Для каждого — вызывает ToolRegistry.ExecuteAsync (с approval, если нужно)
   c. Результат передаёт обратно в LM Studio как "role": "tool"
   d. Повторяет запрос (multi-turn)
3. Loop продолжается, пока LM Studio не отдаст финальный текст (без tool_calls)
4. Максимум N итераций (защита от зацикливания) — 10 по умолчанию
```

---

## 4. Модель данных

### 4.1. Сущность `Chat`

**Путь**: `IIChatTools.Data/Entities/Chat.cs`

```csharp
/// <summary>
/// Чат (диалог) пользователя с LLM.
/// </summary>
public class Chat : BaseEntity
{
    /// <summary>Владелец чата.</summary>
    public int UserId { get; set; }

    /// <summary>Навигация на пользователя.</summary>
    public virtual ApplicationUser User { get; set; }

    /// <summary>Заголовок чата (генерируется LLM из первого сообщения).</summary>
    public string Title { get; set; }

    /// <summary>Модель LM Studio, использованная в этом чате.</summary>
    public string Model { get; set; }

    /// <summary>Дата последнего изменения.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Все сообщения этого чата.</summary>
    public virtual ICollection<ChatMessage> Messages { get; set; }
}
```

### 4.2. Сущность `ChatMessage`

**Путь**: `IIChatTools.Data/Entities/ChatMessage.cs`

```csharp
/// <summary>
/// Сообщение в чате.
/// </summary>
public class ChatMessage : BaseEntity
{
    /// <summary>Идентификатор чата.</summary>
    public int ChatId { get; set; }

    /// <summary>Навигация на чат.</summary>
    public virtual Chat Chat { get; set; }

    /// <summary>Роль: "user", "assistant", "system", "tool".</summary>
    public string Role { get; set; }

    /// <summary>Текстовое содержимое.</summary>
    public string Content { get; set; }

    /// <summary>Tool calls (JSON-массив, если assistant вызвал инструменты).</summary>
    public string ToolCallsJson { get; set; }

    /// <summary>ID tool call (для role="tool" — ответ на конкретный вызов).</summary>
    public string ToolCallId { get; set; }

    /// <summary>Имя инструмента (для role="tool").</summary>
    public string ToolName { get; set; }

    /// <summary>Токены (prompt + completion), опционально.</summary>
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
}
```

### 4.3. Миграция

```
dotnet ef migrations add AddChatAndChatMessages \
    --project IIChatTools.Data \
    --startup-project IIChatTools.API \
    --output-dir Migrations/SqlServer
```

---

## 5. API-контракты

### 5.1. `GET /api/chats` — список чатов пользователя

**Response**:
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "title": "Погода в Москве",
      "model": "qwen/qwen3-4b-2507",
      "messageCount": 4,
      "createdAt": "2026-09-21T14:00:00Z",
      "updatedAt": "2026-09-21T14:05:00Z"
    }
  ]
}
```

### 5.2. `GET /api/chats/{id}` — сообщения чата

**Response**:
```json
{
  "success": true,
  "data": {
    "id": 1,
    "title": "Погода в Москве",
    "model": "qwen/qwen3-4b-2507",
    "messages": [
      { "id": 1, "role": "user", "content": "Какая погода в Москве?", "createdAt": "..." },
      { "id": 2, "role": "assistant", "content": "...", "toolCalls": [...], "createdAt": "..." },
      { "id": 3, "role": "tool", "toolName": "web_search", "content": "{...}", "createdAt": "..." },
      { "id": 4, "role": "assistant", "content": "Погода в Москве: +15°C...", "createdAt": "..." }
    ]
  }
}
```

### 5.3. `POST /api/chats` — создать чат

**Request**:
```json
{ "model": "qwen/qwen3-4b-2507" }
```

**Response**:
```json
{ "success": true, "data": { "id": 1, "title": "Новый чат", "model": "..." } }
```

### 5.4. `DELETE /api/chats/{id}` — удалить чат

### 5.5. `POST /api/chat/stream` — отправить сообщение + стриминг

**Request**:
```json
{
  "chatId": 1,
  "message": "Какая погода в Москве?",
  "useTools": true
}
```

**Response (SSE)**: `Content-Type: text/event-stream`

```
event: start
data: {"userMessageId": 5, "chatId": 1}

event: delta
data: {"text": "Погода"}

event: delta
data: {"text": " в Москве"}

event: tool_call
data: {"id": "call_1", "name": "web_search", "args": {"query": "погода Москва"}}

event: tool_approval
data: {"actionId": 42, "toolName": "web_search"}

event: tool_result
data: {"id": "call_1", "name": "web_search", "result": {"success": true, "data": {...}}}

event: delta
data: {"text": "Сейчас в Москве +15°C, ясно."}

event: done
data: {"assistantMessageId": 6, "tokensIn": 120, "tokensOut": 45}

event: error
data: {"message": "Ошибка LM Studio: connection refused"}
```

---

## 6. Chat UI (Razor + JS)

### 6.1. Страница `/chat`

**Путь**: `IIChatTools.API/Views/Chat/Index.cshtml`

**Layout**:
```
┌───────────────────────────────────────────────────────────┐
│  Header: IIChatTools v1.3.0 | Главная | Chat | Status     │
├──────────┬────────────────────────────────────────────────┤
│ Sidebar  │  Chat area                                     │
│          │                                                │
│ [Новый]  │  ┌──────────────────────────────────────────┐  │
│          │  │ User: Покажи файлы                       │  │
│ • Чат 1  │  ├──────────────────────────────────────────┤  │
│ • Чат 2  │  │ Assistant: [Вызываю list_directory...]  │  │
│ • Чат 3  │  │ ▶ tool_call: list_directory              │  │
│          │  │   Files: .tmp, git-test                  │  │
│          │  │ В workspace 2 папки.                     │  │
│          │  └──────────────────────────────────────────┘  │
│          │                                                │
│          │  ┌──────────────────────────────────────────┐  │
│          │  │ Введите сообщение...              [→]    │  │
│          │  └──────────────────────────────────────────┘  │
└──────────┴────────────────────────────────────────────────┘
```

### 6.2. ES-модуль `chat.js`

**Путь**: `IIChatTools.API/wwwroot/js/modules/chat.js`

**Ответственности**:
- Инициализация: загрузка списка чатов, открытие последнего.
- Обработка отправки: SSE через `fetch` + `ReadableStream` (не EventSource — нужен POST).
- Стриминг: рендеринг токенов по мере поступления.
- Tool calls: сворачиваемые блоки.
- Approvals: интеграция с существующей модалкой.
- Сохранение черновиков.

**SSE через fetch**:
```javascript
const response = await fetch('/api/chat/stream', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ chatId, message, useTools: true })
});

const reader = response.body.getReader();
const decoder = new TextDecoder();
let buffer = '';

while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    // Парсинг SSE: разделитель \n\n, поля event: / data:
    const events = buffer.split('\n\n');
    buffer = events.pop();
    for (const evt of events) {
        const lines = evt.split('\n');
        let eventName = 'message', data = '';
        for (const line of lines) {
            if (line.startsWith('event:')) eventName = line.slice(6).trim();
            else if (line.startsWith('data:')) data += line.slice(5).trim();
        }
        handleSSE(eventName, JSON.parse(data));
    }
}
```

---

## 7. Расширение `LmStudioClient`

### 7.1. Новые методы

**Путь**: `IIChatTools.Services/Interfaces/ILmStudioClient.cs`

```csharp
/// <summary>
/// Стримит ответ от LM Studio через SSE.
/// </summary>
/// <param name="messages">История сообщений</param>
/// <param name="tools">Определения инструментов (опционально)</param>
/// <param name="cancellationToken">Токен отмены</param>
/// <returns>Поток чанков (SSE)</returns>
IAsyncEnumerable<LmStudioStreamChunk> ChatStreamAsync(
    IReadOnlyList<LmStudioMessage> messages,
    IReadOnlyList<LmStudioToolDefinition> tools,
    CancellationToken cancellationToken);

/// <summary>
/// Отправляет сообщение без стриминга (для тестов).
/// </summary>
Task<LmStudioChatResponse> ChatAsync(
    IReadOnlyList<LmStudioMessage> messages,
    IReadOnlyList<LmStudioToolDefinition> tools,
    CancellationToken cancellationToken);
```

### 7.2. DTO

**Путь**: `IIChatTools.Services/DTO/LmStudio/LmStudioStreamChunk.cs`

```csharp
public class LmStudioStreamChunk
{
    public string Delta { get; set; }              // Текст
    public List<LmStudioToolCall> ToolCalls { get; set; }  // Если LLM вызвала инструменты
    public bool IsDone { get; set; }
    public string FinishReason { get; set; }       // "stop", "tool_calls", "length"
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
}
```

---

## 8. Схема БД (migration summary)

```
Chats:
├── Id              (PK, int)
├── UserId          (FK → AspNetUsers, int)
├── Title           (nvarchar(200))
├── Model           (nvarchar(100))
├── CreatedAt       (datetime2)
├── UpdatedAt       (datetime2)
└── ConcurrencyStamp (для optimistic concurrency)

ChatMessages:
├── Id              (PK, int)
├── ChatId          (FK → Chats, int)
├── Role            (nvarchar(20): user/assistant/system/tool)
├── Content         (nvarchar(max))
├── ToolCallsJson   (nvarchar(max), null)
├── ToolCallId      (nvarchar(100), null)
├── ToolName        (nvarchar(100), null)
├── TokensIn        (int, null)
├── TokensOut       (int, null)
├── CreatedAt       (datetime2)
└── UpdatedAt       (datetime2)

Indexes:
- IX_Chats_UserId_UpdatedAt
- IX_ChatMessages_ChatId_CreatedAt
```

---

## 9. План работ

### Фаза 1: Backend (1-1.5 недели)

| # | Задача | Оценка |
|---|--------|--------|
| 1.1 | Сущности `Chat`, `ChatMessage` + миграция | 3 ч |
| 1.2 | `IChatService` / `ChatService` — CRUD + Send | 1 день |
| 1.3 | Расширение `LmStudioClient`: `ChatStreamAsync` (SSE), `ChatAsync` | 2 дня |
| 1.4 | `ChatController` — REST (список, get, create, delete) | 4 ч |
| 1.5 | `ChatStreamController` — SSE endpoint | 1 день |
| 1.6 | Tool calling в `ChatService` (loop с ToolRegistry) | 2 дня |
| 1.7 | Интеграция с approvals (переиспользование `_ApprovalModal`) | 4 ч |
| 1.8 | Тесты (unit + integration) | 2 дня |

### Фаза 2: Frontend (1-1.5 недели)

| # | Задача | Оценка |
|---|--------|--------|
| 2.1 | Razor-страница `/chat` + Layout | 4 ч |
| 2.2 | `chat.js` — SSE-клиент, рендеринг стриминга | 2 дня |
| 2.3 | Sidebar: список чатов, создание, удаление | 1 день |
| 2.4 | Tool call blocks (сворачиваемые) | 4 ч |
| 2.5 | Интеграция с `_ApprovalModal` | 4 ч |
| 2.6 | Стили (site.css + chat-специфичные) | 1 день |
| 2.7 | Локализация RU/EN | 4 ч |

### Фаза 3: Дополнительно (0.5-1 неделя)

| # | Задача | Оценка |
|---|--------|--------|
| 3.1 | SignalR вместо polling для approvals | 2 дня |
| 3.2 | Hot-reload настроек (`IOptionsMonitor`) | 4 ч |
| 3.3 | Метрики для чата: `iichattools_chat_messages_total`, `iichattools_chat_stream_duration_seconds` | 4 ч |
| 3.4 | Test coverage → 70% | 1-2 дня |

**Итого**: 3-4 недели.

---

## 10. Риски и решения

| Риск | Вероятность | Митигация |
|------|-------------|-----------|
| LM Studio не поддерживает SSE в нашей версии | 🟡 Средняя | Проверить `/v1/chat/completions?stream=true`; fallback на polling |
| Долгие tool calls блокируют стриминг | 🟠 Высокая | Параллельные вызовы через `Task.WhenAll`; отдельные события tool_result |
| Утечка памяти в SSE при обрыве соединения | 🟡 Средняя | `CancellationToken` + `finally` cleanup; heartbeat события |
| LLM зациклится в tool calling | 🟡 Средняя | Max iterations = 10; circuit breaker при 3 ошибках подряд |
| Размер истории чата растёт | 🟢 Низкая | Пагинация через `?limit=50&offset=0`; retention 90 дней |

---

## 11. Что НЕ делаем в v1.3

- ❌ RAG / Qdrant
- ❌ Multi-agent orchestration (кроме существующего `SubAgent`)
- ❌ Голосовой ввод
- ❌ Загрузка файлов в чат (attachments)
- ❌ Публичный доступ (только для авторизованных)

---

## 12. Критерии готовности (DoD)

v1.3 считается готовым, когда:

- [ ] Пользователь может создать чат, отправить сообщение, получить ответ стримингом
- [ ] LLM автоматически вызывает инструменты (tool calling)
- [ ] Tool calls видны в UI (сворачиваемые блоки)
- [ ] Approvals работают из чата
- [ ] История чатов сохраняется и загружается
- [ ] RU/EN локализация
- [ ] Build 0/0, Tests ≥ 30, Coverage ≥ 70%
- [ ] CI + Docker Publish зелёные
- [ ] CHANGELOG + README + DESIGN обновлены
- [ ] Manual smoke-test: 5 use cases из раздела 2

---

## 13. Утверждённые решения

**Утверждено 2026-09-21**:

| # | Вопрос | Решение |
|---|--------|---------|
| 1 | **Chat UI layout** | ✅ Sidebar (как ChatGPT) |
| 2 | **Streaming protocol** | ✅ SSE (Server-Sent Events) |
| 3 | **Tool calls отображение** | ✅ Инлайн-блоки (сворачиваемые) |
| 4 | **Модель** | ✅ UI-селектор (dropdown) + модель по умолчанию в настройках пользователя |
| 5 | **Retention чатов** | ✅ Авто (N дней, настраивается) + вручную |
| 6 | **SignalR для approvals** | ✅ Отложено в v1.3.x |

### 13.1. UI-селектор модели

- **Расположение**: header chat area, справа от названия чата.
- **Источник данных**: `GET /api/models` — список из LM Studio (`/v1/models`) + наши сохранённые.
- **Значение по умолчанию**:
  - Хранится в `AppSettings` (уже есть) с ключом `Chat.DefaultModel`.
  - При создании нового чата — берётся значение по умолчанию.
  - При выборе в UI — обновляет `Chat.Model` (только для текущего чата).
- **Fallback**: если LM Studio недоступен — показывает `LmStudio:Model` из конфига.

### 13.2. Retention чатов

**Схема:**
- `Audit`-style retention для чатов:
  - Ключи в `appsettings.json`: `Chat:RetentionDays:Default` (30), `Chat:RetentionDays:Max` (365), `Chat:RetentionDays:Enabled` (true).
  - Фоновый сервис `ChatRetentionService` (по аналогии с `AuditRetentionService`).
  - Удаляет `Chat` + все его `ChatMessage` старше N дней.
  - У пользователя — своя retention настройка (`AppSettings`, override глобальной).
- **Ручное удаление**: контекстное меню в sidebar (три точки → «Удалить»).
- **Метрика**: `iichattools_chat_cleanup_total` (счётчик удалённых чатов).
- **Логи**: `ILogger` — `Удалено {Count} чатов старше {Cutoff}`.

### 13.3. SignalR — v1.3.x

Отложено по причинам:
- Фокус v1.3.0 — Chat UI + SSE + Tool calling.
- SignalR — оптимизация (замена polling для `/api/approvals/pending`).
- В v1.3.x — отдельная задача с дизайн-ревью.

---

## 14. Реализация (Фаза 2 «Chat UI» — Implemented 2026-09-22)

**Статус документа:** Draft → **Implemented**
**Дата закрытия Фазы 2:** 2026-09-22

### 14.1. Что реализовано

Все пункты § 9 «План работ → Фаза 2» закрыты.

| Шаг | Коммит | Что |
|-----|--------|-----|
| 2.0.1 | `3f660fb` | Каркас `/chat`: `ChatViewController` + Razor + `chat.css` + локализация (16 ключей). |
| 2.0.2a | `6902a18` | `GET /api/models` — список моделей LM Studio + default. |
| 2.0.2b | `b9f3e7e`, `a69d5c4` | Sidebar: загрузка, создание, удаление, переключение, sync `?chatId=N`. |
| 2.0.3 | `3a53bb5` | SSE-стриминг: `sendMessage`, `readSseStream`, `handleSseEvent`. |
| 2.0.4 | `43b8aa1`, `a79c7ed` | Approvals: модалка, drag-and-drop, X = Reject. |
| 2.0.5a | `168f8f1` | Переименование чатов (✏️) + авто-нумерация «Новый чат N». |
| 2.0.5b | `e763b31` | ChatGPT-style скроллинг (кнопка «↓ Вниз», флаг `autoScroll`). |
| 2.0.6a | — | KI-046: `MessageCount` в `/api/chats`. |

### 14.2. Расхождения с Draft-версией DESIGN.md

Реализация шла эволюционно — некоторые решения в Draft оказались неточными. Финальный вид:

| Раздел DESIGN | Draft | Реализация |
|---|---|---|
| § 5.5 SSE-события | `event: message` + `{ delta }` | `event: delta` + `{ text }` |
| § 5.5 Approvals | `event: tool_approval` + `{ actionId }` | `event: tool_approval_required` + `{ id, name, arguments, expiresAt }` |
| § 5.5 Approve endpoint | — | `POST /api/chat/approvals/{callId}/approve` и `/reject` (не `/api/approvals/*`) |
| § 5.5 SSE JSON | — | **camelCase** (через `CamelCasePropertyNamesContractResolver`) — иначе клиентский JS не найдёт `parsed.id` |
| § 13.1 UI-селектор модели | Dropdown модели в header | Отложено в Фазу 2.1 (endpoint `/api/models` уже готов) |
| § 13.2 Retention чатов | `ChatRetentionService` | Отложено в Фазу 2.1 (`ChatService.DeleteOldChatsAsync` уже есть) |
| § 7 LmStudioClient | `IAsyncEnumerable<LmStudioStreamChunk>` | `IAsyncEnumerable<ChatCompletionChunk>` (имя DTO другое) |

### 14.3. Что отложено в Фазу 2.1 (ChatGPT-like фичи)

Приоритет по итогам обсуждения:

| # | Фича | Приоритет |
|---|------|-----------|
| 1 | Копировать сообщение (📋 при hover) | 🟠 High |
| 2 | Regenerate (перегенерировать последний ответ) | 🟠 High |
| 3 | Стоп-кнопка (прервать стрим) | 🟡 Med |
| 4 | AI-генерация заголовка из первого сообщения | 🟡 Med |
| 5 | Edit user-сообщения + regenerate | 🟡 Med |
| 6 | Поиск по чатам | 🟢 Low |
| 7 | UI-селектор модели (из § 13.1) | 🟡 Med |
| 8 | Retention чатов (из § 13.2) | 🟢 Low |
| 9 | SignalR для approvals (из § 13.3) | 🟢 Low |
| 10 | Метрики `iichattools_chat_*` | 🟢 Low |

