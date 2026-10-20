# Phase KI-245 — Admin UI Orchestrator (runtime whitelist + лимиты)

**Версия:** v2.2.2 (Unreleased)
**Дата:** 2026-10-20
**Правила:** `docs/development/RULES.md` v1.4.36
**Статус:** код + тесты + документация готовы; **коммит не сделан** (по требованию).
**Родительский KI:** KI-126 (Orchestrator-Worker), **KI-245** → ✅ Fixed (v2.2.2).
**DESIGN:** `docs/development/v2.0/DESIGN_ORCHESTRATOR_WORKER.md` § 2.4 / § 7.
**Независимая проверка:** PASS WITH NITS → все Minor исправлены (см. § 5), Nit 5–7, 10 устранены, Nit 8–9 приняты как осознанные.

---

## 1. Файлы (+/-)

### Создано (11 файлов кода, 3 133 строки + отчёт)

| Файл | Строк | Назначение |
|:--|--:|:--|
| `IIChatTools.Services/DTO/Orchestrator/OrchestratorSettingsDto.cs` | 67 | DTO: `MaxSubtasks`, `MaxParallel`, `TokenBudget`, `HumanApproval`, `AllowedWorkerAgents`, `IsOverridden` |
| `IIChatTools.Services/Interfaces/IOrchestratorOptionsProvider.cs` | 57 | Контракт Singleton-провайдера: `Get/Update/Reset/IsOverridden` |
| `IIChatTools.Services/Interfaces/IAdminOrchestratorService.cs` | 66 | Контракт Scoped admin-сервиса: `GetSettingsAsync/UpdateSettingsAsync/ResetAsync` |
| `IIChatTools.Services/Implementation/Orchestrator/OrchestratorOptionsProvider.cs` | 454 | Baseline из `IConfiguration` (indexer + `TryParse`, RULES § 4.56), runtime-override в `ConcurrentDictionary`, сравнение с baseline для `IsOverridden`, clamp/normalize, defaults |
| `IIChatTools.Services/Implementation/Orchestrator/AdminOrchestratorService.cs` | 365 | Валидация → persist в `AppSettings` (`Orchestrator.*`) → `provider.Update()` → audit |
| `IIChatTools.Services/Implementation/Orchestrator/OrchestratorOverridesLoader.cs` | 175 | Разбор `Orchestrator.*` из `AppSettings` при старте; устойчивость к мусору в БД (вынесено из `Program` для тестируемости) |
| `IIChatTools.API/Controllers/AdminOrchestratorController.cs` | 199 | `GET/PUT /api/admin/orchestrator/settings`, `POST /api/admin/orchestrator/reset`, `[Authorize(Policy="AdminOnly")]` |
| `IIChatTools.API/wwwroot/js/modules/admin-orchestrator.js` | 265 | `loadSettings / saveSettings / resetSettings`, вся локализация через `data-*` (RULES § 4.17) |
| `IIChatTools.Tests/UnitTests/Orchestrator/OrchestratorOptionsProviderTests.cs` | 451 | 30 кейсов: baseline/defaults/clamp/update/reset/copy/thread-safety/границы 50–51/равенство baseline |
| `IIChatTools.Tests/UnitTests/Orchestrator/AdminOrchestratorServiceTests.cs` | 460 | 24 кейса: валидация, persist, audit, reset |
| `IIChatTools.Tests/UnitTests/Orchestrator/AdminOrchestratorControllerTests.cs` | 282 | 8 кейсов: `{success,data}`, ошибки, userId из claims |
| `IIChatTools.Tests/UnitTests/Orchestrator/OrchestratorOverridesLoaderTests.cs` | 292 | 11 кейсов: стартовое восстановление override'ов, мусор в БД |
| `docs/development/reports/v2.2/PHASE_KI245.md` | — | этот отчёт |

### Изменено (10 файлов)

| Файл | +/- | Что |
|:--|:--|:--|
| `IIChatTools.Services/Implementation/Tools/Debate/OrchestrateTaskTool.cs` | +19 / −60 | `IConfiguration` → `IOrchestratorOptionsProvider`; удалены `ReadConfig()` и `OrchestrateTaskConfig`; актуальные настройки читаются на каждый вызов |
| `IIChatTools.API/Startup.cs` | +11 / −0 | DI: `AddSingleton<IOrchestratorOptionsProvider>`, `AddScoped<IAdminOrchestratorService>` |
| `IIChatTools.API/Program.cs` | +46 / −0 | `LoadOrchestratorOverridesAsync()` → `OrchestratorOverridesLoader.ApplyAsync`; ошибки persisted-значений не роняют приложение |
| `IIChatTools.API/Views/Home/Admin.cshtml` | +81 / −0 | 8-я вкладка «Оркестратор»: форма (5 полей), бейдж «изменено», Save/Reset, 20 `data-*`-атрибутов |
| `IIChatTools.API/Resources/SharedResources.resx` | +65 / −0 | 21 новый EN-ключ |
| `IIChatTools.API/Resources/SharedResources.ru.resx` | +65 / −0 | те же 21 ключ по-русски (паритет проверен `LocalizationSyncTests`) |
| `IIChatTools.Tests/UnitTests/Debate/OrchestrateTaskToolTests.cs` | +57 / −2 | конструктор на провайдер + тест runtime-override (25 → 26 кейсов) |
| `CHANGELOG.md` | +27 / −0 | `[Unreleased] → Added`: Admin UI Orchestrator + тесты (KI-245) |
| `docs/KNOWN_ISSUES.md` | +107 / −15 | KI-245 → Fixed (v2.2.2) с фактическими файлами; новые KI-273, KI-274; сводка пересчитана (253) |
| `docs/development/RULES.md` | +8 / −2 | v1.4.36: § 2.18 (стандарт отчётов, 7 разделов), § 4.56 (`IConfiguration` — только indexer + `TryParse`) |

**Итого:** 21 файл; новых 3 133 строки кода (+ отчёт), в существующих файлах **+486 / −79**. Миграция БД **не требуется** (хранение в `AppSettings`).

---

## 2. Покрытие

**Новые suite'ы (73 кейса):**

- `OrchestratorOptionsProviderTests` — **30 кейсов**:
  defaults без секции; чтение baseline из config (числа + массив); clamp вне диапазона; fallback при нечисловых значениях; `HumanApproval` case-insensitive + fallback; пустой/«грязный» whitelist (trim, дедупликация); update (значения + нормализация + `IsOverridden`); `Update(null)`; диапазоны `MaxSubtasks` 0/−1/11, `MaxParallel` 0/11, `TokenBudget` 999/1 000 001; пустой/пробельный whitelist; невалидный approval; «failed validation не меняет состояние»; **границы whitelist 50 (ok) / 51 (throw)**; **update, равный baseline → `IsOverridden=false`**; reset к baseline; `Get()` возвращает копию; конкурентный `Parallel.For(1000)` Get/Update/Reset; null-аргументы конструктора.
- `AdminOrchestratorServiceTests` — **24 кейса**:
  baseline; persist всех 5 ключей (`Value/Type/Category`); JSON-массив агентов; повторный update без дублей; нормализация входа; немедленное применение к провайдеру; audit; валидация всех диапазонов и whitelist/approval (+ «нет side effects: БД пуста, провайдер на baseline, audit не пишется»); `Update(null)`; reset (удаляет только `Orchestrator.*`, baseline восстановлен, audit, идемпотентен).
- `AdminOrchestratorControllerTests` — **8 кейсов**:
  GET success/fail; PUT success (userId из claims, тело, data), null body, `ArgumentException` → `{success:false,message}`, generic exception; POST reset success/fail.
- `OrchestratorOverridesLoaderTests` — **11 кейсов** (verification-fix — ранее самый рискованный код был без тестов):
  нет настроек / нет `Orchestrator.*`; полный набор 5 ключей (нормализация approval, JSON-массив); частичный набор (merge с baseline); нечисловое значение; валидный + битый; битый JSON агентов; неизвестное поле; значение вне диапазона → `ArgumentException`, baseline сохранён; невалидный approval; null-аргументы.

**Обновлено (1 кейс):**

- `OrchestrateTaskToolTests` — 25 → **26 кейсов**: конструктор переведён на `IOrchestratorOptionsProvider`; добавлен `ExecuteAsync_RuntimeOverride_IsAppliedOnNextCall` (whitelist меняется в runtime → вторая оркестрация без пересоздания tool'а выполняет ранее отклонённого агента). Все прежние 25 кейсов зелёные.

**Итого по задаче:** +74 тест-кейса (1 253 → 1 327 total).

---

## 3. Результаты

| Проверка | Результат |
|:--|:--|
| `dotnet build IIChatTools.sln` | **0 warnings / 0 errors** |
| `dotnet test IIChatTools.sln` | **1 319 passed / 0 failed / 8 skipped / 1 327 total**, 2–4 s |
| Новые suite'ы отдельно | provider 30/30, service 24/24, controller 8/8, loader 11/11 |
| `OrchestrateTaskToolTests` | 26/26 |
| `LocalizationSyncTests` (RU/EN resx) | зелёный |
| `git status` | только целевые файлы; **коммита нет** (`HEAD = 3598dcf`) |
| Независимая проверка (verifier) | PASS WITH NITS; Minor исправлены (см. § 5), повторный Build/Test — те же 0/0 и 1 319/1 327 |

8 skipped — существующие интеграционные тесты (реальные внешние LLM / реальный Chrome), к KI-245 отношения не имеют.

---

## 4. Smoke

**A. Автоматизированный end-to-end (выполнен, не только план).** Приложение запущено на временной SQLite-БД (`smoke-ki245.db`), `ASPNETCORE_ENVIRONMENT=Development`, порт 5099:

1. `GET /auth/register` → antiforgery-token → `POST /auth/register` (первый пользователь = Admin).
2. `POST /auth/token` → JWT, роли `[Admin]`.
3. `GET /api/admin/orchestrator/settings` → `{success:true, data:{maxSubtasks:5,maxParallel:3,tokenBudget:50000,humanApproval:"Never",allowedWorkerAgents:[6 агентов],isOverridden:false}}`.
4. `PUT .../settings` (`7 / 2 / 123456 / BetweenSubtasks / [web_agent, code_agent]`) → `success:true`, `isOverridden:true`; повторный `GET` подтверждает.
5. `PUT` с `maxSubtasks=0` → `{success:false, message:"MaxSubtasks должен быть в диапазоне 1–10..."}`; состояние осталось прежним (7).
6. **Рестарт приложения** → `GET` снова отдаёт persisted-override (7/2/123456/BetweenSubtasks/[web,code], `isOverridden:true`) — `Program.LoadOrchestratorOverridesAsync` + `OrchestratorOverridesLoader` работают.
7. `POST .../reset` → baseline (5/3/50000/Never/6 агентов, `isOverridden:false`); `GET` подтверждает.
8. `GET /api/admin/orchestrator/settings` **без токена** → HTTP **401**.
9. `GET /Admin` (JWT) → HTTP 200, в HTML присутствуют `tab-orchestrator`, `pane-orchestrator`, все поля (`orch-max-subtasks`, `orch-allowed-workers`, `btn-save-orchestrator`), RU-локализация; `GET /js/modules/admin-orchestrator.js` → 200 (**9 787 байт**).

Временная БД удалена, процесс остановлен. **Не выполнено в smoke:** реальный вызов `orchestrate_task` с LLM (LM Studio в окружении не запущен) — runtime-применение настроек покрыто unit-тестом `ExecuteAsync_RuntimeOverride_IsAppliedOnNextCall`; визуальный клик по вкладке — проверен server-rendered markup + статика, не браузером.

**B. Ручной сценарий для приёмки (QA):**

1. `dotnet run` → `/admin` (Admin) → вкладка **«Оркестратор»** (8-я).
2. Изменить `Allowed worker agents`: убрать `web_agent`, добавить/оставить `code_agent`; Save → тост «Настройки сохранены», бейдж «изменено».
3. В чате: approval → `orchestrate_task` с задачей, требующей web-поиска. Ожидаемо: подзадача с `web_agent` → `status=rejected` («не в whitelist воркеров»), `code_agent`-подзадачи выполняются; блок Orchestrator в UI показывает причины.
4. Вернуть `web_agent` через админку, повторить → подзадача выполняется без рестарта.
5. «Сбросить к умолчаниям» → baseline (5/3/50000/Never/6 агентов); перезапуск приложения → baseline сохраняется.
6. Негатив: `MaxSubtasks=0` / пустой список агентов → клиентская валидация/сообщение сервера, настройки не меняются.

---

## 5. Что не так

1. **`HumanApproval` и `TokenBudget` пока не enforced.** Они валидируются, сохраняются, попадают в `AgentDebateConfigSnapshot` и SSE, но фактический HITL-стоп и стоп по бюджету — это KI-248 / KI-247. Админка управляет **данными**, а не поведением паузы/бюджета. Ограничение родительской фичи, не KI-245.
2. **Whitelist не сверяется с реестром агентов** (свободный `textarea` по ТЗ): опечатка или выключенный агент сохранится, а подзадачи будут молча `rejected`. Зарегистрировано как **KI-273**.
3. **Секции `SubAgents:orchestrate_task` в `appsettings.json` нет** — baseline до первого сохранения = hardcoded defaults провайдера (5/3/50000/Never/6 агентов). До KI-245 это тоже было так (fallback в `ReadConfig`), поведение не изменилось; persisted-override'ы загружаются при старте. Секцию намеренно не добавляли в `appsettings.json`, чтобы не создавать псевдо-агента в `SubAgents` (реестр обходит всех детей секции).
4. **Уже запущенная сессия не перечитывает настройки.** `AgentDebateConfigSnapshot` фиксируется на старте: если агента убрали из whitelist, уже выполняющаяся подзадача завершится, остальные подзадачи **этой** сессии продолжат по старому snapshot; новый вызов `orchestrate_task` уже увидит новый whitelist. Ожидаемое поведение (воспроизводимость сессии), зафиксировано в KI-245.
5. **Ошибка валидации содержит `(Parameter 'source')`** (передаётся `nameof(source)` в `ArgumentException`) — косметический хвост в сообщении UI. Прецедент — `AdminSqlAgentService`; формат не меняли ради консистентности.
6. **Расхождение с постановкой по номеру вкладки:** в задании «10-я или 11-я вкладка», фактически в `Admin.cshtml` было 7 вкладок (Feedback-вкладки нет) → добавлена **8-я**. Функционально соответствует.
7. **Smoke без реального LLM** (см. § 4): end-to-end API + restart + auth проверены; фактическая оркестрация с моделью — только unit-уровень.
8. **Cookie-login путь в smoke не проверялся** — `/Admin` запрошен с JWT через `SmartScheme`; cookie-login стандартный, не менялся.
9. **Persist 5 ключей — без транзакции** (найдено verification'ом): при сбое БД посередине часть ключей может остаться, runtime не обновится, при следующем рестарте применится частичный набор. Паттерн унаследован от `AdminSqlAgentService`; зарегистрировано как **KI-274**.
10. **Принятые nit'ы:** 7 из 20 `data-*`-ярлыков не читаются JS (дублируют Razor-рендер — оставлены для единообразия и будущего использования); строки `Orchestrator.*` видны в generic-вкладке «Настройки» (прямое редактирование применится только после рестарта; так же ведут себя `SqlAgent.*` — pre-existing, скрывать не стали).

---

## 6. Новые KI

- **KI-273 — Orchestrator Admin UI: whitelist не валидируется против реестра агентов** (🟢 Low, Planned v2.2.x). Backend проверяет только синтаксис (непустой список, ≤50, trim/дедупликация), но не `ISubAgentRegistry`/`Enabled`; UI — свободный `textarea`. План: read-model реестра + предупреждение/`datalist`/multi-select.
- **KI-274 — Admin UI Orchestrator: persist 5 ключей без транзакции** (🟢 Low, Planned v2.2.x). Сбой БД посередине upsert'ов оставляет частичный набор; runtime при этом не меняется, но следующий старт применит частичный override. План: транзакция/batch + компенсация.
- **KI-264** (RULES § 2.18 не существовал) — **частично закрыт**: добавлены § 2.18 «Стандарт отчётов (7 разделов)» и § 4.56 «`IConfiguration` — только indexer + `TryParse`» в RULES v1.4.36. Статус KI-264 отдельно не меняли (можно закрыть при следующей ревизии реестра).
- Всего в реестре **253** уникальных номера (Fixed 162 / Planned 52); блокеров нет.

---

## 7. Обоснование архитектуры (Provider + Service, как у SqlAgent)

Выбрана **двухуровневая схема `Singleton OptionsProvider` + `Scoped AdminService`** — та же, что в v1.7.0 для `SqlAgentOptionsProvider` / `AdminSqlAgentService` (ADR-011, DESIGN_DB_AGENT § 5.4/§ 7.6), потому что задача архитектурно идентична: «baseline из конфигурации + runtime-override из админки + persist + восстановление при старте».

**Почему Provider (Singleton):**

- **Единый источник истины в runtime.** `OrchestrateTaskTool` — Scoped, но вызывает Singleton-провайдер на каждый вызов; изменения из админки видны следующей оркестрации **без рестарта** и без кэша в tool'е.
- **Baseline читается один раз** в конструкторе (иммутабелен), runtime-override хранится как **единый неизменяемый снимок** в `ConcurrentDictionary` (ключ `current`). Замена ссылки атомарна → читатели никогда не видят «полуобновлённых» настроек; `Get()` возвращает глубокую копию, поэтому вызывающий не может испортить состояние. `IsOverridden` вычисляется сравнением снимка с baseline (не «факт наличия override»), поэтому no-op Save не зажигает бейдж «изменено».
- **RULES § 4.56:** `OrchestrateTaskTool` больше не инжектит `IConfiguration`; конфигурация читается только в провайдере и только через indexer + `TryParse` (для массива — `GetChildren()`). Это убирает прецедент KI-270/4.56 (падения fake-конфигураций в тестах) и локализует знание о ключах в одном месте.
- **Тестируемость:** провайдер тестируется без DI/БД (30 кейсов, включая конкурентность).

**Почему Service (Scoped):**

- Работа с `AppSettings`/`IAuditService` требует `AppDbContext` → Scoped, как `AdminSqlAgentService`.
- **Валидация до persist** (сервис — граница доверия): некорректные данные не попадают в БД и не меняют runtime; provider дополнительно валидирует defensively (вызов из `OrchestratorOverridesLoader`).
- **Persist отдельными ключами** `Orchestrator.MaxSubtasks|MaxParallel|TokenBudget|HumanApproval|AllowedWorkerAgents(JSON)` — консистентно с `SqlAgent.{name}.{field}`: ключи видны/редактируемы в generic-вкладке «Настройки», легко удалить reset'ом по префиксу, не нужен формат-эволюция одного JSON-блоба. (У `SubAgents.*` — JSON-блоб, но там объект-дескриптор агента; здесь — плоский набор скаляров.)
- Аудит — в сервисе (не в контроллере), контроллер тонкий и единообразный `{success,data}`.

**Почему startup-load вынесен в отдельный loader, а Orchestrator — не `IConfiguration`-биндинг:**

- `OrchestratorOverridesLoader` — тестируемая чистая функция «AppSettings → снимок → провайдер»: 11 кейсов на мусор в БД (нечисловые значения, битый JSON, неизвестные поля, out-of-range), которые иначе роняли бы старт или молча терялись. `Program` только резолвит зависимости и логирует; ошибки persisted-значений не роняют приложение (provider остаётся на baseline).
- Один явный шаг после `SyncDefaultsFromConfigurationAsync` и `LoadSqlAgentOverrides` (симметрия), до старта хоста.
- Альтернативы отклонены: `IOptionsMonitor` + reloadOnChange — не даёт persist и админ-UI; статический синглтон — не тестируется; хранение в `appsettings.json` из приложения — против RULES § 5.1/2.6 и не работает в контейнере.

**Итог:** решение минимально инвазивно (нет миграции, нет новых сущностей), переиспользует проверенный паттерн SqlAgent, сохраняет обратную совместимость (defaults = прежний `ReadConfig`) и даёт runtime-управление через Admin UI.
