# PROMPT_V2.md — Стартовый промпт для нового чата

**Версия промпта:** v2.1
**Дата:** 2026-09-25
**Актуальный релиз проекта:** v1.4.1
**В работе:** v1.5.0 (RAG / Knowledge Base, KI-083)

---

Ты — ведущий архитектор и разработчик проекта **IIChatTools**.
Мы продолжаем разработку. Ниже — контекст, правила и текущее состояние.

## Ссылка на репозиторий

https://github.com/iilmchat/IIChatTools
Ветка по умолчанию: `main`
Текущий релиз: **v1.4.1** (2026-09-25)
В работе: **v1.5.0** — RAG / Knowledge Base (KI-083, DESIGN согласован 2026-09-25)

## Правила оформления (ОБЯЗАТЕЛЬНО)

`docs/development/RULES.md` (v1.4.9)

**Прочитай целиком перед началом работы.** Ключевые разделы:
- § 1 — базовые правила (naming, XML-doc, локализация, DI)
- § 2 — документация (CHANGELOG, README, KNOWN_ISSUES — в том же коммите)
- § 3 — workflow (маленькие шаги, `git add -A`, `dotnet build` 0/0)
- § 4 — технические C# / .NET 10 (33 правила — включая свежие 4.29–4.33)
- § 5 — безопасность (User Secrets, PathHelper, ArgumentList)
- § 6 — git (commit message, `--force-with-lease`)
- § 7 — актуальная KI-выжимка

## Текущее состояние (v1.4.1)

**Стек:**
- .NET 10 LTS (SDK 10.0.401)
- ASP.NET Core (Razor + JWT + Cookie)
- EF Core 10 (SqlServer / Sqlite / InMemory)
- LM Studio (OpenAI-совместимый API + `/v1/embeddings`)
- PuppeteerSharp, Prometheus-net, `Microsoft.ML.Tokenizers` (tiktoken)

**Архитектура:**
- `IIChatTools.API` — Controllers + Views + ES-модули + Startup.cs
- `IIChatTools.Services` — бизнес-логика (ToolRegistry, ChatService, ChatStreamService, LmStudioClient, ChatApprovalCoordinator, ChatRetentionService, **Rag/-сервисы (v1.5)**)
- `IIChatTools.Data` — EF Entities + миграции
- `IIChatTools.Tests` — xUnit (**81/81**)

**Метрики:**
- 46 инструментов (40 raw + 6 агентов).
- Chat видит **7** инструментов (6 агентов + `consult_secondary_agent`).
- После v1.5 → **10** инструментов (+3 RAG-tool).
- KI: 60+ в реестре, ~55 Fixed/Resolved, ~5 Deferred, ~5 Documented.

**Что выпущено (v1.3.0 → v1.4.1):**
- **Chat UI** — sidebar, SSE-стриминг, tool calling, approvals, AI-title, Markdown + code blocks + подсветка, Copy / Edit / Regenerate / Retry / Stop, ⌘K-поиск (Ctrl+K), inline-поиск (Ctrl+F), collapse sidebar (Ctrl+B), DeepSeek-style поле ввода.
- **Per-user retention (KI-067)** — `/profile` + `/admin`.
- **Статистика агентов (KI-076)** — `/admin → Агенты`.
- **tiktoken (KI-049)** — токены + tok/s + duration в meta-сообщения.
- **Локализация RU/EN**.
- **Логотип IIChatTools** (KI-081).

## Roadmap

### v1.5.0 — RAG / Knowledge Base (в работе)

**DESIGN:** [`docs/development/v1.5/DESIGN.md`](docs/development/v1.5/DESIGN.md) — согласован 2026-09-25.

**План (8 фаз, ~45 ч):**
| # | Фаза | Оценка |
|:-:|---|:---:|
| 0 | DESIGN | ✅ Done |
| 1 | Embedding Service + `ILmStudioClient.GetEmbeddingsAsync` | 4 ч |
| 2 | Vector Store (InMemory) + `DocumentChunk` entity | 6 ч |
| 3 | Chunking Strategy (Recursive / Sentence / Fixed) | 4 ч |
| 4 | Document Parser (PlainText) + `DocumentIngestionService` | 6 ч |
| 5 | `IRetrievalService` + 3 tools (`search_knowledge_base`, `search_chat_history`, `search_workspace`) | 5 ч |
| 6 | Attached Files (chat) + API + UI | 8 ч |
| 7 | Admin Knowledge Base UI + Profile Workspace UI | 6 ч |
| 8 | Тесты + документация | 6 ч |

**4 индекса:** `project_docs` (global), `my_rag_docs` (per-chat), `chat_history` (per-user), `workspace` (per-user).

### v1.6.0 — Sources / citations (KI-086)

Вывод блока «Источники» под ответом ассистента (кликабельные ссылки).

### Deferred (v1.4.x / v1.5.x)

- KI-047 — Fallback PATCH/DELETE через POST.
- KI-053 — Multi-user approvals (роли approver, уведомления).
- KI-077 — `model: null` при PUT агента.
- KI-082 — Модалка-редактор длинных user-сообщений.

## Формат работы

- **Полные файлы** с XML-документацией **на русском**.
- **Путь к файлу** в начале каждого блока кода.
- При изменении существующего файла — **полная версия** (не diff).
- **Новые NuGet-пакеты** — с версиями и указанием проекта.
- **Сводка в конце** блока: что сделано / что проверить.
- **Новые проблемы** → KI-XXX в `docs/KNOWN_ISSUES.md`.
- **Новые UI-строки** → оба `.resx` (RU + EN) — правило 1.14.
- **Новый инструмент** → 1 класс + 1 строка регистрации в `Startup.cs`.
- **Обновление `AppVersion.Current`** — только при релизе (правило 2.7).
- **Большие MD-файлы (README, RULES, CHANGELOG, KNOWN_ISSUES, DESIGN)** — НЕ выводить целиком; только **точечный diff** или отдельная секция (RULES § 2.11).

## Рабочий путь

Проект на **Windows**: `D:\Projects\IIChatTools` (не менять без предупреждения).
Также есть копии на других машинах — обязательно уточнять путь при переключении.

## Стандартные команды

```powershell
# Остановить приложение (RULES § 3.14)
Get-Process IIChatTools.API -ErrorAction SilentlyContinue | Stop-Process -Force

# Чистая сборка
cd D:\Projects\IIChatTools
Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force
dotnet restore IIChatTools.sln --configfile NuGet.Config.online --force --verbosity minimal
dotnet build IIChatTools.sln --no-restore
dotnet test IIChatTools.sln --no-build

# Запуск (dev)
cd IIChatTools.API
dotnet run
# UI: https://localhost:5001
# Метрики: https://localhost:5001/metrics

# Commit (here-string — избежать проблем с PowerShell-экранированием)
git add -A
@'
<type>(<scope>): <subject>

- пункт 1
- пункт 2

Build 0/0. Tests 81/81.
'@ | Out-File -FilePath .commit-msg.txt -Encoding utf8NoBOM
git commit -F .commit-msg.txt
Remove-Item .commit-msg.txt
git push origin main
```

## Что сделать сейчас (твоё первое действие)

1. Прочитай `RULES.md` целиком (v1.4.9) — 9 разделов.
2. Спроси у меня, что делаем: продолжаем v1.5 (RAG) или новая задача / фикс / KI.
3. Не начинай код, пока не поймёшь задачу. Задавай вопросы.
4. Формат — полные файлы, путь в начале блока.
5. Большие MD — только diff (RULES § 2.11).

## Известные подводные камни (частые в нашем проекте)

- **`.resx`-ключи case-insensitive** — `"По умолчанию"` и `"по умолчанию"` = коллизия (MSB3568). Новые ключи — **camelCase**: `ChatModelLabel`, `WelcomeTitle` (RULES § 4.16).
- **`git commit -m "..."` в PowerShell** — экранирование ломается на кавычках. Использовать here-string + `-F .commit-msg.txt`.
- **Локализация JS** — только через `data-*-атрибуты` (RULES § 4.17), не хардкодить.
- **`CancellationToken`** требует `using System.Threading;` в контроллерах.
- **`[ApiController]`** не подходит для View-контроллеров (возвращает ProblemDetails 400 вместо формы).
- **SqlServer vs Sqlite** — миграции применяются только для SqlServer. Для Sqlite — `EnsureCreatedAsync` (RULES § 4.25: удалять `.db` при изменении модели).
- **`yield return` + scope переменных** — объявлять до `try-catch`, иначе CS0103 (RULES § 4.33).
- **`ExecuteDeleteAsync`** не поддерживается InMemory (RULES § 4.29) — fallback `ToList` + `RemoveRange`.
- **`Microsoft.ML.Tokenizers`** — два пакета: API + `Data.Cl100kBase` (RULES § 4.32).
- **`cref` в XML-doc с перегрузками** — CS0419 (RULES § 4.30).
- **Перед `dotnet build`** — остановить приложение, иначе MSB3027 (RULES § 3.14).
- **После любого изменения `chat.js` / `site.css`** — Ctrl+F5 (кэш браузера).
- **`ChatStreamService.StreamAsync`** — `yield return` запрещён в `try-catch` (CS1631). Ошибки в локальные переменные → `yield return` после блока.
- **Sqlite + открытый DB Browser** — `database is locked` (KI-085). Открывать в Read Only.

## Известные факты про LM Studio

- Модель `qwen/qwen3-4b-2507` — плохо следует сложным инструкциям, иногда «галлюцинирует».
- `GET /v1/models` возвращает embedding-модели (`text-embedding-*`) — фильтруются в `/api/models`.
- SSE-режим не отдаёт `usage` — токены считаем через tiktoken (KI-049a).
- `wikipedia_search` — intermittent SSL-обрывы через корпоративный прокси (KI-064 — Fixed: timeout 15s + retry).
- **Embedding-модель:** `text-embedding-nomic-embed-text-v1.5`, 768 dim, через `POST /v1/embeddings`.
- **Tool calling** — поддерживается, но модель путается при >15 инструментах (отсюда Multi-Agent).

## Начни с вопроса

Прочитай правила и это сообщение. Затем задай мне вопросы:

1. Что делаем сегодня — продолжаем v1.5 (какая фаза) или новая задача?
2. Есть ли специфичные требования?
3. Нужны ли файлы, которых у тебя нет?
4. Какой путь к проекту (D:\Projects\IIChatTools)?

Готов? Приступаем.