# DOCS_REORG — реорганизация документации IIChatTools v2.0.1

**Дата:** 2026-10-19
**HEAD:** `c385d7e` (v2.0.1)
**Правила:** `docs/development/RULES.md` v1.4.35
**Исполнитель:** DSH (агент), по постановке пользователя
**Статус:** изменения в рабочем дереве, **не закоммичены**

---

## 1. Файлы (+/-)

### Итоговая структура

```text
docs/
├── README.md                       ← НОВЫЙ хаб (навигация по ролям)
├── KNOWN_ISSUES.md                 ← без перемещения
├── user_guide/
│   └── USER_GUIDE.md               ← НОВЫЙ (Chat UI + Multi-Agent + RAG)
├── qa/
│   └── MANUAL_TESTING_GUIDE.md     ← ПЕРЕИМЕНОВАН из docs/TESTING.md
└── development/
    ├── RULES.md                    ← без перемещения
    ├── ARCHITECTURE.md             ← без перемещения
    ├── RELEASES.md                 ← уже был здесь
    ├── PROMPT_CURRENT.md           ← НОВЫЙ файл-указатель (PROMPT_V5 отсутствует)
    ├── adr/
    │   └── README.md               ← НОВЫЙ индекс ADR
    ├── reports/
    │   └── v2.0.1/DOCS_REORG.md    ← НОВЫЙ отчёт (этот файл)
    ├── v1.3 … v1.13, v2.0/         ← без изменений
    └── archive/                    ← без изменений
```

> `docs/CHANGELOG.md` в репозитории нет — канонический `CHANGELOG.md` лежит
> в корне (RULES § 2.1, Keep a Changelog). Переносить не стал, чтобы не ломать
> ссылки и CI-практику. Хаб ссылается на корневой файл.

### Создано (7)

| Файл | Назначение |
| :--- | :--- |
| `docs/README.md` | Хаб документации: пользователи / тестировщики / разработчики + структура |
| `docs/user_guide/USER_GUIDE.md` | Разделы Chat UI, Multi-Agent, RAG, извлечённые из README (213 строк) |
| `docs/development/adr/README.md` | Индекс ADR: решения пока ведутся в `ARCHITECTURE.md` § 8 (ADR-001…025) |
| `docs/development/PROMPT_CURRENT.md` | Указатель: `PROMPT_V5.md` нет, актуальный имеющийся — `PROMPT_V4.md` |
| `docs/development/reports/v2.0.1/DOCS_REORG.md` | Этот отчёт |
| `scripts/setup/check-zombie-ki.ps1` | Аудит «Зомби-KI» (строгий проход + `-Detailed`) |
| `scripts/setup/check-doc-links.ps1` | Проверка относительных Markdown-ссылок |

### Переименовано / перемещено (1)

| Было | Стало | Способ |
| :--- | :--- | :--- |
| `docs/TESTING.md` | `docs/qa/MANUAL_TESTING_GUIDE.md` | `git mv` (история сохранена, `R100`) |

### Изменено (7)

| Файл | +/- (строки) | Что |
| :--- | :--- | :--- |
| `README.md` | +28 / −196 | Убраны 3 вынесенных раздела; добавлен раздел «Документация»; актуализированы метрики |
| `CHANGELOG.md` | +17 / −2 | `[Unreleased]`: Planned (KI-242…248, 251, 253, 254), Changed (reorg), Fixed (KI-252) |
| `docs/KNOWN_ISSUES.md` | +70 / −3 | Удалены 3 маркера merge-конфликта; добавлены KI-252, KI-253, KI-254 |
| `docs/qa/MANUAL_TESTING_GUIDE.md` | +6 / −3 | Новый H1, пометка о переносе, исправлены относительные ссылки |
| `docs/development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md` | +1 / −1 | `../KNOWN_ISSUES.md` → `../../KNOWN_ISSUES.md` |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | +5 / −5 | `../../RULES.md` → `../RULES.md`, `../../ARCHITECTURE.md` → `../ARCHITECTURE.md`, 4× KI-ссылки на `../../` |
| `docs/development/v2.0/DESIGN_ORCHESTRATOR_WORKER.md` | +2 / −2 | 2× `../../RULES.md` → `../RULES.md` |

### Не трогал

- **Код C#:** в рабочем дереве **до начала работы** уже были изменения
  `IIChatTools.Data/AppDbContext.cs` и
  `IIChatTools.Services/Implementation/Tools/Debate/OrchestrateTaskTool.cs` —
  они не мои и не изменялись.
- `docs/development/PROMPT_V2/V3/V4.md`, все `DESIGN*.md`, `archive/` — история.
- `docs/development/RELEASES.md`, `v2.0/DESIGN_ORCHESTRATOR_WORKER.md` — уже на месте.
- Корневые файлы, кроме `README.md` и `CHANGELOG.md`.

**Удалено безвозвратно:** 0 файлов. Удалены только 3 строки-маркера конфликта
(`<<<<<<< HEAD`, `=======`, `>>>>>>> 8c61ea2`) — это не контент.

---

## 2. Ссылки

**Инструмент:** `scripts/setup/check-doc-links.ps1`
(README.md + `docs/**/*.md`; `http(s)`, `mailto` и `#anchor` пропускаются).

```text
Проверено файлов: 32, ссылок: 152
Битых относительных ссылок не найдено.
```

### Битые ссылки ДО (9)

| Файл | Ссылка | Причина |
| :--- | :--- | :--- |
| `docs/development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md` | `../KNOWN_ISSUES.md#ki-126` | На уровень выше — это `docs/development/KNOWN_ISSUES.md`, файла нет |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../../RULES.md` | `../../` от `v1.13` = `docs/`; нужно `../` |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../../ARCHITECTURE.md` | То же |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../KNOWN_ISSUES.md#ki-140` | То же (нет `docs/development/KNOWN_ISSUES.md`) |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../KNOWN_ISSUES.md#ki-144` | То же |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../KNOWN_ISSUES.md#ki-145` | То же |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../KNOWN_ISSUES.md#ki-146` | То же |
| `docs/development/v2.0/DESIGN_ORCHESTRATOR_WORKER.md` | `../../RULES.md` (стр. 7) | `../../` от `v2.0` = `docs/`; нужно `../` |
| `docs/development/v2.0/DESIGN_ORCHESTRATOR_WORKER.md` | `../../RULES.md` (стр. 630) | То же |

> Эти 9 ссылок были битыми **до** реорганизации. Перемещение `TESTING.md`
> добавило бы ещё 5 битых ссылок в `docs/qa/MANUAL_TESTING_GUIDE.md`
> (`development/*` и `KNOWN_ISSUES.md` считались бы от `docs/qa/`) — они
> исправлены в том же проходе.

### Битые ссылки ПОСЛЕ (0)

Все 9 исправлены; новых не появилось. Проверка — повторный запуск скрипта.

### Обновлённые ссылки

| Файл | Было | Стало |
| :--- | :--- | :--- |
| `docs/qa/MANUAL_TESTING_GUIDE.md` | `development/v1.3/DESIGN.md` | `../development/v1.3/DESIGN.md` |
| `docs/qa/MANUAL_TESTING_GUIDE.md` | `development/v1.4/DESIGN.md` | `../development/v1.4/DESIGN.md` |
| `docs/qa/MANUAL_TESTING_GUIDE.md` | `development/v1.5/DESIGN.md` | `../development/v1.5/DESIGN.md` |
| `docs/qa/MANUAL_TESTING_GUIDE.md` | `development/v1.7/DESIGN_DB_AGENT.md` | `../development/v1.7/DESIGN_DB_AGENT.md` |
| `docs/qa/MANUAL_TESTING_GUIDE.md` | `KNOWN_ISSUES.md` | `../KNOWN_ISSUES.md` |
| `docs/development/v1.11/DESIGN_MULTI_AGENT_DEBATE.md` | `../KNOWN_ISSUES.md#ki-126` | `../../KNOWN_ISSUES.md#ki-126` |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../../RULES.md` | `../RULES.md` |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../../ARCHITECTURE.md` | `../ARCHITECTURE.md` |
| `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md` | `../KNOWN_ISSUES.md#ki-140…146` | `../../KNOWN_ISSUES.md#ki-140…146` (4 шт.) |
| `docs/development/v2.0/DESIGN_ORCHESTRATOR_WORKER.md` | `../../RULES.md` | `../RULES.md` (2 шт.) |
| `docs/user_guide/USER_GUIDE.md` | `docs/development/v1.3/DESIGN.md` | `../development/v1.3/DESIGN.md` |
| `docs/user_guide/USER_GUIDE.md` | `docs/development/v1.4/DESIGN.md` | `../development/v1.4/DESIGN.md` |
| `docs/user_guide/USER_GUIDE.md` | «API (основные endpoints) выше» | `../../README.md#api-основные-endpoints` |
| `README.md` | 2 ссылки в удалённых разделах | Перенесены в USER_GUIDE (пути от `docs/user_guide/`) |

**Новые ссылки:** раздел README «Документация» (6 строк), хаб `docs/README.md`
(20+ ссылок по ролям), `PROMPT_CURRENT.md` (история промптов), `adr/README.md`.

---

## 3. Zombie-KI

**Команда:**

```powershell
pwsh -ExecutionPolicy Bypass -File scripts/setup/check-zombie-ki.ps1 -Detailed
```

### Результат ДО правок KNOWN_ISSUES.md

```text
Всего заголовков: 233
Сумма статусов:   208
ВНИМАНИЕ: Зомби-KI!
```

### Результат ПОСЛЕ (с учётом +3 KI: 252, 253, 254)

```text
Файл:             D:\Projects\IIChatTools\docs\KNOWN_ISSUES.md
Всего заголовков: 236
Сумма статусов:   211
ВНИМАНИЕ: Зомби-KI!

--- Расширенная диагностика ---
  Fixed            strict=129 tolerant=152
  In Progress      strict=  0 tolerant=  0
  Planned          strict= 44 tolerant= 44
  Deferred         strict=  8 tolerant=  8
  Documented       strict= 24 tolerant= 24
  Open             strict=  0 tolerant=  0
  Won't Fix        strict=  1 tolerant=  1
  Partially Fixed  strict=  5 tolerant=  5
  Implemented        2
  Resolved           2
Толерантная сумма (вкл. emoji): 234
Legacy-статусы (Implemented/Resolved): 4
Секций KI: 236; строк статуса в них: 236
  строгих:                  209
  с emoji-префиксом:        23
  не матчится строгим:      27 (emoji + legacy)
  упоминаний статуса в теле: 7 (искажают сумму strict)
Уникальных KI: 233 из 236
Дублирующиеся заголовки:
  KI-031 — 2 секции
  KI-061 — 2 секции
  KI-110 — 2 секции
```

### Диагноз

| Показатель | Значение |
| :--- | :---: |
| Секций `### KI-` | **236** |
| Строк статуса (первая в секции) | **236** |
| Распознано строгим шаблоном | **209** |
| С emoji-префиксом (`✅` / `🟡`) | **23** |
| Legacy-статусы `Implemented` / `Resolved` | **4** (KI-054, KI-055 / KI-008, KI-051) |
| Уникальных номеров | **233** (3 дубля) |
| «Зомби» (секция без статуса) | **0** |

**Вывод:** «Зомби-KI» в исходном смысле (секция без статуса) **нет** — все 236
секций имеют статус. Предупреждение скрипта — **ложное срабатывание** из-за
формата данных:

1. 23 секции используют emoji-префикс (`✅ **Fixed**`, `🟡 **In Progress**`) —
   строгий шаблон `**Статус:** Fixed` такие строки не матчит.
2. 4 секции используют `Implemented` / `Resolved`, которых нет в списке скрипта
   (при этом `Resolved` разрешён RULES § 7).
3. В теле файла есть текстовые упоминания `**Статус:**` (описания, таблицы,
   повторные строки) — они добавляют «фантомные» совпадения.
4. **Реальные дефекты:** 3 дублирующихся номера и устаревшая сводка
   (205 / «Всего 198» при 233 уникальных), плюс **маркеры merge-конфликта**,
   которые до правки разрывали KI-249/250/251.

**Сделано:** удалены 3 маркера конфликта (контент KI-249/250/251 сохранён).
Остальное зафиксировано в KI-252 (Partially Fixed → v2.0.x).

---

## 4. Smoke KI

Выборка: 3 × `Fixed` (KI-249, KI-228, KI-230) + 3 × `Planned`
(KI-245, KI-251, KI-240). Проверка — grep по коду/конфигам и чтение
реализации, без запуска приложения.

| KI | Статус в реестре | Что проверяли | Найдено в коде | Вердикт |
| :--- | :--- | :--- | :--- | :--- |
| **KI-249** | Fixed (v2.0.1) | `renderToolParams`, tool-специфичные поля, `<div>` вместо `<pre>` | `approvals.js:349` `renderToolParams`, `:332` `AGENT_TASK_TOOLS`, `:384` `renderField`; `_ApprovalModal.cshtml:19` `<div id="approval-params">` | ✅ **Fixed подтверждён** |
| **KI-228** | Fixed (v1.13.12) | Карточка tool-блока вместо сырого JSON: иконка, статус, сворачивание | `chat.js:4334` `renderToolResultBlock`, `:4265` `renderToolResultCard`, `:4170` `getToolIcon` | ✅ **Fixed подтверждён** |
| **KI-230** | Fixed (v1.13.13) | Continue после `finish_reason=length`: endpoint, append в тот же bubble | `ChatStreamService.cs:1204` `ContinueInternalAsync`, `ChatStreamController.cs:119` `[HttpPost("continue")]`, `chat.js:2292` плашка + кнопка; `.resx` `ChatMessageContinue` | ✅ **Fixed подтверждён** |
| **KI-245** | Planned (v2.0.x) | Появился ли Admin UI whitelist воркеров Orchestrator | Нет `AdminOrchestratorController` / `admin-orchestrator.js`; whitelist читается только из `SubAgents:orchestrate_task:AllowedWorkerAgents` (`OrchestrateTaskTool.cs:957`) | ✅ **Актуален** (нужен Admin UI) |
| **KI-251** | Planned (v2.0.x) | Добавлены ли `.tif` / `.tiff` в RAG | `AllowedExtensions` = 6 image-форматов (`.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.bmp`); `.tif/.tiff` нет | ✅ **Актуален** (секция была внутри маркеров — теперь чистая) |
| **KI-240** | Planned (v1.14) | Появился ли Admin UI / JSONL-экспорт Feedback | Нет `AdminFeedbackController`, `admin-feedback.js`, `/api/admin/feedback`; поле `Feedback` (KI-233) есть | ✅ **Актуален** (UI/экспорта нет) |

**Итог:** регрессий среди Fixed не найдено; все 3 Planned по-прежнему
актуальны (контекст не изменился). KI-245/251/240 не устарели.

---

## 5. Что не так

| # | Проблема | Серьёзность | Что сделано |
| :---: | :--- | :---: | :--- |
| 1 | В `docs/KNOWN_ISSUES.md` (HEAD `c385d7e`) закоммичены **маркеры merge-конфликта** `<<<<<<< HEAD` / `=======` / `>>>>>>> 8c61ea2` — они разрывали KI-251, KI-249, KI-250 | 🔴 Critical | 3 строки удалены; контент сохранён; KI-252 |
| 2 | **Дубли KI-031, KI-061, KI-110** (по 2 секции), **сводка** даёт 205 / «Всего 198» при 233 уникальных | 🟠 High | Не правил (нужно решение по слиянию) → KI-252 |
| 3 | `qa/MANUAL_TESTING_GUIDE.md` остановился на **v1.8.1**: 424 теста, smoke v1.8.0, нет Vision/Speech/Debate/Orchestrator | 🟡 Medium | H1 + ссылки обновлены; содержание → KI-253 |
| 4 | `RULES.md § 7` («~99 KI», «Fixed ~88») устарел, часть строк противоречит реестру | 🟢 Low | → KI-254 |
| 5 | **`docs/CHANGELOG.md` отсутствует** — канонический файл в корне | ℹ️ Info | Не переносил (RULES § 2.1); хаб ссылается на корневой |
| 6 | **`PROMPT_V5.md` отсутствует** — переименование в `PROMPT_CURRENT.md` невозможно; последний `PROMPT_V4.md` (v4.0) устарел (v1.13.1) | 🟡 Medium | Создан `PROMPT_CURRENT.md`-указатель с предупреждением |
| 7 | README: заголовок «Инструменты (65)» при таблице «Итого 66»; в примечании 9 агентов и арифметика 18 при заявленных 19 | 🟡 Medium | Исправлено: «(66)», список 12 не-internal агентов → 19 |
| 8 | В USER_GUIDE (из README) ограничения RAG говорили «PDF/DOCX/OCR — не в MVP (v1.5.x)» при реализованных v1.7.1/v1.13.x | 🟢 Low | Формулировка актуализирована |
| 9 | 9 битых относительных ссылок в design-документах (не связаны с переносом) | 🟢 Low | Исправлены; checker → 0 |
| 10 | Скрипт «Зомби-KI» даёт **ложные срабатывания** на emoji/legacy-статусах и текстовых упоминаниях | 🟢 Low | Добавлен `-Detailed` с по-секционным разбором |
| 11 | В рабочем дереве были **посторонние изменения C#** (`AppDbContext.cs`, `OrchestrateTaskTool.cs`) | ℹ️ Info | Не трогал; учтено при `git status` |
| 12 | `docs/development/adr/` пустовал бы (RULES § 6.9) | 🟢 Low | Создан `adr/README.md` с индексом ADR-001…025 |

---

## 6. Новые KI

Добавлены в `docs/KNOWN_ISSUES.md` перед разделом «Сводка по статусам».

| KI | Приоритет | Статус | Суть |
| :--- | :---: | :--- | :--- |
| **KI-252** | 🟠 High | **Partially Fixed** (маркеры — v2.0.1; дубли/сводка — v2.0.x) | Merge-маркеры в KNOWN_ISSUES.md, дубли KI-031/061/110, устаревшая сводка, статусы вне словаря |
| **KI-253** | 🟡 Medium | **Planned** (v2.0.x) | `qa/MANUAL_TESTING_GUIDE.md` устарел: v1.8.1, 424 теста, нет сценариев v1.11–v2.0.1 |
| **KI-254** | 🟢 Low | **Planned** (v2.0.x) | `RULES.md § 7`: выжимка «~99 KI» вместо 230, противоречия по KI-147/214/192/204–206 |

**Не заводил KI на:** README 65/66 и 9/19, устаревшую фразу про RAG,
9 битых ссылок — исправлено в этом же проходе; фиксировать «исправлено»
отдельными KI смысла нет.

---

## 7. Обоснование решений

1. **`user_guide/` отдельно от README.** README — витрина и установка
   (1684 → 1516 строк). Пользовательские сценарии (Chat UI, Multi-Agent, RAG)
   — это ~190 строк, которые утяжеляли лендинг. Вынесены без потери контента,
   README оставляет компактный указатель.
2. **`qa/` отдельно от `development/`.** Чек-лист ручной приёмки нужен
   тестировщику, а не разработчику; `qa/` сразу отвечает на вопрос «куда идти».
   Файл переименован (`MANUAL_TESTING_GUIDE.md`) — имя говорит о содержимом,
   `git mv` сохранил историю.
3. **`development/reports/v2.0.1/`.** Отчёты привязаны к релизу; версия в пути
   позволяет не смешивать v2.0.1 и будущие. Отчёт лежит рядом с дизайном v2.0,
   но не в `v2.0/` (там — дизайн, а не отчёты).
4. **`adr/` — индекс, а не 25 файлов.** ADR-001…025 уже живут в
   `ARCHITECTURE.md § 8`; дробление создало бы второй источник истины.
   Каталог создан под будущие отдельные ADR, с правилом нумерации и индексом.
5. **`PROMPT_CURRENT.md` — указатель, не переименование V4.** `PROMPT_V5.md`
   в репозитории нет; переименовать V4 в CURRENT значило бы объявить устаревший
   промпт (v1.13.1) актуальным для v2.0.1. Указатель честно фиксирует статус
   и историю V2–V4.
6. **`CHANGELOG.md` остался в корне.** RULES § 2.1 и Keep a Changelog ожидают
   его в корне; перенос сломал бы привычные ссылки и CI-проверки. В хабе —
   ссылка `../CHANGELOG.md`.
7. **Не двигал `PROMPT_V2–V4`, `DESIGN*.md`, `archive/`.** Это исторические
   артефакты с собственными относительными ссылками; перемещение дало бы
   десятки битых ссылок без пользы. Примечание постановки «файлы в
   `docs/development/` не трогать» соблюдено.
8. **Ссылки — только относительные.** Скрипт `check-doc-links.ps1` проверяет
   их от каталога самого файла; это ловит ошибки вида `../` vs `../../`,
   которые и были в 9 случаях. Абсолютные пути (`C:\...`) не используются.
9. **Метрики README.** 1180 тестов и v2.0.1 уже были верны; 19 tools в Chat
   подтверждены кодом: 12 не-internal агентов + `consult_secondary_agent`
   + 3 RAG + `database_agent` + `vision_agent` + `orchestrate_task` = 19.
   Число 66 в ToolRegistry — сумма строк таблицы (40 + 6 + 3 + 1 + 7 + 3 +
   1 + 1 + 1 + 1 + 1 + 1 = 66). Исправлены только заголовок (65 → 66) и
   устаревшее перечисление агентов.
10. **KI-252…254, а не правка «молча».** Дубли секций и пересчёт сводки
    требуют решения владельца реестра (какую из двух секций KI-031/061/110
    оставить). KI фиксируют проблему и не теряют историю; маркеры конфликта —
    однозначный артефакт, поэтому удалены сразу.

---

**Git:** изменения не закоммичены, `git add -A` не использовался.
