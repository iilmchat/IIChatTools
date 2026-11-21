# TODO_KI_SPRINTS — спринты по устранению KI

**Дата:** 2026-11-21
**План KI:** docs/TODO_KI.md (89 записей, включая KI-275)
**Правила:** docs/development/RULES.md v1.4.36

Каждый спринт: 3-5 KI, код + тесты, build 0 warnings/0 errors,
dotnet test зелёный, отчёт по RULES 2.18 (7 разделов) в
docs/development/reports/vX.Y/, коммит со ссылками на KI, пуш в main.
Отчёты в чат не выводятся. KI-159 (Won't Fix) в спринты не входит.

---

## S01 — Документация и сводка реестра (doc-only)

**KI (5):** KI-275, KI-253, KI-254, KI-264, KI-088

| KI | Название |
| :--- | :--- |
| KI-275 | Расхождения в сводке статусов KNOWN_ISSUES.md и результате check-zombie-ki.ps1 |
| KI-253 | qa/MANUAL_TESTING_GUIDE.md устарел (v1.8.1, 424 теста) |
| KI-254 | RULES.md § 7: выжимка KI устарела (99 KI vs 230) |
| KI-264 | Документация: номера KI-257…260 из DESIGN § 12 не совпадают с реестром |
| KI-088 | Актуальный чек-лист ручной приёмки (docs/TESTING.md) |

**Отчёт:** docs/development/reports/v2.2/S01_KI.md

---

## S02 — Незавершённые работы: In Progress + Partially Fixed

**KI (5):** KI-234, KI-057, KI-114, KI-173, KI-182

| KI | Название |
| :--- | :--- |
| KI-234 | Chat: Retry в error-bubble (после network error) |
| KI-057 | `/api/models` возвращает embedding-модели |
| KI-114 | `AgentToolBase` возвращает `ToolResult.Ok` при `Completed=false` |
| KI-173 | Vision Agent: LLM не передаёт `url` отдельно, падает на timeout |
| KI-182 | Vision Planner зацикливается на одном target |

**Отчёт:** docs/development/reports/v2.2/S02_KI.md

---

## S03 — High: file_system_agent и Vision (click/timeout/ui_elements)

**KI (4):** KI-167, KI-177, KI-178, KI-192

| KI | Название |
| :--- | :--- |
| KI-167 | `file_system_agent` галлюцинирует успех вне workspace (рецидив KI-113) |
| KI-177 | Vision Agent: click(target=X) не находит X в свежем describe |
| KI-178 | Vision LLM timeout 300 сек при 2000+ токенах промпта |
| KI-192 | Vision Agent: VL возвращает пустой `ui_elements` на медленно грузящихся страницах |

**Отчёт:** docs/development/reports/v2.2/S03_KI.md

---

## S04 — High/Medium: RAG embeddings, селектор модели, id drift, scan viewer

**KI (4):** KI-227, KI-226, KI-193, KI-205

| KI | Название |
| :--- | :--- |
| KI-227 | RAG embeddings зависят от LM Studio (нет внешнего провайдера) |
| KI-226 | `/api/models` и UI-селектор модели зависят от LM Studio |
| KI-193 | Vision LLM: id drift между кадрами (`search_btn` → `search_button`) |
| KI-205 | RAG: постраничный просмотр PNG сканов |

**Отчёт:** docs/development/reports/v2.2/S04_KI.md

---

## S05 — Medium: Mail Agent (контекст, галлюцинации успеха, per-user ящики)

**KI (3):** KI-111, KI-113, KI-108

| KI | Название |
| :--- | :--- |
| KI-111 | `mail_agent` не помнит контекст между вызовами |
| KI-113 | `mail_agent`: qwen3-4b галлюцинирует «успех» при неудаче |
| KI-108 | Per-user mail accounts (свой ящик у каждого пользователя) |

**Отчёт:** docs/development/reports/v2.2/S05_KI.md

---

## S06 — Medium: Chat UX и выбор агентов (browser/fs/vision, drag-drop)

**KI (5):** KI-128, KI-168, KI-172, KI-179, KI-231

| KI | Название |
| :--- | :--- |
| KI-128 | Browser workflow недоступен через Chat (`browser_agent` + `save_screenshot_to_file`) |
| KI-168 | Chat LLM выбирает `file_system_agent` для задач вне workspace |
| KI-172 | Chat LLM выбирает `vision_agent` для browser-задач |
| KI-179 | Chat LLM зацикливается на Vision-задачах |
| KI-231 | Chat: Drag-and-Drop файлов + Paste Image (Ctrl+V) |

**Отчёт:** docs/development/reports/v2.2/S06_KI.md

---

## S07 — Medium: RAG OCR-прогресс, page viewer, text layer

**KI (3):** KI-204, KI-206, KI-213

| KI | Название |
| :--- | :--- |
| KI-204 | RAG: нет индикации прогресса OCR при загрузке PDF |
| KI-206 | RAG: Page viewer — thumbnails, зум, скролл (улучшения UX) |
| KI-213 | RAG: Page viewer — подгонка text layer под bbox (выделение) |

**Отчёт:** docs/development/reports/v2.2/S07_KI.md

---

## S08 — Medium: Orchestrator/Reflection/Background Jobs

**KI (4):** KI-265, KI-267, KI-266, KI-269

| KI | Название |
| :--- | :--- |
| KI-265 | Durable queue для Background Jobs |
| KI-267 | Reflection: гетерогенная модель самокритики / эскалация |
| KI-266 | Reflection: прямой вызов ISubAgentService минует AuditLogs/Admin UI |
| KI-269 | Reflection: дублирование черновиков в AgentStates без retention |

**Отчёт:** docs/development/reports/v2.2/S08_KI.md

---

## S09 — Low: Orchestrator Admin UI и durable queue

**KI (5):** KI-246, KI-247, KI-248, KI-273, KI-274

| KI | Название |
| :--- | :--- |
| KI-246 | Fallback на другого воркера при fail в Orchestrator |
| KI-247 | Orchestrator: pre-checks circuit breaker / budget на уровне оркестратора |
| KI-248 | Orchestrator: Human-in-the-loop между подзадачами |
| KI-273 | Orchestrator Admin UI: whitelist не валидируется против реестра агентов |
| KI-274 | Admin UI Orchestrator: persist 5 ключей без транзакции |

**Отчёт:** docs/development/reports/v2.2/S09_KI.md

---

## S10 — Low: Vision (PII, внешние VL, Chrome profile, Coordinate-then-Verify, Set-of-Mark)

**KI (5):** KI-138, KI-139, KI-151, KI-162, KI-163

| KI | Название |
| :--- | :--- |
| KI-138 | Vision Agent: маскирование PII на скриншотах |
| KI-139 | Vision Agent: поддержка внешних VL (Claude Computer Use / OpenAI CUA) |
| KI-151 | Chrome temp-профиль не удаляется (`BrowserMetrics-*.pma` locked) |
| KI-162 | Vision Agent: Coordinate-then-Verify |
| KI-163 | Vision Agent: Set-of-Mark (упрощённый, Tesseract-based) |

**Отчёт:** docs/development/reports/v2.2/S10_KI.md

---

## S11 — Low: Vision (drag-select, clipboard, taskbar icons)

**KI (3):** KI-164, KI-165, KI-166

| KI | Название |
| :--- | :--- |
| KI-164 | Vision Agent: drag-select (выделение текста мышью) |
| KI-165 | Vision Agent: чтение буфера обмена (read clipboard) |
| KI-166 | Vision Agent: детекция иконок в taskbar (Win+1, crop, fallback) |

**Отчёт:** docs/development/reports/v2.2/S11_KI.md

---

## S12 — Low: RAG/Chat (экспорт страниц, batch, UX backlog, feedback)

**KI (5):** KI-209, KI-210, KI-211, KI-237, KI-240

| KI | Название |
| :--- | :--- |
| KI-209 | RAG: Page viewer — экспорт отдельной страницы |
| KI-210 | RAG: Batch-экспорт нескольких attachment'ов |
| KI-211 | RAG: Page viewer — прогресс экспорта 100+ страниц |
| KI-237 | Chat UX backlog (бандл мелких улучшений) |
| KI-240 | Chat: Feedback — Admin UI + экспорт JSONL для датасета |

**Отчёт:** docs/development/reports/v2.2/S12_KI.md

---

## S13 — Low: инфраструктура БД (PostgreSQL, Qdrant, внешние БД, snapshot drift, SqlServer)

**KI (5):** KI-242, KI-243, KI-099, KI-096, KI-091

| KI | Название |
| :--- | :--- |
| KI-242 | PostgreSQL как провайдер основного `AppDbContext` |
| KI-243 | Qdrant как внешний `IVectorStore` (persistent vector store) |
| KI-099 | Внешние подключения БД (Postgres / MySQL / Oracle) |
| KI-096 | GitHub Wiki для проекта (roadmap) |
| KI-091 | SqlServer цепочка миграций повреждена (snapshot drift) |

**Отчёт:** docs/development/reports/v2.2/S13_KI.md

---

## S14 — Deferred: multi-user approvals, тесты, модалки, Wiki, PATCH/DELETE fallback

**KI (5):** KI-053, KI-074, KI-082, KI-123, KI-047

| KI | Название |
| :--- | :--- |
| KI-053 | Multi-user approvals для критичных операций |
| KI-074 | Split тестового проекта на Unit / Integration |
| KI-082 | Модалка-редактор длинных user-сообщений |
| KI-123 | Индикатор загрузки списка чатов («Идёт загрузка» + spinner) |
| KI-047 | Fallback PATCH/DELETE через POST для старых сетей |

**Отчёт:** docs/development/reports/v2.2/S14_KI.md

---

## S15 — Low: BrowserSession, run_smoke, Todo race, stale DB, SqlServer snapshot

**KI (5):** KI-257, KI-258, KI-262, KI-070, KI-090

| KI | Название |
| :--- | :--- |
| KI-257 | BrowserSessionControlTool: cast к конкретному BrowserSessionManager |
| KI-258 | run_smoke: assert_text регистрозависим |
| KI-262 | Todo: гонка `MAX(SortOrder)+1` при параллельном add_todo |
| KI-070 | Sqlite: stale DB без новых таблиц (`no such table`) |
| KI-090 | SqlServer-migrations содержали Sqlite-типы (snapshot drift) |

**Отчёт:** docs/development/reports/v2.2/S15_KI.md

---

## S16 — Documented: gh labels, SSO, cookies, email, SQLite locked

**KI (5):** KI-007, KI-009, KI-032, KI-048, KI-093

| KI | Название |
| :--- | :--- |
| KI-007 | gh issue create требует заранее созданные метки |
| KI-009 | ERR_CONNECTION_RESET на сайтах с SSO |
| KI-032 | `Unprotect ticket failed` (старые cookies от .NET Core 3.1) |
| KI-048 | Email автора коммитов в git-истории |
| KI-093 | SQLite `database is locked` (дубликат KI-085, оставлен для истории) |

**Отчёт:** docs/development/reports/v2.2/S16_KI.md

---

## S17 — Documented: wikipedia timeout, snippet, Docker, model capabilities, context length

**KI (5):** KI-094, KI-095, KI-112, KI-116, KI-117

| KI | Название |
| :--- | :--- |
| KI-094 | `wikipedia_search` intermittent timeout в агенте (SSL через прокси) |
| KI-095 | `snippet` `fetch_web_content` может начинаться с текста, дублирующего `label` |
| KI-112 | Docker-образ `iichattools` без `git`, `gh`, `python3`, `node` |
| KI-116 | `gemma-4-12b-coder-fable5-composer2.5-v1` не поддерживает OpenAI tool calling |
| KI-117 | LM Studio: Context Length 8192 недостаточно для агентов |

**Отчёт:** docs/development/reports/v2.2/S17_KI.md

---

## S18 — Documented: галлюцинации числа инструментов, аудио, approval-скриншот, overlay

**KI (5):** KI-120, KI-144, KI-180, KI-186, KI-189

| KI | Название |
| :--- | :--- |
| KI-120 | Chat LLM галлюцинирует количество инструментов |
| KI-144 | Chrome использует virtual audio device по умолчанию (Steam Streaming Microphone) |
| KI-180 | Approval-модалка попадает в скриншот Vision Agent |
| KI-186 | Vision Agent не может работать с UI IIChatTools (fresh profile) |
| KI-189 | После STOP от overlay Chat возвращает корректный ответ (Documented) |

**Отчёт:** docs/development/reports/v2.2/S18_KI.md

---

## S19 — Documented: mermaid, Debate tokens, model null, метрики, embedding-конфиг

**KI (5):** KI-239, KI-241, KI-077, KI-044, KI-089

| KI | Название |
| :--- | :--- |
| KI-239 | Chat: слабые LLM галлюцинируют синтаксис mermaid (gantt, ER) |
| KI-241 | Debate: tokensIn/tokensOut = 0 в AgentDebateRound |
| KI-077 | `model: null` при PUT агента = «сбросить на default», а не «не менять» |
| KI-044 | Метрики `iichattools_audit_entries_total` и `iichattools_lmstudio_requests_total` пока не инкрементируются |
| KI-089 | Секция конфигурации эмбеддингов (`Rag:Embedding` vs `LmStudio:Embedding*`) |

**Отчёт:** docs/development/reports/v2.2/S19_KI.md

---

## S20 — Documented: Avalonia overlay, AuditLogs, code_agent selection

**KI (3):** KI-143, KI-169, KI-118

| KI | Название |
| :--- | :--- |
| KI-143 | Cross-platform overlay на Avalonia (альтернатива KI-142) |
| KI-169 | SubAgent: внутренние tool-вызовы не попадают в AuditLogs |
| KI-118 | Chat LLM не вызывает `code_agent` для простых задач |

**Отчёт:** docs/development/reports/v2.2/S20_KI.md

---
