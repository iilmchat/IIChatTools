# DESIGN_IMPROVEMENTS — программа улучшений IIChatTools (v2.3+)

**Версия:** 1.0
**Дата:** 2026-11-22
**Статус:** Proposed
**Автор:** DSH-ассистент (по итогам 20 KI-спринтов S01–S20)
**Правила:** `docs/development/RULES.md` v1.4.36
**Связанные:** `docs/TODO_KI.md`, `docs/TODO_KI_SPRINTS.md`,
`docs/development/reports/v2.2/S01_KI.md` … `S20_KI.md`

---

## § 1. Контекст и мотивация

За 20 спринтов устранения KIs закрыто 33 записи (Fixed) и 25 Documented
верифицировано. Осталось ~20 открытых KI — почти все они не «точечные баги»,
а симптомы шести системных проблем:

| Проблема | Проявления (KI) | Влияние |
| :--- | :--- | :--- |
| Нет единых абстракций провайдеров | KI-227 (embeddings), KI-226 (модели), KI-242/243 (БД/вектора), KI-139 (VL) | Каждый новый провайдер = правки в 3-5 местах |
| Надёжность «in-memory» | KI-265 (очередь), KI-262 (SortOrder race), KI-274 (persist без транзакции), KI-151 (temp-профили) | Потеря задач и частичные записи при сбое |
| Слабая наблюдаемость | KI-044 (метрики), KI-169 (audit агентов), KI-241 (токены Debate) | Невозможно понять, что происходит в проде |
| LLM-нестабильность без контракта | KI-113/167 (галлюцинации), KI-118/120/168/172/179 (выбор инструмента), KI-193 (id drift) | Рецидивы после смены модели |
| Ручная синхронизация документации | KI-254/264/275 (сводки), KI-253 (чекап), KI-147 (ARCHITECTURE) | Реестр расходится с кодом |
| Узкий протокол Vision backend | KI-164/165/166 (drag/clipboard/taskbar), KI-143 (overlay) | Невозможно расширять сценарии без ломки интерфейса |

Цель программы — перейти от реактивных фиксов к проактивной инженерии:
чёткие контракты, измеримость, отказоустойчивость и самоподдерживающаяся
документация.

---

## § 2. Принципы (industry best practices)

1. **Design for failure.** Любой внешний вызов (LLM, HTTP, БД, Chrome)
   имеет timeout, retry с exponential backoff + jitter и circuit breaker.
   Внешняя отмена (`CancellationToken`) всегда пробрасывается (RULES § 4.28/§ 4.48).
2. **Ports & Adapters.** Провайдеры (LLM, embeddings, vector store, БД,
   браузер) за интерфейсами; конфиг выбирает реализацию; тесты подменяют
   адаптер, а не ядро.
3. **Explicit over implicit.** Контракты инструментов и SSE-событий
   версионируются; несовместимые изменения — через новый контракт.
4. **Observability by default.** Метрики, структурированные логи и audit
   пишутся в одном месте (middleware/decorator), а не вручную в каждом сервисе.
5. **Docs as code.** Реестр KI, сводки и чеклисты генерируются скриптами
   из одного source of truth в CI; расхождение = красный билд.
6. **Small reversible steps.** Каждый этап — 2-4 спринта по 3-5 задач,
   один коммит на задачу, зелёный build/test, отчёт по RULES § 2.18.
7. **Security by design.** Секреты только User Secrets/env; per-user данные
   изолированы; PII маскируется до отправки во внешние LLM.

---

## § 3. Целевая архитектура

### 3.1. Слой провайдеров

```
IEmbeddingProvider   (LmStudio | OpenAI-compatible | ONNX-local)
ILLMProvider         (LmStudio | DeepSeek | OpenAI | RouterAI | Yandex)
IVectorStore         (InMemory | Sqlite | Qdrant)
IDatabaseProvider    (Sqlite | SqlServer | PostgreSQL)
IVisionBackend       (LocalHarness | CDP-attach | external CUA)
```

Выбор — через `Provider:Kind` в конфиге + DI-регистрация одного адаптера.
Ядро не знает конкретных SDK. Совместимость со старыми ключами конфига
сохраняется адаптерами (backward-compatible фасад).

### 3.2. Надёжность

- `IDurableJobQueue` — outbox-таблица + polling, idempotency key,
  recovery `Running → Pending` вместо `Failed` (KI-265).
- `IUnitOfWork` / batch-метод в `IAppSettingsService` — атомарная запись
  набора ключей (KI-274).
- Unique index `(ChatId, SortOrder)` + retry на `DbUpdateException` (KI-262).
- `ResiliencePolicy` — единый `Polly`-подобный helpers для timeout/retry/
  circuit breaker (без новых тяжёлых пакетов — на `CancellationTokenSource`).

### 3.3. Наблюдаемость

- `IMetricsService` с реальными `.Inc()` в `LmStudioClient`, `AuditService`,
  `ChatStreamService`, `BackgroundJobRunner` (KI-044, KI-241).
- Audit decorator для всех `ITool` — запись `agent.*` и `tool.*` централизованно
  (KI-169, KI-266).
- CorrelationId (trace) сквозь SSE → tool → agent → LLM; в логах и audit.

### 3.4. Документация как код

- `scripts/generate-ki-summary.ps1` — пересчёт сводки KNOWN_ISSUES из секций;
  проверка в CI (KI-275).
- `check-zombie-ki.ps1` расширяется проверкой «статус ↔ версия ↔ файлы».
- README/ARCHITECTURE/README-метрики обновляются генератором или тестом
  консистентности (KI-147, KI-253).

### 3.5. Контракт LLM-взаимодействия

- Версионированный `DefaultSystemPrompt` v3 с явными правилами выбора
  инструмента (KI-118/120/168/172/179), JSON-схемами ответов и few-shot.
- Контрактные тесты: на наборе фикстур проверяется, что промпт содержит
  обязательные секции; что tool result с `success=false` не трактуется как успех.
- Normalization слой для id vision-элементов (KI-193) и target fallback (KI-177).

---

## § 4. Этапы и спринты

Всего **6 этапов / 18 спринтов** (D01–D18), по 2-4 KI/задачи на спринт.
Каждый спринт = build 0/0 + tests green + отчёт RULES § 2.18 + commit + push.

### Этап A. Фундамент надёжности (D01–D03)

| Спринт | Задачи | Результат |
| :--- | :--- | :--- |
| D01 | KI-044, KI-241, метрики-декоратор | Реальные счётчики, /metrics не пустой |
| D02 | KI-262, KI-274 | Unique index + транзакция настроек, тесты гонки |
| D03 | KI-265 (часть 1), resilient helpers | Outbox-таблица + idempotency key, единая resilience-политика |

### Этап B. Провайдеры (D04–D07)

| Спринт | Задачи | Результат |
| :--- | :--- | :--- |
| D04 | IEmbeddingProvider (KI-227 доработка) | LmStudio + OpenAI-compatible через конфиг |
| D05 | IVectorStore (KI-243) | Sqlite → Qdrant adapter, миграция без простоя |
| D06 | ILLMProvider unify (KI-226 п.4-5) | `<provider>:<model>` в Chat.Model, обратная совместимость |
| D07 | IDatabaseProvider (KI-242, KI-099, KI-091) | PostgreSQL-адаптер + починенная SqlServer-цепочка |

### Этап C. Безопасность и аудит (D08–D10)

| Спринт | Задачи | Результат |
| :--- | :--- | :--- |
| D08 | KI-108 (per-user mail) | UserMailAccount + IDataProtector + UI /profile |
| D09 | KI-138, PII-masker | Маскирование email/телефонов до VL/LLM |
| D10 | KI-169, KI-266 (доработка), correlationId | Единый audit decorator для всех tools |

### Этап D. Vision-платформа (D11–D13)

| Спринт | Задачи | Результат |
| :--- | :--- | :--- |
| D11 | KI-164, KI-165 | Расширение IVisionBackend: drag_select + read_clipboard |
| D12 | KI-166, KI-143 | Taskbar-иконки + Avalonia overlay (или WPF-фallback) |
| D13 | KI-139, Vision prompt v3 | Внешние VL провайдеры (Claude CUA / OpenAI CUA) через адаптер |

### Этап E. Качество и документы (D14–D16)

| Спринт | Задачи | Результат |
| :--- | :--- | :--- |
| D14 | KI-254/275 генератор сводок, KI-147 | ARCHITECTURE.md актуален, сводка авто-пересчёт |
| D15 | KI-253, KI-237, KI-240 | Чеклист приёмки v2.3, UX-бандл, Feedback JSONL |
| D16 | KI-047, KI-074, KI-082 | PATCH/DELETE fallback, split тестов, модалка редактора |

### Этап F. Верификация и релиз (D17–D18)

| Спринт | Задачи | Результат |
| :--- | :--- | :--- |
| D17 | Контрактные/нагрузочные тесты, smoke v2.3 | Все провайдеры покрыты контрактными тестами |
| D18 | Релиз v2.3.0, RELEASES.md, CHANGELOG | Версия в Directory.Build.props, теги, CI зелёный |

---

## § 5. Метрики успеха

| Метрика | Было | Целевое |
| :--- | ---: | ---: |
| Открытых KI | ~20 | ≤ 5 (только Won't Fix / дальний фокус) |
| Провайдеров за интерфейсом | 1-2 | 3+ на каждый тип |
| Метрик с ненулевыми значениями | 0 (KI-044) | ≥ 8 |
| Покрытие tool-вызовов audit | частичное | 100% |
| Расхождений документации | 3 (KI-254/264/275) | 0 (CI-проверка) |
| Build warnings | 0 | 0 |
| Тесты | 1326/1334, 8 Skip | ≥ 1400, Skip ≤ 8 |

---

## § 6. Риски и ограничения

1. **Смена `Chat.Model` формата** (D06) — нужна миграция существующих чатов
   и fallback для старых значений; риск регрессии LLM-вызовов.
2. **Qdrant/PostgreSQL** (D05/D07) — требуют внешних сервисов; в CI —
   InMemory/Sqlite fallback, реальные прогоны — только на smoke-стенде.
3. **Per-user mail** (D08) — шифрование через ASP.NET DataProtection требует
   стабильного key ring между рестартами (иначе пароли станут нечитаемыми).
4. **Внешние VL** (D13) — стоимость API и доступность из РФ; конфиг
   fallback-цепочки обязателен.
5. **Avalonia overlay** (D12) — новый UI-фреймворк в решение; оценить
   размер бинарника и совместимость с WPF-веткой.

---

## § 7. Порядок выполнения

1. Утвердить Design (этот документ) — коммит `docs(design): ...`.
2. Создать `docs/TODO_DESIGN_SPRINTS.md` с декомпозицией D01–D18.
3. Выполнять спринты последовательно, отчёты — в
   `docs/development/reports/v2.3/Dxx_<name>.md`, каждый — commit + push.
4. После D18 — релиз v2.3.0 по `docs/development/RELEASES.md`.
5. Следующая ревизия Design — после D18 или при появлении 5+ новых KI.
