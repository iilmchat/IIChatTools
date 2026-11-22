# TODO_KI — план устранения незакрытых KI

**Дата:** 2026-11-21
**Источник:** `docs/KNOWN_ISSUES.md` (реестр пересчитан 2026-10-20; HEAD `54f035b`, ветка `main`)
**Правила:** `docs/development/RULES.md` v1.4.36
**Охват:** все незакрытые записи — **89**:
Planned 50, Documented 25, Deferred 8, Partially Fixed 4, In Progress 1, Won't Fix 1.
Всего в реестре 254 секции `### KI-` / 253 уникальных номера.

**Роль файла:** рабочий план устранения. `docs/KNOWN_ISSUES.md` остаётся
единственным source of truth: описания, решения и списки файлов не дублируются,
здесь фиксируются только **порядок**, **приоритет** и **критерии закрытия**.

---

## 1. Сводка

### По статусу

| Статус | Кол-во | Действие |
| :--- | ---: | :--- |
| Planned | 50 | Ре-триаж (см. Фазу 0), затем реализация по приоритету |
| Documented | 25 | Проверка актуальности: закрыть как «не актуально» или перевести в Planned |
| Deferred | 8 | Пересмотреть окно планирования; при подтверждении — в Planned |
| Partially Fixed | 4 | Доделать оставшуюся часть и перевести в Fixed |
| In Progress | 1 | Завершить (KI-234) |
| Won't Fix | 1 | Не планировать (KI-159, оставлен для истории) |

### По приоритету

| Приоритет | Кол-во | Комментарий |
| :--- | ---: | :--- |
| 🔴 High | 1 | KI-116 (Documented) — требуется проверка актуальности |
| 🟠 High | 7 | Из них 5 Planned, 1 Partially Fixed, 1 Documented |
| 🟡 Medium | 23 | Из них 16 Planned, 7 в других статусах |
| 🟢 Low | 58 | Большинство — Planned/Documented с истёкшими версиями |

---

## 2. Порядок работ

### Фаза 0. Ре-триаж (обязательна перед писанием кода)

Большинство записей спланированы на **v1.x**, тогда как текущая development-ветка
уже **v2.2.x**. Версии в реестре устарели, поэтому перед реализацией:

1. Для каждой Planned/Documented/Deferred из списка § 3 проверить по факту
   кода: воспроизводится ли проблема в HEAD `54f035b`.
2. Не воспроизводится → перевести в `Fixed` с формулировкой
   «не воспроизводится в v2.2.x» и ссылкой на проверку.
3. Воспроизводится → обновить поле `**Запланировано:**` на актуальную версию,
   не менять номер KI.
4. Итог ре-триажа — отдельный коммит `docs:` (RULES § 3.1/§ 3.2).

### Фаза 1. P0 — 🔴/🟠 High (8 записей)

Сначала Partially Fixed и In Progress (малый остаток), затем Planned High,
затем Documented High (только после подтверждения актуальности).

### Фаза 2. P1 — 🟡 Medium (23 записи)

Сначала 7 записей в незакрытых статусах (KI-032, KI-009, KI-047, KI-053,
KI-091, KI-114, KI-173), затем 16 Planned Medium. Группировать по подсистемам
(Vision / RAG / Chat / Mail), не разбрасывать по коммитам.

### Фаза 3. P2 — 🟢 Low (58 записей)

Только после Фаз 1–2. Узкие UX-долги (KI-237, KI-151, KI-209…KI-211) объединять
в один пакет по подсистемам, чтобы не плодить микро-коммиты.

### Фаза 4. Сверка реестра

Исправить расхождения, перечисленные в § 5, **после** того как будут закрыты
или переклассифицированы записи из Фаз 0–3 (иначе цифры снова устареют).

---

## 3. Чеклист KI

> Описание проблемы и планируемое решение — в одноимённой секции
> `docs/KNOWN_ISSUES.md` (`### KI-XXX — …`). Здесь приведены только номер,
> название, приоритет, статус и целевая версия из реестра.

### 3.1. In Progress (1)

- [x] **KI-234** — Chat: Retry в error-bubble (после network error) — 🟢 Low, ✅ Fixed в S02 (v2.2.x)

### 3.2. Partially Fixed (4)

- [x] **KI-057** — `/api/models` возвращает embedding-модели — 🟢 Low, ✅ Fixed в S02 (v2.2.x)
- [x] **KI-114** — `AgentToolBase` возвращает `ToolResult.Ok` при `Completed=false` — 🟡 Medium, ✅ Fixed в S02 (v2.2.x)
- [x] **KI-173** — Vision Agent: LLM не передаёт `url` отдельно, падает на timeout — 🟡 Medium, ✅ Fixed в S02 (v2.2.x)
- [x] **KI-182** — Vision Planner зацикливается на одном target — 🟠 High, ✅ Fixed в S02 (v2.2.x)

### 3.3. Planned — High (5)

- [x] **KI-167** — `file_system_agent` галлюцинирует успех вне workspace (рецидив KI-113) — 🟠 High, ✅ Fixed в S03 (v2.2.x)
- [x] **KI-177** — Vision Agent: `click(target=X)` не находит X в свежем describe — 🟠 High, ✅ Fixed в S03 (v2.2.x)
- [x] **KI-178** — Vision LLM timeout 300 сек при 2000+ токенах промпта — 🟠 High, ✅ Fixed в S03 (v2.2.x)
- [x] **KI-192** — Vision Agent: VL возвращает пустой `ui_elements` на медленно грузящихся страницах — 🟠 High, ✅ Fixed в S03 (v2.2.x)
- [x] **KI-227** — RAG embeddings зависят от LM Studio (нет внешнего провайдера) — 🟠 High, ✅ Fixed в S04 (v2.2.x)

### 3.4. Planned — Medium (16)

- [x] **KI-088** — Актуальный чек-лист ручной приёмки (`docs/TESTING.md`) — 🟡 Medium, ✅ Fixed в S01 (v2.2.x)
- [x] **KI-111** — `mail_agent` не помнит контекст между вызовами — 🟡 Medium, ✅ Fixed в S05 (v2.2.x)
- [x] **KI-113** — `mail_agent`: qwen3-4b галлюцинирует «успех» при неудаче — 🟡 Medium, ✅ Fixed в S05 (v2.2.x)
- [x] **KI-128** ✅ Fixed в S06 — Browser workflow недоступен через Chat (`browser_agent` + `save_screenshot_to_file`) — 🟡 Medium, v1.11.x
- [x] **KI-168** ✅ Fixed в S06 — Chat LLM выбирает `file_system_agent` для задач вне workspace — 🟡 Medium, v1.13.x
- [x] **KI-172** ✅ Fixed в S06 — Chat LLM выбирает `vision_agent` для browser-задач — 🟡 Medium, v1.13.x
- [x] **KI-179** ✅ Fixed в S06 — Chat LLM зацикливается на Vision-задачах — 🟡 Medium, v1.13.x
- [x] **KI-193** — Vision LLM: id drift между кадрами (`search_btn` → `search_button`) — 🟡 Medium, ✅ Fixed в S04 (v2.2.x)
- [x] **KI-204** ✅ Fixed в S07 — RAG: нет индикации прогресса OCR при загрузке PDF — 🟡 Medium, v1.13.x
- [x] **KI-205** — RAG: постраничный просмотр PNG сканов — 🟢 Low, ✅ Fixed в S04 (v1.13.x, верификация)
- [x] **KI-206** ✅ Fixed в S07 — RAG: Page viewer — thumbnails, зум, скролл — 🟡 Medium, v1.13.x
- [x] **KI-213** ✅ Fixed в S07 — RAG: Page viewer — подгонка text layer под bbox — 🟡 Medium, v1.13.x / v1.14
- [x] **KI-226** — `/api/models` и UI-селектор модели зависят от LM Studio — 🟡 Medium, ✅ Fixed в S04 (v2.2.x)
- [x] **KI-231** ✅ Fixed в S06 — Chat: Drag-and-Drop файлов + Paste Image (Ctrl+V) — 🟡 Medium, v1.13.14
- [x] **KI-253** — `qa/MANUAL_TESTING_GUIDE.md` устарел (v1.8.1, 424 теста) — 🟡 Medium, ✅ Fixed в S01 (v2.2.x)
- [ ] **KI-265** — Durable queue для Background Jobs — 🟡 Medium, v2.1.x
- [x] **KI-267** ✅ Fixed в S08 — Reflection: гетерогенная модель самокритики / эскалация — 🟡 Medium, v2.2.x

### 3.5. Planned — Low (29)

- [ ] **KI-108** — Per-user mail accounts (свой ящик у каждого пользователя) — 🟢 Low, v1.8.x
- [ ] **KI-138** — Vision Agent: маскирование PII на скриншотах — 🟢 Low, v1.12.x
- [ ] **KI-139** — Vision Agent: поддержка внешних VL (Claude Computer Use / OpenAI CUA) — 🟢 Low, v1.12.x
- [ ] **KI-143** — Cross-platform overlay на Avalonia (альтернатива KI-142) — 🟢 Low, v1.13.x / v1.14.x
- [ ] **KI-151** — Chrome temp-профиль не удаляется (`BrowserMetrics-*.pma` locked) — 🟢 Low, v1.12.x
- [ ] **KI-162** — Vision Agent: Coordinate-then-Verify — 🟢 Low, v1.13.x
- [ ] **KI-163** — Vision Agent: Set-of-Mark (упрощённый, Tesseract-based) — 🟢 Low, v1.14.x
- [ ] **KI-164** — Vision Agent: drag-select (выделение текста мышью) — 🟢 Low, v1.13.x
- [ ] **KI-165** — Vision Agent: чтение буфера обмена (read clipboard) — 🟢 Low, v1.13.x
- [ ] **KI-166** — Vision Agent: детекция иконок в taskbar (Win+1, crop, fallback) — 🟢 Low, v1.13.x
- [ ] **KI-209** — RAG: Page viewer — экспорт отдельной страницы — 🟢 Low, v1.14
- [ ] **KI-210** — RAG: Batch-экспорт нескольких attachment'ов — 🟢 Low, v1.14
- [ ] **KI-211** — RAG: Page viewer — прогресс экспорта 100+ страниц — 🟢 Low, v1.14
- [ ] **KI-237** — Chat UX backlog (бандл мелких улучшений) — 🟢 Low, v1.14+
- [ ] **KI-240** — Chat: Feedback — Admin UI + экспорт JSONL для датасета — 🟢 Low, v1.14
- [ ] **KI-242** — PostgreSQL как провайдер основного `AppDbContext` — 🟢 Low, v1.15+ (дальнее будущее)
- [ ] **KI-243** — Qdrant как внешний `IVectorStore` (persistent vector store) — 🟢 Low, v1.15+ (дальнее будущее)
- [ ] **KI-246** — Fallback на другого воркера при fail в Orchestrator — 🟢 Low, v2.0.x
- [ ] **KI-247** — Orchestrator: pre-checks circuit breaker / budget на уровне оркестратора — 🟢 Low, v2.0.x
- [ ] **KI-248** — Orchestrator: Human-in-the-loop между подзадачами — 🟢 Low, v2.0.x
- [x] **KI-254** — `RULES.md` § 7: выжимка KI устарела (99 KI vs 230) — 🟢 Low, ✅ Fixed в S01 (v2.2.x)
- [ ] **KI-257** — `BrowserSessionControlTool`: cast к конкретному `BrowserSessionManager` — 🟢 Low, v2.1.0
- [ ] **KI-258** — `run_smoke`: `assert_text` регистрозависим — 🟢 Low, v2.1.0
- [ ] **KI-262** — Todo: гонка `MAX(SortOrder)+1` при параллельном `add_todo` — 🟢 Low, v2.1.x
- [x] **KI-266** ✅ Fixed в S08 — Reflection: прямой вызов `ISubAgentService` минует AuditLogs/Admin UI — 🟢 Low, v2.2.x
- [x] **KI-269** ✅ Fixed в S08 — Reflection: дублирование черновиков в `AgentStates` без retention — 🟢 Low, v2.2.x
- [ ] **KI-273** — Orchestrator Admin UI: whitelist не валидируется против реестра агентов — 🟢 Low, v2.2.x
- [ ] **KI-274** — Admin UI Orchestrator: persist 5 ключей без транзакции — 🟢 Low, v2.2.x

### 3.6. Deferred (8)

- [ ] **KI-047** — Fallback PATCH/DELETE через POST для старых сетей — 🟡 Medium, v1.3.x
- [ ] **KI-053** — Multi-user approvals для критичных операций — 🟡 Medium, v1.4.0
- [ ] **KI-074** — Split тестового проекта на Unit / Integration — 🟢 Low, v1.4.x (опционально)
- [ ] **KI-082** — Модалка-редактор длинных user-сообщений — 🟢 Low, v1.5.0+ (дальний фокус)
- [ ] **KI-091** — SqlServer цепочка миграций повреждена (snapshot drift) — 🟡 Medium, v1.5.0-rc (перед релизом)
- [ ] **KI-096** — GitHub Wiki для проекта (roadmap) — 🟢 Low, v1.7+
- [ ] **KI-099** — Внешние подключения БД (Postgres / MySQL / Oracle) — 🟢 Low, v1.8.0 (Фаза 2 KI-097)
- [ ] **KI-123** — Индикатор загрузки списка чатов («Идёт загрузка» + spinner) — 🟢 Low, v1.9.x

### 3.7. Documented (25)

Проверить актуальность; если ограничение снято или признано допустимым —
закрыть с пояснением, если нет — перевести в Planned и дать новую версию.

- [ ] **KI-007** — `gh issue create` требует заранее созданные метки — 🟢 Low
- [ ] **KI-009** — `ERR_CONNECTION_RESET` на сайтах с SSO — 🟡 Medium
- [ ] **KI-032** — `Unprotect ticket failed` (старые cookies от .NET Core 3.1) — 🟡 Medium
- [ ] **KI-044** — Метрики `iichattools_audit_entries_total` / `iichattools_lmstudio_requests_total` не инкрементируются — 🟢 Low, v1.2.x
- [ ] **KI-048** — Email автора коммитов в git-истории — 🟢 Low
- [ ] **KI-070** — Sqlite: stale DB без новых таблиц (`no such table`) — 🟢 Low, v1.3.x
- [ ] **KI-077** — `model: null` при PUT агента = «сбросить на default», а не «не менять» — 🟢 Low
- [ ] **KI-089** — Секция конфигурации эмбеддингов (`Rag:Embedding` vs `LmStudio:Embedding*`) — 🟢 Low
- [ ] **KI-090** — SqlServer-migrations содержали Sqlite-типы (snapshot drift) — 🟢 Low
- [ ] **KI-093** — SQLite `database is locked` (дубликат KI-085, оставлен для истории) — 🟢 Low
- [ ] **KI-094** — `wikipedia_search` intermittent timeout в агенте (SSL через прокси) — 🟢 Low, v1.6.2
- [ ] **KI-095** — `snippet` `fetch_web_content` может начинаться с текста, дублирующего `label` — 🟢 Low
- [ ] **KI-112** — Docker-образ `iichattools` без `git`, `gh`, `python3`, `node` — 🟢 Low
- [ ] **KI-116** — `gemma-4-12b-coder-fable5-composer2.5-v1` не поддерживает OpenAI tool calling — 🔴 High
- [ ] **KI-117** — LM Studio: Context Length 8192 недостаточно для агентов — 🟠 High
- [ ] **KI-118** — Chat LLM не вызывает `code_agent` для простых задач — 🟢 Low
- [ ] **KI-120** — Chat LLM галлюцинирует количество инструментов — 🟢 Low
- [ ] **KI-144** — Chrome использует virtual audio device по умолчанию (Steam Streaming Microphone) — 🟢 Low
- [ ] **KI-169** — SubAgent: внутренние tool-вызовы не попадают в AuditLogs — 🟢 Low
- [ ] **KI-180** — Approval-модалка попадает в скриншот Vision Agent — 🟢 Low
- [ ] **KI-186** — Vision Agent не может работать с UI IIChatTools (fresh profile) — 🟢 Low
- [ ] **KI-189** — После STOP от overlay Chat возвращает корректный ответ — 🟢 Low
- [ ] **KI-239** — Chat: слабые LLM галлюцинируют синтаксис mermaid (gantt, ER) — 🟢 Low
- [ ] **KI-241** — Debate: `tokensIn`/`tokensOut` = 0 в `AgentDebateRound` — 🟢 Low, v1.14.x
- [x] **KI-264** — Документация: номера KI-257…260 из DESIGN § 12 не совпадают с реестром — 🟢 Low, ✅ Fixed в S01 (v2.2.x)

### 3.8. Won't Fix (1)

- [ ] **KI-159** — Vision Agent: координатная сетка на скриншотах — 🟢 Low, Won't Fix.
      **В работу не брать**; запись оставлена для истории. Проверить при следующей
      ревизии Vision Agent, если изменится подход к разметке экрана.

---

## 4. Критерии закрытия каждой записи

1. Поведение воспроизведено/проверено на HEAD; для «не воспроизводится» —
   указан способ проверки (RULES § 2.4).
2. `dotnet build IIChatTools.sln -c Debug` — **0 warnings, 0 errors** (RULES § 3.6).
   Перед сборкой остановить `IIChatTools.API`, если запущен (RULES § 3.14).
3. `dotnet test` — все тесты зелёные (RULES § 3.7); на Windows первый прогон
   после cold build — 44–60 с (RULES § 3.6a).
4. Код + документация — **в одном коммите**: `KNOWN_ISSUES.md` (статус `Fixed`,
   «Исправлено в: vX.Y.Z», список файлов) и `CHANGELOG.md` → `[Unreleased]`
   (RULES § 2.1, § 2.2, § 2.4, § 4.37).
5. Сообщение коммита содержит номер KI (RULES § 2.8), формат
   `<type>(<scope>): <subject>` (RULES § 6.4–6.5); `git add -A` + `git status`
   перед коммитом (RULES § 3.3, § 3.4).
6. Для крупной задачи — отчёт по стандарту 7 разделов
   `docs/development/reports/vX.Y/<PHASE>.md` (RULES § 2.18).
7. Дополнительные ограничения по типу правки:
   - новые UI-строки — в оба `.resx` (RULES § 1.14);
   - пути — только через `PathHelper.TryGetSafeFullPath` (RULES § 1.9/§ 5.3);
   - CLI — только `ProcessStartInfo.ArgumentList` (RULES § 1.10/§ 5.4);
   - изменение интерфейса — grep по всем реализациям и вызовам (RULES § 4.34, § 4.53);
   - новый инструмент — 1 класс + 1 строка в `Startup.cs` (RULES § 1.15),
     плюс `allowedNames` в `ChatStreamService` (RULES § 4.44);
   - секреты — только User Secrets / env (RULES § 5.1).
8. После push — проверить CI Actions (RULES § 3.5).

---

## 5. Расхождения, найденные при составлении плана

При сверке реестра с фактическим содержимым (2026-11-21) обнаружено:

1. **Сводка по статусам не совпадает с телом реестра.**
   В `KNOWN_ISSUES.md` (стр. 6537–6544) указано:
   Fixed 162 (включая Implemented/Resolved), Planned 52, Documented 25,
   Deferred 8, Partially Fixed 4, In Progress 1, Won't Fix 1.
   Фактически по секциям (всего **254**): Fixed 161 + Implemented 2 + Resolved 2
   = **165**, Planned **50**, Documented **25**, Deferred **8**,
   Partially Fixed **4**, In Progress **1**, Won't Fix **1**.
   Уникальных номеров **253** (секции `KI-110a` / `KI-110b` — один номер).
   Расхождение со сводкой: Fixed 165 против 162 (+3; для уникальных номеров
   164 против 162) и Planned 50 против 52 (−2). Documented, Deferred,
   Partially Fixed, In Progress, Won't Fix — совпадают.
2. **`check-zombie-ki.ps1` даёт 240 вместо 253.**
   В той же сводке (стр. 6557–6558) указан результат скрипта
   «Всего KI: 240, Сумма статусов: 240» — на 13 меньше фактического числа.
   Вероятная причина — скрипт не распознаёт часть форматов статуса
   (✅/Implemented/Resolved/Partially Fixed) или секции с суффиксами.
3. **Большинство целевых версий в реестре устарели** (v1.2.x…v1.15+) относительно
   текущей ветки v2.2.x — см. Фазу 0.

Действие: зарегистрировать пункты 1–2 отдельной записью `KI-275` в
`docs/KNOWN_ISSUES.md` (RULES § 1.13, § 2.4), затем исправить сводку и скрипт
в рамках Фазы 4. Номер `KI-275` зарезервирован планом, но **в реестре пока не
создан** — создаётся при начале работ по Фазе 4.

---

## 6. Ограничения плана

- План составлен по состоянию реестра на 2026-10-20; новые KI, появившиеся
  после этой даты, в него не входят.
- Отсутствуют оценки трудозатрат и назначенные исполнители — это задача
  ре-триажа (Фаза 0), а не данном файла.
- Файл не заменяет `KNOWN_ISSUES.md`: при расхождении правильным считается
  реестр, а этот план обновляется под него.
- Отметки `- [ ]` не синхронизируются автоматически со статусами в
  `KNOWN_ISSUES.md` — закрытие записи означает изменение **реестра**, а не только
  галочки здесь.
