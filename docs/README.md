# Документация IIChatTools v2.0.1

**Обновлено:** 2026-10-19
**Назначение:** единая точка входа в документацию проекта. Выберите роль —
и переходите по ссылке.

---

## С чего начать

| Роль | Куда идти |
| :--- | :--- |
| **Пользователь** | [user_guide/USER_GUIDE.md](user_guide/USER_GUIDE.md) — Chat UI, Multi-Agent, RAG |
| **Тестировщик** | [qa/MANUAL_TESTING_GUIDE.md](qa/MANUAL_TESTING_GUIDE.md) — чек-лист ручной приёмки |
| **Разработчик** | [development/RULES.md](development/RULES.md) → [development/ARCHITECTURE.md](development/ARCHITECTURE.md) → [development/RELEASES.md](development/RELEASES.md) |
| **Впервые в проекте** | [README.md](../README.md) — установка, настройка, API, обзор возможностей |

---

## Структура

```text
docs/
├── README.md                  ← этот хаб
├── KNOWN_ISSUES.md            ← реестр проблем (KI-XXX)
├── user_guide/
│   └── USER_GUIDE.md          ← Chat UI, Multi-Agent, RAG
├── qa/
│   └── MANUAL_TESTING_GUIDE.md ← ручная приёмка (бывший docs/TESTING.md)
└── development/
    ├── RULES.md               ← правила разработки (v1.4.35)
    ├── ARCHITECTURE.md        ← архитектура, слои, DI, ADR
    ├── RELEASES.md            ← процесс релиза
    ├── reports/               ← отчёты по задачам/релизам
    │   └── v2.0.1/DOCS_REORG.md
    ├── adr/                   ← индекс архитектурных решений
    ├── v1.3 … v2.0/           ← дизайн-документы по версиям
    └── archive/               ← устаревшие документы (история)
```

---

## Пользователям

- **[Руководство пользователя](user_guide/USER_GUIDE.md)** — как устроен чат,
  мультиагентная работа, RAG и работа с вложениями.
- **Реестр известных проблем** — [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
- **История версий** — [../CHANGELOG.md](../CHANGELOG.md).
- **Установка и настройка** — [../README.md](../README.md).

## Тестировщикам

- **[Чек-лист ручной приёмки](qa/MANUAL_TESTING_GUIDE.md)** — smoke, full regression,
  UI/UX, список непокрытого автотестами.
- **Автотесты:** `dotnet test IIChatTools.sln -c Release` (1180 тестов).
- **Найденный дефект** → запись `KI-XXX` в [KNOWN_ISSUES.md](KNOWN_ISSUES.md)
  (RULES § 1.13).

## Разработчикам

- **[RULES.md](development/RULES.md)** — обязательные правила (v1.4.35).
- **[ARCHITECTURE.md](development/ARCHITECTURE.md)** — слои, схема БД, DI, поток Chat.
- **[RELEASES.md](development/RELEASES.md)** — чек-лист релиза.
- **[adr/](development/adr/README.md)** — индекс архитектурных решений (ADR).
- **[reports/](development/reports/)** — отчёты по задачам и релизам.
- **Дизайн-документы:** [v2.0 Orchestrator-Worker](development/v2.0/DESIGN_ORCHESTRATOR_WORKER.md),
  [v1.13 Speech Recognition](development/v1.13/DESIGN_SPEECH_RECOGNITION.md),
  [v1.12 Vision Agent](development/v1.12/DESIGN_VISION_AGENT.md),
  [v1.11 Actor-Critic Debate](development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md),
  [v1.5 RAG](development/v1.5/DESIGN.md),
  [v1.4 Multi-Agent](development/v1.4/DESIGN.md),
  [v1.3 Chat UI](development/v1.3/DESIGN.md).

---

## Правила работы с документацией

1. **RULES § 2.1–2.4** — документация обновляется в том же коммите, что и код.
2. **RULES § 2.10** — Markdown с code-блоками при вставке в чат оборачивается
   в 4 бэктика снаружи.
3. **RULES § 2.11** — большие MD-файлы не выводятся в чат целиком, только точечные diff'ы.
4. **Ссылки** — только относительные, проверяются скриптом
   [`scripts/setup/check-doc-links.ps1`](../scripts/setup/check-doc-links.ps1).
5. **Новый KI** — в [KNOWN_ISSUES.md](KNOWN_ISSUES.md), формат `### KI-XXX — Название`.
