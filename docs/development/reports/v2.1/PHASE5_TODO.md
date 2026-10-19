# PHASE5_TODO — Todo Tracking (v2.1.0, Фаза 5)

**Дата:** 2026-10-20
**HEAD:** `48f14e2` + незакоммиченные изменения
**Правила:** `docs/development/RULES.md` v1.4.35
**Статус:** изменения в рабочем дереве, **не закоммичены**

---

## 1. Файлы (+/-)

### Создано

| Файл | Назначение |
| :--- | :--- |
| `IIChatTools.Services/Interfaces/ITodoService.cs` | Контракт сервиса плана задач |
| `IIChatTools.Services/Implementation/BackgroundJobs/TodoService.cs` | Scoped-сервис: ownership, `SortOrder`, статусы, `CompletedAt` |
| `IIChatTools.Services/DTO/BackgroundJobs/ChatTodoDto.cs` | DTO + `CreateTodoRequest` / `UpdateTodoStatusRequest` |
| `IIChatTools.Services/Implementation/Tools/BackgroundJobs/AddTodoTool.cs` | `add_todo` |
| `IIChatTools.Services/Implementation/Tools/BackgroundJobs/UpdateTodoStatusTool.cs` | `update_todo_status` |
| `IIChatTools.Services/Implementation/Tools/BackgroundJobs/ListTodosTool.cs` | `list_todos` |
| `IIChatTools.API/Controllers/ChatTodosController.cs` | REST `/api/chats/{chatId}/todos` |
| `IIChatTools.Tests/IntegrationTests/BackgroundJobs/TodoServiceTests.cs` | 7 тестов сервиса |
| `IIChatTools.Tests/UnitTests/BackgroundJobs/TodoToolsTests.cs` | 4 теста tools |

### Изменено

| Файл | Что |
| :--- | :--- |
| `IIChatTools.API/Startup.cs` | `ITodoService` + 3 tool registrations |
| `IIChatTools.Services/Implementation/ChatStreamService.cs` | `allowedNames` + правило 18 в `DefaultSystemPrompt` |
| `IIChatTools.API/Views/Chat/Index.cshtml` | `#chat-todo-panel` над `#chat-messages` + `data-label-*` |
| `IIChatTools.API/wwwroot/css/chat.css` | `.chat-todo-panel`, `.chat-todo-item`, статусы/чек-боксы |
| `IIChatTools.API/wwwroot/js/modules/chat.js` | `loadTodos`, `renderTodoPanel`, toggle, add, reset, F5 |
| `SharedResources.resx` / `.ru.resx` | 5 ключей RU/EN |
| `README.md` | 72 инструмента / Chat 25 / 1216 тестов |
| `docs/KNOWN_ISSUES.md` | KI-262 + сводка 241 |
| `CHANGELOG.md` | `[Unreleased] → Added`: Todo Tracking |

**Не трогал:** `ChatTodo` entity и миграцию (созданы в Фазе 1), `BackgroundJob*`.

---

## 2. Сервис и Tools

### `TodoService` (Scoped)

- `CreateAsync(chatId, userId, title, description)`:
  - проверяет `Chats.UserId == userId` (чужой чат → `UnauthorizedAccessException`);
  - title trim, 1..200; description ≤ 2000; максимум **50** пунктов на чат;
  - `SortOrder = MAX(SortOrder) + 1` (0-based);
  - статус `Pending`, `CreatedAt = UtcNow`.
- `GetForChatAsync`: ownership + `OrderBy(SortOrder).ThenBy(Id)`, `AsNoTracking`.
- `UpdateStatusAsync`: `Pending | InProgress | Completed` (case-insensitive);
  `Completed` → `CompletedAt = UtcNow`, иначе `null`.
- `DeleteAsync`: ownership + удаление.
- Владение проверяется через `Chat.UserId`; для update/delete — `Include(Chat)`.

### Tools

| Tool | Параметры | Возврат |
| :--- | :--- | :--- |
| `add_todo` | `title` (required), `description?` | `{ todoId, title, status, sortOrder }` |
| `update_todo_status` | `todoId`, `status` | `{ todoId, status }` |
| `list_todos` | `status?` | массив `{ todoId, title, description, status, order, completedAt }` |

- Все три: `RequiresApprovalByDefault = false`.
- Все три требуют `ToolExecutionContext.ChatId`; иначе `Fail("Todo доступны только в чате.")`.
- `update_todo_status` дополнительно проверяет, что todo принадлежит
  **текущему чату** (а не просто пользователю).
- `list_todos` валидирует фильтр (`Pending|InProgress|Completed`).

### Prompt / Chat

- В `allowedNames` добавлены `add_todo`, `update_todo_status`, `list_todos`
  (RULES § 4.44). Chat видит **25** инструментов.
- В `DefaultSystemPrompt` добавлено правило 18:
  «Для сложных задач (3+ шагов) — `add_todo`; обновляй через
  `update_todo_status`; не добавляй todo для простых задач».
- `ToolRegistry` — **72** (было 66 + 3 job + 3 todo).

---

## 3. REST и UI

### REST (`ChatTodosController`)

`[Authorize] [Route("api/chats/{chatId:int}/todos")]`.

| Метод | Поведение |
| :--- | :--- |
| `GET` | список DTO, ordered by `SortOrder` |
| `POST` | `{ title, description }` → создание |
| `PATCH /{id}` | `{ status }`; проверяет принадлежность todo чату |
| `DELETE /{id}` | удаление; проверяет принадлежность |

Ошибки: чужой чат/todo → 404, невалидный статус/title → 400.

### UI

- `#chat-todo-panel` — **над** `#chat-messages` (flex-элемент, сворачиваемая
  по высоте область).
- Заголовок, прогресс `completed/total`, кнопка «Добавить».
- Пункты: чек-бокс `☐ / ◐ / ☑`, текст; `Completed` — зачёркнут.
- Клик по чек-боксу: `Pending → InProgress → Completed → Pending` → `PATCH`.
- «Добавить» → `window.prompt` (локализованный label) → `POST`.
- F5/смена чата: `GET /api/chats/{chatId}/todos`; `resetTodoPanel` при
  пустом состоянии/смене чата.
- После `tool_result` от `add_todo`/`update_todo_status` и после `done` —
  debounce 250 мс → `loadTodos` (UI не ждёт отдельного SSE).
- Все строки — через `data-label-*` (RULES § 4.17) и `.resx` (RULES § 1.14).

---

## 4. Smoke (Build/Test)

```text
dotnet build IIChatTools.sln -c Debug
Сборка успешно завершена. Предупреждений: 0, Ошибок: 0.
```

```text
dotnet test IIChatTools.Tests/IIChatTools.Tests.csproj -c Debug --no-build
Пройден! : не пройдено 0, пройдено 1216, пропущено 8, всего 1224, длительность 3 s.
```

Дополнительно:

```text
node --check IIChatTools.API/wwwroot/js/modules/chat.js
exit=0
```

`LocalizationSyncTests` проходит (5 новых ключей в RU + EN).

---

## 5. Что не так

1. **`SortOrder` гонка.** `MAX(SortOrder)+1` без транзакции/уникального
   индекса: два параллельных `add_todo` могут получить одинаковый order.
   См. KI-262.
2. **`Count >= MaxTodosPerChat`** — тоже count-then-create; при гонке
   можно превысить 50 на 1-2 пункта.
3. **UI-добавление через `window.prompt`.** Минимально и локализовано,
   но не модалка: нет описания, отмены по Escape-стилю, валидации на клиенте.
4. **Нет SSE `todo_updated`.** Другой таб/устройство не увидит изменение
   до F5; текущий таб обновляется после `tool_result`/`done` + debounce.
5. **`TodoService.UpdateStatusAsync` не принимает `chatId`.** Проверка
   «todo в этом чате» живёт в контроллере/tool; прямой вызов сервиса
   может обновить todo пользователя из другого чата.
6. **`GetForChatAsync` вызывается дважды в `PATCH`** (before/after) —
   лишние два запроса; для UI не критично.
7. **`ChatTodo` не имеет unique index `(ChatId, SortOrder)`** — см. KI-262.
8. **Максимум 50 todo** захардкожен; в конфиг не вынесен.
9. **`list_todos` возвращает прямой массив** (не `{ items, count }`),
   в отличие от некоторых других tools; LLM это понимает, но стиль
   неоднороден.
10. **`update_todo_status` возвращает входной `status`**, а не
    канонизированный; при `completed` в ответе будет `completed`,
    хотя в БД `Completed`. UI обновляет данные через `GET`/DTO.

---

## 6. Новые KI

| KI | Приоритет | Статус | Суть |
| :--- | :---: | :--- | :--- |
| **KI-262** | 🟢 Low | Planned (v2.1.x) | Todo: гонка `MAX(SortOrder)+1` / count-then-create; нужен unique index + retry или per-chat lock |

Других новых KI не заводил: остальные пункты § 5 — UX/архитектурные
замечания, не блокеры v2.1.0. Сводка реестра: **241** уникальный KI.

---

## 7. Обоснование решений

1. **Prompt + PATCH, а не SSE на каждый чих.** Todo меняется редко
   (3–7 пунктов за сложную задачу); SSE-событие на каждый `add/update`
   добавило бы новый контракт, подписчиков и риск утечек. После
   `tool_result`/`done` UI делает дешёвый `GET` (debounce 250 мс) — этого
   достаточно для одного пользователя. Мульти-таб — отдельная задача.
2. **`data-*` + `.resx`.** RULES § 4.17/§ 1.14: JS не содержит
   UI-строк; `LocalizationSyncTests` проверяет паритет RU/EN.
3. **Три узких tool'а, а не `manage_todo`.** RULES § 4.44: действия
   `add/update/list` явно видны LLM; approval не нужен — план обратим
   и принадлежит чату.
4. **`ChatTodo` — отдельная сущность, а не сообщение.** План не
   засоряет историю/контекст; LLM читает его через `list_todos`,
   UI — через REST.
5. **`SortOrder = MAX+1`.** Простейший детерминированный порядок без
   reorder-UI; гонка задокументирована в KI-262.
6. **`CompletedAt` в сервисе.** Единая точка: и tool, и REST, и UI
   получают одинаковое поведение; `InProgress` очищает дату (reopen).
7. **Scoped `TodoService`.** Работает с scoped `AppDbContext`; ownership
   проверяется в каждом методе, а не только в контроллере.
8. **Панель над лентой.** По требованию задачи; `flex: 0 0 auto` +
   `max-height:30vh` не ломают скролл сообщений.
9. **Чек-боксы циклом.** Один клик — следующее состояние; не нужен
   отдельный dropdown. LLM обновляет те же статусы через tool.
10. **`window.prompt` для добавления.** Минимум кода в Фазе 5; модалка
    с описанием — UX-долг, не блокер (см. § 5.3).
