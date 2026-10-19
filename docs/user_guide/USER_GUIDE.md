# Руководство пользователя IIChatTools v2.0.1

**Версия:** 1.0
**Обновлено:** 2026-10-19
**Актуально для:** IIChatTools v2.0.1
**Источник:** разделы «Chat UI», «Multi-Agent», «RAG / Knowledge Base» из корневого `README.md` (перенесены при реорганизации документации v2.0.1).

Руководство описывает пользовательские возможности платформы: чат-интерфейс,
мультиагентную работу и RAG (поиск по знаниям). Установка, настройка, API и
описание остальных агентов (Database / Mail / External-LLM / Vision) — в
корневом [README.md](../../README.md).

**Содержание:**

- [Chat UI](#chat-ui)
- [Multi-Agent (v1.4.0)](#multi-agent-v140)
- [RAG / Knowledge Base (v1.5.0)](#rag--knowledge-base-v150)

---
## Chat UI

Полноценный чат-интерфейс (`/chat`) по аналогии с ChatGPT/DeepSeek:

**Возможности:**
- 📋 **Sidebar** — список чатов с относительными датами (`только что`, `5 мин назад`, `вчера`, `22.09`) и счётчиком сообщений.
- 🔍 **Поиск по чатам** — server-side фильтр (KI-068) + ⌘K-модалка (`Ctrl+K`, KI-078B) с превью совпадений.
- 🔎 **Внутричатовый поиск** (KI-078A) — `Ctrl+F` в ленте активного чата: подсветка `<mark>`, навигация ↑/↓, авто-скролл.
- ➕ **Создание / переименование (✏️) / удаление (🗑)** чатов прямо в sidebar.
- ↔️ **Свернуть/развернуть sidebar** (KI-079) — кнопка внутри панели + хоткей `Ctrl+B`, состояние в `localStorage`.
- 🔢 **Авто-нумерация** новых чатов: «Новый чат», «Новый чат 2», «Новый чат 3», …
- 🤖 **AI-генерация названия** из первого сообщения (ChatGPT-style, после первого ответа).
- 🎛 **Селектор модели** в header — переключение модели чата (`PATCH /api/chats/{id}`).
- 🌊 **SSE-стриминг** ответа LLM — потоковая отрисовка с мигающим курсором.
- 📝 **Markdown-рендеринг** ответов (`marked` + `DOMPurify`) + **подсветка синтаксиса** (`highlight.js`).
- 💻 **Code blocks** — шапка с языком + кнопки Copy / Download.
- 🛠 **Tool calling** — LLM автоматически вызывает инструменты (до 5 итераций, SSE `tool_call` / `tool_result`). Tool-блоки рендерятся карточками `<details>`: иконка агента + короткий summary + status badge, JSON — во вложенном свёрнутом блоке (KI-228).
- 📊 **Mermaid + KaTeX** (KI-232) — ` ```mermaid ` code-block рендерится как SVG-диаграмма (flowchart / sequence / class / state / ER / gantt / pie / journey / mindmap / timeline / gitgraph / quadrant), а `$...$` и `$$...$$` — как формулы KaTeX. Lazy-load библиотек (первое вхождение в чате). `securityLevel: 'strict'` + DOMPurify sanitize (defense in depth). Fallback: если библиотека не загрузилась — исходник как `<pre>`.
- 👍 **Feedback на assistant-message** (KI-233) — кнопки 👍 / 👎 в hover-actions под ответом. Оценка сохраняется в `ChatMessage.Feedback` (`positive` / `negative` / `null`). Повторный клик — toggle (снять оценку). Активная кнопка подсвечивается (зелёная / красная). Собирается датасет для будущего RLHF (UI-вкладка + JSONL-экспорт — v1.14, KI-240).
- ➕ **Continue после лимита** (KI-230) — если ответ обрезан по лимиту контекста (`finish_reason=length`), под bubble появляется плашка ⚠️ + кнопка «➕ Продолжить». Клик → стрим дописывается в **тот же** bubble (append в БД, один пузырь, тексты сливаются без шва). ChatGPT-style.
- 🖼 **Lightbox для скриншотов** (KI-229) — PNG от `vision_agent` открывается в full-screen overlay: zoom (wheel / кнопки), pan (drag / стрелки), fit-width / fit-page, скачать PNG, скопировать путь, `Esc` / клик по фону — закрыть. При F5 (base64 не хранится в БД) — placeholder + кнопка «Открыть скриншот» через endpoint `GET /api/chat/{chatId}/screenshots/{taskId}/{fileName}`.
- 📎 **RAG-вложения** — прикрепить файлы к чату **тремя способами**: 📎-кнопкой, **drag-and-drop** на поле ввода, **Ctrl+V** для скриншотов из буфера (KI-231). PlainText + Pdf + Docx + Images (36 расширений, ≤32 MB, ≤5 файлов). Сканы PDF и картинки — OCR через Tesseract (v1.13.x, KI-203 / KI-238). Чипы с метриками под полем ввода, кнопка «Очистить RAG». Auto-inject top-K из attached-чанков в system prompt (Шаг 6C).
- 📚 **Источники (Sources)** — под ответом ассистента: список использованных LLM источников с указанием имени/URL. Для RAG — `RULES.md · chunk 15 · score 0.71`; для web/wiki — кликабельная ссылка (`Москва — Википедия`, `example.com`). Snippet — в tooltip при hover. **Live-режим** (через SSE `done`) и **F5-режим** (из `ChatMessageDto.Sources`). Покрыто: RAG-чанки, `wikipedia_search`, `web_search`, `fetch_web_content` (включая проброс через агентов).
- ✅ **Approvals** — mutating-инструменты требуют подтверждения:
  - Модалка с именем инструмента, JSON-параметрами, countdown (5 минут).
  - Drag-and-drop за заголовок.
  - Approve/Reject; закрытие крестиком = Reject.
- ✏️ **Edit user-сообщения** → автоматическая регенерация ответа.
- 🔄 **Regenerate / Retry** — перегенерировать ответ ассистента или повторить после Stop.
- ⏹ **Stop** — прерывание стрима (`AbortController`), частичный ответ отбрасывается.
- 📋 **Copy** на каждом сообщении.
- 🔢 **Статистика в meta-строке** (KI-049a/b, KI-084a/b) — `123 / 45 токенов · 7.4 tok/s · 9.1 с` под ответом ассистента. Токены — tiktoken (±5–10% для Qwen); tok/s и длительность — Stopwatch.
- 📜 **Persistентная история** — все диалоги в БД + **retention** (авто-удаление старых).
- ⬇️ **ChatGPT-style скроллинг** — кнопка «↓ Вниз», автоскролл отключается при ручной прокрутке вверх.
- 💾 **Enter** — отправка, **Shift+Enter** — новая строка, автоувеличение textarea.
- 🎨 **DeepSeek-style поле ввода** (KI-080) — закруглённое поле на всю ширину, круглые SVG-кнопки Send/Stop внутри.
- 🎤 **Голосовой ввод** (v1.13.0, KI-140) — кнопка 🎤 в поле ввода. Офлайн-распознавание через Whisper.net (аудио не покидает сервер). Хоткей `Ctrl+Shift+Space`. VAD — auto-stop по тишине (v1.13.1). Выбор микрофона — в `/profile → 🎤 Аудио` (v1.13.1, KI-145), `deviceId` сохраняется в `UserSettings`.
- 🌐 **Локализация RU/EN**.

**Точки входа:**
- UI: `/chat`
- API: `/api/chats`, `/api/chat/stream`, `/api/chat/regenerate`, `/api/chat/messages/{id}/edit`, `/api/chat/approvals/*`, `/api/chats/{id}/generate-title`, `/api/models`

**Скриншоты** — в [DESIGN v1.3](../development/v1.3/DESIGN.md).

---

## Multi-Agent (v1.4.0)

> **Историческая справка.** Раздел описывает состояние v1.4.0
> (6 агентов + fallback). В v2.0.1 Chat видит **19 инструментов**;
> актуальный список — [`README.md` → «Инструменты»](../../README.md#инструменты-66).

Chat работает через **7 верхнеуровневых инструментов** — 6 специализированных
суб-агентов + универсальный fallback:

| Агент | Инструментов | Модель (по умолчанию) | Approval |
|:---|:---:|:---:|:---:|
| `file_system_agent` | 13 | `qwen/qwen3-4b-2507` | ✅ |
| `code_agent` | 3 | `gemma-4-12b-coder...` | ✅ |
| `web_agent` | 3 | `qwen/qwen3-4b-2507` | ❌ |
| `git_agent` | 7 | `qwen/qwen3-4b-2507` | ✅ |
| `github_agent` | 7 | `qwen/qwen3-4b-2507` | ✅ |
| `planner_agent` | 2 | `gemma-4-12b-coder...` | ❌ |
| `consult_secondary_agent` | *(browser + fallback)* | `LmStudio:Model` | ✅ |

**Зачем:** одна модель (особенно 4B) плохо выбирает инструмент из 40.
Внутри агента — узкий набор (2-13 инструментов) + свой system prompt →
точнее выбор, меньше токенов.

**Как работает:**
1. Пользователь пишет задачу.
2. Chat (LLM) выбирает агента → SSE `tool_call`.
3. Модалка approval: «Агент X хочет выполнить задачу: ...» → approve.
4. Внутри агента — multi-turn loop (до `MaxSteps`, обычно 10) с **белым списком** инструментов.
5. Финальный ответ агента → Chat формулирует ответ пользователю.

**Retention чатов (KI-067):** per-user override глобального срока хранения.
- `/profile` — своя страница: срок хранения (дней) + флаг «Не удалять».
- `/admin` → вкладка «Пользователи» → ⚙ в строке → модалка retention.
- Ключи: `Chat.RetentionDays` (int), `Chat.DoNotDelete` (bool) в таблице `UserSettings`.
- Приоритет: `DoNotDelete = true` перебивает `RetentionDays`.

**Управление:** `/admin` → вкладка **«Агенты»** — список, редактирование (DisplayName,
Model, MaxSteps, SystemPrompt, AllowedTools, RequiresApproval, Disabled), сброс к defaults.
**Статистика (KI-076):** карточки над списком — TotalRuns, AvgTime, SuccessRate, LastRun
по каждому агенту (источник — `AuditLogs`, `GET /api/admin/agents/stats`).
Изменения сохраняются в БД (`AppSettings`, ключ `SubAgents.{name}`) и восстанавливаются
при старте приложения.

**API:**
- `GET  /api/admin/agents` — список агентов.
- `PUT  /api/admin/agents/{name}` — обновить дескриптор.
- `POST /api/admin/agents/{name}/reset` — сбросить к appsettings.json.

**Конфигурация:** секция `SubAgents` в `appsettings.json` (6 агентов × настройки).
Описания и промпты — в [DESIGN v1.4](../development/v1.4/DESIGN.md).

---

## RAG / Knowledge Base (v1.5.0)

**✅ Реализовано (все фазы 0-8 закрыты, релиз v1.5.0).** Семантический поиск
по документам проекта, приложенным к чату файлам и истории чатов.
Embeddings — LM Studio (`text-embedding-nomic-embed-text-v1.5`, 768 dim), векторное
хранилище — `InMemoryVectorStore` (Singleton, теряет данные при рестарте; для
`project_docs` возможна авто-переиндексация).

### 4 индекса

| Индекс | Область | Источник | Управление |
|:---|:---|:---|:---|
| `project_docs` | Global | README, RULES, KNOWN_ISSUES, CHANGELOG, RELEASES | `/admin → База знаний` |
| `my_rag_docs` | Per-chat | Файлы, приложенные к чату (📎) | `/chat` — «Очистить RAG» |
| `chat_history` | Per-user | История сообщений пользователя | (LLM — через `search_chat_history`) |
| `workspace` | Per-user (opt-in) | Файлы workspace пользователя | `/profile → Индексация workspace` |

### Как работает

1. **Ingestion:** файл → парсинг → чанкинг (recursive, 500 токенов, overlap 64) →
   embeddings (batch 64) → запись в `DocumentChunks` (БД) + `InMemoryVectorStore`.
2. **Retrieval:** запрос → embedding → cosine top-K → фильтрация по метаданным
   (chatId / userId) и по `MinScore` → enrichment из БД.
3. **Auto-inject (Шаг 6C):** при отправке сообщения, если у чата есть attached-чанки
   в `my_rag_docs`, top-K из них вставляется в system prompt (порог — `AutoInjectMinScore`).
4. **3 tool для LLM:** `search_knowledge_base`, `search_chat_history`, `search_workspace`.
5. **Sources / citations (v1.6.0):** под ответом ассистента — блок «📚 Источники»
   со списком RAG-чанков, реально использованных LLM. Собираются из двух
   источников: (а) auto-inject (system prompt); (б) `tool_result` от `search_*`.
   Дедупликация по `(type, documentPath, chunkIndex)`. Сохраняются в
   `ChatMessage.MetadataJson` (camelCase), отдаются в `ChatMessageDto.Sources`.

Chat видит **16 инструментов** (9 агентов + `consult_secondary_agent`
+ 3 RAG-tool + `database_agent` — v1.7.0, KI-097 + `code_agent_with_review`
— v1.11.0, KI-126 + `vision_agent` — v1.12.0, KI-131).

### Форматы и лимиты

- **Поддерживаемые форматы (PlainTextParser 28 + PdfParser + DocxParser = 30 расширений):** `.txt`, `.md`, `.csv`,
  `.tsv`, `.log`, `.json`, `.xml`, `.yaml`, `.yml`, `.html`, `.htm`, `.cs`, `.py`,
  `.js`, `.ts`, `.java`, `.go`, `.rs`, `.sql`, `.sh`, `.ps1`, `.razor`, `.cshtml`,
  `.css`, `.scss`, `.dockerfile`, `.gitignore`, `.editorconfig`.
- **Лимиты (дефолт):** 32 MB на файл, 30 MB суммарно на чат, 5 файлов на чат.
- **Кодировки:** BOM-детект (UTF-8 / UTF-16 LE/BE), fallback Windows-1251.
- **PDF / DOCX:** поддерживаются с **v1.7.1** (KI-104). PDF — через PdfPig
  (текстовый слой), DOCX — через OpenXml SDK. **Сканы PDF** (без текстового
  слоя) — через Tesseract OCR (**v1.13.x**, KI-203; `Rag:Ingestion:Ocr:Enabled`).

### UI

- **`/chat`** — 📎-кнопка рядом с полем ввода; чипы с метриками (`📄 file.pdf · 3 чанка · [×]`),
  строка `RAG: N чанков [Очистить RAG]`.
- **`/admin → База знаний`** — таблица 4 индексов, переиндексация `project_docs`,
  просмотр / удаление чанков (пагинация 20/стр.), модалка настроек RAG (8 полей).
- **`/profile → Индексация workspace`** — opt-in checkbox, статус, progress-bar,
  кнопка «Переиндексировать». Фоновая индексация через `Task.Run` + `IServiceScopeFactory`.

### Конфигурация (секция `Rag` в `appsettings.json`)

| Ключ | Дефолт | Описание |
|:---|:---|:---|
| `Rag:AutoIndexProjectDocs` | `false` | Индексировать `project_docs` при старте приложения |
| `Rag:Embedding:Model` | `text-embedding-nomic-embed-text-v1.5` | Модель эмбеддингов LM Studio |
| `Rag:Embedding:Dimensions` | `768` | Размерность вектора |
| `Rag:Embedding:BatchSize` | `64` | Максимум текстов за один вызов `/v1/embeddings` |
| `Rag:Embedding:TimeoutSeconds` | `30` | Таймаут эмбеддинг-запроса |
| `Rag:Chunking:Strategy` | `recursive` | `recursive` / `sentence` / `fixed` |
| `Rag:Chunking:ChunkSize` | `500` | Целевой размер чанка (токенов) |
| `Rag:Chunking:ChunkOverlap` | `64` | Перекрытие между чанками |
| `Rag:Chunking:MinChunkSize` | `100` | Минимальный размер чанка |
| `Rag:Ingestion:MaxFileSizeBytes` | `33554432` (32 MB) | Лимит файла |
| `Rag:Ingestion:MaxFilesPerChat` | `5` | Лимит файлов на чат |
| `Rag:Ingestion:MaxTotalSizePerChat` | `31457280` (30 MB) | Суммарный лимит на чат |
| `Rag:Ingestion:ProjectDocsPaths` | см. `appsettings.Development.json` | Пути для `project_docs` |
| `Rag:Retrieval:DefaultTopK` | `5` | Top-K по умолчанию |
| `Rag:Retrieval:OverFetchMultiplier` | `2` | Over-fetch для фильтрации |
| `Rag:Retrieval:MinScore` | `0.25` (dev) / `0.3` (prod) | Минимальный cosine score |
| `Rag:Attachments:AutoInjectTopK` | `5` | Сколько чанков из `my_rag_docs` вставлять в system prompt |
| `Rag:Attachments:AutoInjectMinScore` | `0.35` | Порог для auto-inject |
| `Rag:Attachments:StorageSubfolder` | `chat-attachments` | Подпапка в workspace для файлов |

### Ограничения (осознанные, MVP)

- **InMemoryVectorStore** теряет embeddings при рестарте → `project_docs` может быть
  переиндексирован автоматически (`AutoIndexProjectDocs=true`) или вручную через админку.
- **PDF / DOCX / OCR** — реализованы: PDF/DOCX с v1.7.1 (KI-104), сканы PDF через OCR с v1.13.x (KI-203).
- **Re-ranking (cross-encoder)** — не в MVP.
_(Sources / citations реализованы в v1.6.0 — см. раздел «Chat UI» и «RAG / Knowledge Base».)_

### API

См. [`README.md` → «API (основные endpoints)»](../../README.md#api-основные-endpoints): 4 endpoint'а для вложений чата,
6 для админки Knowledge Base, 5 для Workspace-индекса в профиле.

