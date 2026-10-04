# PROMPT_V4.md — Стартовый промпт для нового чата

**Версия промпта:** v4.0
**Дата:** 2026-10-04
**Актуальный релиз:** v1.13.1 (2026-10-04)
**Статус:** v1.13.1 выпущен (Speech Recognition + VAD + device picker). В работе — v1.12.x roadmap (WPF overlay KI-142, Sandbox KI-131 Ф3, RemoteVnc Ф4) → потом KI-146 (fallback-полировка + дедупликация label).

---

## § 0. Главное правило форматирования

**Не более одного уровня code fence'ов в твоём ответе.**

- Обычный код — тройной бэктик, **не вкладывать внутрь другого**.
- Пример внутри markdown-блока — **4-пробельный отступ**.
- Команда (inline) — `dotnet build`.
- **MD-файлы, содержащие внутренние code-блоки** — оборачивать в **4 бэктика**.

**Почему:** 8+ инцидентов с развалившейся разметкой (v1.5.0 → v1.13.1). DeepSeek-парсер ломает вложенные fence'ы. Инцидент 2026-10-04: правка DESIGN_SPEECH_RECOGNITION.md с ASCII-схемой без обёртки в ` ``` ` → Markdown съел переносы, заголовки слились.

**Большие MD-файлы** (README, RULES, CHANGELOG, KNOWN_ISSUES, RELEASES, PROMPT_V4, DESIGN*):
только **точечный diff** («Найти X / Заменить на Y») или **отдельная секция**. Никогда — целиком.

Если риск поломки критичен — **порциями**: сначала 1-3 правки, после подтверждения — следующие.

**PowerShell-специфика:**
- `<<'EOF'` (bash heredoc) **НЕ работает** — используй `@'...'@ | Out-File .file -Encoding utf8NoBOM`.
- Multi-line команды — через backtick `` ` `` в конце строки.
- **Не вставляй сразу блок команд + здесь-строку** — PowerShell парсит весь блок до выполнения; ошибка в одной команде отменяет все.

---

## § 1. Ссылка на репозиторий

- **GitHub:** https://github.com/iilmchat/IIChatTools
- **Ветка:** `main`
- **Текущий релиз:** v1.13.1 (2026-10-04). Tag + GitHub Release созданы.
- **В работе:** v1.12.x roadmap.

### История коммитов после релиза v1.13.1

- `41c48d2` — `docs: sync v1.13.1 release + RELEASES §1a + RULES §7 + KI-147`
  (docs-only, CI + Docker — зелёные).
- `4af0b50` — `feat(speech): configurable VAD + adaptive threshold (fix11)`.
  Tag `v1.13.1` указывает на этот коммит.
  
---

## § 2. Правила оформления (ОБЯЗАТЕЛЬНО)

**`docs/development/RULES.md` — v1.4.28 (2026-10-03).**

Ключевые разделы:

- **§ 1** — базовые правила (naming, XML-doc, локализация, DI).
- **§ 2** — документация (CHANGELOG, README, KNOWN_ISSUES — в том же коммите).
- **§ 3** — workflow (маленькие шаги, **`git add -A`**, build 0/0).
- **§ 4** — технические правила C# / .NET 10 (**51 правило**, вкл. 4.49–4.51).
- **§ 5** — безопасность.
- **§ 6** — git.
- **§ 7** — актуальная KI-выжимка.
- **§ 8** — история правил.

**Самые важные:**

- **§ 3.3** — `git add -A` вместо selective.
- **§ 3.14** — перед `dotnet build` останови приложение (MSB3027/MSB3021).
- **§ 4.17** — локализация JS — только через `data-*`.
- **§ 4.30** — `cref` с перегрузками → `<c>...</c>`, не `<see cref="..."/>`.
- **§ 4.34** — расширение интерфейса → grep по всем fake-заглушкам.
- **§ 4.44** — новый top-level `ITool` → обязательно в `allowedNames` Chat.
- **§ 4.45** — `Path.GetFileName` на Linux: нормализуй `\` → `/`.
- **§ 4.46** — default interface method не виден через конкретный тип.
- **§ 4.48** — `HttpClient.Timeout` нельзя менять после первого `SendAsync`.
- **§ 4.49** — `AddMemoryCache` / `AddOptions` / `AddHttpClient` принимают `Action<T>`.
- **§ 4.50** — `params` + именованный аргумент = **CS8323**.
- **§ 4.51** — `ITool`, зависящий от `IToolRegistry` / `ISubAgentService` → `Func<T>` (ADR-002).

**Специфика v1.13.1 (свежий опыт):**

- **Razor + `Configuration.GetValue<double>` в `data-*`** — обязательно `.ToString(CultureInfo.InvariantCulture)`. В ru-RU `0.015` → `"0,015"` → `parseFloat` = 0.
- **Chrome может выбрать virtual audio device по умолчанию** (Steam Streaming / VB-Cable / VoiceMeeter / OBS Virtual Audio) — валидный `MediaStreamTrack`, но `maxAbs=0` (KI-144).
- **Chrome pruning-ит `ScriptProcessorNode`** даже с `destination` — `onaudioprocess` фирес, буфер пустой. Решение — `MediaStreamTrackProcessor` (WebCodecs, fix6).

---

## § 3. Текущее состояние (v1.13.1)

### Стек

- **.NET 10 LTS** (SDK 10.0.401).
- **ASP.NET Core** (Razor + JWT + Cookie).
- **EF Core 10** (SqlServer / Sqlite / InMemory).
- **LM Studio** (OpenAI-совместимый API + `/v1/embeddings`).
- **Whisper.net 1.8.1** + `Whisper.net.Runtime` — офлайн STT (v1.13.0).
- **MailKit 4.18.1** + MimeKit — IMAP/SMTP.
- **PdfPig 0.1.9** + **DocumentFormat.OpenXml 3.1.0** — PDF/DOCX в RAG.
- **Microsoft.Data.Sqlite 10.0.12** + **Microsoft.Data.SqlClient 6.1.6** — SqlAgent.
- **PuppeteerSharp 7.1**, **Prometheus-net**, **Microsoft.ML.Tokenizers** (tiktoken).
- **System.Drawing.Common 10.0.0** — GDI-скриншоты Vision Agent.

### Архитектура — 4 слоя

- **IIChatTools.API** — Controllers + Views + ES-модули + `Startup.cs` + `Program.cs`.
- **IIChatTools.Services** — бизнес-логика (Core / RAG / SqlAgent / Mail / ExternalLlm / Cache / Speech / VisionAgent).
- **IIChatTools.Data** — EF Entities + миграции (SqlServer).
- **IIChatTools.Tests** — xUnit (**1016/1016**, 5 Skip).

### Метрики

- **61 инструмент** в `ToolRegistry`:
  - 40 raw (включая `consult_secondary_agent`).
  - +10 специализированных агентов (`file_system`, `code`, `code_reviewer`, `code_agent_with_review`, `web`, `git`, `github`, `planner`, `mail`, `external_llm`).
  - +3 RAG-tool.
  - +1 `database_agent`.
  - +7 mail-tools.
  - +3 external-llm-tools.
  - +1 `vision_agent` (v1.12.0).
- **Chat видит 16 инструментов** (v1.13.1).
- **Тесты:** 1016/1016 (1011 pass, 5 Skip — реальные внешние API).
- **KI:** 98 в реестре; Fixed ≈ 87; Deferred = 5; Documented = 13; In Progress = 0; Planned = 11; Partially Fixed = 1; Implemented = 3.

### Релизы (краткая хроника)

- **v1.13.1** (2026-10-04) — Speech Recognition fixes (fix1-fix11), KI-144/145/146.
- **v1.13.0** (2026-10-04) — Speech Recognition (KI-140).
- **v1.12.0** (2026-10-04) — Vision Agent (KI-131, MVP: local-harness).
- **v1.11.0** (2026-10-02) — Actor-Critic (KI-126).
- **v1.10.1** (2026-10-01) — Темы UI (KI-122).
- **v1.10.0** (2026-10-01) — Gemini (KI-110b).
- **v1.9.0** (2026-10-01) — Anthropic (KI-110a).
- **v1.8.2** (2026-10-01) — Tool result cache.
- **v1.8.1** (2026-09-30) — External-LLM Agent (KI-109).
- **v1.8.0** (2026-09-29) — Mail Agent (KI-107).
- **v1.7.0** (2026-09-29) — Database Agent (KI-097).
- **v1.6.0** (2026-09-28) — Sources / citations (KI-086).
- **v1.5.0** (2026-09-28) — RAG / Knowledge Base (KI-083).
- **v1.4.0** (2026-09-24) — Multi-Agent (KI-052).
- **v1.3.0** (2026-09-23) — Chat UI (KI-055).

---

## § 4. Roadmap текущей сессии

### Пункт B (первый) — v1.12.x: Vision Agent, продолжение

**DESIGN:** `docs/development/v1.12/DESIGN_VISION_AGENT.md` (v2.1, MVP Released).

MVP v1.12.0 закрыт (local-harness + vision_agent). Отложено в v1.12.x:

1. **KI-142 — WPF overlay** (реальный on-screen indicator).
   - Отдельный проект `IIChatTools.VisionOverlay` (WPF, `net10.0-windows`).
   - Прозрачное always-on-top окно + кнопка STOP.
   - NamedPipe IPC (`iichattools-vision-overlay`).
   - `WpfVisionOverlayLauncher : IVisionOverlayLauncher` вместо `NoopVisionOverlayLauncher`.
   - Оценка: ~4-6 ч.

2. **KI-131 Ф3 — Sandbox backend.**
   - `SandboxVisionBackend` (Windows Sandbox + TightVNC).
   - Требует Windows 10/11 Pro + включение feature `Containers-DisposableClientVM` (admin + reboot).
   - Оценка: ~10 ч.

3. **KI-131 Ф4 — RemoteVnc backend.**
   - `VncMcpVisionBackend` (MCP-клиент для удалённой машины).
   - Оценка: ~8-10 ч.

### Пункт A (второй) — KI-146 (Planned, ~1 ч)

**Fallback-полировка + дедупликация label в device picker'е.**

Из smoke v1.13.1 выявлено 2 наблюдения:

1. **Fallback → virtual default.** Если сохранённое устройство недоступно
   (`OverconstrainedError`), `speech.js` вызывает `getUserMedia({ audio })` без
   `deviceId` — Chrome отдаёт системный default, который на машинах со Steam
   снова оказывается **Steam Streaming Microphone** (тоже нулевой сигнал).
   Пользователь получает 2 «нулевых» записи подряд вместо осмысленной ошибки.
   **Решение:** после fallback проверить `track.label` на паттерн virtual —
   если матчит, показать ошибку сразу (не запускать запись).

2. **Дубликаты в `<select>`.** Chrome перечисляет одно физическое устройство
   как 3 разных `deviceId` с префиксами в label: «Микрофон (X)», «По умолчанию —
   Микрофон (X)», «Оборудование — Микрофон (X)». Все работают, но захламляют
   список. **Решение:** дедупликация по нормализованному label (убрать префиксы
   «По умолчанию — », «Оборудование — ») с сохранением первого `deviceId`.

**Файлы (план):** `wwwroot/js/modules/speech.js`, `wwwroot/js/modules/profile-audio.js`.
**Оценка:** ~1 ч.

### После этого — бэклог

- **KI-126 Фаза 2** — Debate для `planner_agent` (Pro / Contra / Judge). ~5-6 ч.
- **KI-126 Фаза 3** — Orchestrator-Worker + Blackboard. v2.0.
- **KI-108** — Per-user mail accounts.
- **KI-091** — SqlServer цепочка миграций (обязательно перед prod-SqlServer).
- **KI-082** — Модалка-редактор длинных user-сообщений.
- **Per-user External-LLM API keys**.

### Отложено

- KI-047, KI-053, KI-099, KI-111, KI-113, KI-128, KI-137, KI-138, KI-139, KI-141, KI-143.

---

## § 5. Формат работы

1. **Полные файлы** с XML-документацией на русском.
2. **Путь к файлу** в начале каждого блока кода.
3. **Правка существующего** — точечный diff (Найти / Заменить на).
4. **Новый файл** — выводить целиком.
5. **Несколько правок в одном файле** — нумеровать: Правка 4.1, Правка 4.2.
6. **Новые NuGet-пакеты** — с версиями и указанием проекта.
7. **Сводка в конце**: что сделано / что проверить.
8. **Новые проблемы** → `KI-XXX` в `docs/KNOWN_ISSUES.md`.
9. **Новые UI-строки** → оба `.resx` (RU + EN) — правило 1.14.
10. **Новый инструмент** → 1 класс + 1 строка регистрации в `Startup.cs`.
11. **`CHANGELOG.md` — в КАЖДОМ коммите с кодом** (RULES § 2.1 + § 4.37).
12. **Обновление `Directory.Build.props` `<Version>`** — только при релизе.

### Формат вывода кода

Каждый файл — отдельным блоком с заголовком и путём:

    📄 Файл N — название (новый | правка)
    Путь: <repo-root>/.../File.cs
    [открывающий fence с языком, например csharp]
        код
    [закрывающий fence]

При правке — пошагово:
```
Найти: (полный фрагмент)
Заменить на: (новый фрагмент)
```

### Обязательные секции в конце ответа

- 🔨 **Build + test** — команды + ожидание (0/0; N/N тестов).
- 🚀 **Commit** — here-string + `git add -A` + `push`.
- 🧪 **Smoke** — что проверить (сценарии / DevTools / SQL).
- 📊 **Сводка** — статус + что жду.
- 🎯 **Что дальше** — предложение следующего шага (с оценкой).

### Если чего-то не хватает

**Не выдумывай.** Скажи явно: «Нужен файл X». Дождись. Не предлагай «примерно так».

### Перед началом работы — дождись «ДА»

Не начинай писать код, пока пользователь не подтвердил план / DESIGN / предыдущий шаг.
Исключение: прямое «делай» / «приступай».

### Рабочий путь

- **Основной (Windows):** `K:\AI\Work\IIChatTools` (актуальный на 2026-10-04).
- Была копия на `C:\Projects\AI\IIChatTools` — уточнять при переключении.

### Стандартные команды

**Остановить приложение** (RULES § 3.14):

    Get-Process IIChatTools.API -ErrorAction SilentlyContinue | Stop-Process -Force

**Build + test:**

    cd <repo-root>
    dotnet build IIChatTools.sln
    dotnet test IIChatTools.sln --no-build

**Запуск (dev):**

    cd <repo-root>/IIChatTools.API
    dotnet run

UI: `https://localhost:5001`; Метрики: `https://localhost:5001/metrics`.

**Commit:**

    cd <repo-root>
    git add -A
    git status --short

    @'
    <type>(<scope>): <subject>

    пункт 1
    пункт 2

    Build 0/0. Tests N/N.
    '@ | Out-File -FilePath .commit-msg.txt -Encoding utf8NoBOM
    git commit -F .commit-msg.txt
    Remove-Item .commit-msg.txt
    git push origin main

**GitHub Release** (PowerShell-совместимо):

    @'
    ## Заголовок

    тело...
    '@ | Out-File -FilePath .release-notes.md -Encoding utf8NoBOM

    gh release create vX.Y.Z `
      --title "vX.Y.Z — Название" `
      --notes-file .release-notes.md

    Remove-Item .release-notes.md

**⚠️ НЕ используй bash heredoc `<<'EOF'` в PowerShell** — падает с `ParserError` и **отменяет все команды в блоке**. Инцидент 2026-10-04.

---

## § 6. Известные подводные камни

### Общие

- **`Path.GetFileName`** на Linux — нормализуй `\` → `/` (RULES § 4.45).
- **Кэш браузера** после правок JS/CSS — `Ctrl+Shift+R` + DevTools → «Disable cache».
- **RAG-tools в `allowedNames`** (RULES § 4.44) — только для top-level `ITool`, не для `AgentToolBase`.
- **Sources: 4-полевой ключ дедупа** `(Type|DocumentPath|Url|ChunkIndex)`.
- **`.resx` ключи case-insensitive** — коллизия → MSB3568.
- **`git commit -m "..."` в PowerShell** — экранирование ломается. Here-string + `-F`.
- **Sqlite + открытый DB Browser** — `database is locked` (KI-085). Read Only.
- **`ExecuteDeleteAsync` не поддерживается InMemory** (RULES § 4.29).
- **`cref` в XML-doc с перегрузками** — CS0419 (RULES § 4.30).
- **Перед `dotnet build` — останови приложение** (RULES § 3.14).
- **Перед `dotnet ef migrations add`** — проверь `Database:Provider` (RULES § 3.15).
- **`AddMemoryCache` / `AddOptions` / `AddHttpClient`** — `Action<T>`, не фабрика (RULES § 4.49).

### Speech Recognition (v1.13.x)

- **Chrome может выбрать virtual audio device** по умолчанию (Steam Streaming / VB-Cable / VoiceMeeter / OBS Virtual Audio) — `MediaStreamTrack` валидный, но `maxAbs=0`. См. KI-144. Решение — device picker в `/profile → 🎤 Аудио` (KI-145).
- **Chrome pruning-ит `ScriptProcessorNode`** даже с `destination` — `onaudioprocess` фирес, буфер пустой. Решение — `MediaStreamTrackProcessor` (WebCodecs).
- **Razor + `Configuration.GetValue<double>` в `data-*`** — обязательно `.ToString(CultureInfo.InvariantCulture)`. В ru-RU `0.015` → `"0,015"` → `parseFloat` = 0.
- **Фиксированный `SilenceRms=0.015` не работает на тихих микрофонах** (RMS речи 0.006–0.010). Решение — адаптивный порог (`Vad:AdaptiveEnabled=true`, `max(minObservedRms × 2.0, 0.001)`).
- **`data-speech-vad-*`** на `#chat-messages` — читаются в `_loadRuntimeConfig()`.

### Vision Agent (v1.12.0)

- **Только `local-harness`** (Windows). Sandbox — Ф3 (KI-131), RemoteVnc — Ф4.
- **Нет on-screen indicator** — заглушка `NoopVisionOverlayLauncher`. См. KI-142.
- **Скриншоты не сохраняются** в `ChatMessage.MetadataJson`. В workspace — на 1 ч.
- **VL-модель должна быть мультимодальной** (`ministral-3-3b-instruct-2512`). Обычный `qwen3-4b` не подойдёт.
- **Whitelist процессов** — действие отменяется, если фокус ушёл на неразрешённый процесс.
- **`System.Drawing.Common`** в plain `net10.0` — Windows-only в рантайме. На Linux `ScreenshotAsync` бросит `PlatformNotSupportedException`.

### Mail Agent (v1.8.0)

- **Yandex: App Password ≠ IMAP.** Разные настройки. `LOGIN invalid credentials` — в 90% IMAP не включён.
- **App Password — 16 символов** без пробелов.
- **Логин — полный email** (`user@yandex.ru`).
- **Вложения при `read_email` НЕ сохраняются** (v1.8.0).

### External-LLM (v1.8.1+)

- **Anthropic / OpenAI / Groq / Gemini** — требуют VPN из РФ.
- **DeepSeek** — работает без VPN.
- **`include_context: false`** по умолчанию.
- **Circuit breaker: 3 fail → 5 мин skip**.
- **`DailyBudgetUsd = $5/день`** (per-user).

---

## § 7. Известные факты про LM Studio

- **Context Length ≥ 16384** для всех агентов (KI-117).
- `GET /v1/models` возвращает embedding-модели — фильтруются в `/api/models`.
- SSE-режим не отдаёт usage — токены через tiktoken (KI-049a).
- `wikipedia_search` — intermittent SSL-обрывы (KI-064 Fixed, KI-094 Documented).
- Embedding: `text-embedding-nomic-embed-text-v1.5`, 768 dim, `POST /v1/embeddings`.
- **`gemma-4-12b-coder-fable5-composer2.5-v1` НЕ поддерживает tool calling** (KI-116).
- **Model по умолчанию:** `qwen/qwen3-4b-2507`.
- **Vision Agent:** `ministral-3-3b-instruct-2512` (мультимодальная) + `qwen3-coder-30b-a3b-instruct` (planner).

---

## § 8. Начни с вопроса

Прочитай правила и это сообщение. Затем задай мне вопросы:

1. **Что делаем сегодня:**
   - **B. v1.12.x roadmap** — WPF overlay (KI-142) → Sandbox (KI-131 Ф3) → RemoteVnc (KI-131 Ф4)?
   - Или сразу **A. KI-146** (fallback-полировка + дедупликация label, ~1 ч)?
2. **Что с pending-коммитом** v1.13.1 (DESIGN/README/Directory.Build.props/CHANGELOG в рабочем дереве)? Сделать `chore(v1.13.1): sync docs` сейчас или отложить?
3. Есть ли специфичные требования?
4. Нужны ли файлы, которых у тебя нет?
5. Какой путь к проекту — `K:\AI\Work\IIChatTools` или другой?

**Контекст B (v1.12.x, если её делаем):**
- DESIGN — `docs/development/v1.12/DESIGN_VISION_AGENT.md` v2.1 (MVP Released).
- Порядок: **WPF overlay (KI-142)** → Sandbox (Ф3) → RemoteVnc (Ф4).
- **Первый шаг:** WPF overlay — отдельный проект `IIChatTools.VisionOverlay` (WPF, `net10.0-windows`).
- Альтернатива: Avalonia (KI-143) — cross-platform, но отложено на v1.14+.
- Нужен доступ к `docs/development/v1.12/DESIGN_VISION_AGENT.md` § 4.6, § 6.4, § 7.6 (Ф6.7).

**Контекст A (KI-146, если её делаем):**
- Файлы: `speech.js`, `profile-audio.js`.
- Проблема 1 — fallback → virtual default.
- Проблема 2 — дубликаты в `<select>`.
- Оценка: ~1 ч.

**Готов? Приступаем.**