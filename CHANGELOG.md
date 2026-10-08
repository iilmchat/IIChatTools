# Changelog

Все значимые изменения проекта IIChatTools документируются в этом файле.

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/).
Проект придерживается [Semantic Versioning](https://semver.org/lang/ru/).

Типы изменений:
- **Added** — новая функциональность
- **Changed** — изменения в существующей функциональности
- **Deprecated** — функции, помеченные как устаревшие
- **Removed** — удалённая функциональность
- **Fixed** — исправления
- **Security** — исправления уязвимостей и утечек
- **Documented** — Задокументировано багов

---

## [Unreleased]

### Documented
- **v1.13.x (KI-137) — Vision Agent: OCR-fallback для мелкого текста**:
  зарегистрирован DESIGN-документ
  [`docs/development/v1.13/DESIGN_VISION_OCR.md`](docs/development/v1.13/DESIGN_VISION_OCR.md)
  (v1.0 Draft). KI-137 → `In Progress`.
  - **Что планируется:** OCR-fallback (Tesseract) поверх VL-описания для
    распознавания мелкого текста (8-10 px шрифты, капча, плотные таблицы).
  - **Ключевые решения:** full-res PNG для OCR (не downscale), merge
    OCR-слов в `ui_elements` через центр bbox, триггеры A (проактивный)
    и B (реактивный по `fail` Planner'а), один проход OCR на задачу,
    секция конфига `VisionAgent:Ocr`.
  - **Общий сервис:** `IOcrService` (из KI-203, RAG-OCR) — переиспользуется.
  - **Оценка:** ~3.5 ч (8 фаз). См. DESIGN § 10.

### Added
- **v1.13.x (KI-137, Ф1) — Vision Agent: DTO + опции для OCR-fallback**:
  - **Новые DTO:**
    - `FullResolutionScreenshotDto` (PNG + Width + Height) —
      для `IVisionBackend.ScreenshotFullResolutionAsync`.
    - `VisionOcrOptions` (секция `VisionAgent:Ocr`: `Enabled`,
      `TessDataPath`, `Languages`, `TriggerA`, `TriggerB`,
      `ShortLabelThreshold`, `MergeMaxDistancePx`).
  - **Расширенные DTO:**
    - `UiElementDto.Source` — `"vl"` (default) / `"ocr"` / `"merged"`.
    - `ScreenDescriptionDto.OcrText` + `OcrWordsCount`.
    - `VisionTaskResultDto.OcrText` — для Chat LLM.
    - `VisionAgentOptions.Ocr` — секция `VisionOcrOptions`.
  - **Не меняется:** ни один сервис / интерфейс (это Ф2/Ф3/Ф4).
  - **Build 0/0. Тесты 1080/1080 (без изменений).**

- **v1.13.x (KI-137, Ф2) — Vision Agent: full-res скриншот для OCR**:
  - `IVisionBackend.ScreenshotFullResolutionAsync()` — новый default-метод
    (→ `null`, обратная совместимость для Sandbox / VncMcp fakes).
  - `LocalHarnessVisionBackend` override — GDI-захват экрана **без** downscale
    (для OCR мелкого текста, 8-10 px). Не применяет `VisionImageResizer.Resize`
    и не проверяет `MaxScreenshotBytes` (PNG — внутренний, не уходит в VL).
  - **RULES § 4.34:** существующие fakes (`FakeVisionBackend`) не сломаны —
    default-метод возвращает `null`.
  - **Build 0/0. Тесты 1080/1080 (без изменений).**

- **v1.13.x (KI-207) — RAG: Page viewer — text layer + поиск + выделение**:
  - **Проблема (smoke KI-205):** PNG — «тупая картинка», нельзя
    выделить текст или найти фразу в документе.
  - **Backend:**
    - `PageTextLayerDto` + `WordBoxDto` — слова + bbox в natural PNG px.
    - `IOcrService.RecognizeWithLayoutAsync` — Tesseract word boxes.
    - `PdfParser` — text layer для OCR-страниц (Tesseract) и текстовых
      PDF (PdfPig `NearestNeighbourWordExtractor`). Сохранение
      `page-N.json` рядом с `page-N.png`.
    - `GET .../pages/{N}/ocr` — сырой JSON PageTextLayerDto.
  - **Frontend:**
    - Прозрачный text layer поверх PNG (выделение + Ctrl+C).
    - Ctrl+F / кнопка 🔍 в toolbar — поисковая панель в модалке.
    - Гибридный поиск: фраза целиком → fallback OR по токенам
      (ловит разрыв слов между страницами).
    - Async префетч всех страниц → сквозной счётчик по документу
      (`Поиск… N/M` → `K / M`).
    - Навигация ↑/↓ / Enter / Shift+Enter по всему документу
      с автоперелистыванием.
    - Подсветка через `rgba(255, 235, 59, 0.4)`.
    - `e.code === 'KeyF'` — RU/EN раскладки работают одинаково.
    - `window.addEventListener(..., { capture: true })` —
      перехват Ctrl+F до браузера.
  - **7 итераций fix'ов** (`68c79a8` → `4f44cb5`): camelCase JSON,
    приоритет Ctrl+F, img-cache race, position relative для text-layer,
    keyboard layout, compound query, сохранение hits при переходах.
  - **Известное ограничение:** при вводе query до полной загрузки
    списка страниц (`totalPages === 0`) префетч находит 0 hits.
    Фикс запланирован в KI-208.
  - **Тесты:** 1080/1080 (без изменений — backend без новых unit-тестов,
    frontend smoke).

- **v1.13.x (KI-206) — RAG: Page viewer UX (thumbnails, zoom, scroll)**:
  - **Проблема (smoke KI-205):** базовая модалка без thumbnails, зума,
    скролла колесом. Пользователь не может быстро навигировать по
    большим сканам.

### Fixed
- **v1.13.x (KI-205) — RAG: постраничный просмотр PNG сканов**:
  - **Проблема:** временные PNG-страницы удалялись в `finally`. Пользователь
    не видел, что распознал OCR. При ошибках не понимал, где проблема
    (OCR / PDF / качество скана).
  - **Фаза 1 (backend, `e9f4cea`):**
    - `ParseOptions` (new DTO) — `SavePagesDirectory`, `RenderDpi`.
    - `IRagDocumentParser.ParseAsync(file, options, ct)` — default-метод
      (обратная совместимость).
    - `PdfParser` — сохранение PNG каждой страницы в
      `{SavePagesDirectory}/page-N.png` (независимо от OCR).
    - `DocumentIngestionService` — создание директории + передача через
      `ParseOptions`.
    - `ChatAttachmentService` — создание `{chatFolder}/{safeName}-pages/`
      при upload, удаление при delete/clear.
    - `ChatAttachmentsController` — 2 endpoint'а: список PNG и сам PNG.
    - `ChatAttachmentDto` — +`PagesCount`, +`PagesAvailable`.
    - `appsettings*.json` — `Ocr:SavePagesToWorkspace` (prod=false, dev=true).
  - **Фаза 2 (frontend):**
    - `chat.js`: кнопка «👁 N» в чипе attachment'а (если `pagesAvailable`).
      Модалка с `<img>` + навигация ←/→ + счётчик «N / M» + Escape + прелоад
      следующей страницы.
    - `chat.css`: стили `.chat-pages-modal-*`, `.chat-attachment-chip-pages`.
    - `Index.cshtml`: `data-label-pages-*` + модалка.
    - `.resx` (RU + EN): 6 ключей (`RagPages*`).
  - **Файлы:** `ParseOptions.cs` (new), `IRagDocumentParser.cs`, `PdfParser.cs`,
    `IngestionRequest.cs`, `DocumentIngestionService.cs`, `ChatAttachmentDto.cs`,
    `ChatAttachmentService.cs`, `ChatAttachmentsController.cs`,
    `appsettings*.json`, `chat.js`, `chat.css`, `Index.cshtml`, `SharedResources*.resx`.
  - **Тесты:** 1080/1080 (без изменений — backend без новых unit-тестов,
    smoke покрывает).

- **v1.13.x (KI-203) — RAG: OCR-fallback для сканов PDF**:
  - **Проблема:** `PdfParser` (PdfPig) извлекает только текстовый слой
    PDF. Сканы (фото договоров, отсканированные книги) дают пустой
    `page.Text` → чанки не создаются → RAG не находит содержимое.
  - **3 фазы:**
    - **Фаза 1** (`b19d6d2`): `IOcrService` + `OcrOptions` +
      `TesseractOcrService` (Singleton, Lazy engine, потокобезопасный).
      NuGet `Tesseract 5.2.0` + `PDFtoImage 5.0.0`. Скрипт
      `download-tessdata.ps1` (tessdata_best, rus+eng, ~30 MB).
    - **Фаза 2** (`c4ed09a`): `PdfParser` — `ParseInternalAsync` с
      OCR-fallback для страниц < `MinTextCharsPerPage` (50) символов.
      `ShouldOcrFallback` (public static, тестируется без PDF).
      `RenderPageToPng` — `Conversion.SavePng` (PDFtoImage 5.x,
      `Index page`, temp-файлы). Метаданные `ocrPagesUsed`.
    - **Фаза 3**: `appsettings.json` (`Enabled: false` — prod),
      `.Development.json` (`Enabled: true` — dev).
  - **Graceful degradation:** на Linux `IsReady = false` (native
    lib tesseract отсутствует в NuGet) → парсер работает как раньше.
  - **Файлы:** `IOcrService.cs`, `OcrOptions.cs`, `TesseractOcrService.cs`,
    `PdfParser.cs`, `FakeOcrService.cs`, `PdfParserTests.cs`,
    `download-tessdata.ps1`, `Startup.cs`, `appsettings*.json`.
  - **Тесты:** 1068 → **1079** (+11: 3 constructor + 8 Theory).
- **v1.13.x (KI-194-fix3) — Vision LLM не различает портал и статью**:
  - **Симптом (smoke 2026-10-07, chatId=62/63):** задача «найди статью
    про Москву» — Enter отправлен, переход на `ru.wikipedia.org/wiki/Москва`
    произошёл (search_input переместился с y=372 на y=180, value сохранился),
    но VL-модель возвращает **тот же `description`** («Страница Википедии
    с языками») и **те же `language_link×3`** — как на портале. Пропускает
    крупный заголовок «Москва».
  - **Причина:** правило 22 промпта (`VisionUiDescribe`) — «**Всегда**
    включай ровно ТРИ: Русский, English, Deutsch». VL буквально следует
    промпту, даже когда языковых ссылок на странице нет. Плюс правило 8
    говорит «игнорируй заголовки» → VL пропускает `article_title`.
  - **Fix:**
    - **Правило 8** — приоритет `article_title` / `main_heading` (первый
      элемент списка). «Крупный заголовок статьи — это НЕ decoration».
    - **Правило 22** — «включай ТОЛЬКО ТЕ, что видишь». Два случая:
      портал (3 языка) / статья (0 языков). «НЕ ВЫДУМЫВАЙ».
    - **Правило 23** — description отражает **главное**: «Статья \"Москва\"
      в Википедии», а не «страница с языками».
    - **Правило 14 (Planner)** — усилен признак: «search_input переместился
      в шапку (y≈180) + есть article_title → ТОЧНО done».
  - **Файлы:** `VisionSystemPrompt.cs`.
  - **Тесты:** 1043/1043 (fix в промпте, без изменения кода).

### Added
- **v1.13.x (KI-203 Phase 2) — RAG: OCR-fallback для сканов PDF**:
  - **Проблема:** `PdfParser` (PdfPig) извлекает только текстовый слой.
    Сканы (фото договора, отсканированная книга) дают пустой `page.Text`
    → чанки не создаются → RAG не находит содержимое.
  - **Fix:**
    - `PdfParser` инжектит `IOcrService` + `IOptions<OcrOptions>`.
    - `ParseInternalAsync` — для страниц с текстовым слоем <
      `MinTextCharsPerPage` (50) применяет OCR через `IOcrService`.
    - `RenderPageToPng` — рендер страницы через PDFtoImage (PDFium).
    - `ShouldOcrFallback` (public static) — решает, применять ли OCR
      (учитывает `Enabled` / `IsReady` / `MinTextCharsPerPage` / `MaxPagesToOcr`).
    - Метаданные `ocrPagesUsed` — если был хотя бы один OCR.
  - **Файлы:** `PdfParser.cs`, `FakeOcrService.cs` (new),
    `PdfParserTests.cs`.
  - **Тесты:** 1068 → **1079** (+11: 3 constructor + 8 Theory).

  - **Симптом:** одна и та же кнопка на соседних кадрах описывается
    разными id (`search_btn` → `search_button`). Planner сгенерирует
    разные `click`-actions, детектор цикла KI-187 их не поймает,
    approval-модалка показывается дважды.
  - **Fix:**
    - Новый `VisionIdNormalizer.Normalize(id)` — приводит типовые
      суффиксы к единой форме: `_btn`/`_butt` → `_button`,
      `_lnk` → `_link`, `_field`/`_inp`/`_tb` → `_input`,
      `_chk`/`_cb` → `_checkbox`, `_dd`/`_sel` → `_dropdown`,
      `_txt` → `_text`, `_opt` → `_option`. Регистр префикса сохранён.
    - `ScreenDescriptionParser.ParseUiElement`: `id = Normalize(id)` —
      нормализация при парсинге VL-ответа.
    - `VisionAgentService.DetectPlannerCycle`: сравнивает
      нормализованные target'ы — drift больше не обходит детектор.
  - **Файлы:** `VisionIdNormalizer.cs` (new), `ScreenDescriptionParser.cs`,
    `VisionAgentService.cs`, `VisionIdNormalizerTests.cs` (new).
  - **Тесты:** 1043 → **1068** (+25).
- **v1.13.x (KI-194) — Vision Planner: premature `done` для goal-задач**:
  - **Симптом:** задача «найди статью про Москву» — шаг 1 `type`, шаг 2 `click
    search_button`, экран **не изменился** (всё ещё главная Wikipedia с полем
    поиска). Planner возвращает `done`: «Поиск выполнен и кнопка нажата».
    `success=True`, задача фактически не решена.
  - **Причина:** Planner LLM (qwen3-4b) не различает два типа задач:
    **literal** («кликни X» — достаточно клика, KI-202) и **goal** («найди X» —
    нужен РЕЗУЛЬТАТ на экране). Для goal-задач возвращал `done` после
    первого успешного клика без проверки результата.
  - **Fix:** правило **14** в `PlannerPlanNext` — явно разделяет literal/goal
    задачи. Для goal-задач требует проверить признаки результата
    (search_results, article_title, confirmation, новые элементы) **перед**
    `done`. Few-shot примеры «задача не выполнена» / «задача выполнена».
  - **Файлы:** `VisionSystemPrompt.cs`.
  - **Тесты:** 1043/1043 (без изменений — fix в промпте).
- **v1.13.x (KI-192) — Vision Agent: retry на пустом `ui_elements`**:
  - **Симптом:** VL-модель на медленно грузящихся страницах (gismeteo, РЖД)
    на первом кадре возвращает `ui_elements=[]` (белый экран, спиннер).
    Planner LLM видит «пустой экран» → `wait` → снова пусто → через 2-3
    итерации `fail`.
  - **Fix:**
    - `VisionAgentService.RunTaskAsync` — при `ui_elements=[]` **и**
      `history.Count == 0` (первый кадр) — retry-loop до 3 раз с паузой
      `PageStabilityCheckMs × 4` (≈2 сек). Planner **не вызывается**
      до успешного describe. Логирование retry на `LogInformation`.
    - `VisionSystemPrompt.PlannerPlanNext` — правило **8a**: «Если
      `ui_elements=[]` и это первый кадр — верни `wait`, не `fail`».
  - **Файлы:** `VisionAgentService.cs`, `VisionSystemPrompt.cs`,
    `VisionAgentServiceTests.cs` (+1 тест).
  - **Тесты:** 1042 → **1043**.

### Added
- **v1.13.x (KI-161) — Vision Agent: PuppeteerSharp CDP-attach**:
  DOM+Vision hybrid: 0 px ошибки клика для DOM-доступных элементов
  (вместо ±20-30 px у VL).
  - **Ф1 (Fixed):** контракты + DTO. `IChromeCdpSession`,
    `ICoordinateProvider`, `CoordinateRequest/Result`,
    `CdpElementQuery/Result/ViewportInfo`,
    `VisionCoordinateProviderOptions`, `VisionCdpOptions`
    (2 интерфейса + 7 DTO).
  - **Ф2 (Fixed):** `PuppeteerSharpCdpSession` — реализация
    `IChromeCdpSession`. Подключение к Chrome через
    `Puppeteer.ConnectAsync` (`--remote-debugging-port=9222`,
    `DefaultViewport=null`). JS-скрипт поиска
    элемента: `label` (substring, case-insensitive) →
    `position` (ближайший центр, tolerance 200 px) → `type`
    (первый видимый). JS-скрипт viewport-метрик: DPR,
    `window.screenX/Y`, chrome UI offset. Конвертация
    VL-bounds (screenshot-space) → viewport-css внутри
    `FindElementAsync`. `Disconnect()` в `DisposeAsync` — не
    убивает Chrome. Не бросает — при любой ошибке
    `Found=false` + `Error`. PuppeteerSharp 7.1: используем
    конкретные классы `Browser` / `Page` (интерфейсы
    `IBrowser` / `IPage` появились в v10+).
  - **Ф3 (Fixed):** `DomCoordinateProvider` + `VisionCoordinateProvider` —
    две реализации `ICoordinateProvider`.
    `DomCoordinateProvider` (создаётся через `new` внутри
    `LocalHarnessVisionBackend`) — обёртка над `IChromeCdpSession`,
    конвертация viewport-css → window-css → screen-px → screenshot-space.
    `VisionCoordinateProvider` (DI Singleton) — fallback:
    bounds-center из VL-описания (KI-190).
  - **Ф4 (Fixed):** `IVisionBackend` + `LocalHarnessVisionBackend`.
    - `IVisionBackend`: 2 default-метода — `GetCoordinateProvider()`
      (→ null) и `GetScreenshotScale()` (→ (1.0, 1.0)).
    - `VisionAgentOptions.CoordinateProvider` — новое поле
      (секция `VisionAgent:CoordinateProvider`).
    - `LocalHarnessVisionBackend.OpenAsync`: добавляет
      `--remote-debugging-port=9222` (порт из конфига); после
      получения HWND пытается подключиться к Chrome через CDP.
      При успехе создаёт `DomCoordinateProvider`; при неудаче —
      `_cdpSession = null`, VL-fallback.
    - `GetCoordinateProvider()` override — возвращает
      `_domCoordinateProvider` (или null).
    - `GetScreenshotScale()` override — возвращает текущий
      `_screenshotScaleX/Y`.
    - `CloseBrowser` — сначала `Disconnect` CDP, потом kill Chrome.
  - **Ф5 (Fixed):** `VisionAgentService.ResolveCoordinatesAsync` —
    DOM-first. `Mode ∈ {dom, auto}` + `backend.GetCoordinateProvider() != null`
    → `DomCoordinateProvider.ResolveAsync`. При `Found=true` и
    `X/Y > 0` — координаты из DOM. Иначе — VL-fallback
    (Verify KI-162-2 → bounds-center KI-190 → center).
    `Mode="vision"` — DOM-блок пропускается.
  - **Ф6 (Fixed):** DI + appsettings.
    - `Startup.cs` (`RegisterVisionAgentTools`):
      `PuppeteerSharpCdpSession` (Scoped),
      `IChromeCdpSession` (Scoped, factory),
      `VisionCoordinateProvider` (Singleton).
      `DomCoordinateProvider` **не в DI** — создаётся через `new`
      внутри `LocalHarnessVisionBackend` (DESIGN § 2.5, нужна
      конкретная CDP-сессия задачи).
    - `appsettings.json` (prod): `CoordinateProvider:Mode = "vision"`,
      `Cdp.Enabled = false` — безопасный дефолт (KI-190, без CDP).
    - `appsettings.Development.json`: `Mode = "auto"`,
      `Cdp.Enabled = true` — DOM если CDP подключён, иначе VL.
    - **NullLogger:** `PuppeteerSharpCdpSession` создаётся
      в `LocalHarnessVisionBackend.OpenAsync` через `new` с
      `NullLogger<PuppeteerSharpCdpSession>.Instance` (backend не
      имеет `ILoggerFactory`; переход на DI-инжект — v1.13.x-fix,
      если понадобятся логи CDP).
  - **Ф7 (Fixed):** тесты.
    - `DomCoordinateProviderTests` (12): конвертация
      viewport→screen→screenshot (identity, offsets, DPR=1.25,
      scale=2), edge cases (не подключён, not found, throw,
      cancellation, DPR=0, scale=0, propagation label/type).
    - `VisionCoordinateProviderTests` (5): bounds-center,
      null bounds, zero-size bounds, null request, Name.
    - `PuppeteerSharpCdpSessionTests` (9): 6 non-skip
      (empty URL, whitespace, not-connected, dispose idempotent,
      null query) + 3 skip (реальный Chrome).
    - **Итого:** +26 тестов (1016 → 1042).
  - **Ф8 (Fixed):** README (раздел «CDP-attach»),
    `docs/KNOWN_ISSUES.md` (KI-161 → Fixed),
    `docs/development/RULES.md` (§ 7 — KI-161 Fixed),
    этот CHANGELOG. **Все 8 фаз KI-161 закрыты.**
  - **До:** VL-координаты ±20-30 px, `bounds-center` (KI-190) —
    попадание не для всех элементов.
  - **После:** DOM-координаты **0 px** для DOM-доступных элементов
    (button / link / text_input / checkbox / radio / select),
    VL-fallback для canvas / WebGL / shadow-DOM / iframe / desktop.

### Fixed
- **v1.13.x (KI-202) — Vision Planner возвращает `fail` вместо `done`
  после успешного клика**:
  - **Симптом:** задача «Открой wikipedia.org и кликни по ссылке "Русский"».
    Шаг 1: `click language_link_1` — **успешно** (CDP-attach нашёл
    `#js-link-box-ru`, клик выполнен, URL сменился на ru.wikipedia.org).
    Шаг 2: Planner → `fail` с reason «Ссылка «Русский» уже была кликнута».
    `success=false, steps=1` (задача фактически решена).
  - **Причина:** правило 10 промпта (KI-182) запрещало **повторять**
    click X, но не говорило явно, что **если задача была «кликни по X»
    и X уже кликнут — это `done`**. qwen3-4b интерпретировала «не
    повторяй» как «не могу кликнуть → fail».
  - **Fix:** правило 10 расширено: «Если задача была «кликни по X» и X
    был успешно кликнут — верни `done`, НЕ `fail`». Добавлен явный
    ПРИМЕР.
  - **Файлы:** `VisionSystemPrompt.cs`.

- **v1.13.x (KI-201) — Vision LLM обрывает JSON → ui_elements=[] →
  клик не валидируется**:
  - **Симптом:** после KI-200 VL-модель генерирует 8 элементов, но
    не укладывается в `MaxTokens=768` → обрыв JSON на 8-м элементе
    → `JObject.Parse` падает → `ui_elements=[]` → Planner click
    `language_link` отклоняется валидатором → loop до timeout.
  - **Первопричины (2):** (а) VL-модель генерирует слишком много
    языковых ссылок после KI-198; (б) парсер не восстанавливает
    частичный JSON.
  - **Fix:**
    - **Правило 22 промпта:** «МАКСИМУМ 3 ЯЗЫКОВЫЕ ССЫЛКИ: Русский,
      English, Deutsch. Всего ≤ 5 элементов.»
    - **Правило 23 промпта:** `description` ≤ 100 символов.
    - **Правило 24 промпта:** «Если уже 5 элементов — НЕМЕДЛЕННО
      закрой `]` и `}`.»
    - **`ScreenDescriptionParser.TryRecoverPartialElements`:** новая
      функция — при провале `JObject.Parse` сканирует текст с
      балансировкой `{`/`}` и вытаскивает все полные объекты.
    - **`VisionLlm.MaxTokens` 768 → 1024** (dev).
  - **Файлы:** `ScreenDescriptionParser.cs`, `VisionSystemPrompt.cs`,
    `appsettings.Development.json`.

- **v1.13.x (KI-200) — Vision LLM зацикливается на однотипных элементах
  (language_link) + HttpClient.Timeout 100s**:
  - **Симптом:** после KI-198 (правило 22 — «включай языковые ссылки»)
    VL-модель Qwen2.5-VL-7B генерировала **все языковые ссылки подряд**
    (`language_link` × 30+), зациклилась на 693+ токенов. LM Studio лог:
    `n_gen = 693, tg = 8.11 t/s` → `Client disconnected`.
    Наш клиент — `Vision LLM не ответила за 300 секунд`.
  - **Корни (2 бага):**
    1. Правило 22 не ограничивало количество языковых ссылок —
       VL-модель включила все 30+ на Wikipedia.
    2. `HttpClient.Timeout = 100s` (дефолт .NET) конкурировал с нашим
       `cts.CancelAfter(300)` — **RULES § 4.48**.
  - **Fix:**
    - **Правило 22 промпта:** «НЕ БОЛЕЕ 8 элементов ВСЕГО. Если
      языковых ссылок больше 6 — включай только упомянутые в задаче
      или самые популярные (Русский, English, Deutsch). НЕ перечисляй
      все языки подряд.»
    - **Правило 23 промпта:** «Если уже 8 элементов — НЕМЕДЛЕННО
      закрой `]` и `}`.»
    - **`HttpClient.Timeout = Timeout.InfiniteTimeSpan`** в
      `LmStudioVisionClient` (2 места) и `LmStudioPlannerClient`.
      Таймаут — только через `cts.CancelAfter`.
    - **`VisionLlm.MaxTokens` 1792 → 768** (dev) — отсекает зацикливание.
    - **`VisionLlm.TimeoutSeconds` 300 → 180** (dev) — реальное время
      генерации ~70 сек, 180 = 2.5× запас.
  - **Файлы:** `VisionSystemPrompt.cs`, `LmStudioVisionClient.cs`,
    `LmStudioPlannerClient.cs`, `appsettings.Development.json`.

- **v1.13.x (KI-198, KI-199) — Vision LLM: языковые ссылки + галлюцинация
  `center`**:
  - **KI-198 (High):** на главной странице Wikipedia Vision LLM
    (Qwen2.5-VL-7B) возвращает только `search_input` + `search_btn`,
    пропуская крупные языковые ссылки («Русский», «English», ...).
    Planner LLM корректно отвечает `fail`: «Нет элемента с текстом
    "Русский" на странице».
    - **Причина:** правило 8 промпта `VisionUiDescribe` велит
      игнорировать «заголовки-тексты / decorations» — VL воспринимала
      крупные языковые ссылки в центре как заголовок.
    - **Fix (частично):** правило 8 — языковые ссылки явно
      обозначены как `type=link` (не decorations); добавлено
      правило 22 — на порталах (Wikipedia / GitHub) приоритет
      кликабельных ссылок над заголовками.
    - **Файлы:** `VisionSystemPrompt.cs` (`VisionUiDescribe`).
    - **Возможные итерации:** поднять hard cap 8 → 15; более крупная
      VL-модель; CDP-fallback по label (KI-161).
  - **KI-199 (Medium):** VL-модель возвращает `center`, не
    соответствующий `bounds` (smoke chatId=57, шаг 2: `bounds.y=375,
    h=30`, а `center.y=20` — область адресной строки Chrome).
    - **Fix:** `ScreenDescriptionParser.Parse` — игнорирует `center`
      из ответа VL, всегда пересчитывает как `bounds.x + w/2,
      bounds.y + h/2` (если bounds заданы). Правило 11 промпта
      `VisionUiDescribe` усилено — center обязан удовлетворять
      bounds-center.
    - **Файлы:** `ScreenDescriptionParser.cs`, `VisionSystemPrompt.cs`.

- **v1.13.x (KI-197) — Vision Planner: галлюцинация actions вне whitelist**:
  - **Симптом:** Planner LLM (qwen3-4b) на задаче «Открой wikipedia.org
    и кликни по ссылке "Русский"» возвращает `action=navigate` (или
    `goto` / `open` / `search`) — action вне whitelist. `VisionActionParser`
    → `fail` с явным списком допустимых actions → `success=false, steps=0`.
  - **Причина:** qwen3-4b видит задачу «Открой wikipedia.org» →
    интерпретирует её как **навигацию** (знакомый паттерн `navigate`
    из Playwright / Puppeteer). В `PlannerPlanNext` не было явного
    запрета выдумывать actions вне списка, не было примеров
    типичных ошибок, не было объяснения что URL передаётся в
    `run_task(url=...)` **до** loop'а.
  - **Fix 1 (main):** в `PlannerPlanNext` добавлен новый блок
    «КРИТИЧНО — СПИСОК ACTIONS ЗАКРЫТ» (перед «ГЛАВНОЕ ПРАВИЛО»):
    - Явный whitelist 11 actions.
    - 5 примеров типичных галлюцинаций с `❌`
      (`navigate` / `goto` / `open` / `search` / `scroll_to`).
    - Объяснение: URL уже открыт ДО loop'а; пусто в `ui_elements` →
      `wait` (не `navigate`); есть элемент → `click` / `type` /
      `press_key`.
    - `VisionSystemPrompt.cs`.
  - **Fix 2 (v1.13.x, fixed):** `VisionActionParser.Parse` — reason
    при неизвестном action теперь содержит подсказку о типичной
    browser-hallucination (`navigate` / `goto` / `open` / `search` /
    `scroll_to` / ...). `LmStudioPlannerClient` — `LogWarning` при
    `action=fail` (видно в логе сразу + raw content LLM).
    - Файлы: `VisionActionParser.cs`, `LmStudioPlannerClient.cs`.
  - **Воспроизведение:** 100% (smoke chatId=53/54/55/56 — все падали
    на step 2 после `wait`).
  - **Связанные:** KI-161 (работает), KI-192 (не проявляется),
    KI-194 (не проявляется), KI-195 / KI-196 (работают).

- **v1.13.x (KI-195, KI-196) — Vision Planner: hotkey-first + no-click-after-Enter**:
  - **KI-195 (Fixed):** правило 13 в `PlannerPlanNext` — после
    `type target=search_input` для отправки формы использовать
    `press_key(key="Enter")`, а не `click` по submit-кнопке. Enter
    работает 100% надёжно, координаты VL не требуются.
  - **KI-196 (Fixed):** расширение правила 13 — если в history уже есть
    `press_key(key=Enter)` после `type`, submit выполнен. НЕ кликать
    `search_button` повторно, даже если `ui_elements` показывает тот же
    search-элемент (Wikipedia оставляет строку поиска на странице
    результатов). Возвращать `done`.
  - **Smoke chatId=46:** до фикса — `type → press_key Enter → click
    search_button → fail`. После фикса ожидается:
    `type → press_key Enter → done`.
  - **Файлы:** `VisionSystemPrompt.cs` (`PlannerPlanNext`).

- **v1.13.x (KI-162-fix2) — Coordinate-then-Verify: verify-координаты НЕ точнее
  bounds-center**:
  - **Симптом:** при `vision_agent(action='click', target='search_button')` на
    Wikipedia клик уходил на 20 px левее целевой кнопки. Smoke-прогон
    (chatId=37/38/39): `bounds-center=(689,385)` → попадание,
    `verify=(675,387)` → промах на левую границу. Confidence при этом — 0.90
    (ложная).
  - **Root cause:** Qwen2.5-VL-7B даёт ±20–40 px noise даже на кропе ×2.
    Verify-координаты (`VerifyTargetResultDto.X/Y`) — не улучшение,
    а второй источник шума. `bounds-center` из KI-190 — надёжнее.
  - **Fix:** `VisionVerifyHelper.TryVerifyAsync` теперь **всегда возвращает
    bounds-center** (fallbackX, fallbackY). Verify-координаты
    логируются для диагностики (видеть, что VL «видит» на кропе), но
    в клик не идут. Verify оставлен как диагностический инструмент;
    `VisionAgent:Verify:Enabled=false` полностью отключает вызов.
  - **Файлы:** `VisionVerifyHelper.cs`, `VisionAgentService.cs`,
    `VisionAgentTool.cs`.
  - **Известное ограничение:** `Verify:Enabled=false` не удаляет код —
    `VerifyTargetAsync` остаётся в интерфейсе для будущих экспериментов
    с более мощными VL-моделями (KI-161, KI-163).

- **v1.13.x (KI-185) — Chat LLM: лишняя смена tool'а после fail от vision_agent**:
  - **Симптом:** после `vision_agent(action='run_task') → success=false`
    Chat LLM автоматически вызывала `consult_secondary_agent`
    (с модалкой approval) вместо честного ответа пользователю.
  - **Root cause:** правило 11 в `DefaultSystemPrompt` **само разрешало**
    переключение («либо переключись на `consult_secondary_agent`
    (для browser-задач)»). qwen3-4b добросовестно следовала промпту.
  - **Fix:**
    - Правило 11: убран совет про переключение на `consult_secondary_agent`.
      Добавлено: «после 2+ Fail — верни честный ответ БЕЗ автоматической
      смены tool'а».
    - Правило 13: явно запрещено переключаться на `consult_secondary_agent`,
      `web_agent` и т.п. после fail от vision_agent.
  - **Файлы:** `ChatStreamService.cs` (`DefaultSystemPrompt`).

- **v1.13.x (KI-183, KI-184) — Chat LLM: галлюцинация URL + лишние вызовы**:
  - **KI-183:** Chat LLM выдумывала URL (`ii-chattools.com`) для
    `vision_agent(action='describe')` — домен не в whitelist → Fail.
    Fix: правило 10 в `DefaultSystemPrompt` — «НЕ выдумывай URL. Для UI-задач
    приложения IIChatTools — НЕ указывай url вообще».
    Файлы: `ChatStreamService.cs`.
  - **KI-184:** после 2+ Fail от Vision Agent Chat LLM вызывала
    `planner_agent(task='Запомнить, что создание чата не удалось')` —
    записывала в `MemoryEntries` факт провала. Fix: правило 11 в
    `DefaultSystemPrompt` — «после 2+ Fail подряд — верни честный ответ,
    НЕ вызывай planner_agent / save_memory».
    Файлы: `ChatStreamService.cs`.

- **v1.13.x (KI-187, KI-188) — Vision Agent: детектор цикла + graceful pipe**:
  - **KI-187 (High):** Planner LLM зацикливался на `wait` после успешного
    action. На wikipedia.org: `type "Москва"` → `click search_button` →
    4×`wait` → повторный `click search_button` → loop до STOP. Fix:
    детектор `DetectPlannerCycle` в `VisionAgentService` — 3+ подряд `wait`
    или 3+ подряд одинаковый `(action, target)` → fail. Плюс правило 12
    в `PlannerPlanNext` — не делать `wait` при 2+ предыдущих.
    Файлы: `VisionAgentService.cs`, `VisionSystemPrompt.cs`.
  - **KI-188 (Low):** при STOP от overlay `WpfVisionOverlayHandle.SendCommand`
    падал с `IOException: Pipe is broken` (overlay уже мёртв). В логах —
    полный stack trace. Fix: `try/catch (IOException)` в `SendCommand` +
    `try/catch (Exception)` в `SetFinalStatus`. Файлы: `WpfVisionOverlayHandle.cs`.

- **v1.13.x (KI-185) — Chat LLM делает лишние vision_agent actions после Fail**:
  - **Симптом:** после Fail от `vision_agent(action='run_task')` Chat LLM
    возвращает честный ответ, но **затем** вызывает
    `vision_agent(action='click', target='system_status_button')` —
    самодеятельность. Approval-модалка висит 5 минут.
  - **Fix:** правило 13 в `DefaultSystemPrompt` — «после fail от vision_agent
    НЕ вызывай другие vision_agent actions. Верни честный ответ и жди
    указаний пользователя».
    Файлы: `ChatStreamService.cs`.

- **v1.13.x (KI-186) — Vision Agent не может работать с UI IIChatTools**:
  - **Симптом:** Vision Agent открывает `https://localhost:5001` в fresh
    Chrome profile без cookies. Видит главную страницу (не `/chat`),
    кнопки «Новый чат» нет. Задачи типа «нажми Новый чат» невыполнимы.
  - **Это by design** (изоляция профиля — часть 5 уровней безопасности,
    DESIGN § 6.1). Задокументировано.
  - **Fix (Commit F, промпт):** правило 13 явно указывает — для UI IIChatTools
    НЕ вызывать vision_agent, вернуть честный ответ.
  - **Отложенные решения:** CDP-attach (KI-161), cookies-передача.
    Файлы: `ChatStreamService.cs`.

- **v1.13.x (KI-182) — Vision Planner зацикливается на одном target**:
  - **Симптом:** Planner LLM при `run_task` повторял `click new_chat_button`
    на шаге 2, хотя на шаге 1 клик уже был успешным (чат создан). Задача
    не завершалась `done`, loop исчерпывал MaxSteps.
  - **Fix (Commit E, soft):** правило 10 в `PlannerPlanNext` — «если в history
    был успешный click по target X и цель достигнута — верни done».
    Правило 11 — обработка неполных задач (ввод текста после клика).
    **Hard-fix (детектор цикла в `VisionAgentService`) — Commit F.**
    Файлы: `VisionSystemPrompt.cs`.

- **v1.13.x (KI-177, KI-180, KI-181) — Vision Agent: модалка в кадре +
  tool-selection для UI-задач**:
  - **KI-180:** approval-модалка IIChatTools попадала в GDI-скриншот
    Vision Agent. Bootstrap скрывает модалку с анимацией ~300 мс,
    а `VisionAgentTool` делал `ScreenshotAsync` **сразу** после
    `WaitForDecisionAsync` → модель видела
    `[confirm_dialog, cancel_button, confirm_button]`.
    Fix: пауза `Task.Delay(400)` перед `ScreenshotAsync` в 3 методах
    (`HandleDescribeAsync`, `HandleScreenshotAsync`,
    `HandleCoordinateActionAsync`).
    Файлы: `VisionAgentTool.cs`.
  - **KI-177:** `click(target=X)` → FAIL «target 'X' не найден» —
    следствие KI-180 (модалка в кадре). Fix: тот же (пауза).
  - **KI-181:** Chat LLM выбирала `planner_agent` для UI-задач
    в приложении IIChatTools (например, «создать новый чат»).
    Fix: правило 13 в `ChatStreamService.DefaultSystemPrompt`.
    Файлы: `ChatStreamService.cs`.
  - **Warning CS1570:** удалён лишний `/// <summary>` из
    `VisionSystemPrompt.cs` (мусор от неверно применённой правки
    Commit C). Файлы: `VisionSystemPrompt.cs`.
    при `vision_agent(action='click')` — Bootstrap закрывает её с анимацией
    ~300 мс, а tool выполняется сразу после `WaitForDecisionAsync`. Vision LLM
    видит `[confirm_dialog, cancel_button, confirm_button]` вместо реальных
    элементов страницы.
  - **KI-177 (Planned, следующий коммит):** `click(target='X')` → FAIL
    «target 'X' не найден» — следствие KI-180. Требует `VisionAgentTool.cs`
    (кэш последнего `ScreenDescriptionDto`) или маскировки модалки.
  - **Что сделано сейчас:** правило 12 в Chat DefaultSystemPrompt +
    правило 21 в VisionUiDescribe (стабильные id).
  - **Что осталось:** fix самого tool — следующий коммит.

### Fixed
- **v1.13.x (KI-178, KI-179) — Vision Agent: timeout + зацикливание Chat LLM**:
  - **KI-178:** `MaxTokens = 1024` не укладывалось в 300 с таймаут на
    `Qwen2.5-VL-7B` (8.5 t/s): при prompt ~2000 токенов время генерации
    достигало 240+ сек, иногда модель зацикливалась и не останавливалась.
    Fix: `MaxTokens` 1024 → **768**; правило 22 в `VisionUiDescribe` —
    стоп-условие «остановись после `]`, не дублируй элементы».
    Файлы: `appsettings.Development.json`, `VisionSystemPrompt.cs`.
  - **KI-179:** qwen3-4b в Chat зацикливалась на Vision-задачах:
    `describe → click(Fail) → describe → click(Fail) → …` до лимита 5
    итераций. В финальном ответе — галлюцинация «Успешно определил…».
    Fix: правила 11 (при `success=false` — не повторяй, перепланируй)
    и 12 (используй id из последнего describe) в `DefaultSystemPrompt`.
    Файлы: `ChatStreamService.cs`.

- **v1.13.x (KI-176) — base64 в history ломает чат (Critical)**:
  - **Симптом:** после `vision_agent(action=screenshot)` любой следующий
    запрос в чате падает с `400: request (260605 tokens) exceeds the
    available context size (16384 tokens)`.
  - **Root cause:** `vision_agent(screenshot)` возвращает
    `{ path, base64: "...", sizeBytes }` (~344 KB base64). ChatStreamService
    сохранял tool-сообщение в БД **с base64** и передавал в LM Studio
    **с base64** → 260k токенов → 400.
  - **Fix:** новый helper `SanitizeToolResultForLlm(object data)` —
    рекурсивно клонирует JSON, удаляет поля `base64` и
    `imageBase64DataUrl`. Применяется **только** к:
    (а) `Content` tool-сообщения для БД; (б) `messages.Add(...)`
    tool-сообщения для LLM. В SSE-событии `tool_result` base64
    **остаётся** (UI рендерит PNG).
  - **Файлы:** `ChatStreamService.cs` (2 правки + 2 helper-метода).

- **v1.13.x (KI-173, KI-174) — Vision Agent: tool-selection + timeout**:
  - **KI-173:** Chat LLM не передавала `url` отдельным аргументом в
    `vision_agent(action=run_task)`, а кладла его в текст `task` → loop
    работал на текущем экране (чат IIChatTools) вместо Chrome → timeout.
    Fix: (а) правило 10 в `ChatStreamService.DefaultSystemPrompt`;
    (б) fallback в `VisionAgentService.RunTaskAsync` — extract `https?://...`
    из `request.Task` regex'ом (страховка от qwen3-4b).
  - **KI-174:** `VisionLlm.TimeoutSeconds = 180` не покрывал худший случай
    для Qwen2.5-VL-7B (2048 токенов × 8.4 t/s ≈ 245 с). Fix:
    `TimeoutSeconds = 300`, `MaxTokens = 1024` (достаточно для `ui_elements[]`).
  - **Файлы:** `ChatStreamService.cs`, `VisionAgentService.cs`,
    `appsettings.Development.json`.

- **v1.13.x (KI-114, KI-167, KI-168) — smoke #3 «Запуск скрипта»:
  tool-selection + ложный успех агента.**
  - **KI-114 (уточнён 2026-10-05 по итогам smoke #3):**
    `AgentToolBase.ExecuteAsync` — Fail только если `result.Completed == false`
    **И** `UsedTools.Count == 0` (агент вообще ничего не сделал — реальный
    провал). Раньше всегда возвращался `Ok` — LLM в Chat видела
    «success: true» даже при провале SubAgent (усиливало KI-113).
    Первая версия фикса (Fail при любом `Completed == false`) давала
    **ложные негативы**: задача выполнена, но SubAgent упёрся в `MaxSteps`
    до финального ответа. Файлы: `AgentToolBase.cs`.
  - **KI-167:** `SystemPrompt` `file_system_agent` (dev + prod) — добавлены
    правила 4 (жёсткий запрет внешних путей + честный Fail), 5 (не врать
    про успех), 6 (перечислить частичный результат). `Description`
    уточнено: «работает ТОЛЬКО внутри workspace».
  - **KI-168:** `ChatStreamService.DefaultSystemPrompt` — правило 8:
    «внешние пути (`c:\...`, `D:\...`, `/tmp/`, `/usr/...`) → `code_agent`
    (Python / JS). НЕ `file_system_agent` (вне workspace не может), НЕ
    `execute_command` (`cmd`/`powershell` не в whitelist)». Плюс ПРИМЕР 3.
  - **❗ Открытие:** `ExecuteCommandTool.AllowedCommands` = `git, gh, dotnet,
    node, npm, npx, python3, pip3` — `cmd`/`powershell` **отсутствуют**.
    Поэтому правило «внешние пути → `execute_command`» заменено на
    «→ `code_agent`».
  - **Требуется** ре-smoke сценария #3 (с путём **внутри workspace** для
    чистоты проверки) и smoke #1 (DeepSeek API).

### Added
- **v1.13.x (KI-162) — Vision Agent: Coordinate-then-Verify (crop + upscale)**:
  После первого `describe` VL-модель даёт `bounds` целевого элемента с
  ошибкой ±30 px (Qwen2.5-VL-7B на 1280×720). Добавлена **верификация
  координат вторым VL-вызовом** на кропе вокруг `bounds`:
  - `IVisionLlmClient.VerifyTargetAsync(croppedPng, targetDescription,
    originalBounds, ct)` — новый метод интерфейса.
  - `LmStudioVisionClient.VerifyTargetAsync` — multimodal POST с кропом.
  - `ExternalVisionClient.VerifyTargetAsync` — заглушка `Found=false`
    (multimodal у External — KI-141, Planned).
  - `AutoVisionClient.VerifyTargetAsync` — fallback-цепочка по
    `FallbackChain`; критерий успеха — `Found=true` (в отличие от
    `DescribeAsync`, где — непустое Description).
  - `VisionImageResizer.CropAndUpscale(png, x, y, w, h, padding, upscale)` —
    вырезает регион с padding'ом (min 200×200 px, clamp к границам),
    апскейлит ×N через HighQualityBicubic.
  - `VerifyResponseParser` — устойчивый парсер
    `{ x, y, confidence, found }` (markdown-обёртки, case-insensitive,
    невалидный JSON → `Found=false`).
  - `VisionVerifyOptions` (секция `VisionAgent:Verify`): `Enabled=true`,
    `CropPadding=100`, `Upscale=2`, `MinConfidence=0.6`.
  - `VisionSystemPrompt.GetVerifyTargetPrompt(targetDescription)` —
    промпт: координаты **в системе кропа**, потом обратный пересчёт
    `origX = cropX + llmX / upscale`.
  - Интеграция в `VisionAgentTool.HandleCoordinateActionAsync`: после
    `describe` → crop → VerifyTargetAsync → при `Found=true &&
    Confidence≥MinConfidence` используются уточнённые координаты,
    иначе — fallback на `bounds`-center (KI-190).
  - **Ожидаемый эффект:** ±3–5 px вместо ±30 px. Требует GPU-offload для
    Qwen2.5-VL-7B (иначе latency второго вызова 60–120 сек).
  - **Ограничение MVP:** Verify пока только в одиночных `click`-actions
    (через `VisionAgentTool`). В `run_task`-loop (через
    `VisionAgentService.ResolveCoordinates`) — отдельная итерация
    (KI-162-2, Planned).
  - **Windows-only:** `CropAndUpscale` использует `System.Drawing.Common`;
    на Linux — грациозный fallback на bounds center.

- **v1.13.x (KI-162 + KI-162-2) — Vision Agent: Coordinate-then-Verify (crop + upscale)**:
  После первого `describe` VL-модель даёт `bounds` целевого элемента с
  ошибкой ±30 px (Qwen2.5-VL-7B на 1280×720). Добавлена **верификация
  координат вторым VL-вызовом** на кропе вокруг `bounds`:
  - **KI-162:** базовый механизм — `IVisionLlmClient.VerifyTargetAsync`,
    `VisionImageResizer.CropAndUpscale`, `VerifyResponseParser`,
    `VisionVerifyOptions`, `VisionSystemPrompt.GetVerifyTargetPrompt`.
  - **KI-162-2:** `VisionVerifyHelper.TryVerifyAsync` — общий helper,
    который используют **оба** пути:
    - `VisionAgentTool.HandleCoordinateActionAsync` (одиночные click-actions);
    - `VisionAgentService.ResolveCoordinatesAsync` (полный `run_task` loop).
  - `LmStudioVisionClient.VerifyTargetAsync` — multimodal POST с кропом.
  - `ExternalVisionClient.VerifyTargetAsync` — заглушка `Found=false`
    (multimodal у External — KI-141, Planned).
  - `AutoVisionClient.VerifyTargetAsync` — fallback-цепочка по
    `FallbackChain`; критерий успеха — `Found=true`.
  - Конфиг (`VisionAgent:Verify`): `Enabled=true`, `CropPadding=100`,
    `Upscale=2`, `MinConfidence=0.6`.
  - Промпт: координаты **в системе кропа**, обратный пересчёт
    `origX = cropX + llmX / upscale` в helper'е.
  - Fallback на bounds-center (KI-190) — если Verify выключен, VL вернула
    `Found=false` или `Confidence < MinConfidence`.
  - **Ожидаемый эффект:** ±3–5 px вместо ±30 px.
  - **Windows-only:** `CropAndUpscale` использует `System.Drawing.Common`;
    на Linux — грациозный fallback на bounds center.

- **v1.12.x — WPF overlay для Vision Agent (KI-142, DESIGN § 4.6, § 6.4)**:
  on-screen indicator теперь — реальное WPF-приложение
  `IIChatTools.VisionOverlay.exe` (net10.0-windows, отдельный процесс).
  Полупрозрачное always-on-top окно в правом верхнем углу (320px),
  показывает прогресс «Шаг N из M», текущее действие, кнопку STOP.
  ESC / кнопка STOP → немедленная отмена задачи (реализовано через
  `IVisionOverlayHandle.StopToken` + `CreateLinkedTokenSource` в
  `VisionAgentService`).
  - **Новый проект:** `IIChatTools.VisionOverlay` (WPF, WinExe,
    `net10.0-windows`, `app.manifest` с PerMonitorV2 DPI-awareness).
    **Не наследует** `Directory.Build.props` (свои `Version` / `Copyright`).
    Не ссылается на другие проекты решения — самодостаточен.
  - **IPC:** NamedPipe `iichattools-vision-overlay-{taskId}`,
    JSON-строки, разделитель `\n`. Overlay — сервер, API — клиент
    (подтверждено архитектурно, DESIGN § 4.6). Команды:
    `progress` / `final_status` / `close` / `stop`.
  - **Клиент-сторона (в `IIChatTools.Services`):**
    `WpfVisionOverlayLauncher` (Singleton) + `WpfVisionOverlayHandle`
    (per-task, `IDisposable`). Retry-подключение к pipe: 5 сек,
    шаг 100 мс. `Process.Start` — через `ArgumentList` (RULES § 1.10).
  - **Расширение контракта:** `IVisionOverlayHandle.StopToken`
    (`CancellationToken`). Noop-реализация возвращает
    `CancellationToken.None`. `VisionAgentService` связывает его с
    `timeoutCts` через `CreateLinkedTokenSource`; `catch`-блоки
    различают STOP-overlay (`Отменено пользователем`) — проверяется
    **первым**, чтобы пользовательское действие имело приоритет
    над timeout.
  - **DI-switch** (`Startup.RegisterVisionAgentTools`): регистрируются
    оба launcher'а (Singleton), runtime-выбор по `Wpf.IsAvailable`
    (Windows + exe найден) → WPF, иначе — Noop (degraded mode на Linux).
  - **Копирование overlay в output API:** Target `CopyVisionOverlayToOutput`
    в `IIChatTools.API.csproj` кладёт содержимое
    `IIChatTools.VisionOverlay/bin/$(Configuration)/net10.0-windows/`
    в `$(OutDir)VisionOverlay/`. `ProjectReference` с
    `ReferenceOutputAssembly=false` — только для порядка сборки.
  - **`IIChatTools.sln`:** overlay **не добавлен намеренно** — WPF не
    собирается на Linux (NETSDK1100). Windows-only проект. Сборка на
    Windows — через Target `BuildAndCopyVisionOverlay` в
    `IIChatTools.API.csproj` (AfterTargets=Build, Condition=Windows).
  - **Отложено (v1.12.x):** Sandbox backend (Ф3), RemoteVnc (Ф4),
    smoke под `--no-build` при первом холодном старте (WPF overlay
    может не успеть за 5 сек на медленных дисках — план: увеличить
    retry до 8 сек + логировать фактическое время подключения).

### Fixed
- **v1.13.x (KI-146) — Speech: fallback → virtual hard error +
  дедупликация label в device picker**:
  - **speech.js:** если сохранённый микрофон недоступен
    (`NotFoundError` / `OverconstrainedError` / `NotReadableError` / `AbortError`)
    и fallback на системный default вернул **virtual** устройство
    (Steam Streaming / VB-Cable / VoiceMeeter / OBS) — запись
    **не начинается**, показывается hard error «Выберите физический
    в Профиль → Аудио». Раньше пользователь получал 2 «нулевых» опыта
    подряд: сначала «микрофон недоступен», потом «virtual default →
    maxAbs=0». Флаг `_deviceFallbackUsed` различает «default оказался
    virtual по независимым причинам» (warning, KI-144) от
    «fallback → default → virtual» (hard error, KI-146).
  - **profile-audio.js:** дедупликация `<select>` по нормализованному
    label. Chrome перечисляет одно физ. устройство как 3 разных `deviceId`
    с префиксами «По умолчанию — », «Оборудование — », «Default — »,
    «Communications — ». `normalizeDeviceLabel` их убирает,
    `populateDevices` группирует по `label.toLowerCase()`, внутри группы
    выбирает `deviceId`, совпадающий с `savedDeviceId` (чтобы подсветка
    сохранённого выбора работала), иначе — первый по порядку.
  - **Файлы:** `speech.js` (+1 флаг, +1 hard-error block в
    `_startRecording`, правка catch вокруг первого `getUserMedia`),
    `profile-audio.js` (+`DEVICE_LABEL_PREFIXES`, +`normalizeDeviceLabel`,
    замена `populateDevices`).
  - **Требует добавить** (отдельным коммитом или позже):
    ключ `labelSpeechFallbackVirtual` в `SharedResources.resx` (RU + EN)
    + `data-label-speech-fallback-virtual` на `#chat-messages`.
    Пока — fallback на русский текст в JS.

- **v1.12.x — Vision Agent: рабочий smoke на Qwen2.5-VL-7B
  (KI-131, KI-150, KI-155, KI-156, KI-157, KI-158, KI-160)**:
  - `VisionLlm.Model` = `qwen2.5-vl-7b-instruct` (dev + prod) вместо
    `ministral-3-3b-instruct-2512`, который падал с HTTP 400 (mmproj не загружен).
  - `VisionLlm.MaxTokens` = `2048`, `Limits.MaxSteps` = `15`,
    `Limits.MaxTaskSeconds` = `500` (dev).
  - `PlannerPlanNext`: anti-loop правила — few-shot примеры + запрет
    повторять одно действие > 2 раз (KI-160).
  - `ScreenDescriptionParser`: дедупликация `ui_elements` по `id` +
    hard cap 8 + отсечение id из цифр (KI-160).
  - **KI-150** (downscale 1920×1200 → 1024×640),
    **KI-155** (TryRefocusChrome перед mutation-действиями),
    **KI-156** (логотип IIChatTools в overlay),
    **KI-157** (scale координат VL-модели ×1.875),
    **KI-158** (маска overlay на скриншоте) — подтверждены в smoke.
  - **Требования:** LM Studio **Context Length ≥ 8192**,
    **GPU Offload ≤ 20** (Qwen2.5-VL-7B ≈ 6 GB).
  - **Проверено:** 3 успешных smoke-прогона подряд (`success: true`,
    4 шага, ~250 с).

- **KI-148** (v1.12.x) — Vision Agent: Chrome остаётся без фокуса после
  `OpenAsync`. `TryFocusChromeAsync` в `LocalHarnessVisionBackend`:
  ожидание `MainWindowHandle` + `AttachThreadInput` + `ShowWindow(SW_MAXIMIZE)`
  + `BringWindowToTop` + `SetForegroundWindow` (retry 3 × 300 мс).
  Без `AttachThreadInput` Windows игнорирует foreground-stealing из
  фонового процесса. `Win32Interop.cs`: +`SetForegroundWindow`,
  `ShowWindow`, `BringWindowToTop`, `AttachThreadInput`,
  `GetCurrentThreadId`, `SW_SHOW/SW_RESTORE/SW_MAXIMIZE`.

- **KI-149** (v1.12.x) — WPF overlay перехватывал фокус при клике.
  `MainWindow.OnSourceInitialized`: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`
  через новый `OverlayWin32.cs` + `ShowActivated="False"` в XAML.
  Клик по overlay не отбирает фокус у Chrome.

- **KI-152** (v1.12.x) — Vision Agent: timeout не различался от ошибки
  в локальном catch. В 3 локальных `try`-блоках loop
  (`ScreenshotAsync` / `DescribeAsync` / `PlanNextAsync`) добавлен
  `catch (OperationCanceledException) { throw; }` перед `catch (Exception)`.
  Внутри loop используется `effectiveCts.Token` (связан с timeout и
  overlay-STOP), а не `timeoutCts.Token`.

- **KI-142** (v1.12.x, Ф6.7) — WPF overlay для Vision Agent.
  Полная реализация (была заглушка `NoopVisionOverlayLauncher`).
  См. `### Added` выше.

### Won't Fix
- **KI-159 — Vision Agent: координатная сетка на скриншоте**.
  Нарисованная сетка сбивает VL-модель (`mistralai/ministral-3-3b`
  принимал числа за «Excel-таблицу»). Откачено. См. KI-159.

### Documented
- **v1.13.x (KI-167/168/169) — smoke #3 «Запуск скрипта»: провал tool-selection +
  ложный успех агента.**
  - **KI-167** (Planned): `file_system_agent` галлюцинирует успех вне workspace
    (рецидив KI-113 + усилитель KI-114). Проверено: `c:\projects\test\` и
    `script.bat` не существуют; AuditLogs `agent.file_system_agent | Status=Success`.
  - **KI-168** (Planned): Chat LLM выбирает `file_system_agent` для задач вне
    workspace. Нужно правило 8 в `DefaultSystemPrompt` + уточнение Description.
  - **KI-169** (Documented): SubAgent не аудирует внутренние tool-вызовы —
    видно только статус агента целиком. Снижает observability при разборе.
  - **Дополнительный вывод:** smoke-сценарий «Запуск скрипта» (#3) для
    проверки **tool-selection** лучше писать с путём **внутри workspace**
    (например, `%USERPROFILE%\IIChatToolsWorkspace\test\`) — тогда
    `file_system_agent` сможет сработать, и smoke будет чистым.

### Documented
- **v1.13.x (KI-192) — Vision Agent: VL возвращает пустой `ui_elements`
  на медленно грузящихся страницах**:
  - **Симптом (smoke 2026-10-07, chatId=41/42):** после успешного
    `type search_input "Москва"` → `click search_button` Wikipedia
    грузит страницу результатов **дольше**, чем длится один VL-цикл
    (37–81 сек на этом железе). Следующий `DescribeAsync` возвращает
    `{ description: "…", ui_elements: [] }` → Planner LLM видит
    «пустой экран» → `wait` → снова пусто → `fail`.
  - **Причина:** Qwen2.5-VL-7B на CPU/частичном GPU-offload — **8.6 t/s**.
    Между Describe и Describe страница может завершить навигацию, но
    следующий кадр ловится в промежуточном состоянии (белый экран).
    Loop **не различает** «пустой экран = ещё грузится» и «пустой экран =
    задача невыполнима».
  - **План (v1.13.x):** retry в `VisionAgentService.RunTaskAsync` при
    `ui_elements=[]` и `history.Count=0` — пауза 2 сек + повтор Describe
    (до 3 раз), без вызова Planner. Плюс правило в `PlannerPlanNext`:
    «при `ui_elements=[]` на первом кадре — верни `wait`, не `fail`».
  - **Связанные:** KI-131, KI-160, KI-194.

- **v1.13.x (KI-193) — Vision LLM: id drift между кадрами
  (`search_btn` → `search_button`)**:
  - **Симптом:** одна и та же кнопка поиска на соседних кадрах описывается
    разными id. Детектор цикла KI-187 не срабатывает (id разные),
    approval-модалка показывается дважды.
  - **План:** fuzzy-matching target'ов в `VisionActionValidator`
    (edit distance ≤ 2) + нормализация суффиксов (`_btn` / `_button`).

- **v1.13.x (KI-194) — Vision Planner: `done` без фактической проверки
  результата**:
  - **Симптом:** после `click search_button` с промахом (клик ушёл в
    левую границу) Planner LLM возвращает `done` с reason «Поиск по
    Москве выполнен и кнопка нажата». Задача фактически не решена,
    но `VisionTaskResultDto.Success = true`.
  - **Причина:** qwen3-4b считает, что раз в history был `click` без
    явного fail — задача выполнена. Проверки «сменилось ли состояние
    экрана» нет.
  - **План:** правило в `PlannerPlanNext` + post-verification
    (финальный describe после `done`). Долгосрочно — CDP-attach (KI-161).
  - **Рецидив** KI-113 (галлюцинация успеха SubAgent).

- **KI-144** — Chrome использует virtual audio device по умолчанию
  (Steam Streaming Microphone) → `maxAbs=0`. Решение — device picker
  в `/profile → 🎤 Аудио` (KI-145).
  (Steam Streaming Microphone) → `maxAbs=0`. Решение — device picker
  в `/profile → 🎤 Аудио` (KI-145).

- **KI-151** (Planned, v1.12.x) — Chrome temp-профиль не удаляется:
  `BrowserMetrics-*.pma` заблокирован ~500 мс после kill. Отдельный KI.

---

## [1.13.1] — 2026-10-04

**Speech Recognition — серия исправлений после MVP v1.13.0 (fix1-fix11).**
Первый production smoke (Chrome + Yandex Browser + Plantronics .Audio 478 USB
+ Windows) выявил проблемы в браузерных API и UX. Все исправления собраны
под тегом v1.13.1: миграция на `MediaStreamTrackProcessor` (WebCodecs),
фильтр галлюцинаций Whisper, VAD + хоткей, device picker в `/profile`,
warning при виртуальных устройствах.

**Тесты:** 1011 → **1016** (+5: `SpeechControllerTests`).

### Added
- **v1.13.1-fix11 (KI-140)**: VAD-параметры и `MAX_RECORDING_MS` вынесены
  из хардкода в `appsettings.json` + **адаптивный VAD**.
  - **Fix11**: вместо `const` в `speech.js` — секция `Speech:Vad` в
    `appsettings.json` / `.Development.json`; значения инжектятся через
    `data-speech-vad-*` + `data-speech-max-record-ms` на `#chat-messages`
    (RULES § 4.17); `speech.js` читает их через `_loadRuntimeConfig()`.
  - **Fix11a**: `CultureInfo.InvariantCulture` для `double` в Razor-`data-*`
    (ru-RU отдавала `0,015` → `parseFloat` = 0). Плюс защита в
    `_loadRuntimeConfig.num()` (запятая → точка).
  - **Fix11c**: адаптивный VAD. Раньше фиксированный порог `SilenceRms=0.015`
    работал только на микрофонах с RMS речи > 0.015. На тихих микрофонах
    (RMS речи ~0.006–0.010) VAD считал речь тишиной и обрывал запись через
    2 сек. Теперь при `Vad:AdaptiveEnabled=true` (default) порог вычисляется
    как `max(minObservedRms × NoiseMultiplier, AbsoluteMinRms)` — динамически
    подстраивается под уровень микрофона. Новые параметры:
    `AdaptiveEnabled=true`, `NoiseMultiplier=2.0`, `AbsoluteMinRms=0.001`.
    Авто-stop-лог содержит `min` (наблюдаемый минимум) и `mode` для
    диагностики.
  - **Файлы:** `SpeechOptions.cs` (+`SpeechVadOptions` + 3 поля),
    `appsettings*.json` (+`Speech:Vad` + 3 ключа), `Views/Chat/Index.cshtml`
    (+9 `data-*`), `speech.js` (`const` → `let`, +`_loadRuntimeConfig`,
    +адаптивный расчёт порога).

### Added
- **v1.13.1-fix10 (KI-140)**: VAD (Voice Activity Detection) — авто-остановка
  записи по тишине + хоткей `Ctrl+Shift+Space`.
  - **VAD:** параллельно с записью запускается `AudioContext` + `AnalyserNode`
    поверх того же `_stream`. Опрос RMS каждые 200 мс. Если RMS < 0.015
    держится ≥ 2000 мс (и запись идёт уже ≥ 700 мс) — auto-stop. Некритичен:
    если `AudioContext` недоступен, VAD молча отключается (manual / 60-сек
    auto-stop продолжают работать). Не создаёт ресурсов, если `_stream` не
    получен.
  - **Хоткей:** `Ctrl+Shift+Space` на `/chat` — toggle start/stop записи.
    `preventDefault()` (в textarea этот шорткат вводит `&nbsp;`). Не
    конфликтует с `Ctrl+B` / `Ctrl+F` / `Ctrl+K` (`chat.js` — без `shiftKey`).
  - **Файлы:** `speech.js` (+4 константы, +7 переменных состояния,
    +3 функции VAD, +1 hotkey-функция, правки `init`/`dispose`/`_startRecording`).

- **v1.13.1-fix9 (KI-140-fix, follow-up KI-144/KI-145)**: warning при выборе
  «виртуального» микрофона. Если пользователь не выбрал микрофон в
  `/profile → Аудио`, а браузер отдал трек, чей `label` матчит паттерн
  `/(steam|vb[-\s]?cable|virtual|voicemeeter|obs)/i` — одноразовый toast
  «Выбран виртуальный микрофон — возможен нулевой сигнал. Выберите физический
  в Профиль → Аудио.». Показывается **один раз за сессию**, только когда
  `_audioDeviceId === null` (пользователь не сделал явный выбор). Флаг
  сбрасывается в `disposeSpeechRecognition`.
  **Файлы:** `speech.js` (+1 константа + 1 флаг + 1 функция), `SharedResources*.resx`
  (+1 ключ ×2), `Views/Chat/Index.cshtml` (+1 `data-*`).

- **v1.13.1-fix8 (KI-145)**: выбор микрофона в `/profile → 🎤 Аудио`.
  - Кнопка «Разрешить доступ к микрофону» — триггерит запрос разрешения,
    затем `enumerateDevices()` возвращает реальные label'ы устройств.
  - `<select>` со списком `audioinput` + опция «Системный по умолчанию».
  - Кнопка «Протестировать» — записывает 2 сек через `MediaStreamTrackProcessor`,
    показывает `RMS` / `maxAbs` (зелёный = работает, красный = нулевой сигнал).
  - Выбор сохраняется в `UserSettings` (ключ `Audio.InputDeviceId`).
  - `speech.js` использует сохранённый `deviceId` в `getUserMedia`;
    при недоступности — тихий fallback на системный default + один toast
    «Сохранённый микрофон недоступен... Проверьте Профиль → Аудио.»
  - Новый endpoint `PUT /api/profile/audio-device` (отделён от
    `/api/profile/settings`, чтобы не задевать retention-поля).
  - Локализация RU + EN (15 ключей × 2). **Файлов:** 11.

### Documented
- **KI-146 (Planned, v1.13.x)**: device picker — улучшить fallback и
  дедупликацию label. Зафиксировано 2 наблюдения при smoke KI-145:
  (1) fallback после `OverconstrainedError` через `getUserMedia({ audio })`
  может снова вернуть Steam Streaming (нулевой сигнал); (2) Chrome
  показывает одно физическое устройство как 3 разных `deviceId` с префиксами
  «Микрофон (X)» / «По умолчанию — Микрофон (X)» / «Оборудование — Микрофон (X)».
  Оценка: ~1 ч.

### Fixed
- **v1.13.1-fix7 (KI-140-fix)**: Chrome может выбрать по умолчанию
  **виртуальное** audio-устройство (Steam Streaming Microphone / VB-Cable /
  OBS Virtual Audio / VoiceMeeter) — оно возвращает валидный
  `MediaStreamTrack`, но **все сэмплы = 0** (`maxAbs=0.000000`).
  Yandex Browser ведёт себя иначе — тот же код работал.
  **Fix:** `speech.js` логирует `label` + `deviceId` при старте записи;
  сообщение об ошибке при нулевом сигнале содержит имя устройства + ссылку
  на `chrome://settings/content/microphone`. **KI-144** — Documented.

- **v1.13.1-fix6 (KI-140-fix)**: Chrome (Chromium) pruning-ит
  `ScriptProcessorNode` даже с прямым подключением к `destination` —
  `ScriptProcessorNode` даже с прямым подключением к `destination` —
  `onaudioprocess` фирес, но `inputBuffer` занулён. Yandex Browser
  ведёт себя мягче (реально тянет данные).
  **Fix:** основной путь — `MediaStreamTrackProcessor` (WebCodecs API,
  Chromium 94+, 2021). Читает PCM из `MediaStreamTrack` напрямую, без
  Web Audio. Web Audio + ScriptProcessorNode — fallback для Firefox < 128
  / Safari / старого Chromium. Плюс диагностика `track.getSettings()`
  (`muted`, `enabled`). **Файл:** `speech.js` (полная замена).

- **v1.13.1-fix5 (KI-140-fix)**: Chromium pruning-ит граф
  `processor → MediaStreamAudioDestinationNode` без читателя `.stream` —
  `onaudioprocess` не вызывается реально (все сэмплы = 0). Yandex
  Browser ведёт себя мягче и всё равно тянет данные.
  **Fix:** `_processor.connect(_audioCtx.destination)` вместо sink'а —
  ScriptProcessor по умолчанию не копирует input → output, поэтому на
  выходе тишина (нет feedback), но граф живой. Плюс явное
  `outputBuffer.fill(0)` (защита от auto-copy).
  **Проверено:** Yandex ✅, Chrome ✅ (после фикса). **Файл:** `speech.js`.

- **v1.13.1-fix5 (KI-140-fix)**: Chromium pruning-ит аудио-граф до
  `GainNode.gain = 0` → `ScriptProcessorNode.onaudioprocess` фирес,
  но `inputBuffer` пустой (все сэмплы = 0). WAV получался валидного
  размера (252 KB), но с нулями → Whisper галлюцинировал.
  **Fix:** `GainNode(gain=0) → destination` → `MediaStreamAudioDestinationNode`
  (sink, не выводящий звук в колонки, но держащий граф живым). Плюс
  `await _audioCtx.resume()` (autoplay policy) + диагностический лог
  `maxAbs` в первых 3 чанках + ранний выход при `RMS < 1e-5` (защита
  от галлюцинаций на нулевом входе). **Файл:** `speech.js`.

- **v1.13.1-fix4 (KI-140-fix)**: `decodeAudioData` в Chromium (Chrome /
  Yandex Browser) иногда возвращает AudioBuffer с занулёнными каналами
  для WebM/Opus от MediaRecorder — duration и sampleRate корректные,
  но все сэмплы = 0. Whisper на таком входе выдаёт классические
  галлюцинации: «Редактор субтитров А.Семкин Корректор А.Егорова»,
  «[музыка]», «[шум]».
  **Fix:** переход с `MediaRecorder` + `decodeAudioData` на прямой
  захват PCM через `AudioContext` + `ScriptProcessorNode`. Плюс
  peak-normalization (boost тихих записей ×N до peak 0.7, N ≤ 10).
  **Файл:** `speech.js` (полная замена).

---

## [1.13.0] — 2026-10-04

**Speech Recognition — офлайн-распознавание речи в Chat UI (KI-140).**
Офлайн-распознавание речи через **Whisper.net** (whisper.cpp bindings, MIT).
Кнопка 🎤 в `.chat-input-box` (слева от 📎) → MediaRecorder (WebM/Opus) →
WAV 16 kHz mono (Web Audio API + `OfflineAudioContext`) → `POST /api/speech/transcribe`
→ Whisper.net (Singleton, ленивая загрузка модели `ggml-base.bin` ~142 MB).

**Вся обработка — локально**, аудио не покидает сервер. Работает в РФ без VPN.
Приватность: аудио не отправляется в облако (в отличие от Web Speech API / OpenAI Whisper API).

**Тесты:** 1011 → **1016** (+5: `SpeechControllerTests`).

### Added
- **DESIGN v1.13 — Распознавание речи (KI-140, Draft)**:
  `docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md`. Офлайн-распознавание
  речи в чате через **Whisper.net** (whisper.cpp bindings, MIT). Локальная
  модель `ggml-base.bin` (~142 MB). Клиент кодирует WAV 16 kHz mono через
  Web Audio API + MediaRecorder. Без Python / ffmpeg / внешних CLI.
  Приватность: аудио не покидает сервер. Работает в РФ без VPN.
  План Ф1-Ф3 (MVP, ~3 ч): Backend (`SpeechController` + Singleton
  `WhisperNetTranscriptionService`) → Frontend (`speech.js` + кнопка 🎤)
  → Скрипт скачивания + smoke. Фаза 4 (опционально): hotkey, VAD,
  streaming, GPU. **KI-140** → Planned (v1.13.0). Сводка: 92 → 93 KI.

- **Speech Recognition — Whisper.net (KI-140, Ф1–Ф3.6)**: офлайн-распознавание
  речи в Chat UI. Кнопка 🎤 в `.chat-input-box` (слева от 📎) → MediaRecorder →
  WAV 16 kHz mono (Web Audio API + `OfflineAudioContext`) → `POST /api/speech/transcribe`
  → Whisper.net (Singleton, ленивая загрузка модели `ggml-base.bin` ~142 MB).
  - **Ф1** (`cc02339`): backend — `ISpeechRecognitionService` +
    `WhisperNetTranscriptionService` + `SpeechController` + DI + 2 config-файла.
  - **Ф2** (`ecc2628`): frontend — `speech.js` + кнопка 🎤 + CSS (idle/recording/
    transcribing/error) + 7 ключей `.resx` (RU+EN).
  - **Ф3** (`ca6da86`): скрипт `scripts/setup/download-whisper-model.ps1`
    (tiny|base|small|medium|large-v3) + `.gitignore`.
  - **Ф3.5** (`8867c96`): fix `[BLANK_AUDIO]` — фильтр 20+ служебных маркеров
    Whisper + `NoSpeechThreshold=0.8` + `TryReadWavDurationMs()` (парсинг
    WAV-заголовка — закрывает техдолг § 11.3 DESIGN).
  - **Ф3.6**: `SpeechControllerTests` (8 unit-тестов:
    Disabled / NullFile / ZeroLength / TooLarge / Valid / EmptyResult /
    ModelNotFound / Cancelled).
  - **Приватность:** аудио не покидает сервер, работает в РФ без VPN.
  - **Docs-only доработки:** DESIGN_SPEECH_RECOGNITION.md v1.13.0 (7 правок
    после ревью — OfflineAudioContext, auto-stop 60 сек, RequestSizeLimit 20 MB,
    Language auto/ru, VAD priority).
  - **KI-140** → Fixed (v1.13.0).

### Changed
- **DESIGN v1.13 (KI-140) — уточнения после ревью (2026-10-03)**:
  - **Ресемплинг WAV** — `OfflineAudioContext` вместо `AudioContext({sampleRate:16000})`
    (Chrome игнорирует hint; native rate обычно 48 kHz). § 4.2.
  - **Auto-stop 60 сек** — enforced на клиенте (`MAX_RECORDING_MS` в `speech.js`,
    синхронизировано с `Speech:MaxAudioSeconds`). § 4.2, § 6.
  - **`RequestSizeLimit`** — 20 MB hard cap (было 11 MB); реальный лимит —
    `Speech:MaxFileSizeBytes = 10 MB` (валидация в контроллере). § 3.5.
  - **Language** — `"auto"` в `appsettings.Development.json`, `"ru"` в prod.
    § 3.2.
  - **VAD** (Phase 4) — на клиенте через `AnalyserNode`, не требует backend.
    Приоритет Ф4: VAD > hotkey > GPU > streaming. § 8.4.
  - § 11 переименован в «Принятые решения и отложенные вопросы»:
    7 решений + 4 отложенных + 3 пункта технического долга.

### Fixed
- **v1.13.0 (KI-140, Ф3.5)**: `[BLANK_AUDIO]` — Whisper на тишине/шуме
  выдавал служебные маркеры как текст. Фильтр 20+ маркеров
  (`[BLANK_AUDIO]`, `[MUSIC]`, `[SOUND]`, …) + `NoSpeechThreshold = 0.8`
  (было whisper.cpp default 0.6) + `TryReadWavDurationMs()` (парсинг
  WAV-заголовка; `MinAudioDurationMs = 300` — ранний выход на коротких
  записях). Закрывает техдолг § 11.3 DESIGN.

---

## [1.12.0] — 2026-10-04

**Vision Agent (KI-131, MVP: LocalHarness + vision_agent).**
Управление компьютером через визуальные подсказки (Computer Use pattern):
скриншот → анализ UI Vision-моделью → действие мышью/клавиатурой.
Оркестрация трёх моделей (Chat LLM + Planner LLM + Vision LLM), три
backend'а (Local / Sandbox / RemoteVnc), 5 уровней безопасности
(изоляция + whitelist процессов + on-screen indicator + approval/бюджет
+ валидатор/audit).

**Scope MVP v1.12.0:** `LocalHarnessVisionBackend` + `vision_agent` в Chat
(**16 инструментов**, было 15).

**Отложено в v1.12.x:** WPF overlay — реальный on-screen indicator
(**KI-142**), Sandbox backend (Ф3), RemoteVnc backend (Ф4).

**Тесты:** 654 → **1011** (+357, 5 Skip — реальные внешние провайдеры +
Whisper).

### Added
- **DESIGN v1.12 — Vision Agent (KI-131, Ф7)**: `vision_agent` — top-level ITool в Chat.
  - **`VisionAgentTool : ITool`** (`Implementation/Tools/VisionAgent/`) — 12 actions:
    - `run_task` — полный loop через `IVisionAgentService` (approval).
    - `describe` / `screenshot` — read-only (screenshot + Vision LLM).
    - `click` / `double_click` / `right_click` / `move_mouse` — мышь;
      target резолвится через describe, x/y — напрямую.
    - `type` / `press_key` / `hotkey` / `scroll` / `wait` — клавиатура / пауза.
  - **Per-action approval** (KI-101): read-only (`describe`, `screenshot`,
    `move_mouse`, `scroll`, `wait`) — без approval; mutation + `run_task` — с approval.
  - **DI:** `services.AddScoped<ITool, VisionAgentTool>()` в `RegisterVisionAgentTools`
    (при `VisionAgent:Enabled = true`).
  - **RULES § 4.44:** `vision_agent` добавлен в `allowedNames` в
    `ChatStreamService.StreamAsync`. Chat видит **16 инструментов** (было 15).
  - **Безопасность:** валидация через `IVisionActionValidator` (blocked keys F12,
    Ctrl+Alt+Del, Alt+Tab), `target` сверяется с `ui_elements`, координаты — из
    `describe` или x/y.
  - **Тесты:** `VisionAgentToolTests` (+~24: structural, approval, handlers).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф8 (интеграционные тесты) / Ф3/Ф4.

- **DESIGN v1.12 — Vision Agent (KI-131, Ф6.9)**: тесты VisionAgentService.
  - **Fakes/**: `FakeVisionBackend` (управляемые ошибки + call log),
    `FakeVisionLlmClient` (queue responses), `FakePlannerLlmClient`
    (queue actions + userId tracking), `FakeVisionScreenshotStore`.
  - **`VisionAgentServiceTests`** (+15 кейсов): null/empty request,
    rate-limit exceeded, happy done, fail action, maxSteps exhausted,
    screenshot/describe/plan failures, backend action fail (step error,
    loop продолжается), target not found (validator reject), все 11 actions
    forwarded, URL → OpenAsync, screenshots saved each step, external
    cancellation, userId propagation.
  - **🎉 Ф6 закрыта** (кроме 6.7 overlay — отдельный под-этап позже).
    Loop `screenshot → describe → plan → validate → act` работает
    end-to-end с rate-limit + timeout + retention + screenshots.
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф7 (`VisionAgentTool` → Chat).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф6.8)**: overlay launcher (no-op).
  - **`IVisionOverlayLauncher.cs`** (в одном файле с `IVisionOverlayHandle`):
    `IsAvailable` + `StartAsync(taskId, maxSteps, ct)` → handle.
    Handle: `UpdateProgress(step, maxSteps, action)` + `SetFinalStatus(summary,
    success)` + `IDisposable`.
  - **`NoopVisionOverlayLauncher.cs`** (Singleton, `IDisposable`-handle): сейчас
    `IsAvailable = false`, все методы — no-op. Используется до реализации
    WPF overlay'я в **Ф6.7** (отдельный проект `IIChatTools.VisionOverlay`).
  - **`VisionAgentService.RunTaskAsync`**: стартует overlay при
    `_overlayLauncher.IsAvailable && Backend.Local.ShowOverlay`, эмитит
    `UpdateProgress(step, maxSteps, "screenshot")` перед каждым скриншотом,
    `SetFinalStatus("Готово"/"Ошибка", success)` + `Dispose()` в `finally`.
    Ошибки overlay логируются, но не прерывают loop.
  - **DI:** `services.AddSingleton<IVisionOverlayLauncher, NoopVisionOverlayLauncher>()`.
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф6.9 (тесты сервиса).
  - **Отложено:** Ф6.7 (реальный WPF overlay) — отдельный проект, перед Ф7.

- **DESIGN v1.12 — Vision Agent (KI-131, Ф6.6)**: очистка скриншотов (TTL).
  - **`VisionScreenshotCleanupResult.cs`** (DTO): DeletedTaskFolders / DeletedFiles
    / FreedBytes / ErrorCount / DurationMs.
  - **`IVisionScreenshotCleaner.cs`**: `CleanupAsync(ct)`.
  - **`VisionScreenshotCleaner.cs`** (Scoped): обходит
    <c>{userWorkspace}/screenshots/*</c> всех пользователей (userIds из
    <c>AppDbContext.Users</c>), удаляет папки с <c>LastWriteTimeUtc &lt; now -
    RetentionHours</c>. Retention clamp [1, 168] ч. Ошибки на уровне одной
    папки не прерывают остальные. Метрики: папки, файлы, байты, ошибки.
  - **`VisionRetentionService.cs`** (HostedService): периодический вызов
    <c>IVisionScreenshotCleaner</c> через <c>IServiceScopeFactory</c>. Первый
    прогон через 5 мин, далее каждые 30 мин. Отключается при
    <c>VisionAgent:Enabled = false</c> или <c>Privacy:SaveToWorkspace = false</c>.
    По образцу <c>AuditRetentionService</c> / <c>ChatRetentionService</c>.
  - **DI:** <c>AddScoped&lt;IVisionScreenshotCleaner, VisionScreenshotCleaner&gt;</c>
    + <c>AddHostedService&lt;VisionRetentionService&gt;</c>.
  - **Тесты:** <c>VisionScreenshotCleanerTests</c> (+~8 кейсов: old/fresh/boundary,
    multiple folders, multiple users, save-disabled, no folder, no users).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф6.8 (launcher stub).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф6.5)**: сохранение скриншотов.
  - **`IVisionScreenshotStore.cs`**: `SaveAsync(userId, taskId, stepIndex, png, ct)`
    → относительный путь; `GetTaskDirectoryAsync(userId, taskId)`.
  - **`VisionScreenshotStore.cs`** (Scoped, `IWorkspaceResolver` + `IOptions`):
    сохраняет PNG в <c>{workspace}/screenshots/{taskId}/step-NNN.png</c>.
    Все пути — через `PathHelper.TryGetSafeFullPath` (RULES § 1.9). taskId
    нормализуется regex-ом (`[a-zA-Z0-9_-]`, остальное → `_`). Резолвинг
    workspace — через `IWorkspaceResolver.GetWorkspacePathAsync(userId)`.
    Ошибки сохранения логируются, но не пробрасываются (best-effort).
  - **`VisionAgentService.RunTaskAsync`**: после успешного `ScreenshotAsync`
    каждого шага вызывается `_screenshotStore.SaveAsync`. `result.FinalScreenshotPath`
    обновляется на путь последнего успешно сохранённого PNG. Ошибки сохранения
    — Warning, loop продолжается.
  - **DI:** `services.AddScoped<IVisionScreenshotStore, VisionScreenshotStore>()`.
  - **Тесты:** `VisionScreenshotStoreTests` (+~10 кейсов).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф6.6 (`VisionRetentionService`).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф6.4)**: InMemoryVisionRateLimiter.
  - **`VisionRateLimitResult.cs`** (DTO): `Allowed` / `RetryAfterSeconds` /
    `RemainingInWindow`.
  - **`IVisionRateLimiter.cs`**: `TryAcquire(userId)` → `VisionRateLimitResult`.
  - **`InMemoryVisionRateLimiter.cs`** (Singleton, `IDisposable`): fixed-window
    `MaxTasksPerUserPer5Min` (default 5, clamp [1, 100]). `ConcurrentDictionary` +
    per-user `lock`. Cleanup Timer каждые 5 мин (удаляет окна без активности
    > 10 мин, по образцу KI-043). TryAcquire инкрементирует счётчик только
    при успехе (отказ не тратит слот).
  - **`VisionAgentService.RunTaskAsync`**: +rate-check в начале (после
    валидации request, до main loop). При отказе — `VisionTaskResultDto`
    с `Error = "Превышен лимит запусков Vision Agent (5 задач / 5 минут).
    Попробуйте через N с."` (без throw).
  - **DI:** `services.AddSingleton<IVisionRateLimiter, InMemoryVisionRateLimiter>()`
    в `RegisterVisionAgentTools`.
  - **Тесты:** `InMemoryVisionRateLimiterTests` (+~9 кейсов: first/within/over,
    per-user isolation, invalid userId, decreasing remaining, clamps, dispose).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф6.5 (сохранение скриншотов).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф6.3)**: timeout `MaxTaskSeconds`.
  - **`VisionAgentService.RunTaskAsync`**: main loop обёрнут в
    `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` +
    `CancelAfter(Limits.MaxTaskSeconds)` (RULES § 4.48 — не `HttpClient.Timeout`).
    Все await внутри loop (OpenAsync, ScreenshotAsync, DescribeAsync,
    PlanNextAsync, ExecuteActionAsync, Task.Delay) получают `timeoutCts.Token`.
  - **Различение отмены** через `catch when`:
    - **Timeout** (внутренний CTS сработал, внешний не отменён) →
      `result.Error = "Превышен лимит времени задачи (N с)."`, **не throw**
      (плановая остановка).
    - **Внешняя отмена** (Stop в чате / HTTP abort) → throw (как было).
  - **Log:** при старте задачи — `timeout=Ns`; при timeout — Warning с
    реальным числом шагов.
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф6.4 (`InMemoryVisionRateLimiter`).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф6.2)**: VisionAgentService loop.
  - **VisionAgentService.cs** (Scoped, `IVisionAgentService`): оркестратор
    loop'а `screenshot → describe → plan → validate → act → repeat`.
    - Main loop до `MaxSteps` (clamp из `request.MaxSteps` или
      `Limits.MaxSteps`).
    - Стартовый `backend.OpenAsync(url)` при заданном `url`.
    - На каждом шаге: `ScreenshotAsync` → `DescribeAsync` (VL) →
      `PlanNextAsync` (Planner, с `userId`) → `Validate` (валидатор) →
      `ExecuteActionAsync` (backend).
    - `done` → `Success = true` + `Summary`; `fail` → `Success = false`
      + `Error`.
    - Ошибки валидации → шаг с `Error` (loop продолжается, LLM перепланирует).
    - Ошибки backend'а → шаг с `Error` (loop продолжается).
    - `OperationCanceledException` → `throw` (отмена пробрасывается).
    - Заполняет `VisionTaskResultDto` (Steps, Summary, Error,
      TotalDurationMs).
  - **`ResolveCoordinates`** (private static): если задан `target` —
    ищет элемент в `screen.UiElements` и берёт `Center.X/Y` (fallback —
    `Bounds`). Если target нет — использует `x` / `y`. Это единственный
    способ «перевести» семантические id LLM в пиксели backend'а.
  - **`ExecuteActionAsync`** (private static): switch по `actionType` →
    вызов соответствующего метода `IVisionBackend` (11 действий).
  - **DI:** `services.AddScoped<IVisionAgentService, VisionAgentService>()`
    в `RegisterVisionAgentTools`.
  - **Отложено (следующие подшаги Ф6):** timeout `MaxTaskSeconds` (Ф6.3),
    rate limiter (Ф6.4), сохранение скриншотов (Ф6.5), retention (Ф6.6),
    overlay (Ф6.7), тесты сервиса (Ф6.9).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф6.3 (timeout + MaxSteps).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф6.1)**: VisionActionValidator.
  - **VisionActionValidator.cs** (Singleton, `IVisionActionValidator`):
    7 правил валидации перед backend-выполнением:
    1. Null / пустой action → reject.
    2. Whitelist 11 actions (совпадает с `VisionActionParser.KnownActions`).
    3. `done` / `fail` — без доп. проверок.
    4. `type` — clamp `Text` до `ActionValidation.MaxTextLength`.
    5. `scroll` — clamp `DeltaY` до `ActionValidation.MaxScrollDelta`.
    6. `press_key` — блок клавиш из `BlockedKeys`.
    7. `hotkey` — блок одиночных клавиш из `BlockedKeys` + комбинаций из
       `BlockedHotkeys` (нормализация: sorted lowercase через «+», порядок
       не важен).
    8. `target` (если задан) — существование в `screen.ui_elements[].id`.
    9. `x` / `y` — неотрицательность (верхняя граница — backend'ом,
       `VisionMouseCoordinates`).
  - **Sanitization:** если был clamp длины / deltaY — возвращает
    `Success = true` + `SanitizedAction` (с обрезанным / clamped значением).
    Backend должен использовать `SanitizedAction`, если он не null.
  - **Тесты:** `VisionActionValidatorTests` (+~22 кейса).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф6.2 (`VisionAgentService` loop).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф5.4)**: Auto-клиенты с fallback chain.
  - **AutoVisionClient.cs** (Singleton, `IVisionLlmClient`): перебор
    `VisionLlm.FallbackChain`. Резолв по `Func<IVisionLlmClient>` (ADR-002),
    lazy-кеш в поле. Критерий успеха: непустое `Description` или непустой
    `UiElements`. `NotSupportedException` от External → следующий в цепочке.
    Все не-ready / все упали → `InvalidOperationException`.
  - **AutoPlannerClient.cs** (Singleton, `IPlannerLlmClient`): аналогично,
    но критерий успеха — `action != "fail"`. Все fail → возвращает последний
    fail. Все не-ready / все упали → `fail`-действие с reason.
  - **`Func<IVisionLlmClient>` / `Func<IPlannerLlmClient>`** вместо конкретных
    типов — паттерн ADR-002, позволяет тестировать Auto* через fake-фабрики
    (Moq без IHttpClientFactory mock).
  - **DI:** в `RegisterVisionAgentTools` регистрируются все 3 уровня
    (`LmStudioVisionClient` / `ExternalVisionClient` / `AutoVisionClient`).
    Финальный `IVisionLlmClient` / `IPlannerLlmClient` резолвится через
    `switch` по `VisionAgent:{VisionLlm,PlannerLlm}:Provider` —
    `"lmstudio"` (default) / `"external"` / `"auto"`.
  - **Тесты:** `AutoVisionClientTests` (+~11) + `AutoPlannerClientTests` (+~11).
  - **🎉 Ф5 закрыта.** 4 клиента (Vision+Planner × LmStudio+External) + 2 Auto*
    с fallback. Vision Agent готов к Ф6 (loop + overlay).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф6 (`VisionAgentService` loop).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф5.3)**: External Planner + Vision skeleton.
  - **VisionPlannerPayloadBuilder.cs** (public static): общий helper для
    формирования user-message (task / history / screen / plan) — устраняет
    дублирование между LmStudio и External Planner-клиентами.
  - **ExternalPlannerClient.cs** (Singleton, `IPlannerLlmClient`):
    text-only вызов внешнего провайдера через `IExternalLlmClient`
    (v1.8.1, KI-109). Провайдер резолвится из `PlannerLlm.FallbackChain`
    (первый `external:{name}`), иначе — `IExternalProviderRegistry.DefaultProvider`.
    Парсинг ответа — через общий `VisionActionParser`. `IncludeContext = false`
    (privacy). `userId` передаётся в `CompleteAsync` (budget tracker KI-109).
  - **ExternalVisionClient.cs** (Singleton, `IVisionLlmClient`, **скелет**):
    `IsReady = false`, `DescribeAsync` бросает `NotSupportedException`.
    Причина: `IExternalLlmClient` (v1.8.1) — text-only; multimodal требует
    расширения ядра KI-109 тремя разными формат-билдерами (OpenAI / Claude /
    Gemini). Заведён **KI-141** (Planned, v1.12.x).
  - **`IPlannerLlmClient.PlanNextAsync`** — добавлен опциональный
    `int userId = 0` перед `CancellationToken` (для External budget tracker).
    `LmStudioPlannerClient` принимает, игнорирует (LM Studio — локально,
    без budget). Breaking change для Ф1, но затронут только 1 имплементатор.
  - **DI:** регистрация через конкретные классы
    (`AddSingleton<LmStudioVisionClient>` / `AddSingleton<ExternalVisionClient>`
    / `AddSingleton<LmStudioPlannerClient>` / `AddSingleton<ExternalPlannerClient>`)
    + фабрика для `IVisionLlmClient` / `IPlannerLlmClient` (по умолчанию LmStudio).
    Выбор Provider (lmstudio/external/auto) — в Ф5.4 (Auto*).
  - **Тесты:** `ExternalVisionClientTests` (+2), `ExternalPlannerClientTests` (+6).
  - **KI-141** (Planned, v1.12.x) — External VL providers (multimodal images).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф5.4 (Auto* fallback chain).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф5.2)**: LmStudioPlannerClient
  + VisionActionParser.
  - **VisionActionParser.cs** (public static, устойчивый парсер):
    markdown-обёртки, case-insensitive, отрезание текста до/после JSON.
    Whitelist из 11 actions (`click`, `double_click`, `right_click`,
    `move_mouse`, `type`, `press_key`, `hotkey`, `scroll`, `wait`,
    `done`, `fail`). Нормализация action к lowercase. Извлечение полей:
    `target` / `x` / `y` / `text` / `key` / `keys[]` / `deltaY` / `reason`.
    При любой ошибке (невалидный JSON, отсутствие `action`, неизвестный
    action) — fallback: `action = "fail"` + reason с деталями.
  - **LmStudioPlannerClient.cs** (Singleton, `IPlannerLlmClient`):
    text-only POST к LM Studio `/v1/chat/completions`. System-prompt =
    `VisionSystemPrompt.PlannerPlanNext`. User-message — JSON c полями
    `task`, `history`, `screen`, `plan` (camelCase, без null). `history`
    обрезается до `PlannerLlmOptions.MaxHistorySteps` (default 20) —
    защита от переполнения контекста. Timeout через
    `CancellationTokenSource.CancelAfter` (RULES § 4.48).
  - **Тесты:** `VisionActionParserTests` (+~19 кейсов: clean для каждого
    action / coords / markdown / case-insensitive / text-before-after /
    unknown action / missing action / broken JSON / null/empty / free text).
  - **DI:** `services.AddSingleton<IPlannerLlmClient, LmStudioPlannerClient>()`
    внутри `RegisterVisionAgentTools`.
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф5.3
    (`ExternalVisionClient` + `ExternalPlannerClient` через `IExternalLlmClient`).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф5.1)**: LmStudioVisionClient
  + ScreenDescriptionParser + VisionSystemPrompt.
  - **VisionSystemPrompt.cs** (public static): 2 константы —
    `VisionUiDescribe` (описать UI в JSON формата `ScreenDescriptionDto`)
    + `PlannerPlanNext` (выбрать действие в JSON формата `VisionActionDto`).
  - **ScreenDescriptionParser.cs** (public static, устойчивый парсер):
    снимает markdown-обёртки (` ```json ` / ` ``` `), извлекает JSON-объект
    (текст до/после игнорируется), читает поля case-insensitive
    (`description` / `Description` / `DESCRIPTION`), пропускает элементы без
    `id`/`type`, безопасен к BOM, полностью невалидному JSON. При полном
    провале — fallback: `Description = сырой текст`, `UiElements = []`.
  - **LmStudioVisionClient.cs** (Singleton, `IVisionLlmClient`):
    multimodal POST к LM Studio `/v1/chat/completions` — content-массив
    `[text, image_url]`, PNG в base64 data-URL. Timeout через
    `CancellationTokenSource.CancelAfter` (RULES § 4.48), не через
    `HttpClient.Timeout`. Парсинг ответа: `choices[0].message.content`
    (строка или массив текстовых блоков). На пустой content — возвращает
    пустой DTO.
  - **Тесты:** `ScreenDescriptionParserTests` (+~16 кейсов: clean / markdown /
    text before/after / case-insensitive / missing fields / invalid elements /
    free text / broken JSON / null / BOM).
  - **DI:** `services.AddSingleton<IVisionLlmClient, LmStudioVisionClient>()`
    внутри `RegisterVisionAgentTools`.
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф5.2 (PlannerLlmClient).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф2.9)**: whitelist процессов.
  - **Win32Interop.cs:** +`GetForegroundWindow()`,
    +`GetWindowThreadProcessId(hWnd, out pid)`.
  - **VisionProcessWhitelistChecker.cs** (public static, кроссплатформенный):
    `IsProcessAllowed(processName, allowedList, out error)` — сверка с
    whitelist процессов. Нормализация: trim + strip `.exe` +
    case-insensitive. Пустой whitelist = allow all (fail-safe UX).
    `NormalizeProcessName(name)` — отдельная чистая функция.
  - **LocalHarnessVisionBackend:** `EnsureForegroundProcessAllowed()` —
    вызывается <b>перед каждым</b> mouse / keyboard действием
    (`ClickAsync`, `DoubleClickAsync`, `RightClickAsync`, `MoveMouseAsync`,
    `TypeAsync`, `PressKeyAsync`, `HotkeyAsync`, `ScrollAsync`).
    НЕ вызывается перед `ScreenshotAsync` (read-only). Bypass при
    `Backend:Local:AllowNonBrowserProcesses = true` (dev-режим, DESIGN § 5.1).
    Пропускается при `GetForegroundWindow = 0` или упавшем
    `Process.GetProcessById`.
  - **Тесты:** `VisionProcessWhitelistCheckerTests` (+~26 кейсов:
    known / .exe / mixed-case / unknown / null whitelist / empty whitelist /
    null processName / normalize).
  - **🎉 Ф2 закрыта:** `IVisionBackend` полностью реализован в
    `LocalHarnessVisionBackend` со всеми защитами (whitelist доменов +
    процессов, DPI-координаты, Unicode-ввод, downscale скриншотов).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф3
    (`SandboxVisionBackend`, ~10 ч).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф2.8)**: downscale PNG.
  - **VisionImageResizer.cs** (public static):
    - `CalculateTargetSize(origW, origH, maxW, maxH)` — pure function,
      кроссплатформенная. Пропорциональный downscale (сохранение aspect ratio),
      без увеличения. Тестируется в CI ubuntu-latest.
    - `Resize(pngBytes, maxW, maxH)` — `[SupportedOSPlatform("windows")]`,
      `InterpolationMode.HighQualityBicubic`. Если размеры не меняются —
      возвращает тот же массив (0 копий).
  - **`LocalHarnessVisionBackend.ScreenshotAsync`** — downscale вызывается
    после захвата GDI, до проверки `MaxScreenshotBytes`. Экономит трафик
    (PNG 4K ≈ 2-3 MB → 1024×576 ≈ 200-300 KB) и упрощает VL-модели (не тратит
    токены на обработку 4K).
  - **Тесты:** `VisionImageResizerTests` (+~9 кейсов: CalculateTargetSize —
    small/exact/wide/tall/square/ultrawide/invalid/extreme-small; Resize —
    runtime-skip на Linux).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф2.9 (whitelist процессов
    через `GetForegroundWindow` + `GetWindowThreadProcessId`).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф2.7)**: прокрутка колеса мыши.
  - **VisionScrollHelper.cs** (public static): `Clamp(deltaY, maxAbs)` +
    `ToWheelMouseData(deltaY)` (инверсия знака — Win32 семантика
    `MOUSEEVENTF_WHEEL` обратна user-facing deltaY). Единицы — в
    `WHEEL_DELTA` (120 = один щелчок). Отдельно для тестов.
  - **LocalHarnessVisionBackend:** реализован `ScrollAsync` — clamp к
    `ActionValidation.MaxScrollDelta` (defense in depth), конвертация знака,
    `SendInput` с `MOUSEEVENTF_WHEEL`. `deltaY = 0` → no-op.
  - **Win32Interop:** +`MOUSEEVENTF_WHEEL = 0x0800`, +`WHEEL_DELTA = 120`.
  - **Тесты:** `VisionScrollHelperTests` (+~12 кейсов: clamp границ / fallback
    к 1 / инверсия знака / инволюция).
  - **🎉 `IVisionBackend` полностью реализован в `LocalHarnessVisionBackend`**
    (11 методов + DisposeAsync). Остались downscale (Ф2.8) и whitelist
    процессов (Ф2.9).
  - **KI-131** — In Progress (v1.12.0).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф2.6)**: клавиатура через SendInput.
  - **VisionKeyMapper.cs** (public static): маппинг имён клавиш → VK-коды.
    Именованные (`Enter`/`Return`, `Escape`/`Esc`, `Delete`/`Del`,
    `PageUp`/`PgUp`, стрелки, `Home`, `End`, ...) + F1-F12 + A-Z + 0-9.
    Модификаторы (`Ctrl`/`Control`, `Alt`, `Shift`, `Win`/`Meta`/`Cmd`)
    с L/R-вариантами. Case-insensitive. `IsModifier`-флаг.
  - **LocalHarnessVisionBackend:** реализованы:
    - `TypeAsync` — Unicode через `KEYEVENTF_UNICODE` (кириллица / эмодзи /
      surrogate pairs). `\n` конвертируется в VK_RETURN (Down+Up).
    - `PressKeyAsync` — VK-код через `VisionKeyMapper.GetVirtualKey`,
      Down + Up. `ArgumentException` при неизвестном имени.
    - `HotkeyAsync` — `["Ctrl", "C"]` → Down(mods) → Down+Up(final) →
      Up(mods в обратном порядке). Один элемент = обычное нажатие.
  - **Win32Interop:** +`KEYEVENTF_KEYUP = 0x0002`, +`KEYEVENTF_UNICODE = 0x0004`.
  - **Тесты:** `VisionKeyMapperTests` (+~18 кейсов: named / letters / F1-F12 /
    case-insensitive / unknown / null / modifiers / IsModifier).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф2.7 (scroll через
    `MOUSEEVENTF_WHEEL` + `WaitAsync` — уже готов).

### Fixed
- **v1.12.0 (KI-131, Ф2.5-fix)**: `LocalHarnessVisionBackend` — 7 warnings
  CA1416 (Windows-only API: `Bitmap`, `Graphics.FromImage`,
  `Graphics.CopyFromScreen`, `ImageFormat.Png`, `PixelFormat.Format32bppArgb`,
  `CopyPixelOperation.SourceCopy`). Причина: `System.Drawing.Common` в plain
  `net10.0` без `-windows` TFM. Fix: `[SupportedOSPlatform("windows")]` на
  класс + локальный `#pragma warning disable CA1416` вокруг
  `RegisterVisionAgentTools` в `Startup.cs`. Каждый метод теперь не требует
  собственной проверки `IsOSPlatform`.

### Added
- **DESIGN v1.12 — Vision Agent (KI-131, Ф2.5)**: мышь через SendInput.
  - **Win32Interop.cs** (internal): P/Invoke `SendInput` + `GetSystemMetrics`,
    структуры `INPUT` / `MOUSEINPUT` / `KEYBDINPUT` / `HARDWAREINPUT` (union
    через `[FieldOffset(0)]`). Готово к переиспользованию в Ф2.6 (клавиатура).
  - **VisionMouseCoordinates.cs** (public static): `NormalizeToAbsolute(x, y,
    screenW, screenH)` — Win32 absolute coordinates 0..65535 для
    `MOUSEEVENTF_ABSOLUTE`. Валидация границ, отдельно для тестов.
  - **LocalHarnessVisionBackend:** реализованы `ClickAsync` (Move + LeftDown +
    LeftUp), `DoubleClickAsync` (два клика с паузой 50 мс), `RightClickAsync`
    (Move + RightDown + RightUp), `MoveMouseAsync` (только Move). Всё через
    `SendInput` (batch из 3 INPUT в одном вызове). Проверка результата
    `SendInput` с `GetLastWin32Error`.
  - **Тесты:** `VisionMouseCoordinatesTests` (+8 кейсов: углы / центр /
    4K / 1-pixel screen / границы / negative / outside).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф2.6 (клавиатура:
    `TypeAsync` Unicode + `PressKeyAsync` + `HotkeyAsync`).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф2.3-Ф2.4)**: whitelist + Chrome + screenshot.
  - **Ф2.3 (`OpenAsync`):** проверка домена через новый
    `VisionWhitelistValidator` (public static; поддержка exact + `*.wildcard`,
    DeniedDomains приоритетнее, AllowAnyDomain как «открытый режим»).
    Запуск Chrome/Edge через `BrowserLocator.Resolve` (переиспользуется из
    v1.1) с fresh-профилем в `%TEMP%\vision-profile-{instanceId}`,
    `--no-first-run`, `--disable-blink-features=AutomationControlled`,
    `--start-maximized`. Без `--headless` — окно видимое (управляем мышью).
  - **Ф2.4 (`ScreenshotAsync`):** GDI-захват всего экрана через
    `Graphics.CopyFromScreen`, PNG-кодирование, проверка лимита
    `MaxScreenshotBytes`. На Linux бросает `PlatformNotSupportedException`
    (`System.Drawing.Common` deprecated .NET 7+).
  - **Cleanup:** `DisposeAsync` → `CloseBrowser` (Kill entire tree + удаление
    временного профиля).
  - **Тесты:** `VisionWhitelistValidatorTests` (+12 кейсов: exact/wildcard/
    denied/allowAny/scheme/invalid/case-insensitive/null).
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф2.5 (мышь через SendInput).

- **DESIGN v1.12 — Vision Agent (KI-131, Ф2.1-Ф2.2)**: Local backend skeleton.
  - **Ф2.1 (deps):** `System.Drawing.Common 10.0.0` в `IIChatTools.Services.csproj`
    (GDI-скриншоты, plain `net10.0` — не ломает Linux-сборку Docker).
    **Отказ от** `SystemHarness.Core` / `SystemHarness.Windows` — TFM-конфликт
    NU1202 (`net10.0-windows10.0.19041` несовместим с plain `net10.0`).
  - **Ф2.2 (skeleton):** `LocalHarnessVisionBackend` реализует `IVisionBackend`
    (Ф1): `Name = "local-harness"` + 11 методов + `IAsyncDisposable`.
    Реализован только `WaitAsync` (простая пауза); остальные методы —
    `NotImplementedException` с указанием фазы (Ф2.3-Ф2.9).
    Регистрация в DI — `Startup.RegisterVisionAgentTools` (условно,
    `VisionAgent:Enabled = true`). Секция `VisionAgent` добавлена в
    `appsettings.json` (Enabled=false) и `appsettings.Development.json`
    (Enabled=true + минимальный набор опций для будущих smoke).
  - **DoD Ф2.2:** решение компилируется, DI-регистрация условная —
    при `Enabled=false` backend не в контейнере, Chat видит 15 инструментов
    как раньше.
  - **KI-131** — In Progress (v1.12.0). Следующая — Ф2.3 (`OpenAsync` +
    Chrome fresh profile + whitelist доменов).

- **DESIGN v1.12 — Vision Agent (KI-131, Фаза 1)**: контракты и DTO.
  `docs/development/v1.12/DESIGN_VISION_AGENT.md` v2.1 (Implemented, Фаза 1).
  16 файлов в `IIChatTools.Services/DTO/VisionAgent/` (11 DTO) +
  `IIChatTools.Services/Interfaces/` (5 интерфейсов):
  - **Опции:** `VisionAgentOptions` (+ 4 вложенных: Limits / Whitelist /
    ActionValidation / Privacy), `VisionBackendOptions` (+ 3 вложенных:
    Local / Sandbox / RemoteVnc), `VisionLlmOptions`, `PlannerLlmOptions`.
  - **Action/UI:** `VisionActionDto`, `VisionActionResult`,
    `UiElementDto` (+ 2 вложенных), `ScreenDescriptionDto`.
  - **Task/Step:** `VisionStepDto`, `VisionTaskRequest`, `VisionTaskResultDto`.
  - **Интерфейсы:** `IVisionBackend`, `IVisionLlmClient`,
    `IPlannerLlmClient`, `IVisionActionValidator`, `IVisionAgentService`.
  - **KI-131** → In Progress (v1.12.0). Следующая фаза — Ф2
    (`LocalHarnessVisionBackend`, ~8 ч).

- **v1.11.0 (KI-136)**: tool-сообщение **не сохранялось** в БД  при отмене SSE (F5 / Stop) — фактический root cause, не покрытый
  KI-134/135.
  **Причина:** `yield return ChatStreamEvent.ToolResult(...)` шёл **до**
  `AddMessageAsync`. При F5 SSE-соединение закрыто → controller попытался
  записать событие в aborted Response → `OperationCanceledException` →
  `IAsyncEnumerable` из `StreamAsync` disposed → код после `yield` **никогда
  не выполнялся**, tool-сообщение терялось полностью.
  **Fix:** сохранение tool-сообщения и `AddSourcesToAccumulator` —
  **до** `yield return ToolResult(...)`. После yield остаётся только
  `messages.Add(...)` (in-memory, безопасно).

- **v1.11.0 (KI-134)**: tool-сообщение **не сохранялось** в БД при
  отмене SSE-стрима (F5 / Stop).
  **Причина:** `ChatStreamService` вызывал `AddMessageAsync(...,
  cancellationToken)`. При отмене `cancellationToken` уже отменён →
  `OperationCanceledException` → tool-сообщение терялось.
  **Fix:** сохранение assistant с `tool_calls` и tool-сообщения —
  с `CancellationToken.None`. Иначе теряется вся история tool-call'а
  (не только для дебатов — для **любого** tool'а).
  Также убрано условие `toolResult.Success` при записи `debateSessionId`
  в `MetadataJson` — при отмене tool возвращает `Fail` с `Data.sessionId`.

- **v1.11.0 (KI-135)**: при отмене `code_agent_with_review` (F5 посреди
  feedback-блока или Stop) сессия завершалась как `Cancelled` в БД, но
  UI-блок не восстанавливался при F5.
  **Причина:** `CodeAgentWithReviewTool` возвращал
  `Fail("Операция отменена")` **без** `Data.sessionId` → маппинг
  `toolCallId → sessionId` пуст.
  **Fix:** возвращать `Data = { sessionId, cancelled = true }` даже в `Fail`.
  После F5 блок дебатов восстанавливается с вердиктом «❌ Отменено».

### Added
- **v1.11.0 (KI-129, Шаг 2 — frontend)**: восстановление блоков
  Actor-Critic при перезагрузке страницы (F5).
  - `chat.js:selectChat` — `prefillDebatesFromChatDetail()` заполняет
    `state.debates[sid]` из `chatDetail.debateSessions` + маппинг
    `toolCallId → sessionId` из tool-сообщений (`debateSessionByToolCallId`).
  - `chat.js:renderMessage` — для assistant-сообщений с `toolCallsJson`,
    содержащим `code_agent_with_review`, вставляет блок дебатов через
    `renderDebateContainerInner(sid)` (формат — как в live-режиме).
  - Orphaned-сессии (`InProgress` / `Pending` без `completed`) —
    бейдж «⚠️ Прервано», **без** feedback-UI.
  - Локализация: новый ключ `ChatDebateOrphaned` (RU + EN) + `data-label-debate-orphaned`
    на `#chat-messages`.
  - Обновлён `state.debateSessionByToolCallId` в `showEmptyState` / `selectChat`.

### Fixed
- **v1.11.0 (KI-132)**: `code_agent_with_review` возвращал раздутый
  `ToolResult.Data` с полным `rounds[]` (каждый `actorOutput` — код
  на ~1500-2500 токенов). При 2-3 раундах tool_result раздувался до
  ~5000+ токенов → на следующей итерации Chat LLM контекст превышал
  8192 (или 16384) → LM Studio возвращал 400.
  **Fix:** убрано поле `rounds[]` из `ToolResult.Data`. История раундов
  остаётся доступной через SSE-событие `debate_round` (live) и
  `GET /api/chats/{id}` → `DebateSessions[].Rounds` (F5).
  `finalArtifact` (одобренный код) — сохраняется.

- **v1.11.0 (KI-133)**: кнопка «Пропустить» в feedback-блоке между
  раундами Actor-Critic не разблокировала сервер — только скрывала
  UI-блок. `AgentDebateCoordinator.WaitForFeedbackAsync` продолжал
  ждать 5 минут (FeedbackTimeout), затем возвращал null.
  **Fix:** новый метод `IAgentDebateCoordinator.SkipFeedbackAsync`
  (Singleton-координатор резолвит TCS значением `null` — сигнал skip).
  Новый endpoint `POST /api/chat/debate/{sessionId}/skip`.
  `chat.js:skipFeedback` — теперь вызывает сервер.

### Added
- **v1.11.0 (KI-129, Шаг 1 — backend)**: подготовка к восстановлению
  блоков Actor-Critic при F5.
  - `IAgentDebateSessionService.GetSessionsByChatAsync(chatId, userId)` —
    возвращает все сессии чата с eager-load раундов (`Include(Rounds)`),
    сортировка `StartedAt asc`. Проверка владения чатом — по образцу
    `StartAsync`. Публичный API для `ChatController`.
  - `AgentDebateStatusDto` расширен 6 полями: `Task`, `FinalArtifactJson`,
    `MaxRounds`, `HumanApproval`, `ActorAgent`, `CriticAgent`. Парсинг
    `ConfigSnapshotJson` — через общий private helper `BuildStatusDto`
    (переиспользуется в `GetStatusAsync` и `GetSessionsByChatAsync`).
  - `ChatDetailDto.DebateSessions` — новое свойство
    (`IReadOnlyList<AgentDebateStatusDto>`).
  - `ChatMessageDto.DebateSessionId` (`int?`) — для tool-сообщений
    `code_agent_with_review` содержит ID соответствующей сессии.
  - `ChatStreamService` сохраняет `MetadataJson = { debateSessionId }`
    в tool-сообщение `code_agent_with_review` (из `ToolResult.Data.sessionId`).
  - `ChatController.GetChatAsync` заполняет `DebateSessions` + парсит
    `DebateSessionId` через новый helper `ParseDebateSessionId`.
  - Тесты: +4 unit (`GetSessionsByChatAsync_*`) + расширение
    `GetStatusAsync_ReturnsDto_WithRounds` (проверка `MaxRounds` /
    `HumanApproval` / `Task` / `ActorAgent` / `CriticAgent`).
  - Frontend (рендер блоков при F5) — **следующий коммит** (Шаг 2).

---

## [1.11.0] — 2026-10-02

**Actor-Critic мультиагенты (KI-126, Фаза 1).** Автономное взаимодействие
Actor (`code_agent`) и Critic (`code_reviewer_agent`) через top-level tool
`code_agent_with_review`. Цикл до 3 раундов, эскалация на внешнюю LLM при
`Uncertain`, Human-in-the-loop между раундами, UI селектор
«диалог / сворачиваемый». Плюс fix KI-127 (`code_agent_with_review` выбор)
и полный фикс KI-130 (code_agent и вложения чата через RAG).

Тесты: **592 → 654** (+62, 5 Skip).

### Added
- **v1.11.0 Фаза 1 (KI-126, Шаг 1A)**: сущности `AgentDebateSession` + `AgentDebateRound`.
- **v1.11.0 Фаза 1 (KI-126, Шаг 1B)**: `IAgentDebateSessionService` + state machine.
- **v1.11.0 Фаза 1 (KI-126, Шаг 1C)**: агент `code_reviewer_agent` (Critic).
- **v1.11.0 Фаза 1 (KI-126, Шаг 1D)**: top-level `ITool` `code_agent_with_review`.
- **v1.11.0 Фаза 1 (KI-126, Шаг 1E)**: SSE-события (`debate_*`) + persistence + Human-in-the-loop.
- **v1.11.0 Фаза 1 (KI-126, Шаг 1F)**: эскалация на `ask_external_llm` при `Uncertain`.
- **v1.11.0 Фаза 1 (KI-126, Шаг 1G)**: UI селектор вида + диалоговый / свёрнутый рендер + feedback.
- **v1.11.0 Фаза 1 (KI-126, Шаг 1H)**: финальная локализация дебатов (+22 ключа RU + EN).
- **v1.11.0 Фаза 1 (KI-126, Шаг 1I)**: unit-тесты (+18 тестов).

### Fixed
- **v1.11.0 (KI-127)**: Chat LLM выбирает `code_agent_with_review` для сложных задач.
- **v1.11.0 (KI-130)**: code_agent может читать вложения чата через RAG
  (`search_knowledge_base` с параметром `indexName='my_rag_docs'`).

### Documented
- **KI-128** — Browser workflow недоступен через Chat (Planned, v1.11.x).
- **KI-129** — Debate blocks not restored on F5 (Planned, v1.11.x).

### Added
- **v1.11.0 Фаза 1 (KI-126, Шаг 1H) — закрыт**: финальная локализация
  дебатов. Все ранее хардкодные строки (Actor / Critic / Round N / вердикты /
  «Max N rounds» / «No feedback» / «No issues» / severity / «Total cost» /
  «escalated» / `<summary>` details) вынесены в `.resx` (RU + EN) и
  передаются в JS через `data-label-debate-*` на `#chat-messages` (RULES § 4.17).
  Новые ключи: 22 (`ChatDebateTitle`, `ChatDebateActor`, `ChatDebateCritic`,
  `ChatDebateRound`, `ChatDebateMaxRounds`, `ChatDebateRoundsCount`,
  `ChatDebateInProgress`, `ChatDebateVerdict*` ×5, `ChatDebateDetailsShow/Hide`,
  `ChatDebateTotalCost`, `ChatDebateEscalated`, `ChatDebateNoFeedback`,
  `ChatDebateNoIssues`, `ChatDebateSeverity*` ×3). Хелперы `L()` / `fmt()` /
  `getSeverityLabel()` в `chat.js`. Проверка синхронности —
  `LocalizationSyncTests` (уже часть `dotnet test`). Осталось в Ф1: 1I (тесты),
  1J (релиз v1.11.0).

- **v1.11.0 Фаза 1 (KI-126, Шаг 1G) — закрыт**: Actor-Critic UI полностью
  готов. Три подшага: 1G.1 (селектор вида + рендер dialog), 1G.2 (рендер
  collapsed + `<details>`), 1G.3 (feedback между раундами + возврат
  `HumanApproval = BetweenRounds` в dev). Все три — с локализацией RU + EN
  и CSS на Bootstrap-переменных. Известное ограничение: KI-129 (F5 не
  восстанавливает блок). Осталось в Ф1: 1H (финальная локализация), 1I (тесты),
  1J (релиз v1.11.0).

### Fixed
- **KI-130 — code_agent может читать вложения чата через RAG**
  (v1.11.0, **Fixed**).

  **Что работает (итоговое решение):**
  - ✅ Rule 7 в `ChatStreamService.DefaultSystemPrompt` — Chat правильно
    выбирает `code_agent_with_review` / `code_agent` для задач
    «исправь код в файле X» (не `file_system_agent`).
  - ✅ `SearchKnowledgeBaseTool` расширен параметром `indexName`:
    `'project_docs'` (default, документация проекта) или
    `'my_rag_docs'` (файлы, приложенные к текущему чату через 📎).
    Для `my_rag_docs` `chatId` подставляется автоматически из
    `ToolExecutionContext.ChatId` (пробрасывается через всю цепочку:
    ChatStreamService → CodeAgentWithReviewTool → AgentToolBase →
    SubAgentService → SearchKnowledgeBaseTool).
  - ✅ Обновлён `Description` tool'а и `SystemPrompt` `code_agent`
    (правило 5) — LLM явно знает, что приложенные файлы нужно искать
    через `search_knowledge_base(indexName='my_rag_docs')`, а не через
    `file_system_agent` / `read_file` / `execute_command('ls')`.

### Added
- **v1.11.0 Фаза 1 (KI-126, Шаг 1G.3)**: UI feedback между раундами
  Actor-Critic + возврат `HumanApproval = "BetweenRounds"` в dev.
  - `ChatDebateStartedDto.HumanApproval` (string) — UI понимает, ждёт ли
    сервер feedback (без этого не показать inline-блок).
  - `CodeAgentWithReviewTool` — передаёт `configSnapshot.HumanApproval`
    при эмите `debate_started`.
  - `appsettings.Development.json`: `HumanApproval` → `BetweenRounds`
    (было `Never` — временная мера Шага 1F-fix).
  - `chat.js`: `state.feedbackDrafts` (по sessionId); inline-блок с
    `<textarea>` + Send / Skip + countdown 5 мин; делегированный `input`
    для сохранения текста при перерендере; глобальный `setInterval(1 s)`
    для countdown. `submitFeedback` → `POST /api/chat/debate/{id}/inject`.
    `skipFeedback` — локальная пометка (сервер сам разблокируется через timeout).
    Состояния: `pending` / `sending` / `sent` / `skipped` / `timeout`.
  - CSS `.chat-debate-feedback*`.
  - Локализация RU + EN: 6 ключей (`DebateFeedback*`).

### Documented
- **KI-129** — Debate blocks not restored on F5 (session/rounds in DB,
  UI ignores them). Planned, v1.11.x. Блок Actor-Critic отображается только
  в live-режиме (SSE). При F5 история сообщений загружается из БД, но
  данные debate-сессий не восстанавливаются в UI. При этом сами сессии
  УЖЕ сохраняются в `AgentDebateSession` / `AgentDebateRound` (Шаг 1E-part2).
  Проблема в UI-слое: `ChatController.GetChatAsync` не отдаёт debate-данные.
  Связанные: KI-126 (Шаг 1E-part2).

- **KI-128** — Browser workflow недоступен через Chat (`browser_agent` +
  `save_screenshot_to_file`). Planned, v1.11.x. Пример: «Открой rzd.ru и
  сделай скриншот в minsk.png» → Chat отвечает отказом. Технически capability
  есть (`browser_session_control(screenshot)` возвращает PNG base64), но:
  нет `browser_agent` в `SubAgents`, нет способа сохранить PNG в workspace,
  нет правила в `DefaultSystemPrompt`. Связанные: KI-052, KI-127, KI-118, KI-120.

### Added
- **v1.11.0 Фаза 1 (KI-126, Шаг 1G.2)**: рендеринг блоков дебатов в
  «свёрнутом» режиме. Кэш `state.debates[sessionId]` для перерендера при
  смене вида. Функции `renderDebateContainerInner` / `renderDebateRoundDialog` /
  `renderDebateCompletedDialog` / `refreshDebateViews` — единая точка
  рендера, поддерживающая оба вида. Свёрнутый режим: финальный вердикт
  (✅ Approved / ⚠️ Rejected / ⏳ In progress) + `finalArtifact` +
  `<details>` со всеми раундами (свёрнут по умолчанию после завершения;
  раскрыт во время сессии). CSS для `<details>` / `summary`. Кэш `state.debates`
  сбрасывается при `selectChat` и `showEmptyState`.

- **v1.11.0 Фаза 1 (KI-126, Шаг 1G.1)**: UI-селектор вида отображения
  раундов Actor-Critic («диалог» / «свёрнутый»). Кнопка-переключатель в
  `#chat-header` (SVG-иконка, локализация через `data-*`). Состояние —
  `localStorage["chat.debateView"]` (по умолчанию `collapsed`).
  Рендеринг SSE-событий `debate_started` / `debate_round` / `debate_completed`
  в режиме «диалог» (раунды как отдельные блоки: Actor + Critic + вердикт).
  Блок дебатов размещается в `.chat-message-tools` (перед финальным текстом
  ответа LLM). CSS-стили `.chat-debate-*` (Bootstrap-переменные).
  Локализация RU + EN.
  **Известное ограничение:** блок отображается только в live-режиме (SSE).
  При перезагрузке страницы (F5) история дебатов не восстанавливается,
  так как события не сохраняются в БД (persistence — отложен).

- **v1.11.0 Фаза 1 (KI-126, Шаг 1A)**: сущности `AgentDebateSession` +
    `AgentDebateRound` для Actor-Critic мультиагентов. 2 DbSet + конфигурация
    в `AppDbContext` (FK `Chat` Cascade + FK `Session` Cascade, 3 индекса:
    `IX_AgentDebateSessions_ChatId_StartedAt`, `IX_AgentDebateSessions_User_Status`,
    `IX_AgentDebateRounds_Session_RoundNumber`). Миграция
    `AddAgentDebateSessions` (SqlServer, `decimal(18,6)` для cost-полей).

- **v1.11.0 Фаза 1 (KI-126, Шаг 1B)**: сервис `IAgentDebateSessionService` +
    `AgentDebateSessionService` (Scoped). Concurrency-guard: max 3 активных
    сессии на пользователя (по образцу `MaxBrowserSessionsPerUser`).
    State machine: `Pending` → `InProgress` → (`Completed` | `Failed` | `Cancelled`).
    DTO: `AgentDebateStatusDto`, `AgentDebateRoundDto`,
    `AgentDebateConfigSnapshot` (MaxRounds / TokenBudget / ActorModel /
    CriticModel / AllowEscalation / HumanApproval). Регистрация в DI
    (`Startup.cs`). Фоновый цикл раундов и `InjectFeedbackAsync` —
    заглушки (реальные — в Шагах 1D / 1E). Тесты: +8
    (`AgentDebateSessionServiceTests`).

- **v1.11.0 Фаза 1 (KI-126, Шаг 1C)**: агент-критик `code_reviewer_agent`
    (Critic) для Actor-Critic. Наследник `AgentToolBase`. Модель
    `qwen/qwen3-4b-2507`, `MaxSteps=3`, `RequiresApproval=false`
    (read-only анализ), `AllowedTools=[]` (критик не вызывает инструменты).
    SystemPrompt — по DESIGN § 2.3 (edge cases / безопасность / обработка
    ошибок; формат JSON `{verdict, issues, summary}`). Секция
    `SubAgents:code_reviewer_agent` в обоих `appsettings*.json`. Регистрация
    в DI (`RegisterSpecializedAgentTools`). Тесты: +4
    (`CodeReviewerAgentToolTests`).

### Changed
- **v1.11.0 Фаза 1 (KI-126, Шаг 1F-fix)**: `appsettings.Development.json` →
  `SubAgents:code_agent_with_review:HumanApproval = "Never"` (было
  `"BetweenRounds"`). **Причина:** UI для feedback между раундами
  появится только в Шаге 1G; без него tool после каждого `Rejected`
  блокируется на 5 минут (timeout в `AgentDebateCoordinator`), что
  выглядит как «зависание». В prod-`appsettings.json` дефолт
  `"BetweenRounds"` — сохраняется.

### Added
- **v1.11.0 Фаза 1 (KI-126, Шаг 1F)**: эскалация на внешнюю LLM при
  `verdict = Uncertain` (DESIGN § 3.3). `CodeAgentWithReviewTool`:
  - при `Uncertain` + `AllowEscalation = true` + зарегистрированном
    `EscalationProvider` — вызов `IExternalLlmClient.CompleteAsync` с
    вопросом критика + задачей + кодом actor'а;
  - SSE-событие `debate_escalated` (success / failure);
  - **второй раунд критика** с ответом внешней LLM как доп. контекстом
    (`BuildCriticTaskWithExternalContext`);
  - финальный verdict — из второго раунда; записывается в `AgentDebateRound`
    с `WasEscalated = true`, `EscalationProvider`, `TokensIn/Out`, `CostUsd`;
  - при неудаче (провайдер не зарегистрирован / budget exceeded / HTTP error
    / второй раунд упал) — оставляем `Uncertain`, `WasEscalated = false`.
  - DI: `+IExternalLlmClient`, `+IExternalProviderRegistry` (оба Singleton).
  - Тесты: +4 (`ExecuteAsync_UncertainWithEscalation_EscalatesAndRechecks`,
    `..._UncertainWithoutEscalation_Skips`, `..._EscalationHttpFails_KeepsUncertain`,
    `..._EscalationProviderNotRegistered_Skips`).
  - **Budget guardrail** — внутри `ExternalLlmClient` (DESIGN_EXTERNAL_LLM § 4.1),
    tool его не дублирует.

### Fixed
- **v1.11.0 Фаза 1 (KI-126, Шаг 1E-fix4)**: `DefaultSystemPrompt` в
  `ChatStreamService` — базовый system-prompt с явными правилами выбора
  инструментов (6 правил + 1 пример). Добавляется к КАЖДОМУ чату **первым**
  system-сообщением (стабильный префикс для KV-cache).
  **Обновлены 3 теста** в `ChatStreamServiceTests`: `RagContextInSystemPrompt`
  (теперь проверяет 2 system-сообщения: Default + RAG),
  `NoRagChunks_NoInjection` и `LowScoreFiltered` (оба теперь проверяют
  наличие **одного** DefaultSystemPrompt вместо отсутствия system-сообщения).
  **Причина Шага 1E-fix4:** Шаг 1E-fix3 (усиление только `Description`'ов)
  **не сработал** — qwen3-4b игнорирует описания tools (position-bias:
  `code_agent` идёт первым в алфавитном списке). System-prompt имеет
  высокий приоритет в 4B-модели.

- **v1.11.0 Фаза 1 (KI-126, Шаг 1E-fix3)**: митигация KI-127 — Chat LLM
  выбирает `code_agent` вместо `code_agent_with_review` для сложных задач.
  - Усилен `Description` `code_agent_with_review` в обоих `appsettings*.json`:
    явные триггеры («алгоритм», «парсер», «валидация», «обработка данных»,
    «edge cases», «обработка ошибок», «безопасность», «security»,
    «производительность», «парсинг», «сортировка», «структуры данных», «unicode»).
  - Ограничен `Description` `code_agent`: «ТОЛЬКО для простых задач
    (rename, add import, тривиальные однострочники). Для сложных — `code_agent_with_review`».
  - KI-127 → Fixed (v1.11.0).

### Docs / rules
- **RULES § 4.51**: `ITool`, зависящий от `IToolRegistry` / `ISubAgentService`,
    должен инжектить `Func<T>` (ADR-002). Прецедент — `CodeAgentWithReviewTool`
    (KI-126, Шаг 1D-fix2). Симптом «A circular dependency was detected» виден
    только при `dotnet run` (`ValidateOnBuild`), не на build/test.

### Added
- **v1.11.0 Фаза 1 (KI-126, Шаг 1E-part2)**: полная интеграция
    `code_agent_with_review` — персистенция сессии + SSE-события +
    Human-in-the-loop.
    `ToolExecutionContext.EventWriter` (`ChannelWriter<ChatStreamEvent>`) —
    для эмита событий из tool'а. `ChatStreamService` — Channel-based
    параллельный стриминг events во время `await ExecuteAsync`
    (для tool'ов, которые эмитят прогресс: `debate_started` /
    `debate_round` / `debate_completed`).
    `CodeAgentWithReviewTool`:
    (а) persistence в `AgentDebateSession` / `AgentDebateRound` через
    `IAgentDebateSessionService` (при `context.ChatId != null`),
    (б) SSE-события через `EventWriter`,
    (в) Human-in-the-loop между раундами при
    `HumanApproval = "BetweenRounds"` (TCS-ожидание через
    `IAgentDebateCoordinator`, макс 5 мин).
    Endpoint `POST /api/chat/debate/{sessionId}/inject` + DTO
    `InjectDebateFeedbackRequest`. `ChatStreamController` — DI + endpoint.
    Тесты: обновлён `CodeAgentWithReviewToolTests` (+3 сценария —
    events, human-approval, без ChatId).
    **Отложено в 1F:** эскалация на `ask_external_llm` при `Uncertain`.

- **v1.11.0 Фаза 1 (KI-126, Шаг 1E-part1)**: инфраструктура SSE + Human-in-the-loop
  для Actor-Critic. Новые DTO `ChatDebateStartedDto` / `ChatDebateRoundDto` /
  `ChatDebateEscalatedDto` / `ChatDebateCompletedDto` + 4 factory-метода в
  `ChatStreamEvent` (`debate_started` / `debate_round` / `debate_escalated` /
  `debate_completed`). `ToolExecutionContext.ChatId` (nullable `int`) — для
  привязки сессии к чату. `IAgentDebateCoordinator` + `AgentDebateCoordinator`
  (Singleton, `ConcurrentDictionary<int, TaskCompletionSource<string>>` с
  `RunContinuationsAsynchronously` — RULES § 4.23) — реальный
  `InjectFeedbackAsync`. 3 новых метода в `IAgentDebateSessionService`
  (`MarkInProgressAsync` / `AddRoundAsync` / `CompleteAsync`).
  Тесты: +7 (`AgentDebateCoordinatorTests`). Регистрация coordinator в DI.

- **v1.11.0 Фаза 1 (KI-126, Шаг 1D)**: top-level `ITool`
    `code_agent_with_review` — оркестратор Actor-Critic. Не наследник
    `AgentToolBase` (DESIGN § 5.1, по образцу `DatabaseAgentTool`).
    Координирует `code_agent` (actor) и `code_reviewer_agent` (critic)
    через `IToolRegistry`. Цикл: actor → critic → (Rejected → повтор
    с feedback) до Approved / MaxRounds. Lenient-парсер вердикта
    (JSON в markdown-блоке, свободный текст). `RequiresApproval=true`.
    `Description` — для сложных задач кодинга (DESIGN § 5.3). RULES § 4.44:
    добавлен в `allowedNames` в `ChatStreamService`. Секция
    `SubAgents:code_agent_with_review` в обоих `appsettings*.json`.
    Регистрация в DI. Тесты: +9 (`CodeAgentWithReviewToolTests`).
    **Отложено в 1E/1F:** персистенция сессии в `AgentDebateSession` /
    `AgentDebateRound`, SSE-события `debate_*`, Human-in-the-loop,
    эскалация на `ask_external_llm`.

### Fixed
- **v1.11.0 Фаза 1 (KI-126, Шаг 1D-fix)**: circular dependency при
  `dotnet run`. `CodeAgentWithReviewTool` (ITool) инжектил `IToolRegistry`
  напрямую, а `ToolRegistry` строит `IEnumerable<ITool>` (включая сам
  `CodeAgentWithReviewTool`) → цикл `ToolRegistry → IEnumerable<ITool> →
  CodeAgentWithReviewTool → IToolRegistry`. Сборка и тесты (614/614)
  не ловили — проблема видна только при старте ServiceProvider
  (`ValidateOnBuild`). Решение по образцу ADR-002: заменить прямой
  `IToolRegistry` на ленивую фабрику `Func<IToolRegistry>` +
  регистрация `services.AddScoped<Func<IToolRegistry>>(sp => () =>
  sp.GetRequiredService<IToolRegistry>())` (рядом с существующей
  `Func<ISubAgentService>`). Прецедент — `ConsultSecondaryAgentTool`
  и `AgentToolBase`.

---

## [1.10.1] — 2026-10-01

**Темы оформления UI (KI-122).** 5 тем (Light / Dark / Dimmed /
Solarized Light / High Contrast) + переключатель в navbar рядом с RU/EN.
Попутно закрыт **KI-092** (Bootstrap `aria-hidden` warning — Bootstrap 5.3.2
уже использует `inert`). UI-only patch, без изменений API / серверной логики.

### Added
- **KI-122 — темы оформления UI**:
  - **B1/5: CSS-переменные 5 тем.** `data-bs-theme="light"` на `<html>`
    (default). Кастомные темы `dimmed` (в стиле GitHub Dimmed) /
    `solarized-light` (Ethan Schoonover) / `high-contrast` (WCAG AAA) —
    через override Bootstrap-переменных (`[data-bs-theme="..."]`) в `site.css`.
    `light` / `dark` — из коробки Bootstrap **5.3.2** (уже установлен,
    `data-bs-theme` доступен). Заменён хардкод `body { background: #f8f9fa; }`
    → `var(--bs-tertiary-bg)` (иначе смена темы не работала бы).
  - **B2/5: модуль `theme.js`.** `wwwroot/js/modules/theme.js` —
    `getTheme` / `setTheme` / `getAvailableThemes` / `clearTheme` /
    `watchSystemTheme`. Хранение — `localStorage["theme"]` (fallback на
    `prefers-color-scheme: dark`). Anti-FOUC: синхронный inline-скрипт
    в `<head>` `_Layout.cshtml` (до первого paint). Событие `theme-changed`
    для UI-синхронизации.
  - **B3/5: переключатель `<select id="theme-select">` в navbar.**
    Перед RU/EN. Стиль — `.navbar-theme-select` (Bootstrap-переменные).
    `theme.js` — новая функция `initThemeSelect(selectEl)` (идемпотентная,
    синхронизирует value с `theme-changed`). Локализация — +6 ключей × 2
    языка (`ThemeSelectTitle`, `ThemeLight`, `ThemeDark`, `ThemeDimmed`,
    `ThemeSolarizedLight`, `ThemeHighContrast`).
  - **B4.1/5: navbar + footer + ссылки на Bootstrap-переменные.**
    `_Layout.cshtml`: убраны устаревший `navbar-light` и хардкод `bg-white`
    из `<nav>`; `text-dark` из 8 ссылок (Главная, Чат, Статус, Профиль,
    Админка, Тест, RU, EN). `_LoginPartial.cshtml`: `text-dark` из 4 ссылок
    (displayName, Выход, Регистрация, Вход). `site.css`: `.footer` →
    `var(--bs-body-bg)`; `.agent-stat-*` и `.rag-chunk-*` → переменные тем.
  - **B4.2/5: `chat.css` на Bootstrap-переменные.** ~70 замен `#xxx` →
    `var(--bs-*)` в 17 секциях: `.chat-container`, `.chat-sidebar*`,
    `.chat-list-item*` (включая active/hover/inline-edit), `.chat-messages`,
    `.chat-input-box` / `.chat-input-textarea`, `.chat-message-content`
    (user/assistant), `.chat-tool-block` / `.chat-tool-result`,
    `.chat-typing-indicator`, `.chat-message-error`, `.chat-scroll-down`,
    `.chat-message-action*`, `.chat-markdown*` (blockquote/code/table),
    `.chat-code-block*`, `.chat-model-select`, `.chat-message-edit`,
    `.chat-sidebar-icon-btn`, `.chat-search-bar` / `.chat-search-btn`,
    `.chat-global-search-dialog` / `-input` / `-close` / `-clear` / `-item*`,
    `.chat-message-sources*`, `.chat-attachments-bar` / `-chip*`,
    `.chat-input-action-attach`. Использованы `--bs-body-bg`,
    `--bs-tertiary-bg`, `--bs-secondary-bg`, `--bs-border-color`,
    `--bs-secondary-color`, `--bs-emphasis-color`, `--bs-primary`,
    `--bs-danger`, `--bs-success`, `--bs-code-color`, `--bs-link-color`,
    `--bs-primary-bg-subtle`, `--bs-danger-bg-subtle`, `--bs-success-bg-subtle`,
    `--bs-danger-text-emphasis`, `--bs-danger-border-subtle`.
  - **B4.3/5: финал CSS + highlight.js override.**
    - `_ApprovalModal.cshtml`: `bg-light` → `bg-body-secondary` (pre с JSON
      параметрами — теперь темизирован).
    - `Admin.cshtml`: `tab-content bg-white` → `bg-body`; убраны 6×
      `<thead class="table-light">` (Bootstrap 5.3 `.table-light` фиксирует
      `#f8f9fa` / `#000` — в тёмных темах даёт светлый фон).
    - `Status.cshtml`: убран `<thead class="table-light">`.
    - `site.css`: новое правило `.table > thead > tr > th` →
      `var(--bs-tertiary-bg)` / `var(--bs-emphasis-color)` /
      `var(--bs-border-color)`.
    - `wwwroot/css/theme-hljs.css` (новый): override ~15 ключевых классов
      hljs для 4 тем — `dark` / `dimmed` (палитра github-dark),
      `high-contrast` (WCAG AAA), `solarized-light` (Ethan Schoonover).
    - `Chat/Index.cshtml`: подключён `theme-hljs.css` после
      `github.min.css` и `chat.css`.

### Fixed
- **KI-092 — Bootstrap `aria-hidden` warning при закрытии вложенных модалок**
  (попутно с KI-122). Bootstrap 5.3.2 использует атрибут `inert` вместо
  `aria-hidden` — warning исчез. Проверено на smoke v1.10.1 (модалки `/admin`
  `#adminModal` / `#ragChunksModal`, approval-модалка `/chat`). Ранее
  Bootstrap 5.2 давал warning «Blocked aria-hidden on an element because
  its descendant retained focus».

---

## [1.10.0] — 2026-10-01

**Google Gemini (KI-110b).** Третий формат API в External-LLM Agent:
`ProviderFormat.Gemini` теперь полностью реализован (не заглушка).
Плюс три followup-фикса v1.9.0: KI-123 (индикатор загрузки списка чатов),
KI-124 (интеграционный тест Anthropic), KI-125 (MailKit/MimeKit security).

Обратная совместимость: 6 существующих провайдеров (DeepSeek, OpenAI,
Groq, Together AI, Ollama, Anthropic) работают без изменений конфига.

Тесты: **559 → 592** (+33, 5 Skip внешних).

### Security
- **KI-125 — MailKit / MimeKit 4.8.0 → 4.18.1** (v1.9.0-followup):
  закрыты 2 Moderate advisory:
  - `GHSA-9j88-vvj5-vhgr` (MailKit, STARTTLS Response Injection + SASL
    mechanism downgrade) — fixed в 4.16.0;
  - `GHSA-g7hc-96xr-gvvx` (MimeKit, CRLF Injection в quoted local-part) —
    fixed в 4.15.1.
  `Directory.Build.props`: `<MailKitVersion>4.8.0 → 4.18.1</MailKitVersion>`.
  MimeKit подтягивается транзитивно на ту же версию.
  **Было 6 warnings NU1902** (Services / API / Tests × 2 advisory) —
  после обновления 0. Breaking changes не ожидаются (4.16.0 — только
  security fix + Dispose RNG).

### Added
- **v1.10.0 Фаза 4 (KI-110b): конфигурация Gemini + README**:
  - `appsettings.json` / `appsettings.Development.json`:
    `gemini.BaseUrl` `v1beta` → `v1` (stable API).
    `DisplayName` без «(v1.9.x, KI-110b)» — фича реализована.
    Тарифы: `$0.0001 / $0.0004` за 1k токенов (вместо placeholder 0).
  - README («External-LLM Agent») — заголовок обновлён до v1.10.0;
    intro упоминает 3 формата (OpenAI / Anthropic / Gemini);
    блок про Gemini-заглушку заменён на полноценную инструкцию
    (VPN из РФ, free tier 15 RPM / 1500 req/day, `x-goog-api-key`).
  - **Обратная совместимость:** 6 существующих провайдеров
    (DeepSeek, OpenAI, Groq, Together, Ollama, Anthropic) — без изменений.

- **v1.10.0 Фаза 3 (KI-110b): рефакторинг `ExternalLlmClient`**:
  - Switch по `ProviderFormat`: `Gemini` → `CompleteGeminiAsync`
    (удалена заглушка `NotSupportedException`).
  - `CompleteGeminiAsync` — `POST {BaseUrl}/models/{model}:generateContent`,
    `x-goog-api-key` header (не Bearer / не query), вызов builders из
    Фаз 1-2. **Модель в URL, не в body** (специфика Gemini).
  - Удалён `catch (NotSupportedException)` — больше не нужен
    (несуществующая фича стала работающей).
  - Общая обвязка (circuit breaker / budget / cost / audit) — без изменений.
  - Тесты: `ExternalLlmClientTests` — удалён старый тест заглушки,
    +4 новых (`x-goog-api-key`, `/models/...:generateContent`, парсинг
    candidates, 500 без retry).
  - Интеграционный `[Fact(Skip=...)]` для Gemini — env
    `EXTERNALLLM__GEMINI__APIKEY`, VPN из РФ.
  - **588 → 592** (587 pass, 5 skip).

- **v1.10.0 Фаза 2 (KI-110b): `GeminiResponseParser`**:
  - `Implementation/ExternalLlm/Formats/GeminiResponseParser.cs` — static helper.
    Извлекает текст из `candidates[0].content.parts[]` (склейка блоков
    с `text` через `\n`), токены из `usageMetadata.promptTokenCount` /
    `candidatesTokenCount`. Блоки `thought: true`, `functionCall`,
    `functionResponse`, `inlineData`, `codeExecutionResult` — игнорируются
    (v1.10.0 — только prompt → text). Не падает при `SAFETY` / пустом
    `candidates[]` — возвращает пустую строку + токены.
  - Тесты: `GeminiResponseParserTests` (12 — базовые + edge-cases:
    multi-part, empty parts, no candidates, SAFETY, thought/fc-блоки,
    null-guard). **576 → 588** (+12, 4 skip).

- **v1.10.0 Фаза 1 (KI-110b): `GeminiRequestBuilder`**:
  - `Implementation/ExternalLlm/Formats/GeminiRequestBuilder.cs` — static helper.
    Собирает тело для `POST {BaseUrl}/models/{model}:generateContent`:
    `contents[]` (`{role, parts:[{text}]}`), `systemInstruction` (отдельный
    Content-объект, без `role`), `generationConfig` (`maxOutputTokens` обязателен,
    `temperature` clamp [0, 2]).
  - **Модель в URL, не в body** — builder её не возвращает (специфика Gemini).
  - Тесты: `GeminiRequestBuilderTests` (13 — 12 базовых + edge-cases:
    null-guards ×2, whitespace-system, empty-prompt, model-not-in-body).
    **563 → 576** (+13, 4 skip).

- **v1.10.0 Фаза 0 (KI-110b): DESIGN_GEMINI.md** — дизайн-документ для
  Google Gemini (`docs/development/v1.9/DESIGN_GEMINI.md`). Draft согласован
  2026-10-01. Ключевые решения:
  - API **v1** (stable) — BaseUrl `v1beta` → `v1`;
  - Auth — **`x-goog-api-key` header** (не query `?key=`);
  - `POST /models/{model}:generateContent` (non-stream);
  - `systemInstruction` — отдельное поле (объект Content);
  - `generationConfig.maxOutputTokens` — обязателен.
  План: 5 фаз (Builders → Parser → Client → Config → Релиз), ~5 ч.
  Целевой релиз — **v1.10.0**. KI-110b → **In Progress**.

- **KI-123 — индикатор загрузки списка чатов** (Deferred → Fixed):
  spinner (Bootstrap `.spinner-border`) + локализованный текст
  «Идёт загрузка списка чатов…» в sidebar `/chat`.
  Появляется на время `GET /api/chats` (включая поиск с `?search=`).
  Локализация через `data-label-loading` на `#chat-list` (RULES § 4.17),
  +2 ключа в `.resx` (RU + EN). `chat.js` — новый helper
  `renderChatListLoading(listEl)`, вызывается из `loadChats()` вместо
  hardcoded «Загрузка…».

### Fixed
- **KI-124 — Интеграционный тест Anthropic Claude** (v1.9.0-followup):
  добавлен `Anthropic_RealRequest_ReturnsResponse` в
  `ExternalLlmIntegrationTests.cs` — `[Fact(Skip=...)]`, env
  `EXTERNALLLM__ANTHROPIC__APIKEY`, требует VPN из РФ. Проверяет путь
  `CompleteAnthropicAsync` (`POST /v1/messages`, `x-api-key`,
  `anthropic-version: 2023-06-01`).
  `CreateRealClient` — +опциональный `ProviderFormat format = OpenAI`
  (обратно совместимо).
  **562 → 563 tests** (559 pass, **4** skip).

---

## [1.9.0] — 2026-10-01

**Anthropic Claude (KI-110a).** DESIGN согласован 2026-09-30
(`docs/development/v1.9/DESIGN_ANTHROPIC_GEMINI.md`). План: 5 фаз
(ProviderFormat → Builders → Client → Config → Релиз). Обратная
совместимость: 5 существующих OpenAI-совместимых провайдеров не меняются.

### Added
- **v1.9.0 Фаза 1 (KI-110a): `ProviderFormat` enum + `ExternalProviderOptions.Format`**:
  - `ProviderFormat` (`DTO/ExternalLlm/ProviderFormat.cs`) — enum:
    `OpenAI = 0` (default) / `Anthropic = 1` / `Gemini = 2` (v1.9.x, KI-110b).
    При вызове `Gemini` — `NotSupportedException` (заглушка).
  - `ExternalProviderOptions.Format` — новое свойство, default = `OpenAI`.
    5 существующих провайдеров (DeepSeek, OpenAI, Groq, Together AI, Ollama)
    не задают `Format` в `appsettings.json` — работают без изменений конфига.
  - Тесты: `ExternalProviderRegistryTests` +2 (Anthropic проходит валидацию;
    `Default = OpenAI` для backward compat). **529 → 531**.

- **v1.9.0 Фаза 2 (KI-110a): Anthropic builders**:
  - `AnthropicRequestBuilder` (`Implementation/ExternalLlm/Formats/`) — static helper,
    собирает `JObject` для `POST {BaseUrl}/messages`. Отличия от OpenAI:
    `max_tokens` обязателен, `system` — отдельным полем (не роль в `messages[]`),
    `temperature` clamp [0, 1] (не [0, 2]), `stream` не добавляется (v1.9.0 non-stream).
  - `AnthropicResponseParser` (`Implementation/ExternalLlm/Formats/`) — static helper,
    извлекает `content[]` (склейка блоков `type=="text"` через `\n`),
    `usage.input_tokens` / `usage.output_tokens`. Блоки `type=="tool_use"`
    игнорируются (v1.9.0 — без function calling). Не падает при отсутствии полей.
  - Тесты: `AnthropicRequestBuilderTests` (12) + `AnthropicResponseParserTests` (8).
    **531 → 551** (3 Skip внешних).

### Added
- **v1.9.0 Фаза 3 (KI-110a): рефакторинг `ExternalLlmClient`**:
  - `CompleteAsync` — switch по `ProviderFormat` → приватные методы:
    - `CompleteOpenAiAsync` — существующее поведение (DeepSeek, OpenAI, Groq,
      Together, Ollama), вынесено без изменений логики;
    - `CompleteAnthropicAsync` — новый путь (`POST /messages`, `x-api-key` +
      `anthropic-version: 2023-06-01`, вызов `AnthropicRequestBuilder` /
      `AnthropicResponseParser` из Фазы 2);
    - `Gemini` → `NotSupportedException` (v1.9.x, KI-110b). Не увеличивает
      fail-счётчик circuit breaker (не сетевая ошибка);
    - неизвестный `Format` → `InvalidOperationException` (fail-fast).
  - `SendWithRetryAsync` / `SendOnceAsync` — параметризованы
    `IReadOnlyDictionary<string, string> headers` вместо хардкоженного
    Bearer. Поддержка `Authorization` (типизировано в `Headers.Authorization`),
    `x-api-key`, `anthropic-version` (через `TryAddWithoutValidation`).
  - Общая обвязка (`circuit breaker` / `budget` / cost-calc / audit) —
    без изменений, применяется ко всем форматам единообразно.
  - Тесты: `ExternalLlmClientTests` +7 (x-api-key, anthropic-version,
    `/messages` endpoint, парсинг content[], multi-block, 400 без retry,
    Gemini NotSupported, unknown format). **551 → 558** (3 Skip).

### Added
- **v1.9.0 Фаза 4 (KI-110a): конфигурация Anthropic / Gemini + README**:
  - `appsettings.json` / `appsettings.Development.json` — **+2 провайдера**
    в `ExternalLlm:Providers`:
    - `anthropic` — `Format: "Anthropic"`, `claude-haiku-4-5`,
      BaseUrl `https://api.anthropic.com/v1`, тарифы $0.001 / $0.005
      за 1k токенов (input / output);
    - `gemini` — `Format: "Gemini"`, `gemini-2.0-flash`,
      BaseUrl `https://generativelanguage.googleapis.com/v1beta`.
      **Заглушка** — при вызове `NotSupportedException` (KI-110b, v1.9.x).
  - README («External-LLM Agent») — Anthropic в intro; инструкция получения
    ключа (`VPN обязателен`, `sk-ant-...`, `dotnet user-secrets`); отдельный
    блок про Gemini-заглушку; обновлён раздел «Ограничения» (Anthropic
    поддержан; Gemini — v1.9.x).
  - **Обратная совместимость:** 5 существующих провайдеров (DeepSeek, OpenAI,
    Groq, Together, Ollama) не задают `Format` — продолжают работать
    с дефолтом `ProviderFormat.OpenAI`.

### Changed
- **v1.9.0 Фаза 2 (KI-110a): `ExternalLlmRequest.System`** (nullable) — нужно
  для правила «`system` добавляется, если не пуст» (DESIGN § 3.4).
  В OpenAI-ветке поле игнорируется (system сейчас не поддерживается).
  Backward-compatible: все существующие инициализаторы работают.

---

## [1.8.2] — 2026-10-01

**Tool result cache + prefix stability + KI-121.**
Whitelist-кэш для «дорогих» инструментов (Wikipedia, web_search, RAG-поиск)
на базе `IMemoryCache`: повторный вызов с теми же аргументами и от того же
пользователя → мгновенный ответ без внешнего запроса. RAG-контекст в
`ChatStreamService` теперь отдельным system-сообщением **после** основного —
KV-cache LM Studio не инвалидируется при добавлении attachments (TTFT ↓
на 30-60%). KI-121: LLM `external_llm_agent` больше не галлюцинирует
«использованные провайдеры», когда все вернули Fail.

### Added
- **Tool result cache (Слой 2 многоуровневого кэширования, v1.8.2)**:
  - `ToolResultCacheOptions` + `ToolCacheEntryOptions` — настройки whitelist
    (per-tool `Enabled` + `TtlSeconds`).
  - `IToolResultCache` + `ToolResultCache` (Singleton, `IDisposable`) —
    обёртка над `IMemoryCache`.
  - `CanonicalJsonHelper` — канонизация JSON (рекурсивная сортировка ключей
    `JObject` по `Ordinal`, порядок `JArray` сохраняется) + SHA-256 + формат
    ключа `tool:{name}:u{userId}:{sha256}`.
  - Интеграция в `ToolRegistry.ExecuteAsync` — проверка кэша до вызова
    инструмента, сохранение после (только при `Success == true`).
    Опциональный параметр `IToolResultCache` в конструкторе (default `null` —
    обратная совместимость с существующими тестами).
  - **Whitelist (6 инструментов):** `wikipedia_search` (TTL 1 ч),
    `web_search` (15 мин), `fetch_web_content` (1 ч),
    `search_knowledge_base` (1 ч), `search_chat_history` (5 мин),
    `search_workspace` (5 мин).
  - **Не кэшируется:** `ask_external_llm` (приватность + стоимость),
    `send_email`, `save_file`, `run_python`, `execute_query`,
    `list_directory`, `read_file` — побочные эффекты / дешевизна.
  - **Ключ включает `userId`** — per-user изоляция (для `search_chat_history` /
    `search_workspace` / `search_knowledge_base`, чтобы не было утечки между
    пользователями).
  - **Инвалидация по префиксу** через `CancellationChangeToken`:
    `IMemoryCache` не поддерживает prefix-eviction, поэтому per-tool `CTS`,
    `InvalidateAll(tool)` отменяет все записи инструмента. Пригодится после
    reindex RAG.
  - **Настройка** — `appsettings.json` → секция `ToolCache`
    (`Enabled`, `SizeLimit`, `Tools[*].TtlSeconds`). `SizeLimit` clamp
    `[100, 1_000_000]`.
  - **Метрики Prometheus:** `iichattools_tool_cache_hits_total` /
    `iichattools_tool_cache_misses_total` (label `tool_name`).
  - **Тесты:** +33 (496 → 529) — `CanonicalJsonHelperTests` (10),
    `ToolResultCacheTests` (14+ после фикса CS8323),
    `ToolRegistryCacheTests` (6).

### Changed
- **Chat — prefix stability (Слой 1, v1.8.2)**: RAG-контекст из `my_rag_docs`
  теперь добавляется **отдельным** system-сообщением **ПОСЛЕ** основного
  `chat.SystemPrompt` (раньше склеивались в одно через `\n\n`). Эффект:
  стабильный префикс не меняется при добавлении / удалении attachments →
  KV-cache LM Studio не инвалидируется → TTFT ↓ на 30-60% при наличии RAG.
  Без attachments поведение не меняется.

### Fixed
- **KI-121 — `external_llm_agent`: галлюцинация «использованные провайдеры»**:
  уточнено правило 4 в `SystemPrompt` (appsettings + .Development) — указывать
  ТОЛЬКО успешно ответивших провайдеров; при полном отказе явно писать
  «Ни один провайдер не доступен» и перечислять причины. Раньше LLM трактовала
  Fail как «была попытка = использован».

### Documented
- **KI-122** — смена темы оформления UI (Deferred, v1.9.x). Флаг: текущий
  Bootstrap 5.2 → нужен 5.3+ (`data-bs-theme`).
- **KI-123** — индикатор загрузки списка чатов («Идёт загрузка» + spinner).
  Deferred, v1.9.x.

### Docs / rules
- **RULES § 4.49** — `AddMemoryCache` / `AddOptions` / `AddHttpClient`
  принимают `Action<T>`, не `Func<IServiceProvider, T>`; читать конфиг
  через `Configuration.GetValue<T>` в `Startup`.
- **RULES § 4.50** — `params` + именованный аргумент = CS8323; флаг-параметр
  всегда позиционный первый.

---

## [1.8.1] — 2026-09-30

**External-LLM Agent (KI-109).** Агент `external_llm_agent` + 3 инструмента внутри:
`ask_external_llm` (с опциональным `compare_with`), `list_external_providers`,
`check_internet_connection`. 5 OpenAI-совместимых провайдеров
(DeepSeek / OpenAI / Groq / Together AI / Ollama). Оркестратор с 4 сценариями
(Fallback / Специализация / Разные знания / Сравнение). Budget guardrails
($5/день, 500k токенов), circuit breaker (3 fail → 5 мин skip), privacy-first
(без PII в логах). Chat видит **13 инструментов** (было 12).
Тесты: **424 → 496** (+72, из них 3 Skip — реальные провайдеры).

### Added
- **External-LLM Agent — Фаза 5: integration-тесты (v1.8.1, KI-109)**:
  - `ExternalLlmIntegrationTests` (`Tests/IntegrationTests/ExternalLlm/`) — 4 теста:
    - `CompleteAsync_UnknownProvider_FailsBeforeHttp` — **без Skip** (fail-fast без сети).
    - `DeepSeek_RealRequest_ReturnsResponse` — `[Fact(Skip=...)]`.
    - `OpenAI_RealRequest_ReturnsResponse` — `[Fact(Skip=...)]`.
    - `Ollama_RealRequest_ReturnsResponse` — `[Fact(Skip=...)]`.
  - Для запуска — env-переменная `EXTERNALLLM__{PROVIDER}__APIKEY` + убрать Skip вручную.
  - **DoD:** `dotnet build` 0/0, `dotnet test` 495 → **496/496** (1 fail-fast + 3 Skip).

### Documented
- **KI-120 (new)** — Chat LLM галлюцинирует количество инструментов: перечисляет 13
  корректно, потом пишет «правильно: 10». Ограничение qwen3-4b (аналогично KI-118).
  **Зашитого числа в `ChatStreamService` НЕТ** (проверено). Workaround для smoke:
  спрашивать «перечисли» вместо «сколько».

### Added
- **External-LLM Agent — Фаза 4: агент `external_llm_agent` (v1.8.1, KI-109)**:
  - `ExternalLlmAgentTool` (`Implementation/Tools/SubAgent/`) — наследник
    `AgentToolBase`, `Name = AgentName = "external_llm_agent"`.
    `RequiresApprovalByDefault` резолвится из дескриптора
    (`SubAgents:external_llm_agent:RequiresApproval = false`).
  - `Startup.cs` — 1 строка в `RegisterSpecializedAgentTools`:
    `services.AddScoped<ITool, ExternalLlmAgentTool>()`.
  - `appsettings.json` + `.Development.json` — секция `SubAgents:external_llm_agent`
    (`Enabled: true`, `Model: qwen/qwen3-4b-2507`, `MaxSteps: 5`,
    `RequiresApproval: false`, `AllowedTools: [ask_external_llm,
    list_external_providers, check_internet_connection]`, SystemPrompt с 4 сценариями).
  - **ChatStreamService — НЕ требует правок:** `external_llm_agent` — наследник
    `AgentToolBase`, попадает в `allowedNames` через `SubAgentRegistry.GetEnabled()`
    (RULES § 4.44 здесь **не** применим — только для top-level `ITool` в Chat).
  - **Chat видит 13 инструментов** (было 12): 8 агентов + consult + 3 RAG +
    `database_agent` + `mail_agent` + `external_llm_agent`.
  - **Тесты:** без unit (всё через `SubAgentRegistry` — конфигурация).
  - **DoD:** `dotnet build` 0/0. `dotnet test` 495/495 (без изменений).
- **External-LLM Agent — Фаза 3: 3 tools + регистрация (v1.8.1, KI-109)**:
  - `AskExternalLlmTool` (`Implementation/Tools/ExternalLlm/`) — `ask_external_llm`.
    Одиночный режим (`provider` + `prompt`) и сравнение (`compare_with` — 2 параллельных
    запроса через `Task.WhenAll`, возврат `{ primary, secondary, totalCostUsd }`).
    Параметры: `prompt` (required), `provider?`, `compare_with?`, `include_context?`
    (Фаза 3: принимается, но не инжектится — реальный контекст в Фазе 4), `max_tokens?`,
    `temperature?` (clamp [0, 2]). Approval не требуется (защита — DailyBudgetUsd).
  - `ListExternalProvidersTool` — `list_external_providers`. Возвращает `{ default,
    providers[] }` с именем, DisplayName, моделью, доступностью (circuit breaker),
    тарифами, последней ошибкой. Без параметров.
  - `CheckInternetConnectionTool` — `check_internet_connection`. Лёгкий `GET /models`
    через `IExternalLlmClient.TestConnectionAsync`. Параметр `provider?`
    (по умолчанию `DefaultProvider`).
  - `Startup.cs`: `RegisterExternalLlmTools(services)` — 3 `Scoped<ITool>`, только при
    `ExternalLlm:Enabled = true`. Chat их **не видит** напрямую — только через
    `external_llm_agent` (Фаза 4, `AllowedTools` в `SubAgents:*`). **ChatStreamService
    не правится** (RULES § 4.44 применим только к top-level `ITool` в Chat).
  - **Тесты:** +16 (`AskExternalLlmToolTests` ×10, `ListExternalProvidersToolTests` ×3,
    `CheckInternetConnectionToolTests` ×3).
  - **DoD:** `dotnet build` 0/0, `dotnet test` 479 → **495/495**.
- **External-LLM Agent — Фаза 2.5–2.6: Client + wire-up (v1.8.1, KI-109)**:
  - `ExternalLlmClient` (`Implementation/ExternalLlm/`) — Singleton, `IExternalLlmClient`.
    OpenAI-совместимый POST `{BaseUrl}/chat/completions` с Bearer-токеном. Retry 1×
    при 5xx / 429 (не при 4xx и timeout). Circuit breaker + Budget tracker +
    Cost calculator интегрированы. Privacy: без prompt/content в логах.
  - `IExternalLlmClient.CompleteAsync(int userId, ...)` — расширена сигнатура:
    `userId` нужен для per-user budget tracker (DESIGN § 6.4). `TestConnectionAsync`
    без `userId` (не тратит бюджет).
  - `Startup.cs` — раскомментированы 4 регистрации (`Configure<ExternalLlmOptions>` +
    3 Singleton + Client).
  - `appsettings.json` / `.Development.json` — секция `ExternalLlm` (`Enabled = false`
    по умолчанию, 5 провайдеров: deepseek / openai / groq / together / ollama).
  - `Program.cs` — fail-fast блок: resolve `IExternalProviderRegistry` при
    `ExternalLlm:Enabled = true` (иначе валидация сработала бы только на первом вызове).
  - **Тесты:** +14 (`ExternalLlmClientTests`).
  - **Fix (в том же коммите):** `HttpClient.Timeout` нельзя менять после первого
    `SendAsync` (`InvalidOperationException: This instance has already started...`).
    В retry-цикле с переиспользованием `HttpClient` (mock, DI-контейнер) это ломается.
    Заменено на `CancellationTokenSource.CancelAfter` — таймаут привязан к запросу,
    а не к клиенту (см. RULES § 4.48).
  - **DoD:** `dotnet build` 0/0, `dotnet test` 465 → **479/479**.
- **External-LLM Agent — Фаза 2.1–2.4: Infrastructure (v1.8.1, KI-109)**:
  - `ExternalProviderRegistry` (`Implementation/ExternalLlm/`) — Singleton, читает
    `ExternalLlm:Providers` из конфигурации. Fail-fast валидация при `Enabled = true`
    (DESIGN_EXTERNAL_LLM § 5.6).
  - `ExternalLlmCircuitBreaker` (`Implementation/ExternalLlm/`) — Singleton, `IDisposable`.
    Per-provider, N подряд fail → skip на `BreakDurationSeconds`. Cleanup Timer 5 мин (KI-043).
  - `ExternalLlmBudgetTracker` (`Implementation/ExternalLlm/`) — Singleton, `IDisposable`.
    Per-user, daily budget + tokens. Lazy-reset при смене дня UTC. Cleanup Timer 30 мин.
  - `ProviderCostCalculator` (`Implementation/ExternalLlm/`) — static helper: USD по токенам.
  - **Тесты:** +41 (Registry ×10, CircuitBreaker ×11, BudgetTracker ×11, CostCalculator ×9).
  - **DoD:** `dotnet build` 0/0, `dotnet test` 424 → **465/465**.
- **External-LLM Agent — Фаза 1: DTO + интерфейсы (v1.8.1, KI-109)**:
  - `DTO/ExternalLlm/` — 7 файлов: `ExternalLlmOptions`, `ExternalLlmCircuitBreakerOptions`,
    `ExternalProviderOptions`, `ExternalLlmRequest`, `ExternalLlmResponse`,
    `ExternalLlmComparisonDto`, `ProviderHealthStatus`.
  - `Interfaces/` — 4 файла: `IExternalLlmClient`, `IExternalLlmCircuitBreaker`,
    `IExternalLlmBudgetTracker`, `IExternalProviderRegistry`.
  - `Startup.cs` — закомментированный блок будущих регистраций (раскомментируется в Фазе 2.6).
  - **DoD:** `dotnet build` 0/0.
- **README — раздел «Docker — что работает, что нет» (v1.8.x, KI-112)**:
  - Таблица: Chat UI, Mail Agent, RAG, SqlAgent, file_system_agent, web_agent —
    работают; code_agent, git_agent, github_agent — нет (нет утилит в lightweight-образе).
  - DataProtection volume `iichattools-keys:/home/app/.aspnet/DataProtection-Keys`.
  - LM Studio через `host.docker.internal` (+ `--add-host` на Linux).
  - Mail Agent credentials через env-переменные с префиксом `Mail__`.
  - HTTPS redirect note + `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.

### Fixed
- **KI-116 — `code_agent` и `planner_agent` тоже откачены на qwen3-4b (v1.8.x)**:
  - **Подтверждение gemma не tool-calling:** логи LM Studio для `planner_agent`
    показали `"tool_calls": []` при правильном `reasoning_content` с
    запланированными `save_memory(...)`. `save_memory` **не вызывался**.
  - **Fix:** `SubAgents:code_agent:Model` и `SubAgents:planner_agent:Model`
    → `qwen/qwen3-4b-2507`.
  - **KI-116** → **Fixed** (v1.8.x).
- **KI-118 — Chat LLM не вызывает `code_agent` для простых задач (new, Documented)**:
  - Запрос «Через Python посчитай 2+2» → Chat LLM вывела код, но **не
    вызвала `code_agent`**. Требует усиления Description.
- **Mail Agent — успех: qwen3-4b + Context Length 16384 (v1.8.x, KI-115 → Fixed)**:
  - **Диагноз (лог LM Studio):** `prompt_tokens: 8145, completion_tokens: 47,
    finish_reason: "length"` — упор в дефолтный Context Length 8192. Агент
    физически не мог дописать ответ.
  - **Fix:**
    - `SubAgents:mail_agent:Model` → `qwen/qwen3-4b-2507` (после отката gemma).
    - **LM Studio: Context Length 8192 → 16384** для qwen3-4b.
  - **Результат:** `prompt_tokens: 10194, completion_tokens: 295, finish_reason: "stop"` —
    письмо прочитано **полностью** (тело, ключевые моменты, детали).
  - **KI-115** → **Fixed** (v1.8.x).
  - **KI-117** (new, Documented): требование к LM Studio — `Context Length ≥ 16384`
    для агентов. README обновлён (новый раздел «Требования к LM Studio»).
- **Mail Agent — откат модели на qwen3-4b + KI-116 (v1.8.x)**:
  - **Диагноз:** в KI-115 попытались заменить модель `mail_agent` на
    `gemma-4-12b-coder-fable5-composer2.5-v1` (как у `code_agent`), но
    **gemma не генерирует `tool_calls[]`** — она пишет вызов функции как
    plain text в `content` (см. лог LM Studio, KI-116).
  - **Fix:** откат `SubAgents:mail_agent:Model` обратно на
    `qwen/qwen3-4b-2507` (умеет tool calling). Few-shot промпт оставлен.
  - **KI-116** — новый документированный баг: gemma-4-12b не
    tool-calling-совместима. Требует проверки `code_agent` / `planner_agent`.
  - **KI-115** — статус `Planned` → `In Progress` (требуется архитектурный
    фикс, см. план ниже).

### Changed
- **Mail Agent — увеличены `MaxSteps` (10 → 15) + усилен SystemPrompt
  (v1.8.x, KI-111 / KI-113)**:
  - `MaxSteps: 10 → 15` в `SubAgents:mail_agent` (`appsettings.json` +
    `appsettings.Development.json`). Причина: Chat LLM передаёт `maxSteps=5`
    вместо дефолта, чего не хватает для list_emails + read_email.
  - SystemPrompt: добавлены **ПРАВИЛА ЭФФЕКТИВНОСТИ** (без промежуточных
    разведок, типовые задачи в 1-2 вызова) и **ПРАВИЛА ЧЕСТНОСТИ**
    (не говорить «успешно», если не выполнено — см. KI-113).

### Added
- **KI-111..114 — 4 новые записи в KNOWN_ISSUES**:
  - **KI-111** (Planned) — `mail_agent` не помнит контекст между вызовами.
  - **KI-112** (Documented) — Docker-образ без `git`/`gh`/`python3`/`node`.
  - **KI-113** (Planned) — `mail_agent`: qwen3-4b галлюцинирует успех.
  - **KI-114** (Documented) — `AgentToolBase` возвращает `Ok` при `Completed=false`.
- **Mail Agent — Troubleshooting в README (v1.8.x, KI-107-follow)**:
  подраздел «Troubleshooting (Yandex и другие)» в `README.md`.
  Разбор типичных ошибок `MailKit.Security.AuthenticationException: LOGIN
  invalid credentials or IMAP is disabled` — 3 причины (IMAP не включён
  в веб-интерфейсе, пароль не App Password, Username не полный email),
  проверка через внешний IMAP-клиент, troubleshooting timeout,
  `Workspace:RootPath`, rate limit.

---

## [1.8.0] — 2026-09-29

**Mail Agent (IMAP/SMTP через MailKit 4.8.0, KI-107).**
Почтовый агент `mail_agent` + 7 инструментов внутри: `send_email` (approval),
`list_emails`, `read_email`, `search_emails`, `delete_email` (approval),
`move_email` (approval), `mark_as_read`. Rate limiting 20 писем/час, 30 чтений/мин.
Privacy-first (без PII в логах). Вложения в `mail-attachments/{uid}/`, ≤ 10 MB.
Глобальные credentials (App Password в User Secrets). Chat видит **12 инструментов**
(было 11). Реализовано в 5 фазах + релизная документация. Тесты: **409 → 424**.

### Added
- **DESIGN v1.8 — Mail Agent (Draft)** (`docs/development/v1.8/DESIGN_MAIL_AGENT.md`):
  дизайн-документ для почтового агента (IMAP/SMTP через MailKit 4.8.0).
  7 инструментов внутри агента `mail_agent`: `send_email` (approval),
  `list_emails`, `read_email`, `search_emails`, `delete_email` (approval),
  `move_email` (approval), `mark_as_read`. Глобальные credentials (App Password
  в User Secrets). Rate limiting 20 писем/час. Privacy-first (без PII в логах).
  Вложения в `Workspace/users/{id}/mail-attachments/{uid}/`, ≤ 10 MB.
  Целевой релиз — v1.8.0.
- **DESIGN v1.8 — External-LLM Agent (Draft)** (`docs/development/v1.8/DESIGN_EXTERNAL_LLM.md`):
  дизайн-документ для агента внешних LLM (DeepSeek / OpenAI / Groq / Together AI / Ollama).
  3 инструмента внутри агента `external_llm_agent`: `ask_external_llm` (с опциональным
  `compare_with` для сценария «сравнение»), `list_external_providers`,
  `check_internet_connection`. Оркестратор с 4 сценариями (Fallback / Специализация /
  Разные знания / Сравнение). `include_context: false` по умолчанию.
  Budget guardrails: `DailyBudgetUsd = $5`, `DailyTokensLimit = 500k`, `MaxTokens` per request.
  Circuit breaker (3 fail → skip 5 мин). Целевой релиз — v1.8.0.
- **KI — заведены 4 записи (Planned, v1.8.0/v1.8.x/v1.9+)**:
  - **KI-107** — Mail Agent (IMAP/SMTP через MailKit). v1.8.0.
  - **KI-108** — Per-user mail accounts (свой ящик у каждого пользователя). v1.8.x.
  - **KI-109** — External-LLM Agent (OpenAI-совместимые провайдеры). v1.8.0.
  - **KI-110** — Anthropic Claude + Google Gemini (свои форматы запросов). v1.9+.
- **Mail Agent — Фаза 1 (v1.8.0, KI-107)**: NuGet (MailKit 4.8.0) + DTO + интерфейсы.
  - `Directory.Build.props`: `<MailKitVersion>4.8.0</MailKitVersion>`.
  - `IIChatTools.Services.csproj`: `<PackageReference Include="MailKit" ... />`.
  - `DTO/Mail/` — 12 файлов: `MailOptions`, `MailEndpointOptions`, `MailAttachmentsOptions`,
    `MailSearchOptions`, `MailRateLimitOptions`, `MailAccountCredentials`,
    `MailMessageSummaryDto`, `MailMessageDto`, `MailAttachmentDto`, `SendMailRequest`,
    `SearchMailRequest`, `RateLimitResult`.
  - `Interfaces/` — 4 файла: `IMailClient`, `IMailAccountProvider`,
    `IMailAttachmentService`, `IMailRateLimiter`.
  - **DoD:** `dotnet build` 0/0. Все типы компилируются, но пока не используются.
- **Mail Agent — Фаза 2 (v1.8.0, KI-107)**: MailKitClient + GlobalMailAccountProvider.
  - `Implementation/Mail/GlobalMailAccountProvider.cs` — Singleton, читает `IOptions<MailOptions>`,
    возвращает `MailAccountCredentials`. Fail-fast при `Enabled=false` / пустых Host / отсутствии creds.
  - `Implementation/Mail/MailKitClient.cs` — Singleton, `IDisposable`. Реализация
    `IMailClient`: 8 методов (list/read/search/send/delete/move/mark/test).
    Per-call connect → operation → disconnect. IMAP-пул с TTL — отложен (KI-107-more).
    Privacy: в логах — только метаданные (uid, count, bytes). Вложения — Фаза 4.
  - `Startup.cs` — DI: `Configure<MailOptions>` + `Configure<MailRateLimitOptions>` +
    `AddSingleton<IMailAccountProvider, GlobalMailAccountProvider>` +
    `AddSingleton<IMailClient, MailKitClient>`.
  - `appsettings.json` / `.Development.json` — секция `Mail` (`Enabled=false` по умолчанию,
    dev — placeholder `CHANGE_ME_VIA_USER_SECRETS` для creds).
  - **Тесты:** +3 smoke (`MailKitClientSmokeTests`).
  - **DoD:** `dotnet build` 0/0. `dotnet test` — 375 → **378/378**.
- **Mail Agent — Фаза 3A (v1.8.0, KI-107)**: 3 read/mutating tool'а.
  - `Implementation/Tools/Mail/ListEmailsTool.cs` — `list_emails` (read-only).
    Параметры: `mailbox?="INBOX"`, `count=20` (clamp 1..100), `unseenOnly=false`.
  - `Implementation/Tools/Mail/ReadEmailTool.cs` — `read_email` (read-only).
    Параметры: `uid` (required), `mailbox?="INBOX"`, `saveAttachments=true`.
  - `Implementation/Tools/Mail/SendEmailTool.cs` — `send_email` (**approval**).
    Параметры: `to[]`, `cc[]?`, `bcc[]?`, `subject`, `body`, `isHtml=false`,
    `attachments[]?`. Валидация: email, ≤ 10 получателей, непустое body.
    Rate limiting (20/час) + attachments — Фаза 4.
  - `Startup.cs` — `RegisterMailTools(services, Configuration)`: 3 инструмента
    только при `Mail:Enabled = true`.
  - **Privacy:** в логах — только количество (recipients, attachments, bodyLen).
  - **Тесты:** +16 (`MailToolsTests`).
  - **DoD:** `dotnet test` — 378 → **394/394**.
- **Mail Agent — Фаза 3B (v1.8.0, KI-107)**: 4 tool'а (search / delete / move / mark_as_read).
  - `Implementation/Tools/Mail/SearchEmailsTool.cs` — `search_emails` (read-only).
    Параметры: `from?`, `subject?`, `since?` (YYYY-MM-DD), `before?`, `unseenOnly?`,
    `mailbox?="INBOX"`, `limit=20` (clamp 1..100). Требуется хотя бы 1 фильтр.
  - `Implementation/Tools/Mail/DeleteEmailTool.cs` — `delete_email` (**approval**).
    Параметры: `uid` (required), `mailbox?="INBOX"`. Перемещает в Trash.
  - `Implementation/Tools/Mail/MoveEmailTool.cs` — `move_email` (**approval**).
    Параметры: `uid` (required), `from?="INBOX"`, `to` (required).
  - `Implementation/Tools/Mail/MarkAsReadTool.cs` — `mark_as_read` (**без approval** —
    мелкое действие, approval уже был на уровне агента).
  - `Startup.cs` — `RegisterMailTools` +4 регистрации.
  - **Тесты:** +15 (`MailTools3BTests`).
  - **DoD:** `dotnet test` — 394 → **409/409**.
- **Mail Agent — Фаза 4 (v1.8.0, KI-107)**: MailAttachmentService + InMemoryMailRateLimiter.
  - `Implementation/Mail/MailAttachmentService.cs` — `IMailAttachmentService`.
    - `SaveIncomingAsync` — сохранение вложения в `{workspace}/mail-attachments/{uid}/{sha256}.ext`
      (дедупликация по SHA256, защита от path-traversal через `PathHelper`).
    - `ResolveForSendAsync` — валидация относительных путей + лимиты
      (MaxFileSize / MaxTotalSize / MaxFilesPerMessage).
  - `Implementation/Mail/InMemoryMailRateLimiter.cs` — `IMailRateLimiter`, Singleton, `IDisposable`.
    Per-user лимиты: `SendsPerHour`, `SendsPerMinute`, `ReadsPerMinute`, `MaxHourlyBytesPerUser`.
    Timer cleanup каждые 5 минут (по образцу KI-043).
  - `SendEmailTool` — интеграция `IMailRateLimiter.CheckSend` перед отправкой
    + `RecordBytesSent` после. При превышении — `ToolResult.Fail` с RetryAfter.
  - `Startup.cs` — DI: `AddSingleton<IMailRateLimiter, InMemoryMailRateLimiter>` +
    `AddScoped<IMailAttachmentService, MailAttachmentService>`.
  - **Тесты:** +11 (`MailAttachmentServiceTests` 7 + `InMemoryMailRateLimiterTests` 6).
    Плюс 7 тестов SendEmailTool обновлены (rate limiter в конструкторе) + 2 новых.
  - **DoD:** `dotnet test` — 409 → **420/420**.
  - **Отложено:** сохранение вложений при `read_email` (требует переделки `IMailClient` —
    Фаза 5+).
- **Mail Agent — Фаза 5 (v1.8.0, KI-107)**: агент `mail_agent` в Chat.
  - `Implementation/Tools/SubAgent/MailAgentTool.cs` — наследник `AgentToolBase`
    (по образцу 6 других агентов). `Name = AgentName = "mail_agent"`.
    `RequiresApprovalByDefault` резолвится из дескриптора
    (`SubAgents:mail_agent:RequiresApproval = true`).
  - `Startup.cs` — 1 строка в `RegisterSpecializedAgentTools`:
    `services.AddScoped<ITool, MailAgentTool>()`.
  - `appsettings.json` + `.Development.json` — секция `SubAgents:mail_agent`
    (`Enabled: true`, `Model: qwen3-4b`, `MaxSteps: 10`, `RequiresApproval: true`,
    `AllowedTools: [7 mail-tools]`, system prompt про «никогда не отправляй без просьбы»).
  - **ChatStreamService — НЕ требует правок:** `mail_agent` — наследник
    `AgentToolBase`, попадает в `allowedNames` через
    `SubAgentRegistry.GetEnabled()` (как 6 других агентов). RULES § 4.44
    здесь не применим (в отличие от `database_agent` — он не наследник).
  - **Chat видит 12 инструментов** (было 11): 6 агентов + consult + 3 RAG +
    `database_agent` + `mail_agent`.
  - **Тесты:** без unit (3 override'а — нечего тестировать). Smoke —
    через `/api/tools` + Chat UI.
  - **DoD:** `dotnet build` 0/0. `dotnet test` — 424/424 (без изменений).

---

## [1.7.1] — 2026-09-29

**PDF / DOCX в RAG + локализация `/status` + оригинальное имя в источниках (KI-103, KI-104, KI-105, KI-106).**
PdfParser (PdfPig 0.1.9) + DocxParser (OpenXml 3.1.0) — RAG расширен с 28 → 30 форматов.
Fix accept для `<input type="file">` в `/chat` (.pdf, .docx). Fix локализации `/status`
(4 hardcoded RU). Замена `System.IO.Packaging` 8.0.0 → 10.0.0 (транзитивная уязвимость,
KI-105). Оригинальное имя в источниках RAG для attachments (KI-106). Тесты: **341 → 375** (+34).

### Fixed
- **RAG — оригинальное имя файла в источниках для attachments (v1.7.1, KI-106)**:
  - **Симптом:** в UI-блоке «📚 Источники» под ответом ассистента для
    приложенных к чату файлов показывалось `a2a41a01…docx` (GUID) вместо
    оригинального `Договор.docx`.
  - **Причина:** `ChatAttachmentService.UploadAsync` сохраняет файл на диск
    как `{guid}.ext` (by design, KI-083 Шаг 6A), а в `IngestionRequest.Source`
    передавался `StoragePath` (= `chat-attachments/{chatId}/{guid}.ext`).
    `RagSourceBuilder.BuildLabel` берёт имя из `DocumentPath` → GUID.
  - **Fix:**
    - `ChatAttachmentService` — helper `BuildRagDocumentPath(chatId, fileName, subfolder)`
      → `"chat-attachments/{chatId}/{fileName}"`.
    - `UploadAsync`: в `IngestionRequest.Source` передаётся RAG-путь
      (оригинальное имя), не `StoragePath`.
    - `DeleteAsync`: сначала удаляет по новому пути; при 0 — fallback на
      `entity.StoragePath` (для записей до v1.7.1).
    - **Физический файл** на диске — по-прежнему `{guid}.ext` (без изменений).
  - **Старые записи** (до v1.7.1) остаются с GUID в `DocumentPath` —
    одноразовая миграция не делается (косметика).
  - **Тесты:** +1 (`UploadAsync_StoresOriginalFileName_InRagDocumentPath`).
    Тест `DeleteAsync_LegacyAttachment_FallsBackToStoragePath` — не добавлен
    (нужен `FakeIngestionService.DeleteReturnValues`; в следующий KI).
  - **Связанные:** KI-083, KI-086.
- **Admin UI — `/status` локализация badge'ей (v1.7.1, KI-103)**:
  - **Симптом:** при переключении языка на EN badge'и БД и зависимостей
    оставались на русском («Онлайн», «Установлено», «Не установлено»).
  - **Причина:** hardcoded RU-строки в `status.js` (функция `render`).
    Остальные строки `Status.cshtml` уже были через `@Localizer[...]`.
  - **Fix:**
    - `Status.cshtml` (`#status-root`) — `data-label-online`,
      `data-label-offline`, `data-label-installed`, `data-label-not-installed`.
    - `status.js` — helper `pageLabels()` (читает `data-*` → camelCase) +
      замена 4 hardcoded RU-строк на `labels.*`.
    - `.resx` (RU + EN) — **+4 ключа** (`StatusOnline`, `StatusOffline`,
      `StatusInstalled`, `StatusNotInstalled`). Синхронизация через
      `LocalizationSyncTests`.
  - **Не трогал:** `statusBadge()` — возвращает `Success`/`Error`/`Pending`/
    `Cancelled` — это **данные из API** (`AuditLog.LogStatus`), не UI.
  - **DoD:** badge'и переводятся RU/EN.

### Security
- **KI-105 — транзитивная уязвимость `System.IO.Packaging 8.0.0` (v1.7.1)**:
  - Обнаружено `dotnet restore` после добавления `DocumentFormat.OpenXml 3.1.0`
    (KI-104). **2 high-severity** advisory: `GHSA-f32c-w444-8ppv` +
    `GHSA-qj66-m88j-hmgj` (DoS) на транзитивный `System.IO.Packaging 8.0.0`
    — 6 warnings NU1903 в 3 проектах.
  - **Fix:** явный `PackageReference Include="System.IO.Packaging" Version="10.0.0"`
    в `IIChatTools.Services.csproj` — перебивает транзитивную 8.0.0.
    Прецедент — KI-022 (SQLitePCLRaw).
  - Версия в `Directory.Build.props` (`$(SystemIOPackagingVersion)`).
  - **Профилактика** (в том же коммите): скрипт
    scripts/setup/check-vulnerabilities.ps1 — обёртка над
    dotnet list package --vulnerable --include-transitive с exit-кодом
    1 при обнаружении уязвимостей. README — раздел «Проверка уязвимостей».  

### Added
- **RAG — PDF / DOCX парсеры (v1.7.1, KI-104)**:
  - **`PdfParser`** (`Implementation/Rag/Parsers/`) — Singleton, `Name = "PdfPig"`,
    расширение `.pdf`. PdfPig 0.1.9 (Apache 2.0). Извлекает текстовый слой
    через `PdfDocument.Open(bytes)` → `GetPages()` → `page.Text`. Метаданные:
    `format`, `parser`, `pageCount`. `ParsedDocument.PageCount = doc.NumberOfPages`.
    Corrupt/encrypted PDF → `InvalidDataException`.
  - **`DocxParser`** (`Implementation/Rag/Parsers/`) — Singleton, `Name = "OpenXml"`,
    расширение `.docx`. DocumentFormat.OpenXml 3.1.0 (MIT). Извлекает текст
    через `WordprocessingDocument.Open(ms, isEditable: false)` →
    `MainDocumentPart.Document.Body.Descendants<Paragraph>().InnerText`.
    Метаданные: `format`, `parser`, `paragraphCount`. `PageCount = null`.
    Corrupt / `.doc` (старый формат) → `InvalidDataException`.
  - **`Startup.cs`** — DI: `services.AddSingleton<IRagDocumentParser, PdfParser>()` +
    `services.AddSingleton<IRagDocumentParser, DocxParser>()` (после PlainText).
    Реестр (`RagDocumentParserRegistry`) подхватывает через `IEnumerable<T>`.
  - **`appsettings.json`** — `Rag:Ingestion:AllowedExtensions` + `.pdf`, `.docx`.
  - **NuGet:** `Directory.Build.props` — `<PdfPigVersion>0.1.9</PdfPigVersion>`,
    `<OpenXmlVersion>3.1.0</OpenXmlVersion>`, `<SystemIOPackagingVersion>10.0.0</SystemIOPackagingVersion>`.
  - **Тесты:** `PdfParserTests` (**16** — CanParse_T ×4, CanParse_F ×6,
    Name, Extensions, 4 `ThrowsAsync`) + `DocxParserTests` (**17** —
    CanParse_T ×4, CanParse_F ×6, Name, Extensions, 3 `ThrowsAsync`,
    ValidDocx, EmptyDocx). Реальный PDF-контент не проверяется
    (PdfPig read-only); DOCX генерируется самим OpenXml SDK.
    **Всего: 341 → 374**.

---

## [1.7.0] — 2026-09-29

**Database Agent — read-only SQL-доступ LLM к БД приложения (KI-097).**
4 действия инструмента `database_agent` (`list_databases` / `list_tables` /
`describe_table` / `execute_query`), 5 уровней безопасности (read-only роль,
валидатор, whitelist, timeout+auto-LIMIT, approval+audit), admin UI
`/admin → SQL Agent`, per-action approval (KI-101), fix локализации `/admin`
(KI-102). Тесты: **312 → 341**.

### Added
- **Database Agent — Фаза 1: контракты + DTO (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.1)**:
  - **DTO (`DTO/SqlAgent/`)** — 5 файлов: `DatabaseConnectionInfoDto`,
    `SqlTableInfoDto`, `SqlColumnInfoDto`, `SqlQueryRequest`, `SqlQueryResultDto`.
  - **Interfaces (`Interfaces/`)** — 3 файла: `ISqlAgentService` (оркестратор
    list / describe / execute), `ISqlQueryValidator` (валидация SQL),
    `ISqlConnectionProvider` (фабрика `DbConnection`).
  - **DTO (`DTO/SqlAgent/`)** — расширено: +3 файла `ValidationResult`
    (результат валидации), `SqlAgentOptions` (bind из `appsettings:SqlAgent`),
    `SqlAgentConnectionOptions` (подсекция `Connections[*]`).
    <br/>**Почему DTO, а не Implementation:** `SqlAgentConnectionOptions`
    используется в сигнатуре `ISqlQueryValidator.Validate(...)`, значит
    является частью **контракта**. `Implementation → Interfaces` — да,
    `Interfaces → Implementation` — **нет** (нарушение слоистости).
    Аналогично `ISubAgentRegistry` → `DTO/SubAgent/SubAgentDescriptor`
    и `IChunkingStrategy` → `ChunkingOptions`.
  - **DoD Фазы 1:** `dotnet build` 0/0, `dotnet test` 241/241.
    Все типы компилируются, но пока нигде не используются.
    Реализация — Фазы 2-6.
  - **Правка DESIGN_DB_AGENT § 4.1 и § 7.1:** 3 типа перенесены
    из `Implementation/SqlAgent/` в `DTO/SqlAgent/` (фикс слоистости
    до первого использования).
- **Database Agent — Фаза 2: SqlConnectionProvider + SqlAgentOptionsProvider (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.2)**:
  - **`SqlConnectionProvider`** (`Implementation/SqlAgent/`) — Singleton,
    реализация `ISqlConnectionProvider`. **Switch по `Provider`** вместо
    рефлексии: compile-time проверка, скорость, AOT-совместимость.
    Connection string резолвится из `IConfiguration` при каждом вызове
    (User Secrets / env), без кэша.
  - **`SqlAgentOptionsProvider`** (`Implementation/SqlAgent/`) — Singleton,
    хранит baseline из `IOptions<SqlAgentOptions>` + runtime-overrides
    (для будущей админки, Шаг 6). Потокобезопасен (`lock`). Валидация
    при старте (DESIGN § 5.5): `DefaultConnection` обязателен, `MaxRows` /
    `StatementTimeoutSeconds` — clamp + warning.
  - **`Startup.cs`** — DI-регистрация: `services.Configure<SqlAgentOptions>(...)`
    + `SqlAgentOptionsProvider` (Singleton) + `ISqlConnectionProvider`
    (Singleton).
  - **`Program.cs`** — новый метод `LoadSqlAgentOverrides` (без Async —
    вызывается один раз при старте): читает `AppSettings` с ключами
    `SqlAgent.{name}.{field}`, применяет к провайдеру.
  - **`appsettings.json`** / **`appsettings.Development.json`** — секция
    `SqlAgent` (Connection internal, AllowedTables / DeniedTables,
    QueryValidation). Dev: `Provider=Sqlite`, `MaxRows=50`;
    Prod: `Provider=SqlServer`, `MaxRows=100`.
  - **NuGet** (`Directory.Build.props` + `IIChatTools.Services.csproj`):
    `Microsoft.Data.Sqlite` 10.0.12 + `Microsoft.Data.SqlClient` 6.0.2 —
    ADO.NET-провайдеры для `SqliteConnection` / `SqlConnection`.
  - **`README.md`** — инструкция User Secrets для
    `SqlAgent:Internal:ConnectionString` (dev — Sqlite `Mode=ReadOnly`,
    prod — SqlServer `ApplicationIntent=ReadOnly`).
  - **Тесты** — `SqlConnectionProviderSmokeTests` (4, включая
    DoD-тест «connection string разрешается из config»). Полный набор —
    Фаза 7 (`SqlConnectionProviderTests` + `SqlQueryValidatorTests`).
  - **DoD Фазы 2:** `SqlConnectionProvider` открывает соединение `internal`
    (Sqlite), резолвит connection string из `IConfiguration`.
- **Database Agent — Фаза 3: SqlQueryValidator (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.3)**:
  - **`SqlQueryValidator`** (`Implementation/SqlAgent/`) — Singleton (stateless),
    реализация `ISqlQueryValidator`. **7 шагов валидации** (DESIGN § 6.2):
    <list type="number">
      1. Базовые проверки (пустой, длина, первый токен — SELECT/WITH, multi-statement);
      2. Токенизация (литералы и комментарии **исключаются** из проверок);
      3. Запрет ключевых слов (INSERT, DELETE, DROP, ...);
      4. Запрет функций (load_extension, readfile, ...);
      5. Извлечение таблиц (FROM/JOIN, поддержка schema.table и CTE);
      6. Whitelist / blacklist (Sqlite — case-sensitive, SqlServer — insensitive);
      7. Auto-LIMIT (перед `;`, если LIMIT отсутствует).
    </list>
  - **`SqlToken` / `SqlTokenKind`** (`Implementation/SqlAgent/`, internal) —
    внутренние типы токенизатора. Классификация: Identifier, StringLiteral,
    QuotedIdentifier, Comment, Number, Punctuation. Escape-последовательности
    (`''` в строках, `""` в quoted identifier) обрабатываются.
  - **Startup.cs** — DI-регистрация: `services.AddSingleton<ISqlQueryValidator, SqlQueryValidator>()`.
  - **Тесты** — `SqlQueryValidatorTests` (**27 тестов**):
    базовые (5), keyword-denial (5), function-denial (3), whitelist (10),
    auto-LIMIT (4), комплексный пример из DESIGN § 6.2 (Приложение A).
    Покрытие `SqlQueryValidator` — **> 90%**.
  - **DoD Фазы 3:** валидатор отклоняет `DELETE` / `DROP` / `AspNetUsers` /
    multi-statement, пропускает `SELECT 'DROP TABLE Chats' AS x` (литерал не команда),
    `WITH cte AS (...) SELECT ...` (CTE не таблица). Auto-LIMIT работает.
  - **Fix (в том же коммите, №1):** CS1503 — `HashSet<string>` требует
    `StringComparer` (`IEqualityComparer<string>`), а не `StringComparison`
    (enum для `string.Equals`). Одна строка в `SqlQueryValidator.Validate` (шаг 6).
  - **Fix (в том же коммите, №2):** тестовый хелпер `SqliteProvider` в
    `SqlQueryValidatorTests` создавал `SqlAgentOptions` с пустым
    `Connections`, из-за чего конструктор `SqlAgentOptionsProvider` падал
    с `InvalidOperationException: DefaultConnection='internal' отсутствует
    в Connections` (baseline-валидация DESIGN § 5.5). 29 тестов не
    доходили до валидатора SQL. Fix: добавили минимальный `internal` в
    `Connections` — реальные опции подключения всё равно передаются в
    `Validate(sql, options)` отдельным аргументом.

- **Database Agent — Фаза 4: SqlAgentService + fix KI-100 (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.4)**:
  - **`SqlAgentService`** (`Implementation/SqlAgent/`) — Scoped, реализация
    `ISqlAgentService`. 4 операции: `ListConnectionsAsync`, `ListTablesAsync`,
    `DescribeTableAsync`, `ExecuteQueryAsync`.
    - `ListConnectionsAsync` — метаданные из `SqlAgentOptionsProvider` (без connection string).
    - `ListTablesAsync` — whitelist минус DeniedTables + `SELECT COUNT(*)` (best-effort, -1 при ошибке).
    - `DescribeTableAsync` — провайдер-специфичный запрос (`PRAGMA table_info` для Sqlite,
      `INFORMATION_SCHEMA.COLUMNS` для SqlServer) + пример значения (первая непустая, ≤200 символов).
    - `ExecuteQueryAsync` — валидатор → соединение → `CommandTimeout` →
      `DataReader` с защитой от UNION-обхода (читаем до `MaxRows`, флаг `truncated`).
    - Динамические идентификаторы через `QuoteIdentifier` (`[Name]` с escape `]]`).
    - **Провайдер-специфичные whitelist-сравнения**: Sqlite — `Ordinal` (case-sensitive),
      SqlServer — `OrdinalIgnoreCase` (DESIGN § 6.3).
  - **KI-100 Fix:** `SqlConnectionProvider` резолвит **относительный** `Data Source` Sqlite
    относительно `IWebHostEnvironment.ContentRootPath` (через новый `IAppPathProvider`).
    Без этого фикса первый `execute_query` упал бы с `SQLite Error 14: unable to open
    database file` при запуске не из `IIChatTools.API`.
    - Новый интерфейс `IAppPathProvider` (`Interfaces/`) + `AppPathProvider` (`Implementation/`).
    - Регистрация: `services.AddSingleton<IAppPathProvider>(...)` на основе `IWebHostEnvironment`.
    - `:memory:` и абсолютные пути — не трогаются. Префикс `file:` — тоже.
  - **`Startup.cs`** — DI: `IAppPathProvider` (Singleton) + `ISqlAgentService` (Scoped).
  - **Тесты** — `SqlAgentServiceTests` (**11**), `SqlConnectionProviderSmokeTests` (**+1** на KI-100).
    Всего: **274 → 286**.
  - **DoD Фазы 4:** `SELECT COUNT(*) FROM Chats` возвращает число через
    `ISqlAgentService`; Auto-LIMIT работает; невалидный SQL бросает `ArgumentException`.
  - **Fix (в том же коммите, №1):** `IReadOnlyList<T>` не имеет `.Find` (это instance-метод
    `List<T>`) — в `SqlAgentServiceTests` заменено на LINQ `FirstOrDefault` (×3, +`using System.Linq;`).
    Плюс `Assert.Equal(1, ...Count)` → `Assert.Single(...)` (xUnit2013).
- **Database Agent — Фаза 5: DatabaseAgentTool + интеграция в Chat (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.5)**:
  - **`DatabaseAgentTool : ITool`** (`Implementation/Tools/SqlAgent/`) —
    top-level инструмент (**не** `AgentToolBase` — DESIGN § 4.1).
    Диспетчеризует 4 действия через `ISqlAgentService`:
    `list_databases` / `list_tables` / `describe_table` / `execute_query`.
    Параметры: `action` (required), `connection` (optional),
    `table` (optional), `sql` (optional), `maxRows` (optional).
  - **`Startup.cs`** — новый метод `RegisterSqlAgentTools(services, configuration)`:
    регистрирует `ITool → DatabaseAgentTool` (Scoped), но **только если
    `SqlAgent:Enabled = true`** (DESIGN § 3.4).
  - **`ChatStreamService`** — константа `DatabaseAgentToolName = "database_agent"`
    + блок в `allowedNames` (RULES § 4.44). Без этого LLM не увидел бы tool в `tools[]`.
  - **Тесты** — `DatabaseAgentToolTests` (**13**, включая `ThrowOnExecute`
    для проверки обработки ошибок валидатора). Всего: **286 → 299**.
  - **Approval:** `RequiresApprovalByDefault = true` (все 4 действия).
    **Отклонение от DESIGN § 3.3** (там был per-action approval):
    текущая архитектура `ChatStreamService` не поддерживает per-action —
    флаг `RequiresApprovalByDefault` один на tool. Заведена **KI-101** (Deferred, v1.7.x).
  - **DoD Фазы 5:** Chat видит **11 инструментов** (было 10).
    LLM может вызвать `database_agent` и получить ответ на вопрос про БД.
- **Database Agent — Фаза 5.5: per-action approval (v1.7.0, KI-101 — Fixed)**:
  - **Архитектурный паттерн:** в `ITool` добавлен **default-метод**
    `RequiresApprovalForCall(JObject arguments)` с fallback на
    `RequiresApprovalByDefault`. Все 46 существующих инструментов работают
    без изменений (C# 8+ default interface method).
  - **`IToolRegistry.GetTool(string name)`** — новый метод + реализация
    в `ToolRegistry`.
  - **`ChatStreamService`** — блок `requiresApproval` переписан:
    `toolInstance?.RequiresApprovalForCall(args) ?? true`.
  - **`DatabaseAgentTool.RequiresApprovalForCall`** — override:
    только `execute_query` требует approval. Метаданные
    (`list_databases` / `list_tables` / `describe_table`) — без approval.
  - **`Description` усилен:** явно просит LLM для вопросов про количество
    использовать **сразу** `execute_query` (`SELECT COUNT(*)`), не делать
    «разведку» через `list_databases` / `list_tables`.
  - **Тесты:** +6 (в `DatabaseAgentToolTests`) + 4 (в `ToolRegistryTests`).
    Всего: **300 → 310**.
  - **Fix (в том же коммите, CS0535 ×2):** после расширения `IToolRegistry.GetTool`
    два fake-класса в `ChatStreamServiceTests` (`FakeToolRegistry`, `EmptyToolRegistry`)
    не реализовали новый член. RULES § 4.34 — расширение интерфейса требует
    grep по **всем** fake-заглушкам. `FakeToolDef` расширен до `: ITool` (для
    корректного `RequiresApprovalForCall`), добавлен `GetTool` в оба fake-класса.
  - **Fix (в том же коммите, CS1061):** в `ToolRegistryTests` тест
    `RequiresApprovalForCall_DefaultImplementation_ReturnsRequiresApprovalByDefault`
    объявлял переменные типом конкретного класса (`FakeToolWithApproval`),
    который не переопределяет метод. **Default interface method (C# 8+)**
    доступен только через интерфейсную переменную — иначе CS1061.
    Fix: `ITool toolFalse = new FakeToolWithApproval()`. Заведено
    **RULES § 4.46** (default interface method не виден через конкретный тип).
  - **DoD Фазы 5.5:** при вопросе «Сколько чатов?» — **1 модалка** approval
    (на `execute_query`), а не 3 (как в smoke Фазы 5).
- **Database Agent — Фаза 6A: Admin-сервис (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6)**:
  - **3 DTO** (`DTO/Admin/`): `SqlAgentConnectionItemDto` (состояние подключения
    для UI, включая `IsOverridden`), `UpdateSqlAgentConnectionRequest` (все поля
    опциональны — null = не менять), `SqlAgentTestResultDto` (Success/Message/DurationMs).
  - **`IAdminSqlAgentService` + `AdminSqlAgentService`** (Scoped):
    - `GetAllConnectionsAsync` — актуальные опции (override + baseline) + флаг `IsOverridden`.
    - `UpdateConnectionAsync` — валидация (MaxRows 1–10000, Timeout 1–300),
      upsert override в `AppSettings` (ключи `SqlAgent.{name}.{field}`),
      применение к `SqlAgentOptionsProvider` в runtime, аудит.
    - `TestConnectionAsync` — открывает соединение (даже для `Enabled=false`)
      + `SELECT 1`; все ошибки возвращаются в DTO, не бросаются.
    - `ResetConnectionAsync` — удаляет все ключи `SqlAgent.{name}.*` из AppSettings,
      вызывает `SqlAgentOptionsProvider.Reset(name)`, аудит.
  - **`ISqlConnectionProvider.CreateConnectionAsync`** — новый параметр
    `bool ignoreEnabled = false` (default — прежнее поведение). Используется
    только admin-сервисом для теста отключённых подключений.
  - **`SqlConnectionProvider`** + fake в `SqlAgentServiceTests` — обновлены
    под новую сигнатуру.
  - **`Startup.cs`** — DI: `services.AddScoped<IAdminSqlAgentService, AdminSqlAgentService>()`.
  - **DoD Фазы 6A:** сервис собирается, baseline + override работают,
    `TestConnectionAsync` возвращает Success/Message/DurationMs.
  - **Fix (в том же коммите, CS1503 ×3):** после добавления параметра
    `bool ignoreEnabled = false` в середину `ISqlConnectionProvider.CreateConnectionAsync`
    три **позиционных** вызова в `SqlAgentService` (`ListTablesAsync` /
    `DescribeTableAsync` / `ExecuteQueryAsync`) стали передавать `cancellationToken`
    в слот `ignoreEnabled`. Fix: именованные аргументы
    `CreateConnectionAsync(connection, cancellationToken: cancellationToken)`.
    RULES § 4.34 — уточнён: «изменение сигнатуры метода → grep по вызовам,
    не только по реализациям» (случай б).
- **Database Agent — Фаза 6B: AdminSqlAgentController (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6)**:
  - **`AdminSqlAgentController`** (`IIChatTools.API/Controllers/`) — 4 endpoint'а:
    <list type="bullet">
      <item><description><c>GET /api/admin/sql-agent/connections</c> — список подключений;</description></item>
      <item><description><c>PUT /api/admin/sql-agent/connections/{name}</c> — обновить override-настройки;</description></item>
      <item><description><c>POST /api/admin/sql-agent/connections/{name}/test</c> — SELECT 1 (работает и для Enabled=false);</description></item>
      <item><description><c>POST /api/admin/sql-agent/connections/{name}/reset</c> — сбросить к baseline.</description></item>
    </list>
  - Формат ответа `{ success, data }` / `{ success: false, message }` (RULES § 1.6).
  - `[Authorize(Policy = "AdminOnly")]` — только администраторы.
  - `IStringLocalizer<SharedResources>` — общие сообщения (ошибки сервера).
  - Аудит действий — внутри `AdminSqlAgentService` (не дублируется).
  - **DoD Фазы 6B:** все 4 endpoint'а доступны администратору,
    изменения whitelist применяются в runtime без рестарта.
- **Database Agent — Фаза 6C+6D: UI + локализация (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6)**:
  - **9-я вкладка «SQL Agent»** в `Admin.cshtml` (после «База знаний»).
    Таблица: name (с бейджем «изменено» при override) / displayName / provider /
    enabled / allowed tables / maxRows / timeout / actions.
  - **`admin-sql-agent.js`** (новый модуль, ~250 строк):
    - `loadConnections` / `renderConnectionsTable` — таблица подключений;
    - `openEditModal` — модалка (переиспользует `showModal` из `admin.js`);
      редактирование whitelist/blacklist (textarea построчно), MaxRows, Timeout, Enabled;
    - `testConnection` — `SELECT 1` через `POST .../test`;
    - `resetConnection` — сброс к baseline через `POST .../reset`;
    - локализация — через `data-*` на `#pane-sql-agent` (RULES § 4.17).
  - **`.resx` (RU + EN)** — **22 ключа** (`AdminTabSqlAgent`,
    `SqlAgentColumn*`, `SqlAgentEditTitle`, `SqlAgentReset*`, `SqlAgentTest*`,
    `SqlAgentEnabledLabel`, `SqlAgentAllowedTables`, `SqlAgentDeniedTables`,
    `SqlAgentMaxRowsLabel`, `SqlAgentTimeoutLabel`, `SqlAgentNoConnections`,
    `SqlAgentOverriddenBadge`). Синхронизированы через `LocalizationSyncTests`.
  - **DoD Фазы 6C+6D:** через `/admin → SQL Agent` можно:
    (а) видеть список подключений с бейджем override,
    (б) редактировать whitelist/MaxRows/Timeout/Enabled в модалке,
    (в) проверить подключение (`SELECT 1`),
    (г) сбросить к значениям из appsettings.json. Все изменения — в runtime.
- **Admin UI — Фаза 6E: Fix локализации (v1.7.0, KI-102)**:
  - **Баг** в `admin.js` (`loadWhitelist`, empty-state): literal
    `@Localizer["Убрать из белого списка"]` — синтаксис Razor **не работает**
    в `.js`-файлах. Проявлялся как raw-текст при добавлении инструмента
    в whitelist. Устранён.
  - **Hardcoded RU-строки** в `admin.js` (12), `admin-agents.js` (15),
    `admin-sql-agent.js` (3) — заменены на `data-label-*` (RULES § 4.17).
  - **`Admin.cshtml`** — добавлены `data-label-*` на 4 панели
    (`pane-users`, `pane-settings`, `pane-whitelist`, `pane-agents`).
  - **`.resx` (RU + EN)** — **+30 ключей** (`AdminUser*`, `AdminSettings*`,
    `AdminWhitelist*`, `AdminAgent*`, `AdminAgentModal*`, `AdminTime*`,
    `SqlAgentMaxRowsValidation`, `SqlAgentTimeoutValidation`,
    `SqlAgentConnectionNotFound`). Синхронизация проверяется
    `LocalizationSyncTests`.
  - **Helpers:** `paneLabels(paneId)` в `admin.js` (читает `data-*` →
    camelCase) + локальный `paneLabels()` в `admin-agents.js`.
  - **DoD Фазы 6E:** при переключении RU/EN весь UI `/admin` переводится
    (users / settings / whitelist / agents / SQL Agent).
- **Database Agent — Фаза 7A+7B: тесты admin-слоя (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.7)**:
  - **`AdminSqlAgentServiceTests`** (**13 тестов**):
    `GetAllConnectionsAsync` (2), `UpdateConnectionAsync` — валидация (4),
    persist в AppSettings (2), runtime-применение (1), аудит (1),
    `TestConnectionAsync` (3), `ResetConnectionAsync` (3).
    Fake `IAppSettingsService` (in-memory CRUD) + `Mock<IAuditService>` +
    реальный `SqlAgentOptionsProvider` + `SqliteConnection(":memory:")`.
  - **`AdminSqlAgentControllerTests`** (**7 тестов**):
    `GetConnectionsAsync` (1), `UpdateConnectionAsync` (3 — valid / null / ArgumentException),
    `TestConnectionAsync` (1), `ResetConnectionAsync` (2 — valid / throw).
    Fake `IAdminSqlAgentService` + `FakeStringLocalizer` (IStringLocalizer<T>).
    Прямой вызов контроллера (без `WebApplicationFactory`) — консистентно
    с `AdminKnowledgeControllerTests`.
  - **DoD Фазы 7A+7B:** все admin-endpoints Database Agent покрыты unit-тестами.
    Всего: **312 → 332**.
  - **KI-103** (Documented, план — v1.7.x): на `/status` часть UI — hardcoded RU
    в `status.js` (не входит в 6E — там только `/admin`).
- **Database Agent — Фаза 7C: интеграционные тесты + документация (v1.7.0, KI-097)**:
  - **`SqlAgentIntegrationTests`** (**3 теста**): сквозной путь
    `DatabaseAgentTool → ISqlAgentService → ISqlQueryValidator → ISqlConnectionProvider`
    → реальная Sqlite-БД (временный файл). Проверяет:
    - `ExecuteQuery_ValidSelect_ReturnsRowCount` — успешный SELECT; проверяет
      `Data.Rows[0]["Total"] == 3` (не `Message` — там «Возвращено 1 строк»,
      т.к. это 1 строка с COUNT(*)).
    - `ExecuteQuery_DeniedTable_ViaTool_ReturnsFail` — валидатор отбивает `AspNetUsers`.
    - `ListDatabases_ViaTool_ReturnsInternalConnection`.
  - **README.md** — обновлён:
    - Chat видит **11** инструментов (было 10).
    - Таблица «Инструменты»: +3 RAG +1 Database Agent = **50**.
    - **Новый раздел «Database Agent (v1.7.0)»**: 4 действия, 5 уровней
      безопасности, примеры вопросов, конфигурация, ограничения.
    - API endpoints: +4 строки (`/api/admin/sql-agent/...`).
    - Счётчик тестов: 241 → **341**.
    - **Fix разметки**: восстановлен блок «Миграции и запуск»
      (незакрытый `markdown` code-block из прошлого diff'а).
  - **KNOWN_ISSUES.md:**
    - **KI-097** → **Fixed (v1.7.0)**. Тело: 13 коммитов, 51 тест, все фазы 0-7.
    - **KI-098** → **Fixed (v1.7.0, Фазы 6A-6E)**.
    - **Сводка по статусам:** `Fixed (v1.7.0) = 4`, `Planned = 1`, `Documented = 9`.
      Всего: **62**.
  - **DoD Фазы 7C:** сквозной путь Database Agent проверен integration-тестами,
    документация (README + KNOWN_ISSUES) обновлена. **Всего тестов: 341**.
  - **Fix (в том же коммите, №1):** интеграционный тест проверял `result.Message`
    на contains «3» — но там количество **строк результата** (1 строка с COUNT(*)),
    а не значение COUNT(*). Fix: проверять `Data.Rows[0]["Total"] == 3L`.
  - **Fix (в том же коммите, №2):** README — секция «Миграции и запуск»
    содержала служебную копию diff'а («Заменить на:» + незакрытый
    ` ```markdown `). Восстановлена: два code-блока (`SqlServer` + `Sqlite`)
    + цитата про `EnsureCreatedAsync`.
- **Database Agent — Фаза 7D: TESTING.md (v1.7.0, KI-097, KI-088)**:
  - **`docs/TESTING.md`** — версия 1.5.0 → **1.7.0**:
    - Шапка: связанные DESIGN + KI-097.
    - § 1: 199/199 → **341/341**; ~15 → ~20 минут.
    - § 2 (Smoke): 10 → **16 сценариев** (+5 для Database Agent: #12–16).
    - **§ 3.5 (новый)** — Database Agent (7 сценариев): whitelist, reset,
      runtime-применение, отказы валидатора, `Enabled=false`.
    - § 4 (UI/UX): +2 проверки локализации (`/admin → SQL Agent` + остальные
      admin-вкладки, KI-102).
    - § 5: обновлён счётчик автотестов + добавлены 5.11 (DB Agent end-to-end)
      и 5.12 (runtime-применение overrides).
    - § 6: ссылка на DESIGN v1.7.
    - § 7: строки v1.6.0 и v1.7.0.
  - **DoD Фазы 7D:** TESTING.md полностью отражает v1.7.0.
    KI-097 → Fixed (Фаза 7 полностью закрыта).
  - **Fix (в том же коммите, №2):** `ExecuteQueryAsync` не выставлял `Truncated = true`,
    когда auto-LIMIT был добавлен валидатором. Причина: SQLite/SqlServer **сам** обрезает
    результат по `LIMIT`, reader возвращает ровно `MaxRows` строк, лишней итерации цикла нет,
    и `if (rows.Count >= maxRows)` не срабатывает. Добавлена post-loop проверка:
    `if (!truncated && validation.LimitAdded && rows.Count >= maxRows) truncated = true;`
    (консервативно — false positive возможен, если в таблице ровно `MaxRows` строк;
    принимается как меньшая из зол).
  - **KI-100 (Documented, план — Фаза 4):** относительный путь Sqlite
    connection string (`Data Source=Data/iichattools-dev.db`) резолвится от
    `Environment.CurrentDirectory`, а не от `ContentRootPath`. Проявится в
    Фазе 4 при первом `execute_query`. План: резолвить относительно
    `IWebHostEnvironment.ContentRootPath` в `SqlConnectionProvider`.

### Changed
- **Docs — PROMPT_V2.md v2.4 → v2.5 (post-release v1.6.1)**:
  - Шапка: актуальный релиз `v1.6.0` → **v1.6.1**.
  - Метрики: 216 → **241** тестов.
  - «Что выпущено» — расширено до v1.6.1 (Sources / citations для Web-tools).
  - Roadmap: **v1.6.1 → Done**, v1.6.x → **v1.6.2** (KI-094, KI-095)
    и **v1.7.0** (KI-096 GitHub Wiki + инфраструктура).
  - «Подводные камни» — добавлены: 4-полевой ключ дедупа sources,
    проброс sources через агентов, KI-094 (Wikipedia timeout).
  - «Известные факты про LM Studio» — обновлено: KI-064 + KI-094.

---

## [1.6.1] — 2026-09-28

**Sources / citations для Web-tools (KI-086-post).** Продолжение v1.6.0:
`wikipedia_search`, `web_search`, `fetch_web_content` теперь возвращают
citations → блок «📚 Источники» в UI показывает **все** источники (RAG + web + wiki).
Sources пробрасываются через агентов (`SubAgentTaskResult.Sources`).
Кросс-платформенный fix `DocumentPath` (относительные пути вместо абсолютных).

**Тесты:** 219 → **241** (+22).

### Added
- **Sources — `fetch_web_content` возвращает citation (v1.6.1, KI-086-post, Шаг A4)**:
  - `FetchWebContentTool.ExecuteAsync` — `ToolResult.Ok(data, message, sources)`,
    где `sources` = `[WebSourceBuilder.BuildSingle(title, url, text, "web")]`.
  - `label = <title>` страницы (fallback → `url`), `url = запрошенный URL`,
    `snippet` — первые 200 символов очищенного текста.
  - **Не покрыто (осознанно):** если `extractText = false` (HTML-режим) —
    `sources` = `null` (для отладки, snippet не имеет смысла).

### Added
- **Sources — тесты `WebSourceBuilder` (v1.6.1, KI-086-post, Шаг A5)**:
  - **22** теста (`WebSourceBuilderTests.cs`, включая `[Theory]`-наборы):
    - `Build`: null / empty / valid / limit / skip-empty / dedup-by-url /
      normalize-type ×5 / truncate-snippet.
    - `BuildSingle`: empty-url ×3 / valid-url.
    - `BuildLabel`: fallback-chain ×6.
  - **Тесты:** 219 → **241** (+22).

### Added
- **Sources / KI — записи в KNOWN_ISSUES (v1.6.1, KI-086-post, Шаг A5)**:
  - **KI-094** — `wikipedia_search` intermittent timeout (SSL через прокси).
    Documented, план v1.6.2. Fallback `web_search` закрывает UX.
  - **KI-095** — snippet `fetch_web_content` может дублировать label
    (h1 = title на некоторых страницах). Documented, не баг.
  - **KI-096** — GitHub Wiki для проекта (roadmap v1.7+).
    Scope: публичная wiki / RAG-индексация / автосинхронизация.

### Added
- **Sources — `web_search` возвращает citations (v1.6.1, KI-086-post, Шаг A3)**:
  - `WebSearchTool.ExecuteAsync` — `ToolResult.Ok(data, message, sources)`.
  - `sources` — `WebSourceBuilder.Build(retrieved, "web", maxCount: 5)`:
    `type = "web"`, `label = title`, `url` — распакованный DuckDuckGo-редирект,
    `snippet` — HTML-очищенный (≤ 200 символов).
  - **Fallback-эффект:** даже если `wikipedia_search` упал по timeout (KI-064),
    в `web_agent` отработает `web_search` → citations придут в UI.

### Added
- **Sources — проброс citations через агентов (v1.6.1, KI-086-post, Шаг B)**:
  - **Проблема:** LLM в Chat вызывает `web_agent` (не `wikipedia_search` напрямую).
    Внутри `web_agent` — `wikipedia_search` возвращает `ToolResult.Sources`,
    но `SubAgentService` их **отбрасывал**, отдавая наружу только `finalAnswer`.
  - `SubAgentTaskResult.Sources` (`IReadOnlyList<ChatSourceDto>`) — новое поле.
    Собирается в `SubAgentService` из `ToolResult.Sources` всех inner-вызовов
    с дедупликацией по ключу `(Type|DocumentPath|Url|ChunkIndex)`.
  - `AgentToolBase.ExecuteAsync` — проброс `result.Sources` в `ToolResult.Ok(...)`.
  - `ConsultSecondaryAgentTool.ExecuteAsync` — то же.
  - **Тесты:** +3 в `AgentToolBaseTests` (sources / null / empty).

### Added
- **Sources — `wikipedia_search` возвращает citations (v1.6.1, KI-086-post, Шаг A2)**:
  - `WikipediaSearchTool.ExecuteAsync` возвращает `ToolResult.Ok(data, message, sources)`.
  - `sources` — `WebSourceBuilder.Build(retrieved, "wiki", maxCount: limit)`:
    `type = "wiki"`, `label = title`, `url = https://{lang}.wikipedia.org/?curid={pageId}`,
    `snippet` — HTML-очищенный extract из search API (≤ 200 символов).

### Added
- **Sources — Web-tools: `RetrievedWebResult` + `WebSourceBuilder` (v1.6.1, KI-086-post, Шаг A1)**:
  - `RetrievedWebResult` (`DTO/Rag/`) — унифицированный результат веб-поиска
    (`Title`, `Url`, `Snippet`).
  - `WebSourceBuilder` (`Implementation/Rag/`) — статический хелпер:
    `Build` / `BuildSingle` / `BuildLabel`; переиспользует
    `RagSourceBuilder.TruncateSnippet` (≤ 200 символов с «…»).

### Fixed
- **Sources — snippet `fetch_web_content` дублировал `<title>` (v1.6.1, KI-086-post, Шаг A4.fix / A4.fix2)**:
  - **Симптом:** `snippet` начинался с `Example DomainExample DomainThis domain is...`.
  - **Причина:** `FetchWebContentTool` вырезал `<script>`, `<style>`, `<noscript>`,
    но **не** `<head>` (где живёт `<title>`).
  - **Fix:** добавил `//head` в список удаляемых узлов.
  - **Регрессия A4.fix:** удаление `//head` ДО `SelectSingleNode("//title")` →
    `title=null` → `label=url` вместо `Example Domain`.
  - **Fix A4.fix2:** сначала читаем `//title`, потом удаляем узлы.
  - **Остаток** («Example Domain» в snippet) — это `<h1>` в `<body>`, реальный
    контент, не дубль. См. KI-095.

### Fixed
- **Sources — дедупликация схлопывала web/wiki-источники в один (v1.6.1, KI-086-post, Шаг B.fix)**:
  - **Симптом:** `web_agent` возвращает 7 sources (в curl), но в UI-блоке
    «📚 Источники» отображается только **1**.
  - **Причина:** в `ChatStreamService.AddSourcesToAccumulator` (Шаг 3 v1.6.0)
    ключ дедупликации был `(Type|DocumentPath|ChunkIndex)` — **без `Url`**.
    Для RAG-чанков работало, для web/wiki — все источники схлопывались в один.
  - **Fix:**
    - `ChatStreamService.AddSourcesToAccumulator` — ключ `(Type|DocumentPath|Url|ChunkIndex)`
      с `?? string.Empty` (симметрично `SubAgentService`).
    - `WebSourceBuilder.Build` — внутренняя дедупликация по `Url`.

### Fixed
- **Sources — относительные пути в `DocumentChunk.DocumentPath` (v1.6.1, KI-086-post)**:
  - **Симптом:** в `sources[i].documentPath` уходил абсолютный путь
    `C:\Projects\AI\IIChatTools\docs\development\RULES.md`.
  - **Причина:** `DocumentIngestionService.GetTextFromSourceAsync` для
    `SourceType.File` возвращал `request.FilePath` (абсолютный), игнорируя
    `request.Source` (относительный), который передают все три caller'а.
  - **Fix:** `request.Source` (если задан) → `DocumentPath`; fallback на
    `request.FilePath` для обратной совместимости.
  - **Сопутствующий fix:** `ChatAttachmentService.DeleteAsync` — путь для
    `DeleteDocumentAsync` берётся из `entity.StoragePath`.

### Added
- **Sources — тесты `WebSourceBuilder` (v1.6.1, KI-086-post, Шаг A5)**:
  - 15 тестов (`WebSourceBuilderTests.cs`):
    - `Build`: null / empty / valid / limit / skip-empty / dedup-by-url /
      normalize-type ×5 / truncate-snippet.
    - `BuildSingle`: empty-url ×3 / valid-url.
    - `BuildLabel`: fallback-chain ×6.
  - **Тесты:** 219 → **234** (+15).

### Added
- **Sources / KI — записи в KNOWN_ISSUES (v1.6.1, KI-086-post, Шаг A5)**:
  - **KI-094** — `wikipedia_search` intermittent timeout (SSL через прокси).
    Documented, план v1.6.2. Fallback `web_search` закрывает UX.
  - **KI-095** — snippet `fetch_web_content` может дублировать label
    (h1 = title на некоторых страницах). Documented, не баг.
  - **KI-096** — GitHub Wiki для проекта (roadmap v1.7+).
    Scope: публичная wiki / RAG-индексация / автосинхронизация.

### Fixed
- **Sources — snippet `fetch_web_content` дублировал `<title>` (v1.6.1, KI-086-post, Шаг A4.fix)**:
  - **Симптом:** `snippet` начинался с `<title>` (`Example Domain...`),
    label = URL вместо «Example Domain».
  - **Причина:** первая правка удаляла `<head>` (где живёт `<title>`)
    **до** `SelectSingleNode("//title")` — title → null → fallback на url.
  - **Fix:** сначала извлекаем `//title`, потом удаляем `<script>|<style>|<noscript>|<head>`,
    потом `InnerText`. Остаток «Example Domain» в snippet — это `<h1>`
    внутри `<body>` (реальный контент страницы, не дубль `<title>`).

### Added
- **Sources — `fetch_web_content` возвращает citation (v1.6.1, KI-086-post, Шаг A4)**:
  - `FetchWebContentTool.ExecuteAsync` — `ToolResult.Ok(data, message, sources)`,
    где `sources` = `[WebSourceBuilder.BuildSingle(title, url, text, "web")]`.
  - `label = <title>` страницы (fallback → `url`), `url = запрошенный URL`,
    `snippet` — первые 200 символов очищенного текста.
  - **Ожидаемый эффект:** при вызове `fetch_web_content` в UI-блоке
    «📚 Источники» появляется кликабельная ссылка на загруженную страницу.
  - **Не покрыто (осознанно):** если `extractText = false` (HTML-режим) —
    `sources` = `null` (для отладки, snippet не имеет смысла).

### Fixed
- **Sources — дедупликация схлопывала web/wiki-источники в один (v1.6.1, KI-086-post, Шаг B.fix)**:
  - **Симптом:** `web_agent` возвращает 7 sources (в curl), но в UI-блоке
    «📚 Источники» отображается только **1**.
  - **Причина:** в `ChatStreamService.AddSourcesToAccumulator` (Шаг 3 v1.6.0)
    ключ дедупликации был `(Type|DocumentPath|ChunkIndex)` — **без `Url`**.
    Для RAG-чанков работало (у них есть DocumentPath/ChunkIndex), но для
    web/wiki (оба поля `null`) ключ получался одинаковым — `"wiki||"` — и
    все источники схлопывались в один (первый добавленный).
  - **Fix:**
    - `ChatStreamService.AddSourcesToAccumulator` — ключ `(Type|DocumentPath|Url|ChunkIndex)`
      с `?? string.Empty` (симметрично `SubAgentService`, Шаг B).
    - `WebSourceBuilder.Build` — внутренняя дедупликация по `Url`
      (case-insensitive). DuckDuckGo HTML иногда отдаёт 4 `<div class="result">`
      с одинаковой ссылкой → после фикса в списке 1 запись, а не 4.
  - **Побочный эффект:** старые записи в `DocumentChunks` с `Url = null` —
      ключ для RAG-чанков не изменился (`null` → `""`).

### Added
- **Sources — `web_search` возвращает citations (v1.6.1, KI-086-post, Шаг A3)**:
  - `WebSearchTool.ExecuteAsync` — `ToolResult.Ok(data, message, sources)`.
  - `sources` — `WebSourceBuilder.Build(retrieved, "web", maxCount: 5)`:
    `type = "web"`, `label = title`, `url` — распакованный DuckDuckGo-редирект,
    `snippet` — HTML-очищенный (≤ 200 символов).
  - **Fallback-эффект:** даже если `wikipedia_search` упал по timeout (KI-064),
    в `web_agent` отработает `web_search` → citations придут в UI.

### Added
- **Sources — проброс citations через агентов (v1.6.1, KI-086-post, Шаг B)**:
  - **Проблема:** LLM в Chat вызывает `web_agent` (не `wikipedia_search` напрямую).
    Внутри `web_agent` — `wikipedia_search` возвращает `ToolResult.Sources`,
    но `SubAgentService` их **отбрасывал**, отдавая наружу только `finalAnswer`
    (текст). В UI-блоке «📚 Источники» citations от Web-инструментов не появлялись.
  - `SubAgentTaskResult.Sources` (`IReadOnlyList<ChatSourceDto>`) — новое поле.
    Собирается в `SubAgentService` из `ToolResult.Sources` всех inner-вызовов
    с дедупликацией по ключу `(Type|DocumentPath|Url|ChunkIndex)`.
  - `AgentToolBase.ExecuteAsync` — проброс `result.Sources` в `ToolResult.Ok(...)`.
  - `ConsultSecondaryAgentTool.ExecuteAsync` — то же (для `consult_secondary_agent`,
    который не наследуется от `AgentToolBase`).
  - **Ожидаемый эффект:** citations от `wikipedia_search` (после A2) и
    `web_search` / `fetch_web_content` (после A3/A4) появляются в UI-блоке
    «📚 Источники» под ответом ассистента через `ChatStreamService` accumulator
    (Шаг 3 v1.6.0).
  - **Тесты:** +3 в `AgentToolBaseTests` (sources / null / empty). **216 → 219**.

### Added
- **Sources — `wikipedia_search` возвращает citations (v1.6.1, KI-086-post, Шаг A2)**:
  - `WikipediaSearchTool.ExecuteAsync` возвращает `ToolResult.Ok(data, message, sources)`.
  - `sources` — `WebSourceBuilder.Build(retrieved, "wiki", maxCount: limit)`:
    `type = "wiki"`, `label = title`, `url = https://{lang}.wikipedia.org/?curid={pageId}`,
    `snippet` — HTML-очищенный extract из search API (≤ 200 символов).
  - **Ожидаемый эффект:** в UI-блоке «📚 Источники» под ответом ассистента
    появляются кликабельные ссылки на статьи Wikipedia (после вызова `wikipedia_search`).

### Added
- **Sources — Web-tools: `RetrievedWebResult` + `WebSourceBuilder` (v1.6.1, KI-086-post, Шаг A1)**:
  - `RetrievedWebResult` (`DTO/Rag/`) — унифицированный результат веб-поиска
    (`Title`, `Url`, `Snippet`) для трёх Web-инструментов.
  - `WebSourceBuilder` (`Implementation/Rag/`) — статический хелпер:
    - `Build(results, type, maxCount = 5)` — список результатов → список citations
      (`type` = `wiki` / `web` / ...); фильтрует пустые результаты; ограничивает top-N.
    - `BuildSingle(title, url, snippet, type)` — один citation
      (для `fetch_web_content` и одиночных Wikipedia); возвращает `null`,
      если URL пуст.
    - `BuildLabel(title, url)` — приоритет: `title` → `url` → `"(unknown)"`.
    - Переиспользует `RagSourceBuilder.TruncateSnippet` (≤ 200 символов с «…»).
  - Тесты и подключение в tools — следующие шаги (A2-A5).

### Fixed
- **Sources — относительные пути в `DocumentChunk.DocumentPath` (v1.6.1, KI-086-post)**:
  - **Симптом:** в `sources[i].documentPath` (API `/api/tools/execute`, SSE `done`,
    `ChatMessageDto.Sources`) уходил абсолютный путь `C:\Projects\AI\IIChatTools\docs\development\RULES.md`.
    Некрасиво в API + утечка структуры ФС для мульти-юзера.
  - **Причина:** `DocumentIngestionService.GetTextFromSourceAsync` для
    `SourceType.File` возвращал `request.FilePath` (абсолютный) как `documentPath`,
    **игнорируя** `request.Source` (относительный), который передают **все три**
    caller'а (`AdminKnowledgeService`, `ChatAttachmentService`, `WorkspaceIndexService`).
  - **Fix:** использовать `request.Source`, если задан. Fallback на `request.FilePath`
    для обратной совместимости (прямые вызовы без Source).
  - **Сопутствующий fix:** `ChatAttachmentService.DeleteAsync` — путь для
    `DeleteDocumentAsync` теперь берётся из `entity.StoragePath` (относительный),
    а не из абсолютного `fullPath`. Согласовано с новым форматом в БД.
  - **Миграция данных:** старые записи в `DocumentChunks` остаются с абсолютными
    путями. Для обновления — вручную `/admin → База знаний → Обновить индекс проекта`
    (для `project_docs`). Новые записи (upload вложений, workspace-индексация)
    сразу идут с относительными путями.

### Changed
- **Docs — PROMPT_V2.md v2.3 → v2.4 (post-release v1.6.0)**:
  - Шапка: «Актуальный релиз проекта» v1.5.0 → **v1.6.0**.
  - Метрики: 199 → **216** тестов.
  - «Что выпущено» — расширено до v1.6.0, добавлен раздел Sources / citations.
  - Roadmap: **v1.6.0 — Sources ✅ Done**, v1.5.x → **v1.6.x**
    (Web-tools sources, absolute paths fix, KI-091, KI-070, PDF/DOCX, Qdrant).
  - «Известные подводные камни» — добавлены 3 пункта:
    Path.GetFileName (RULES § 4.45), кэш браузера JS/CSS, RAG-tools в `allowedNames`
    (RULES § 4.44).
  - Ссылка на RULES: v1.4.17 → **v1.4.18**.

_(пусто — новые изменения вносятся сюда)._

---

## [1.6.0] — 2026-09-28

**Sources / citations под ответом ассистента (KI-086).** Блок «📚 Источники»
с RAG-чанками, реально использованными LLM: live через SSE `done` и при
F5-загрузке из `ChatMessageDto.Sources`. 13 коммитов, 17 новых тестов
(199 → 216).

**Известные ограничения v1.6.0:** см. `docs/development/RELEASES.md` § 1a.
Ключевое: Web-tools (Wikipedia, WebSearch, FetchWebContent) sources **не отдают**
в v1.6.0 — план на v1.6.1.

### Changed
- **Sources / citations — Шаг 5.3: KI-086 → Fixed, RULES v1.4.18 (v1.6.0, KI-086)**:
  - **`docs/KNOWN_ISSUES.md`:** KI-086 → **Fixed (v1.6.0)**. Дописано тело —
    13 коммитов, 17 новых тестов, все ключевые файлы. Сводка: +`Fixed (v1.6.0) = 1`,
    `Deferred = 3 → 2`.
  - **`docs/development/RULES.md`:**
    - § 7 (KI-выжимка) — добавлена строка KI-086 → Fixed (v1.6.0).
    - § 8 (история) — новая строка **1.4.18**: правила 4.44 (top-level ITool →
      `allowedNames`), 4.45 (`Path.GetFileName` — кросс-платформенные грабли).
    - Шапка: версия 1.4.17 → **1.4.18**.

### Added
- **Sources / citations — Шаг 5.2: README + TESTING (v1.6.0, KI-086)**:
  - **README.md:**
    - «Chat UI» — новый пункт «📚 Источники (Sources)» (live + F5-режим).
    - «RAG / Knowledge Base → Как работает» — пункт 5 про citations.
    - «Ограничения (осознанные, MVP)» — убран пункт про Sources
      (реализовано в v1.6.0).
  - **docs/TESTING.md:**
    - Smoke § 2 — 11-й сценарий (блок «Источники» после ответа + F5).
      Счётчик сценариев: 10 → 11.
    - Full regression § 3.2 — 3.2.12 (search_knowledge_base + блок
      «Источники» + проверка SSE `done` через DevTools).

### Fixed
- **Sources / citations — Шаг 5.1.fix2: кросс-платформенный BuildLabel (v1.6.0, KI-086)**:
  - **Симптом:** CI падал на `ubuntu-latest` (тест `BuildLabel_AbsoluteWindowsPath_ReturnsFileName`),
    локально на Windows — 213/213.
  - **Причина:** `Path.GetFileName` на Linux распознаёт только `/` как
    разделитель пути. Абсолютный Windows-путь `C:\Projects\...\RULES.md`
    на Linux возвращается целиком, а не `RULES.md`.
  - **Fix:** нормализация разделителей (`\` → `/`) перед `Path.GetFileName`
    в `RagSourceBuilder.BuildLabel`. Тот же паттерн, что в `PathHelper`
    (KI-040 — Linux traversal через backslash).
  - **Тесты:** +3 `[Theory]` `BuildLabel_MixedSeparators_ReturnsFileName`
    (backslash / forward slash / смешанные). **213 → 216**.
  - **Урок (RULES § 4.45, добавлено ниже):** при работе с путями
    через `Path.*` — нормализовать разделители, если вход может прийти
    из Windows-контекста в Linux-CI.
    
- **Sources / citations — Шаг 5.1: unit-тесты RagSourceBuilder (v1.6.0, KI-086)**:
  - **14** тестов в `IIChatTools.Tests/UnitTests/Rag/RagSourceBuilderTests.cs`
    (4 `[Fact]` + `3×[Theory]` + 2 `[Fact]` + 3 `[Fact]` + `2×[Theory]`):
    - `Build`: null / empty / valid / multiple (порядок сохранён).
    - `BuildLabel`: null / empty / absolute Windows path / relative path.
    - `TruncateSnippet`: short / exactly-max / long (с «…») / null.
  - **Тесты:** 199 → **213** (+14). В тексте коммита `86ead5d` указано
    «209» — поправка (я не учёл, что `[Theory]` + `[InlineData]`
    разворачивается в N отдельных тестов).
  - Fix CS1574: `<see cref="ChatSourceDto"/>` — добавлен `using IIChatTools.Services.DTO.Chat;`,
    убран префикс `DTO.Chat.` в cref.

- **Sources / citations — Шаг 4: UI-блок «Источники» (v1.6.0, KI-086)**:
  - **`chat.js`** — `renderSourcesBlock(sources)`:
    - рендерит `<ol>` под ответом ассистента с иконкой 📚;
    - формат элемента: `{label} (фрагмент {N}, score {S})`;
    - `chunkIndex + 1` — 1-based (user-friendly);
    - snippet — в `title` (tooltip при hover);
    - `type: "rag"` — простой текст (файл в workspace/репо);
    - `type: "web"` / `"wiki"` — `<a href>` (задел на v1.6.1).
  - **Live-режим:** `finalizeAssistantBubble` вставляет блок из SSE `done` → `data.sources`.
  - **F5-режим:** `renderMessage` рендерит блок из `msg.sources`
    (структурный массив, `ChatMessageDto.Sources` — Шаг 3.5d).
  - **`.resx` (RU + EN):** +2 ключа — `ChatSourcesHeader` («Источники» / «Sources»),
    `ChatSourceChunkMeta` («фрагмент {0}» / «chunk {0}»).
  - **`chat.css`:** `.chat-message-sources` (компактный список с левой полосой),
    `.chat-message-sources-header`, `.chat-message-sources-item`,
    `.chat-message-sources-meta`. На мобильных meta переносится на новую строку.

### Fixed
- **Sources / citations — Шаг 3.5e: warning CS1574 в ChatDtos.cs (v1.6.0, KI-086)**:
  - `<see cref="IIChatTools.API.Controllers.ChatController.GetChatAsync"/>` →
    `<c>ChatController.GetChatAsync</c>`. Services не ссылается на API
    (ADR-001), cref не резолвится. Тот же паттерн, что уже применён для
    `DocumentChunk` → `IVectorStore` (RULES § 4.30).
  - Причина: шаг 3.5d нарушил RULES § 3.6 «0 warnings».

### Fixed
- **Sources / citations — Шаг 3.5d: camelCase + структурный Sources в ChatMessageDto (v1.6.0, KI-086)**:
  - `ChatMessageDto.Sources` (`IReadOnlyList<ChatSourceDto>`) — структурированный
    массив источников (вместо сырого `MetadataJson`). Парсится на бэкенде
    в `ChatController.ParseSources` (Newtonsoft case-insensitive → работает
    и с camelCase, и с PascalCase записями).
  - `ChatStreamService.SerializeSources` — сериализация в **camelCase**
    (единый формат с SSE). Новые записи в БД — camelCase, старые
    (созданные до этого шага) остаются PascalCase, но парсятся без потерь.
  - `ChatMessageDto.MetadataJson` — оставлено для отладки / API-совместимости.
  - **Причина:** `JsonConvert.SerializeObject` по умолчанию писал PascalCase
    (`{"sources":[{"Type":"rag",...}]}`), а фронт (chat.js) ожидает
    `sources[0].type` (camelCase, как в SSE `done`). Без унификации блок
    «Источники» работал бы в live-режиме, но не после F5.

### Fixed
- **Sources / citations — Шаг 3.5c: RAG-tools не попадали в tools[] Chat (v1.6.0, KI-086)**:
  - **Баг:** в `ChatStreamService.StreamAsync` список `allowedNames` строился
    только из `SubAgentRegistry.GetEnabled()` (6 агентов) + `consult_secondary_agent`.
    RAG-инструменты (`search_knowledge_base`, `search_chat_history`,
    `search_workspace`) были зарегистрированы в DI как `ITool`, но **отфильтровывались** —
    Chat видел 7 инструментов вместо ожидаемых 10.
  - **Симптом:** LLM в чате не могла вызвать `search_knowledge_base` (его не было
    в `tools[]`) и выбирала `file_system_agent` как fallback — тот искал
    `RULES.md` в workspace пользователя и не находил.
  - **Fix:** явное добавление 3 RAG-tools в `allowedNames` через константу
    `RagToolNames` (v1.6.0, KI-086, Шаг 3.5c).
  - **Урок (RULES § 4.44, добавлено ниже):** при добавлении нового top-level
    `ITool` в DI — проверить, что он попал в `allowedNames` для Chat.
    `[ToolRegistry]` — это **все** инструменты, а Chat видит только
    **подмножество** (6 агентов + consult + 3 RAG).

### Added
- **Sources / citations — Шаг 3.5b: DefaultSystemPrompt в чате (v1.6.0, KI-086)**:
  - `ChatStreamService.DefaultSystemPrompt` — базовый system prompt проекта,
    добавляется к **каждому** чату (перед RAG-контекстом и пользовательским
    `Chat.SystemPrompt`). Явно указывает модели использовать
    `search_knowledge_base` для вопросов о проекте и `file_system_agent` —
    для файлов пользователя.
  - Причина: усиления `Description` в Шаге 3.5 недостаточно — qwen3-4b
    по-прежнему выбирала `file_system_agent` при вопросе «Что у нас в RULES.md…».
  - Пользовательские `Chat.SystemPrompt` не перезаписываются — идут после
    базового через пустую строку.

### Fixed
- **Sources / citations — Шаг 3.5: LLM не выбирала search_knowledge_base (v1.6.0, KI-086)**:
  - Усилен `Description` у `search_knowledge_base`: явно перечислены имена файлов
    (`README.md`, `RULES.md`, `KNOWN_ISSUES.md`, `CHANGELOG.md`, `RELEASES.md`,
    `ARCHITECTURE.md`, `DESIGN.md`) + явный запрет искать эти файлы через
    `file_system_agent` / `read_file`.
  - В `appsettings*.json` в `Description` агента `file_system_agent` добавлено
    уточнение «В WORKSPACE ПОЛЬЗОВАТЕЛЯ» и предупреждение: для документации
    проекта использовать `search_knowledge_base`.
  - **Причина:** qwen3-4b при вопросе «Что у нас в RULES.MD про yield return?»
    выбирала `file_system_agent` (воспринимала RULES.MD как файл в workspace),
    а не RAG-tool. LLM внутри агента не находила файл → отвечала «не найдено».
  - **Не блокер Шага 3** — Шаг 3 работает корректно; фикс улучшает выбор
    инструмента моделью.

### Added
- **Sources / citations — Шаг 3: агрегация и сохранение (v1.6.0, KI-086)**:
  - `ChatStreamService` накапливает `sources` за весь stream из двух мест:
    (а) auto-inject RAG-чанки (Шаг 6C — `BuildRagContextAsync` теперь
    возвращает их вместе с текстом блока); (б) `tool_result` от RAG-tools.
  - **Дедупликация** по ключу `(Type|DocumentPath|ChunkIndex)`:
    один чанк из auto-inject и tool_result попадёт в список единожды.
  - **Сохранение** в `ChatMessage.MetadataJson` финального assistant-сообщения
    (и в limit-message при исчерпании 5 итераций tool calling'а) —
    JSON-формат `{ "sources": [...] }`. Пустой список → `null` (поле не пишем).
  - **SSE `done`** получил опциональный параметр `sources` — UI рендерит
    «Источники» сразу без F5 (используется в Шаге 4).
  - `ChatStreamEvent.Done` расширен параметром `IReadOnlyList<ChatSourceDto> sources`
    (обратносовместимо: вызовы без sources работают как раньше).
  - Вспомогательные приватные методы `AddSourcesToAccumulator` / `SerializeSources`
    (в `ChatStreamService`).

### Fixed
- **Sources / citations — Шаг 2.fix: проброс Sources до HTTP/SSE (v1.6.0, KI-086)**:
  - `ToolsController.ExecuteAsync`: проброс `result.Sources` в JSON-ответ
    `/api/tools/execute` (поле `sources`). Без этого фикса RAG-tools возвращали
    sources внутри `ToolResult`, но HTTP-ответ терял их.
  - `ChatStreamService`: проброс `toolResult.Sources` в `ChatToolResultDto`
    SSE-события `tool_result` — UI Шага 4 сможет рендерить «Источники» live.
  - `ChatController.GetChatAsync`: проброс `ChatMessage.MetadataJson` в
    `ChatMessageDto` — источники подтянутся при F5-загрузке истории чата.

### Added
- **Sources / citations — Шаг 2: ToolResult.Sources + RAG-tools (v1.6.0, KI-086)**:
  - `ToolResult.Sources` (`IReadOnlyList<ChatSourceDto>?`) — новый опциональный
    параметр в `Ok(data, message, sources)`. Обратносовместимо: 46 существующих
    инструментов продолжают вызывать `Ok(data, message)` — `Sources` остаётся
    `null`.
  - `ChatToolResultDto.Sources` — проброс в SSE-событие `tool_result`.
  - `RagSourceBuilder` (`IIChatTools.Services/Implementation/Rag/`) — хелпер
    сборки `RetrievedChunkDto → ChatSourceDto`: тип `rag`, label = имя файла
    из `DocumentPath`, snippet обрезается до 200 символов. Используется
    всеми тремя RAG-tool (без дублирования логики).
  - 3 RAG-tool (`search_knowledge_base`, `search_chat_history`,
    `search_workspace`) возвращают `ToolResult.Ok(data, message, sources)`.
  - **Ожидаемый эффект:** после выполнения RAG-tool SSE-событие `tool_result`
    несёт `sources` — UI-блок «Источники» появится в Шаге 4.

### Added
- **Sources / citations — Шаг 1: DTO + ChatMessage.MetadataJson (v1.6.0, KI-086)**:
  - `ChatSourceDto` (`IIChatTools.Services/DTO/Chat/ChatSourceDto.cs`):
    единый DTO источника для блока «Источники» под ответом ассистента.
    Поля: `Type` (`rag`/`web`/`wiki`), `Label`, `Url`, `DocumentPath`,
    `ChunkIndex?`, `Score?`, `Snippet`.
  - `ChatMessage.MetadataJson` (`string`, nvarchar(max)) — метаданные
    сообщения. Пока хранит `{ "sources": [...] }`. Поле рассчитано
    на будущие расширения (страница PDF, timestamp и т.п.).
  - `ChatMessageDto.MetadataJson` — проброс в API (сырой JSON,
    разбор — на клиенте).
  - Миграция `AddChatMessageMetadata` (SqlServer).

---

## [1.5.0] — 2026-09-28

**RAG / Knowledge Base — семантический поиск по документам проекта, приложенным
файлам и истории чатов (KI-083).** 4 индекса (`project_docs`, `my_rag_docs`,
`chat_history`, `workspace`), 3 tool для LLM, вложения в чате (📎),
админка `/admin → База знаний`, opt-in Workspace-индекс в `/profile`.

**Тесты:** 191 → **199** (8 новых).

### Added
- **Docs — `docs/TESTING.md` (KI-088, Шаг 8.6, v1.5.0)**:
  - Чек-лист ручной приёмки: Smoke (10 сценариев, ~15 мин),
    Full regression (35 сценариев по Chat / RAG / Multi-Agent / Система, ~40 мин),
    UI/UX (9 проверок), «Что НЕ покрыто автотестами» (10 пунктов).
  - Дефекты → `KNOWN_ISSUES.md` с `KI-XXX`.
  - Раздел «Что НЕ покрыто» явно перечисляет SSE-стриминг, tool calling loop,
    approvals end-to-end, индексацию workspace, RAG с реальным LM Studio,
    rate limiting, retention, Docker-образ — то, что нельзя проверить
    автотестами.

### Changed
- **Release — финализация v1.5.0: KI / RULES / DESIGN / ARCHITECTURE (v1.5.0, KI-083, Шаг 8.5)**:
  - **`docs/KNOWN_ISSUES.md`:**
    - KI-083 (RAG) → **Fixed (v1.5.0)**; в тело дописана сводка Фазы 8 (8.1 → 8.7).
    - **Дубликат `KI-086`** (SQLite locked) переименован в **`KI-093`**
      с пометкой «дубликат KI-085, оставлен для истории». KI-086 остаётся
      за sources/citations (v1.6.0).
    - **Сводка по статусам** обновлена: `Fixed (v1.5.0) = 1`, `In Progress = 0`.
  - **`docs/development/RULES.md`** — версия 1.4.16 → **1.4.17**:
    - § 7 (KI-выжимка) актуализирована после релиза v1.5.0.
    - § 8 (История): новая строка 1.4.17.
  - **`docs/development/v1.5/DESIGN.md`** — статус `Implemented (Фазы 0-7)` →
    **`Implemented (v1.5.0)`** (релиз 2026-09-28).
  - **`docs/development/ARCHITECTURE.md`** — версия 1.4.1 → **1.5.0**:
    - § 3.3 (RAG entities) — убран тег «(в работе)».
    - § 3.5 (миграции) — `AddDocumentChunks` / `AddChatAttachments` — убран «(план)».
    - § 6.3 (RAG-tool) — `Chat видит 10` (было «+3 инструмента (после v1.5)»).
    - Футер: `v1.4.1` → `v1.5.0`.

### Added
- **Docs — README: раздел «RAG / Knowledge Base» (v1.5.0, KI-083, Шаг 8.1)**:
  - «Ключевые возможности»: RAG-пункт обновлён (Фаза 7 закрыта, ссылка на DESIGN).
- **Release — версия 1.4.1 → 1.5.0 (v1.5.0, KI-083, Шаг 8.3)**:
  - `Directory.Build.props`: `<Version>1.5.0</Version>` + `<Copyright>` + шапка-комментарий.
  - `README.md`: заголовок, Copyright, ghcr-теги (`v1.5.0` / `1.5.0` / `1.5` / `1`).
  - **Версия в UI/логах подхватится автоматически** — `AppVersion.Current` читается
    из `AssemblyInformationalVersionAttribute` (MSBuild формирует из `<Version>`).
  - `CHANGELOG.md` — секция `[Unreleased]` пока **не закрыта** (это Шаг 8.4).

### Added
- **Docs — RELEASES.md: § 1a «Известные ограничения v1.5.0» (v1.5.0, KI-083, Шаг 8.2)**:
  - **§ 1a** — отдельная секция про SqlServer-миграции (KI-091): **не применяются**
    в v1.5.0; dev — Sqlite; prod-SqlServer — после v1.5.x. Инструкция «что делать
    / чего не делать».
  - **§ 1** — расширен чек-лист (ссылка на § 1a).
  - **§ 3** — добавлена проверка известных ограничений (Sqlite + RAG-индексы).
  - **§ 10** — добавлены ссылки на RULES § 3.15 и KI-091.
  - Прочие ограничения (Sources/citations, PDF/DOCX, re-ranking, Qdrant,
    KI-088, KI-092) — зафиксированы в § 1a.

### Added
- **Docs — README: раздел «RAG / Knowledge Base» (v1.5.0, KI-083, Шаг 8.1)**:
  - «Ключевые возможности»: RAG-пункт обновлён (Фаза 7 закрыта, ссылка на DESIGN).
  - «API (основные endpoints)»: +14 endpoint'ов + убран дубликат `generate-title`.
  - «Chat UI»: +1 пункт — RAG-вложения (📎 + чипы + auto-inject).
  - **Новый раздел «RAG / Knowledge Base»**: 4 индекса, как работает, форматы
    и лимиты, UI, конфигурация (таблица параметров), ограничения MVP.
  - «Сборка и тесты»: счётчик 188/188 → 199/199.
  
- **RAG / Knowledge Base — Фаза 7 закрыта: Admin KB UI + Profile Workspace UI (v1.5.0, KI-083)**:
  - **Сводка фазы** (7A → 7D.3, всё запушено 2026-09-25 → 2026-09-28):
    - **7A** — backend: DTO + `IAdminKnowledgeService` + `AdminKnowledgeController` (6 endpoints).
    - **7B** — UI админки: 8-я вкладка «База знаний» + `admin-knowledge.js` + модалка чанков.
    - **7C.1** — backend Workspace Index: `IWorkspaceIndexService` + `ProfileWorkspaceController` (5 endpoints).
    - **7C.2** — UI `/profile`: карточка Workspace index + `profile-workspace.js` (polling 2 с).
    - **7D.1** — 5 unit-тестов `WorkspaceIndexServiceTests`.
    - **7D.2** — 3 unit-теста `AdminKnowledgeControllerTests`.
    - **7D.3** — RULES v1.4.16 (§ 4.42, § 4.43).
  - **Тесты:** 191 → **199** (5 + 3 новых).
  - **Осталось:** Фаза 8 — релиз v1.5.0 (README, KNOWN_ISSUES, RELEASES, tag).

### Added
- **Docs — RULES v1.4.16: +2 правила (v1.5.0, KI-083, Шаг 7D.3)**:
  - **§ 4.42** — `AddDbContext` + `UseInMemoryDatabase(name)` в тестах требует
    явного `InMemoryDatabaseRoot` + вычисления имени БД **до** лямбды (лямбда
    выполняется на каждый scope; без этого данные между scope не шарятся).
  - **§ 4.43** — у `JToken` нет `GetValue(string, StringComparison)`; для
    case-insensitive чтения свойства `JObject` — перебирать `obj.Properties()`
    вручную (реальный MVC сериализует в camelCase, `JsonConvert` в тесте — в
    PascalCase).
  - **Уроки** из Шагов 7D.1 (WorkspaceIndexServiceTests) и 7D.2
    (AdminKnowledgeControllerTests).
  - **§ 8 (История):** версия 1.4.16, дата 2026-09-28.

### Added
- **RAG / Knowledge Base — Шаг 7D.2: Unit-тесты AdminKnowledgeController (v1.5.0, KI-083)**:
  - **`AdminKnowledgeControllerTests`** (3 теста):
    - `GetIndexesAsync_ReturnsFourIndexes` — GET `/indexes` → 200 + 4 индекса
      (`project_docs`, `my_rag_docs`, `chat_history`, `workspace`).
    - `ReindexProjectDocsAsync_ReturnsResult` — POST `/reindex` → 200 +
      `data.documentChunksCreated`, `data.durationMs`.
    - `UpdateSettingsAsync_InvalidStrategy_ReturnsFail` — PUT `/settings`
      с невалидным `ChunkingStrategy` → сервис бросает `ArgumentException`
      → контроллер возвращает `success=false` + сообщение.
  - **Fake-зависимость:** `FakeAdminKnowledgeService` (запоминает вызовы,
    настраиваемые результат / исключение для каждого метода).
  - **Паттерн:** прямой вызов контроллера (без `WebApplicationFactory`),
    консистентно с `ChatAttachmentsControllerTests`. Авторизация
    (`[Authorize(Policy = "AdminOnly")]`) — не проверяется (middleware,
    полная HTTP-интеграция — Шаг 8).

### Added
- **RAG / Knowledge Base — Шаг 7D.1: Unit-тесты WorkspaceIndexService (v1.5.0, KI-083)**:
  - **`WorkspaceIndexServiceTests`** (5 тестов):
    - `GetStatusAsync_Disabled_ReturnsEnabledFalse` — флаг не задан →
      `Enabled=false, ChunkCount=0, IsIndexing=false`.
    - `EnableAsync_SetsFlagAndStartsIndexing` — флаг устанавливается, фоновая
      задача обрабатывает 2 файла (.txt + .md); служебные подпапки
      (`chat-attachments/`) и неподдерживаемые расширения (.png) игнорируются.
    - `DisableAsync_ClearsChunksAndFlag` — флаг сброшен + вызов
      `ClearIndexForUserAsync("workspace", 1)`.
    - `ReindexAsync_WhenDisabled_Throws` — `InvalidOperationException`.
    - `ReindexAsync_WhenEnabled_ClearsAndStarts` — очищает чанки + запускает
      второй прогон, `IngestAsync` вызван повторно.
  - **Инфраструктура тестов:** реальный `ServiceCollection` + InMemory-DB +
    реальный `UserSettingsService`; fake — `FakeIngestionService` (расширен
    `ClearForUserCalls` + `NextClearForUserRemoved`), `FakeWorkspaceResolver`.
    Polling-loop до 5 с для ожидания завершения фоновой задачи.
  - **`FakeIngestionService`:** реализация `ClearIndexForUserAsync` теперь
    записывает вызовы в `ClearForUserCalls` (для ассертов).

### Added
- **RAG / Knowledge Base — Шаг 7C.2: Workspace Index UI (v1.5.0, KI-083)**:
  - **`Views/Profile/Index.cshtml`:** карточка `#profile-workspace-card` под
    карточкой «Хранение чатов»:
    - checkbox «Включить семантический поиск по файлам workspace» + hint;
    - строка статуса (Отключён / Индексация… / Включён · N файлов · M чанков);
    - progress-bar (Bootstrap `.progress`) с процентом — только при `isIndexing`;
    - плашка последней ошибки (`lastError`);
    - кнопка «Переиндексировать» (disabled, если не enabled или идёт индексация).
  - **`wwwroot/js/modules/profile-workspace.js`** (новый модуль):
    - `loadStatus()` — `GET /api/profile/workspace-index` при загрузке страницы.
    - `onToggleEnable` — POST `/enable` (без confirm) или POST `/disable`
      (с `confirm()`, т.к. удаляются все чанки).
    - `onReindex` — POST `/reindex` с confirm-свободным путём (сервер сам
      проверяет `enabled`).
    - **Polling 2 с через `setTimeout`** (не `setInterval`) — гарантирует,
      что следующий запрос не стартует до завершения предыдущего. Автостоп,
      как только `isIndexing = false`.
    - Все строки — через `data-*` атрибуты карточки (RULES § 4.17).
  - **`.resx` (RU + EN):** +14 ключей (camelCase): `ProfileWorkspaceSection`,
    `ProfileWorkspaceEnable`, `ProfileWorkspaceEnableHint`,
    `ProfileWorkspaceStatusLabel`, `ProfileWorkspaceStatusDisabled`,
    `ProfileWorkspaceStatusEnabled`, `ProfileWorkspaceStatusIndexing`,
    `ProfileWorkspaceProgress`, `ProfileWorkspaceReindex`,
    `ProfileWorkspaceEnableSuccess`, `ProfileWorkspaceDisableSuccess`,
    `ProfileWorkspaceReindexSuccess`, `ProfileWorkspaceDisableConfirm`,
    `ProfileWorkspaceLastErrorPrefix`.
  - **`site.css`:** `.profile-workspace-progress` (height 14px) +
    `#profile-workspace-error` (жёлтая левая полоса).
  - **KNOWN_ISSUES:** KI-092 (Bootstrap `aria-hidden` warning при закрытии
    вложенных модалок — задокументировано, не блокер).
  - **Фаза 7 (Admin Knowledge Base UI + Profile Workspace UI) закрыта.**

### Added
- **RAG / Knowledge Base — Шаг 7C.1: Workspace Index backend (v1.5.0, KI-083)**:
  - **DTO (`DTO/Rag/`):**
    - `WorkspaceIndexStatusDto` — Enabled, FilesIndexed, ChunkCount, LastIndexedAt,
      IsIndexing, TotalFiles, FilesProcessed, ChunksCreated, LastError.
  - **`IWorkspaceIndexService` + `WorkspaceIndexService` (Singleton):**
    - `GetStatusAsync` — флаг `Workspace.Index.Enabled` из `UserSettings` +
      метрики из `DocumentChunks` (`COUNT`, `DISTINCT DocumentPath`, `MAX(CreatedAt)`)
      + прогресс in-memory.
    - `EnableAsync` — устанавливает флаг enabled=true, запускает фоновую индексацию.
    - `DisableAsync` — сбрасывает флаг, очищает чанки (через новый
      `ClearIndexForUserAsync`), сбрасывает прогресс.
    - `ReindexAsync` — clear + restart (только если enabled; no-op, если
      индексация уже идёт).
    - Фоновая индексация — `Task.Run` + `IServiceScopeFactory.CreateScope()`
      (Singleton не держит Scoped-зависимости).
    - Обход workspace: фильтр по `IRagDocumentParserRegistry.GetAllSupportedExtensions()`,
      пропуск служебных подпапок (`chat-attachments`, `logs`, `bin`, `obj`,
      `.git`, `node_modules`, `.vs`, `.idea`).
    - Прогресс — `ConcurrentDictionary<int, WorkspaceIndexProgress>` (per-user,
      in-memory; не переживает рестарт).
  - **`IDocumentIngestionService.ClearIndexForUserAsync(indexName, userId, ct)`** —
    расширение контракта Шага 4C.1: селективная очистка per-user индекса
    (только чанки указанного пользователя).
  - **`ProfileWorkspaceController`** (5 endpoints, `[ApiController]`, `[Authorize]`):
    - `GET  /api/profile/workspace-index` — статус (initial load);
    - `GET  /api/profile/workspace-index/status` — статус (polling 2 с);
    - `POST /api/profile/workspace-index/enable`;
    - `POST /api/profile/workspace-index/disable`;
    - `POST /api/profile/workspace-index/reindex`.
  - **DI (`Startup.cs`):** `IWorkspaceIndexService` → `WorkspaceIndexService` (Singleton).
  - **Тесты:** запланированы на Шаг 7D (интеграционные).
  - **UI (`/profile`)** — Шаг 7C.2.

### Added
- **RAG / Knowledge Base — Шаг 7B: Admin Knowledge Base UI (v1.5.0, KI-083)**:
  - **`Admin.cshtml`:** 8-я вкладка «База знаний» (`#tab-knowledge` / `#pane-knowledge`) +
    модалка `#ragChunksModal` для просмотра чанков (pagination, delete).
  - **`admin-knowledge.js`** (новый модуль): таблица 4 индексов (`name`/`docCount`/`chunkCount`/`lastIndexedAt`),
    кнопка «Обновить индекс проекта» (POST reindex + toast с количеством чанков и длительностью),
    кнопка «Настройки RAG» (модалка на базе `showModal` из `admin.js` — 8 полей),
    просмотр чанков с пагинацией (20/стр.), удаление отдельного чанка (с `confirm`).
  - **`.resx` (RU + EN):** +29 ключей (camelCase) — `AdminTabKnowledgeBase`,
    `RagReindex*`, `RagIndexColumn*`, `RagChunk*`, `RagSettings*`, `RagViewChunks`,
    `RagNoIndexes`, `RagDeleteChunk*`.
  - **`site.css`:** стили `.rag-chunk-path` (моноширинный, word-break) и
    `.rag-chunk-preview` (однострочный ellipsis, max-width 500px).
  - **Локализация через `data-*`** (RULES § 4.17): 19 атрибутов на `#pane-knowledge`.
  - **Тесты:** UI-тестов нет (как и для других админ-вкладок; smoke — DevTools).
  - **Зависимости:** `IAdminKnowledgeService` (Шаг 7A, commit `2a05ad2`).

### Added
- **RAG / Knowledge Base — Шаг 7A: Admin Knowledge Base backend (v1.5.0, KI-083)**:
  - **DTO (`DTO/Rag/`):**
    - `RagIndexDto` — Name, Description, ChunkCount, DocumentCount, LastIndexedAt.
    - `RagSettingsDto` — ChunkingStrategy, ChunkSize, ChunkOverlap, MinChunkSize,
      DefaultTopK, MinScore, EmbeddingModel, AutoIndexProjectDocs.
    - `RagChunkDto` — Id, IndexName, DocumentPath, ChatId, UserId, ChunkIndex,
      Tokens, TextPreview, CreatedAt.
  - **`IAdminKnowledgeService` + `AdminKnowledgeService` (Scoped):**
    - `GetIndexesAsync` — 4 индекса (`GROUP BY IndexName`, всегда все 4).
    - `ReindexProjectDocsAsync` — чтение `Rag:Ingestion:ProjectDocsPaths`,
      auto-detect project root (по `IIChatTools.sln`), `ForceReindex=true`, `UserId=0`.
    - `GetChunksAsync` — пагинация (1-based, max pageSize=100).
    - `DeleteChunkAsync` — БД + VectorStore (best-effort).
    - `GetSettingsAsync` — override из AppSettings + fallback на appsettings.json.
    - `UpdateSettingsAsync` — валидация + upsert override'ов в `AppSettings` (Category «RAG»).
  - **`AdminKnowledgeController`** (6 endpoints, `[Authorize(Policy = "AdminOnly")]`):
    - `GET /api/admin/knowledge/indexes`
    - `POST /api/admin/knowledge/indexes/project-docs/reindex`
    - `GET /api/admin/knowledge/chunks?index=&page=&pageSize=`
    - `DELETE /api/admin/knowledge/chunks/{id}`
    - `GET /api/admin/knowledge/settings`
    - `PUT /api/admin/knowledge/settings`
  - **DI (`Startup.cs`):** `IAdminKnowledgeService` → `AdminKnowledgeService` (Scoped).
  - **Config (`appsettings.Development.json`):** полная секция `Rag` —
    `Embedding`, `Chunking`, `Ingestion` (включая `ProjectDocsPaths`), `Retrieval`, `Attachments`.
  - **Тесты:** запланированы на Шаг 7D (интеграционные — 3 шт).
  - **Runtime-эффект override'ов** (применение настроек RAG без restart) — отложено
    на отдельный шаг (аналогично `LoadSubAgentOverridesAsync` в Program.cs).

### Added
- **RAG / Knowledge Base — Шаг 6D: UI вложений чата (v1.5.0, KI-083)**:
  - **Chat UI (`Index.cshtml`):**
    - 📎-кнопка в `.chat-input-box` слева от textarea (DeepSeek-style, SVG-icon).
    - `<input type="file" id="chat-file-input" multiple hidden>` — accept: 26 расширений PlainTextParser.
    - Панель `.chat-attachments-bar` над полем ввода: строка «RAG: N чанков [Очистить RAG]» + chips-контейнер.
    - `data-*`-атрибуты для локализации (8 ключей на панели).
  - **JS (`chat.js`):**
    - State: `attachments` (ChatAttachmentDto[]), `ragChunkCount` (число чанков).
    - `loadAttachments()` — GET вложений активного чата (вызывается в `selectChat`).
    - `uploadFiles(files)` — параллельная загрузка (POST multipart, по одному запросу на файл); toast на каждый файл; ошибка одного не отменяет остальные.
    - `deleteAttachment(id)` — DELETE.
    - `clearAttachments()` — POST clear + `confirm()`.
    - `renderAttachmentsBar()` — рендерит chips (📄 {name} ({size} · {N чанков}) [×]) и summary.
    - `formatFileSize(bytes)` — «N KB» / «N.N MB».
    - `formatChipChunks(count)` — «1 чанк» / «N чанков» через `data-label-*`.
    - `enableInput` теперь управляет также 📎-кнопкой.
    - `showEmptyState` сбрасывает `attachments`/`ragChunkCount`.
  - **CSS (`chat.css`):** секция `.chat-attachments-bar` / `.chat-attachment-chip` / `.chat-attachment-chip-remove` / `.chat-input-action-attach` (+ mobile: meta-chip скрыт).
  - **Локализация (12 ключей × 2 = 24 записи):**
    `RagAttachButton`, `RagAttachButtonTooltip`, `RagClearButton`,
    `RagChunksCountOne`, `RagChunksCountMany`,
    `RagUploadSuccess`, `RagUploadTooLarge`, `RagUploadUnsupportedFormat`,
    `RagUploadExceedsMaxFiles`, `RagUploadExceedsTotalSize`,
    `RagClearConfirm`, `RagClearSuccess`.
  - **Фаза 6 (Attached Files) закрыта.** UI: 6A + 6B + 6C + 6D — все Done.

### Added
- **RAG / Knowledge Base — Шаг 6C: auto-inject top-K в system prompt (v1.5.0, KI-083)**:
  - `ChatStreamService` инжектит `AppDbContext` + `IRetrievalService` (оба Scoped).
  - Новый приватный метод `BuildRagContextAsync(chatId, userId, startUserMessageId, ct)`:
    - Быстрая проверка `AnyAsync` — есть ли чанки в `my_rag_docs` для чата (если нет — retrieval не вызывается).
    - Берёт текст последнего user-сообщения из БД (по `startUserMessageId` — совместимо с regenerate).
    - Вызывает `IRetrievalService.SearchAsync(query, "my_rag_docs", topK, chatId, userId)`.
    - Фильтрует по `Rag:Attachments:AutoInjectMinScore` (default 0.35).
    - Формирует блок по DESIGN § 4.7.5: «Ниже — релевантные фрагменты… [1] source (фрагмент N): текст».
    - При ошибке — лог Warning, продолжаем без RAG.
  - `BuildMessagesAsync`: system prompt = RAG-блок + оригинальный `chat.SystemPrompt` (через `\n\n`).
  - Конфиг: `Rag:Attachments:{AutoInjectTopK=5, AutoInjectMinScore=0.35}` (fallback в коде).
  - Тесты: `ChatStreamServiceTests` +3:
    - `StreamAsync_AutoInject_RagContextInSystemPrompt` — чанки есть → блок в system prompt.
    - `StreamAsync_AutoInject_NoRagChunks_NoInjection` — нет чанков → retrieval не вызывается.
    - `StreamAsync_AutoInject_LowScoreFiltered` — score < MinScore → блок не вставляется.
  - Fake `FakeLmStudioClient` расширен: `CapturedMessages` (все входящие JArray).
  - Fake `FakeRetrievalService` добавлен (записывает вызовы, настраиваемый результат).

### Added
- **Docs — синхронизация README/RULES с RAG-прогрессом (v1.5.0, KI-083)**:
  - README: статус тестов 124/124 → **188/188**; Chat видит 10 инструментов (было 7).
  - README: раздел RAG — прогресс фаз 1-5, 6A/6B.
  - RULES 1.4.14 → 1.4.15: § 4.40 (GUID-имена файлов в тестах),
    § 4.41 (`JToken.Value<T>()` без key → CS7036).

### Added
- **RAG / Knowledge Base — Шаг 6B: ChatAttachmentsController + лимиты multipart (v1.5.0, KI-083)**:
  - `ChatAttachmentsController` (4 endpoints):
    - `POST /api/chat/{chatId}/attachments` — загрузка (multipart, `[FromForm] IFormFile`), `[RequestSizeLimit(40 MB)]`.
    - `GET /api/chat/{chatId}/attachments` — список вложений.
    - `DELETE /api/chat/{chatId}/attachments/{attachmentId}` — удалить одно.
    - `POST /api/chat/{chatId}/attachments/clear` — очистить все.
  - Все endpoints: проверка владения чатом через `IChatService.GetChatAsync`.
  - Формат ответов: `{ success, data }` / `{ success: false, message }` — единый с проектом (без ProblemDetails).
  - `Startup.cs`: подняты лимиты multipart до 40 MB
    (`FormOptions.MultipartBodyLengthLimit` + `KestrelServerOptions.Limits.MaxRequestBodySize`).
    Причина: Kestrel по умолчанию 30 MB → 32 MB файл обрезался бы до нашей валидации.
  - Тесты: `ChatAttachmentsControllerTests` (9, unit через fake `IChatAttachmentService`).
  - HTTP-smoke через `WebApplicationFactory` — запланирован на Шаг 8 (полная integration-серия).

### Added
- **RAG / Knowledge Base — Шаг 6A-тесты: ChatAttachmentServiceTests (v1.5.0, KI-083)**:
  - 11 тестов `ChatAttachmentServiceTests`:
    - Upload: ValidFile + ChunksCount, TooLarge, UnsupportedFormat,
      ExceedsFilesPerChat, ExceedsTotalSize, SameHash_Dedup, ChatNotOwned,
      SavesFileInUserWorkspace.
    - Delete: RemovesFileAndChunks (файл + ingestion + БД).
    - ClearForChat: RemovesAll (+ папка chatId удалена).
    - GetForChat: ReturnsOnlyOwnAttachments.
  - Fake: `FakeWorkspaceResolver` (temp-root + `users/{userId}`),
    `FakeIngestionService` (записывает вызовы, настраиваемый результат).
  - Реальные: `RagDocumentParserRegistry` + `PlainTextParser`,
    `AppDbContext` через `TestDbContextFactory`.

### Added
- **RAG / Knowledge Base — Шаг 6A: ChatAttachment entity + сервис (v1.5.0, KI-083)**:
  - Entity `ChatAttachment` (`IIChatTools.Data/Entities/ChatAttachment.cs`).
  - `AppDbContext`: `DbSet<ChatAttachment>` + конфигурация (FK на `Chat` Cascade, 2 индекса).
  - DTO `ChatAttachmentDto` (`Id, ChatId, UserId, FileName, ContentType, SizeBytes, ChunksCount, UploadedAt`).
  - `IChatAttachmentService` — контракт: Upload / GetForChat / Delete / ClearForChat.
  - `ChatAttachmentService` (Scoped):
    - **Upload:** валидация размера/формата/лимитов; SHA256-дедупликация (тот же файл в том же чате → возвращает существующий); сохранение файла в `{UserWorkspace}/chat-attachments/{chatId}/{guid}.ext`; `IngestionAsync(my_rag_docs, ChatId, UserId)`; при ошибке ingestion — attachment остаётся (ChunksCount=0).
    - **Delete:** файл + чанки (`my_rag_docs`) + запись БД (best-effort, не падаем на ошибках).
    - **ClearForChat:** всё то же, но bulk + удаление пустой папки `chat-attachments/{chatId}`.
  - Конфиг: `Rag:Attachments:{MaxFileSizeBytes, MaxFilesPerChat, MaxTotalSizePerChat, StorageSubfolder}`.
  - **DI (`Startup.cs`):** `IChatAttachmentService` → `ChatAttachmentService` (Scoped).
  - Тесты: отдельный Шаг 6A-тесты.
  - Миграция: `AddChatAttachments`.

### Added
- **RAG / Knowledge Base — Шаг 5C: search_workspace (v1.5.0, KI-083)**:
  - `SearchWorkspaceTool : ITool` (`search_workspace`) — read-only.
    - Индекс `workspace` (per-user, **opt-in**).
    - Параметры: `query` (required), `topK` (optional, 1–20), `filePattern` (optional, substring).
    - **UserId обязателен из context**: `UserId ≤ 0` → Fail.
    - **Opt-in check**: читает `Workspace.Index.Enabled` из `IUserSettingsService`
      (per-user). Если `false` → Fail «Workspace index отключён» (без вызова Retrieval).
    - `filePattern` — post-filter по `DocumentPath` (Contains, OrdinalIgnoreCase),
      с over-fetch (`topK × 3`) для компенсации потерь.
  - **DI (`Startup.cs`):** `RegisterRagTools` — 3 RAG-tool (было 2), все Scoped.
  - Тесты: `SearchWorkspaceToolTests` (5) — Name/Params, Disabled_Fail,
    Enabled_Searches, UsesUserIdFromContext, FilePatternFilters.
  - **Фаза 5 (Retrieval + 3 Tools) закрыта. Chat видит 10 инструментов.**

### Added
- **RAG / Knowledge Base — Шаг 5B: search_knowledge_base + search_chat_history (v1.5.0, KI-083)**:
  - `SearchKnowledgeBaseTool : ITool` (`search_knowledge_base`) — read-only.
    - Индекс `project_docs` (глобальный).
    - Параметры: `query` (required), `topK` (optional, 1–20, default 5, clamp).
  - `SearchChatHistoryTool : ITool` (`search_chat_history`) — read-only.
    - Индекс `chat_history` (per-user).
    - Параметры: `query` (required), `topK` (optional), `chatId` (optional).
    - **UserId обязателен из context**: `UserId ≤ 0` → Fail (защита от утечки).
  - Формат ответа (для LLM): `{ query, count, results: [{rank, score, source, chunkIndex, text}] }`.
  - **DI (`Startup.cs`):** новый метод `RegisterRagTools`; 2 инструмента зарегистрированы как Scoped `ITool`.
  - Тесты: `SearchKnowledgeBaseToolTests` (5) + `SearchChatHistoryToolTests` (5) = 10.

### Added
- **RAG / Knowledge Base — Шаг 5A: IRetrievalService + RetrievalService (v1.5.0, KI-083)**:
  - `RetrievedChunkDto` — ChunkId, Text, Score, DocumentPath, ChunkIndex, IndexName, Metadata.
  - `IRetrievalService` — контракт `SearchAsync(query, indexName, topK?, chatId?, userId?, ct)`.
  - `RetrievalService` (Scoped) — оркестрация:
    - `IEmbeddingService.GetEmbeddingAsync(query)` — эмбеддинг запроса.
    - `IVectorStore.Search` с over-fetch (`topK × OverFetchMultiplier`).
    - Фильтрация по метаданным (chatId / userId).
    - Фильтрация по `Rag:Retrieval:MinScore` (default 0.3).
    - Enrichment из БД: `DocumentChunk.Text` + `MetadataJson` (парсится в `Dictionary<string,string>`).
  - Конфиг: `Rag:Retrieval:{DefaultTopK, OverFetchMultiplier, MinScore}`.
  - **DI (`Startup.cs`):** `IRetrievalService` → `RetrievalService` (Scoped).
  - **Fake-helper:** `IIChatTools.Tests.Fakes.FakeEmbeddingService` (детерминированные векторы +
    `SetVector` для точного контроля score) — переиспользуется в 5A/5B/5C.
  - Тесты: `RetrievalServiceTests` (7) — EmptyQuery, NoVectors, TopK/Sorted,
    MinScore, FiltersByChatId, FiltersByUserId, EnrichesMetadataFromDb.

### Added
- **RAG / Knowledge Base — Шаг 4C.3: DocumentIngestionServiceTests (v1.5.0, KI-083)**:
  - 11 тестов: IngestAsync Text/File/UnsupportedFormat/FileTooLarge,
    SameHash Skips/Reindexes, UpdatesVectorStore,
    DeleteDocumentAsync DB/VectorStore, ClearIndexAsync, ChatId-изоляция.
  - Fake-зависимости: `FakeEmbeddingService` (детерминированные 3-компонентные векторы, без HTTP).
  - Реальные: `PlainTextParser`, `RagDocumentParserRegistry`, `RecursiveChunkingStrategy`,
    `ChunkingStrategyResolver`, `InMemoryVectorStore`, `TokenCounter`.
  - `AppDbContext` — InMemory через `TestDbContextFactory`.
  - **Фаза 4 (Parser + Ingestion) закрыта.**

### Added
- **RAG / Knowledge Base — Шаг 4C.2a: MaxFileSizeBytes проверка (v1.5.0, KI-083)**:
  - `DocumentIngestionService`: при `SourceType = File` — проверка размера файла.
  - Лимит: `Rag:Ingestion:MaxFileSizeBytes` (default **32 MB** = `33_554_432` байт).
  - Превышение → `ArgumentException` с указанием фактического размера и лимита.
  - Проверка **до** `ParseAsync` — не тратим ресурсы на парсинг слишком больших файлов.

### Added
- **RAG / Knowledge Base — Шаг 4C.2: DocumentIngestionService (v1.5.0, KI-083)**:
  - `DocumentIngestionService : IDocumentIngestionService` (Scoped).
  - Оркестрация: `parse → chunk → embed → store`.
    - parse: `IRagDocumentParserRegistry.Resolve(filePath)` → `ParseAsync`.
    - chunk: `IChunkingStrategyResolver.Resolve(config)` + `ChunkingOptions` из `Rag:Chunking`.
    - embed: `IEmbeddingService.GetEmbeddingsAsync(chunks)`.
    - store: `INSERT DocumentChunks` (EF Core) + `IVectorStore.Add` по каждому чанку.
  - Идемпотентность: SHA256 содержимого → skip при совпадении hash (без `ForceReindex`).
  - Источники: File (парсинг), Text (inline, path = `text://{hash12}`), Url — NotSupportedException (v1.5.x).
  - `DeleteDocumentAsync` / `ClearIndexAsync` — чистка БД + `IVectorStore`.
  - **DI (`Startup.cs`):** `IDocumentIngestionService` → `DocumentIngestionService` (Scoped).
  - Тесты — Шаг 4C.3.

### Added
- **RAG / Knowledge Base — Шаг 4C.1: IDocumentIngestionService контракты (v1.5.0, KI-083)**:
  - `IngestionSourceType` (enum) — File | Text | Url.
  - `IngestionRequest` DTO — IndexName, FilePath/Text/Url, ChatId?, UserId, SourceType, ForceReindex, Source.
  - `IngestionResultDto` — IndexName, DocumentChunksCreated, TokensTotal, DurationMs,
    DocumentHash, DocumentPath, Skipped, SkipReason.
  - `IDocumentIngestionService` — IngestAsync / DeleteDocumentAsync / ClearIndexAsync.
  - Реализация — Шаг 4C.2, тесты — Шаг 4C.3.

### Added
- **RAG / Knowledge Base — Шаг 4B: IRagDocumentParserRegistry + DI (v1.5.0, KI-083)**:
  - `IRagDocumentParserRegistry` — контракт (Resolve, GetAllSupportedExtensions).
  - `RagDocumentParserRegistry` — Singleton, получает `IEnumerable<IRagDocumentParser>` через DI.
    - `Resolve` — линейный обход (первый матч по расширению побеждает).
    - `GetAllSupportedExtensions` — union расширений всех парсеров (case-insensitive).
  - **DI (`Startup.cs`):** `IRagDocumentParser` → `PlainTextParser` (Singleton);
    `IRagDocumentParserRegistry` → `RagDocumentParserRegistry` (Singleton).
  - Тесты: `RagDocumentParserRegistryTests` (3) — Resolve Txt, UnknownExt, Union.

### Added
- **RAG / Knowledge Base — Шаг 4A: IRagDocumentParser + PlainTextParser (v1.5.0, KI-083)**:
  - DTO `ParsedDocument` (Text, Metadata, OriginalSizeBytes, PageCount).
  - `IRagDocumentParser` — контракт (Name, SupportedExtensions, CanParse, ParseAsync).
  - `PlainTextParser` — 28 расширений (текст, разметка, код, конфиги).
    - **Чтение байтами** + ручной BOM-детект (`EF BB BF` / `FF FE` / `FE FF`).
    - Strict UTF-8 → fallback Windows-1251 (`CodePagesEncodingProvider`).
    - Defensive strip **всех** ведущих `\uFEFF` (BOM-символов).
    - Нормализация переносов `\r\n` / `\r` → `\n`.
    - Strip Markdown frontmatter (`--- ... ---`).
  - Тесты: `PlainTextParserTests` (8) — CanParse T/F, UTF-8, BOM, CP1251, CRLF, MD, empty.

### Added
- **RAG / Knowledge Base — Шаг 3C: Sentence/Fixed стратегии + Resolver (v1.5.0, KI-083)**:
  - `SentenceChunkingStrategy` (Name="sentence") — split по `. ! ? \n`, группировка предложений до `ChunkSize`.
  - `FixedChunkingStrategy` (Name="fixed") — жёсткий разрез по токенам (`Encode` → срез → `Decode`).
  - `IChunkingStrategyResolver` + `ChunkingStrategyResolver` — выбор по имени (case-insensitive, fallback на recursive).
  - `Startup.cs`: 3 стратегии + resolver (Singleton).
  - Тесты: `SentenceChunkingStrategyTests` (2) + `FixedChunkingStrategyTests` (2) + `ChunkingStrategyResolverTests` (2).
- **RAG / Knowledge Base — Шаг 3B: RecursiveChunkingStrategy (v1.5.0, KI-083)**:
  - `RecursiveChunkingStrategy` (Name="recursive", default) — рекурсивное разбиение: `\n\n` → `\n` → `. ` → … → ` ` → token-fallback.
  - `ChunkOverlap` при склейке (`TakeLastTokens`), `MergeSmallChunks` (MinChunkSize), `SplitByTokens` (fallback).
  - Тесты: `RecursiveChunkingStrategyTests` (6).
- **RAG / Knowledge Base — Шаг 3A: IChunkingStrategy + ChunkingOptions (v1.5.0, KI-083)**:
  - DTO `ChunkingOptions` (ChunkSize=500, ChunkOverlap=64, MinChunkSize=100, Separators, Strategy=recursive).
  - `IChunkingStrategy` — контракт (`Name` + `Chunk`).
  - `ITokenCounter.Encode(string) → IReadOnlyList<int>` + `Decode(IReadOnlyList<int>) → string`.
  - `TokenCounter`: реализация через `TiktokenTokenizer.EncodeToIds/Decode`.
  - Тесты: `TokenCounterTests` +2 (`EncodeDecode_RoundTrip`, `EncodeDecode_Empty`).
- **RAG / Knowledge Base — Шаг 2C: InMemoryVectorStore + DI (v1.5.0, KI-083)**:
  - `InMemoryVectorStore : IVectorStore, IDisposable` — Singleton.
  - `ConcurrentDictionary<string, IndexData>` + `Dictionary<int, VectorEntry>` + `ReaderWriterLockSlim`.
  - Add: write-lock, L2-нормализация, overwrite по chunkId. Search: read-lock, dot product.
  - `Startup.cs`: `services.AddSingleton<IVectorStore, InMemoryVectorStore>()`.
  - Тесты: `InMemoryVectorStoreTests` (11, включая `ConcurrentAdds_ThreadSafe` ×1000).
- **RAG / Knowledge Base — Шаг 2B: IVectorStore + VectorMath + DTO (v1.5.0, KI-083)**:
  - DTO `ChunkMetadata` (DocumentChunkId, IndexName, ChatId?, UserId, DocumentPath, ChunkIndex, Source).
  - DTO `VectorSearchResult` (ChunkId, Score, Metadata).
  - `IVectorStore` — интерфейс (Add, Search, Remove, Clear, Count, GetIndexNames).
  - `VectorMath` — helper (L2Normalize, DotProduct, CosineSimilarity). double-аккумулятор в DotProduct, ZeroNormThreshold=1e-12.
  - Тесты: `VectorMathTests` (6).
- **RAG / Knowledge Base — fix: warning CS1574 + KI-091 (v1.5.0, KI-083)**:
  - `DocumentChunk.cs`: `<see cref="IVectorStore"/>` → `<c>IVectorStore</c>` (тип в Services, у Data нет ссылки на Services).
  - KI-091 (Deferred): SqlServer цепочка миграций повреждена (snapshot drift). План пересборки — v1.5.0-rc.
  - DESIGN.md § 5.5 — примечание про SqlServer долг.
- **RAG / Knowledge Base — Шаг 2A: DocumentChunk entity + миграция (v1.5.0, KI-083)**:
  - Entity `DocumentChunk` (`IIChatTools.Data/Entities/DocumentChunk.cs`).
  - Конфигурация в `AppDbContext.OnModelCreating`: 3 индекса + FK на `Chat` (Cascade, nullable).
  - **FK на ApplicationUser НЕТ** (UserId=0 — маркер «глобальный чанк»; см. DESIGN § 5.2).
  - Миграция `AddDocumentChunks` (`IIChatTools.Data/Migrations/SqlServer/`).
  - DESIGN.md § 5.2 — уточнение про FK-связи.

### Added
- **RAG / Knowledge Base — Фаза 1: Embedding Service (v1.5.0, KI-083)**:
  - DTO `EmbeddingResponse` / `EmbeddingData` / `EmbeddingUsage` (`IIChatTools.Services/DTO/LmStudio/`).
  - `ILmStudioClient.GetEmbeddingsAsync(inputs, model, ct)` — POST `/v1/embeddings` (LM Studio).
  - `IEmbeddingService` + `EmbeddingService` (Singleton, батч 64, модель `nomic-embed-text-v1.5`, 768 dim).
  - **DI:** `ILmStudioClient` → **Singleton** (для совместимости с `EmbeddingService`); `IEmbeddingService` → Singleton.
  - `appsettings.json` + `.Development.json` — секция `Rag:Embedding` (Model, Dimensions, BatchSize, TimeoutSeconds, CacheEnabled).
  - **Тесты:** `EmbeddingServiceTests` (6) + `LmStudioEmbeddingTests` (6, mock HTTP).
  - Design: `docs/development/v1.5/DESIGN.md` § 4.1.

### Added
- **Docs — финальная чистка legacy, часть 5 (v1.4.x, финал)**:
  - `scripts/migrations/` (2 legacy `.ps1`) → `scripts/migrations/archive/v1.0.x/`.
  - `scripts/migrations/README.md` — новый.
  - `docs/development/ARCHITECTURE.md` § 3.5 — добавлены команды для создания миграций (SqlServer / Sqlite).

### Added
- **Docs — финальная чистка legacy, часть 4 (v1.4.x)**:
  - `scripts/build/` (8 legacy `.bat`/`.ps1`) → `scripts/build/archive/v1.0.x/`.
  - `scripts/build/README.md` — новый.
  - **PROMPT_V2.md**: восстановлена секция «📐 Формат вывода кода и ответа» (пример через 4-пробельный отступ — не ломает MD).

### Added
- **Docs — финальная чистка legacy, часть 3 (v1.4.x)**:
  - `scripts/diagnostics/` (3 legacy в архив, 3 удалены) → `scripts/diagnostics/archive/v1.0.x/`.
  - `scripts/diagnostics/README.md` — новый.
  - **PROMPT_V2.md v2.1 → v2.2**: секция «Формат вывода кода и ответа» (структура блоков, обязательные секции, правило «не выдумывай»).

### Added
- **Docs — финальная чистка legacy, часть 2 (v1.4.x)**:
  - `scripts/git/` (5 legacy-скриптов + 2 обёртки) → `scripts/git/archive/v1.0.x/`.
  - `scripts/git/README.md` — новый (описание почему каталог пуст).
  - Завершена чистка `scripts/setup/` (удалены 4 одноразовые обёртки).
  - **KI-088** — Planned: `docs/TESTING.md` (чек-лист ручной приёмки, v1.5.0).

### Added
- **Docs — финальная чистка legacy (v1.4.x)**:
  - `docs/guides/` (3 файла) + `docs/testing/` (2 файла) → `docs/development/archive/v1.0.x/guides-testing/`.
  - `scripts/setup/archive/v1.0.x/` — 8 legacy-скриптов (downloader*.ps1, check-packages, create_structure).
  - Удалены: 4 обёртки в `scripts/setup/` (одноразовые `.sh`/`.ps1`).
  - `scripts/setup/README.md` — новый (описание актуальных скриптов + порядок offline-restore).
  - Версия в шапках актуальных скриптов: v1.1.1 → v1.4.1.
  - `archive/README.md` — обновлена таблица структуры.

### Added
- **Docs — актуальный обзор архитектуры (KI-087, v1.5.x)**:
  - `docs/development/ARCHITECTURE.md` — сводный документ (9 разделов): слои, схема БД, DI-lifetime, поток Chat (SSE + approvals + regenerate), 46 инструментов, внешние зависимости, 10 ADR-style решений.
  - `docs/architecture/` (12 legacy-файлов эпохи v1.0 → v1.1) перенесены в `docs/development/archive/v1.0.x/architecture/`.
  - `archive/README.md` — обновлена структура.
  - `PROMPT_V2.md` — усилено правило о форматировании MD (в начале).

### Planned
- **v1.4.x**: KI-047 (PATCH/DELETE fallback), KI-053 (multi-user approvals — крупная), KI-077 (model:null при PUT), KI-082 (модалка-редактор).
- **v1.5.0**: RAG / Knowledge Base (KI-083) — **в работе**. Прогресс:
  - Фаза 0 (DESIGN) — ✅
  - Фаза 1 (Embedding Service) — ✅
  - Фаза 2 (Vector Store + DocumentChunk) — ✅
  - Фаза 3 (Chunking) — ✅
  - Фазы 4-8 (Parser+Ingestion, Retrieval+Tools, Attachments, Admin UI, Tests) — впереди.
  - См. [`docs/development/v1.5/DESIGN.md`](docs/development/v1.5/DESIGN.md).
- **v1.5.0-rc**: KI-091 (SqlServer миграции), KI-088 (TESTING.md).
- **v1.6.0**: KI-086 (вывод источников / citations из tool_result).

---

## [1.4.1] — 2026-09-25

**Chat UX polish + per-user retention + tiktoken-статистика.**

### Added
- **Admin UI — статистика запусков агентов (KI-076)**:
  - `AgentStatsDto` (`AgentName`, `DisplayName`, `TotalRuns`, `SuccessRuns`, `ErrorRuns`, `AvgDurationMs`, `LastRunAt`, `SuccessRate`).
  - `IAgentStatsService` + `AgentStatsService` — агрегация `AuditLogs` с `ToolName LIKE 'agent.%'` (один SQL-запрос с `GroupBy`).
  - `AgentToolBase` → инжектит `IAuditService`, пишет запись при каждом запуске агента (`ToolName = "agent.{AgentName}"`, `Status = Success / Error / Cancelled`, `DurationMs`, `ResultJson`).
  - `AdminAgentsController`: `GET /api/admin/agents/stats`.
  - `Admin.cshtml` → вкладка «Агенты»: карточки статистики (`agent-stats-grid`).
  - `admin-agents.js`: `loadAgentStats()`, `formatDuration`, `formatPercent`, `formatLastRun`.
  - `site.css`: стили `.agent-stat-card`.
  - 2 новых ключа `.resx` (`AgentStatsLastRun`, `AgentStatsEmpty`).
  - 3 теста `AgentStatsServiceTests`.

### Added
- **Chat — расширенная статистика генерации (KI-084a, v1.4.x)**:
  - `ChatMessage` +3 nullable-поля: `DurationMs`, `FirstTokenMs`, `FinishReason`.
  - `ChatStreamService` — Stopwatch (общая длительность) + время до первого delta + последний `finish_reason`.
  - `ChatStreamEvent.Done` +3 параметра (`durationMs`, `firstTokenMs`, `finishReason`).
  - `ChatMessageDto` +3 поля; `ChatController.GetChatAsync` — маппинг.
  - `chat.js` — meta: `123 / 45 токенов · 7.4 tok/s · 9.1 с`.
  - `.resx` (RU + EN): +2 ключа (`ChatMessageTokPerSec`, `ChatMessageDuration`).
  - **Требует миграции** `AddChatMessageStats` (SqlServer) / удаления `.db` (Sqlite, EnsureCreated).
- **Chat — токены в SSE-событиях `start` и `done` (KI-084b, v1.4.x)**:
  - `ChatStreamEvent.Start` — опциональный 3-й параметр `userTokens` (tiktoken).
  - `ChatStreamService`: `start` передаёт токены user-сообщения (из tiktoken или из БД при Regenerate); `done` передаёт **посчитанные** `contextTokens`/`completionTokens` (раньше — сырые `usage`, часто null).
  - Limit-message (когда исчерпаны 5 итераций): токены тоже считаются и сохраняются.
  - `chat.js`: `updateBubbleMeta()` — обновление meta-строки live; `formatTokenMetaText()` — вынесена из `formatTokenMeta`.
  - Токены теперь видны **сразу** во время стрима (не только после F5).
  - `appendUserMessage`/`appendAssistantBubble`: сохраняют `dataset.time` и `dataset.roleLabel` для последующего обновления meta.
- **Chat UI — отображение токенов в meta-строке (KI-049b, v1.4.x)**:
  - `ChatMessageDto`: +2 nullable-поля (`TokensIn`, `TokensOut`).
  - `ChatController.GetChatAsync`: маппинг токенов.
  - `chat.js`: `formatTokenMeta()` — user `15 токенов`, assistant `123 / 45 токенов`.
  - `Chat/Index.cshtml`: `data-label-tokens-user` / `data-label-tokens-assistant`.
  - `.resx` (RU + EN): +2 ключа (`ChatTokensUser`, `ChatTokensAssistant`).
  - Токены отображаются только при наличии (не null).
- **Chat — подсчёт токенов через tiktoken (KI-049a, v1.4.x)**:
  - Пакеты `Microsoft.ML.Tokenizers` + `Microsoft.ML.Tokenizers.Data.Cl100kBase` 1.0.0
    (API + BPE-словарь; одно без другого не работает).
  - `ITokenCounter` + `TokenCounter` (Singleton): `CountTokens(string)`, `CountConversation(messages)`.
  - `ChatStreamService`: заполняет `TokensIn`/`TokensOut` для user и assistant сообщений.
    - Если LM Studio отдала `usage` (non-stream) — использует точные значения.
    - Иначе — считает через tiktoken (приближение ±5-10%).
  - 7 unit-тестов (`TokenCounterTests`).
- **RULES v1.4.4**: § 4.31 (Retention:Enabled в Development — намеренно false).

### Fixed
- **KI-049a (fix)**: добавлен пакет `Microsoft.ML.Tokenizers.Data.Cl100kBase` — без него `TiktokenTokenizer.CreateForEncoding("cl100k_base")` падает с `InvalidOperationException: The tokenizer data file ... could not be loaded`.
- **KI-067-2 (fix)**: `ExecuteDeleteAsync` не поддерживается InMemory-провайдером EF Core 10. Решение: fallback — загрузка сущностей в память + `RemoveRange` + `SaveChangesAsync` (для тестов). Для SqlServer/Sqlite — прежний bulk-DELETE.

### Added
- **Users — UI для per-user retention (KI-067-3, v1.4.x)**:
  - **Backend:**
    - `UserSettingsDto` (`RetentionDays`, `DoNotDelete`, `GlobalRetentionDays`, `MaxRetentionDays`).
    - `AdminController`:
      - `GET /api/admin/users/{id}/settings` — текущие настройки.
      - `PUT /api/admin/users/{id}/settings` — сохранение (валидация 1..MaxDays, `null` = сброс override).
    - `ProfileController`:
      - `GET /profile` — Razor-страница профиля.
      - `GET /api/profile/settings` — свои настройки.
      - `PUT /api/profile/settings` — сохранение своих настроек.
  - **Frontend:**
    - `Views/Profile/Index.cshtml` — карточка «Хранение чатов» с полями.
    - `wwwroot/js/modules/profile.js` — загрузка/сохранение, дизейбл поля при `DoNotDelete`.
    - `admin.js`: кнопка ⚙ в строке пользователя → модалка с полями retention.
    - `_Layout.cshtml`: пункт меню «Профиль» (для залогиненных).
    - `_LoginPartial.cshtml`: displayName — ссылка на `/profile`.
  - **Локализация:** +10 ключей (`ProfileTitle`, `ProfileMenu`, `ProfileRetentionSection`, `ProfileGlobalHint`, `ProfileRetentionDays`, `ProfileRetentionDaysPlaceholder`, `ProfileRetentionDaysHint`, `ProfileDoNotDelete`, `ProfileDoNotDeleteHint`, `ProfileSave`).
- **Users — per-user retention чатов (KI-067-2, v1.4.x)**:
  - `IUserSettingsService.GetAllWithKeyPrefixAsync(prefix)` — получить все настройки по префиксу (для фонового сервиса).
  - `IChatService.DeleteOldChatsAsync(retentionDays, excludedUserIds)` — bulk-DELETE с исключением пользователей.
  - `IChatService.DeleteOldChatsForUserAsync(userId, retentionDays)` — bulk-DELETE для одного пользователя.
  - `ChatRetentionService`: чтение per-user overrides `Chat.RetentionDays` (int) и `Chat.DoNotDelete` (bool).
    - `DoNotDelete = true` — пользователь исключается полностью.
    - `RetentionDays = N` — свой срок хранения (clamp к `Chat:Retention:MaxDays`).
    - `DoNotDelete` побеждает `RetentionDays`.
  - 6 новых тестов (2 — `UserSettingsServiceTests`, 4 — `ChatServiceRetentionTests`).
- **UI — фирменный логотип IIChatTools (KI-081, v1.4.x)**:
  - `wwwroot/images/logo-icon.svg` — иконка (шестиугольник + переплетение), inline SVG.
  - `wwwroot/images/logo-full.svg` — иконка + текст «IIRuChating».
  - `wwwroot/site.webmanifest` — PWA-манифест (иконки 192/512).
  - Favicon: SVG + ICO + PNG 96 + apple-touch-icon + manifest (от realfavicongenerator).
  - Navbar-brand: логотип-иконка + «IIChatTools vX.Y.Z».
  - Hero на главной: логотип с текстом (240px).
  - Login / Register: логотип с текстом (200px).
  - Empty state в `/chat` (нет чата / пустой чат): иконка-логотип (64px) вместо 💬.
  - `site.css`: `.navbar-brand-logo`, `.home-hero-logo`, `.auth-logo`.
  - `chat.css`: `.chat-empty-logo`.

### Changed
- **Chat UI — поле ввода на всю ширину (KI-080, DeepSeek-style, v1.4.x)**:
  - `.input-group` → `.chat-input-box`: закруглённое поле (`border-radius: 1.5rem`) на всю ширину `.chat-main`, светлый фон (`#f6f8fa`).
  - Textarea: прозрачная внутри поля, без бордера, растёт вверх (`max-height: 200px`).
  - Кнопки Send/Stop: круглые SVG-иконки (стрелка вверх / квадрат) внутри поля справа.
  - Focus-ring на `.chat-input-box:focus-within` (убраны Bootstrap `box-shadow` на textarea).
  - Удалён мёртвый CSS KI-061a (правила для `.input-group`).
  - `enableInput`/`setStreamingUI` без изменений (те же `#btn-send`/`#btn-stop`).

### Added
- **Tests — KI-078B (v1.4.x)**: 8 тестов для `SearchUserChatsWithSnippetAsync`:
  - `EmptyQuery_ReturnsEmpty` — семантика «пустой запрос → пусто» (в отличие от `SearchUserChatsAsync`).
  - `ByTitle_ReturnsMatchedFieldTitle` — `MatchedField="title"`, `Snippet=null`.
  - `ByContent_ReturnsSnippetWithOffsets` — `MatchedField="content"`, offsets указывают на совпадение.
  - `PrioritizesTitleOverContent` — при совпадении и в title, и в content приоритет у title.
  - `SnippetHasEllipsis` — «…» по краям, когда совпадение в середине.
  - `DoesNotLeakOtherUsersChats` — фильтр по userId.
  - `NoMatches_ReturnsEmpty` — пустой результат.
  - `TruncatesLongQuery` — обрезка > 200 символов.
  - Всего тестов: **47 → 55**.
- **Chat UI — ⌘K-модалка поиска по чатам (KI-078B-2, v1.4.x)**:
  - Модалка по центру (`Ctrl+K` или SVG 🔍 в collapsed sidebar).
  - Input с debounce 200ms → `GET /api/chats?search=` (backend KI-078B-1).
  - Список результатов: title + относительная дата + snippet с подсветкой `<mark>`.
  - Навигация ↑/↓ (циклическая), Enter — открыть чат, Esc / клик вне / ✕ — закрыть.
  - Защита от гонок fetch (`globalSearchRequestId`).
  - Fix: SVG 🔍 в collapsed sidebar теперь открывает ⌘K-модалку (было — разворот sidebar).
  - Блокировка скролла body на время открытой модалки.
  - Fix: кнопка «Очистить поле» (появляется при непустом input; сброс без закрытия модалки).
  - Локализация RU + EN (6 ключей: `ChatSearchGlobal*`).
- **Chat API — расширенный поиск с snippet (KI-078B-1, v1.4.x)**:
  - `ChatSearchResultDto` (`Id`, `Title`, `Model`, `UpdatedAt`, `MatchedField`, `Snippet`, `SnippetMatchStart`, `SnippetMatchLength`).
  - `IChatService.SearchUserChatsWithSnippetAsync(userId, search)` — поиск с превью совпадения (≈30 символов до + 100 после), позиция совпадения для подсветки.
  - Приоритет: сначала title, потом content. Один чат — один результат.
  - `ChatListItemDto` расширен 4 nullable-полями (`MatchedField`, `Snippet`, `SnippetMatchStart`, `SnippetMatchLength`) — заполняются только при `search`.
  - `ChatController.GetChatsAsync`: при `search != null` — использует расширенный метод.
- **Chat UI — внутричатовый поиск (KI-078A, v1.4.x)**:
  - Панель поиска в правом верхнем углу `.chat-main` (Ctrl+F / кнопка 🔍 в header).
  - Подсветка совпадений `<mark class="chat-search-hit">` в ленте активного чата (user + assistant).
  - Активное совпадение — `.chat-search-hit-active` (жёлтый фон + синяя рамка) + авто-скролл.
  - Счётчик «N из M», кнопки ↑ / ↓ (циклический переход), Enter / Shift+Enter.
  - Закрытие: `Esc`, клик вне панели, кнопка ✕.
  - Debounce 150ms; поиск по `.chat-message-content` (tool-блоки не трогаем).
  - Авто-закрытие при переключении чата.
  - Локализация RU + EN (7 ключей: `ChatSearchInChat*`).
- **Chat UI — collapse/expand sidebar (KI-079, DeepSeek-style, v1.4.x)**:
  - Collapsed-состояние — mini-rail 56px с иконками (SVG): «+» (новый чат), 🔍 (поиск), `«`/`»` (toggle).
  - Кнопка «+ Новый чат» — текст в expanded, SVG `+` в collapsed.
  - Кнопка 🔍 — только в collapsed (в expanded используется input `#chat-search`).
  - Кнопка toggle — одна, работает в обоих состояниях (title меняется по data-атрибутам).
  - Анимация `width .2s ease`; collapsed → `.chat-container.chat-sidebar-collapsed` (`width: 0`).
  - Сохранение состояния в `localStorage["chat.sidebarCollapsed"]`, восстановление при загрузке.
  - Горячая клавиша `Ctrl+B` (toggle, `preventDefault` — не конфликтует с закладками браузера).
  - Mobile (< 768px): collapse отключён, sidebar всегда виден.
  - Локализация RU + EN (`ChatSidebarCollapse` / `ChatSidebarExpand`).

### Planned
- **v1.4.x**: KI-047 (PATCH/DELETE fallback), KI-049 (tokensIn/Out через tiktoken), KI-053 (multi-user approvals), KI-067 (per-user retention), KI-076 (статистика по агентам), KI-077 (model:null при PUT), KI-078 (внутричатовый поиск + подсветка), KI-079 (collapse sidebar), KI-080 (поле ввода на всю ширину).
- **v1.5.0**: RAG (Qdrant / embeddings), Knowledge base.

---

## [1.4.0] — 2026-09-24

**Multi-Agent (KI-052)** — Chat работает через 6 специализированных суб-агентов + универсальный `consult_secondary_agent`.

### Added
- **v1.4.0 Фаза 1 (KI-052)**: реестр специализированных суб-агентов.
  - DTO `SubAgentDescriptor` — Name, DisplayName (RU), Description, SystemPrompt, AllowedTools, Model, MaxSteps, RequiresApprovalByDefault, Disabled.
  - `SubAgentTaskRequest.SystemPromptOverride` + `ModelOverride` (обратносовместимо).
  - `ISubAgentRegistry` + `SubAgentRegistry` (singleton, читает `SubAgents:*` из appsettings.json).
  - 6 агентов в `appsettings.json` (+ в Development): `file_system_agent`, `code_agent`, `web_agent`, `git_agent`, `github_agent`, `planner_agent`.
  - 4 unit-теста (`SubAgentRegistryTests`).
  - `docs/development/v1.4/DESIGN.md` — дизайн-документ фазы.
- **v1.4.0 Фаза 6.1-6.4 (KI-052)**: backend админки агентов.
  - DTO: `AgentListItemDto`, `UpdateAgentRequest`.
  - `AdminAgentsController`: `GET /api/admin/agents`, `PUT /api/admin/agents/{name}`, `POST /api/admin/agents/{name}/reset`.
  - Persist override'ов: JSON в `AppSettings` по ключу `SubAgents.{name}` (upsert).
  - Восстановление при старте: `Program.LoadSubAgentOverridesAsync`.
  - Локализация: 15 ключей в `.resx` (RU + EN).
- **v1.4.0 Фаза 6.5-6.6 (KI-052)**: frontend админки агентов.
  - `Admin.cshtml`: 7-я вкладка «Агенты» (таблица: техническое имя, отображаемое, модель, инструментов, approval, вкл/выкл, действия).
  - `admin-agents.js`: модуль вкладки (загрузка, редактирование, сброс); ленивая инициализация через `shown.bs.tab`.
  - `admin.js`: `showModal` экспортирован для переиспользования.
  - Модалка редактирования: DisplayName, Description, Model, MaxSteps, SystemPrompt, AllowedTools (textarea построчно), RequiresApproval, Disabled.
- **v1.4.0 Фаза 7 (KI-052)**: тесты `AgentToolBase` (7 новых).
  - `AgentToolBaseTests` (Integration): пустой/длинный task, unknown/disabled агент, передача дескриптора в `SubAgentTaskRequest`, clamp maxSteps к 30.
  - Всего тестов: **47/47** (было 40).

### Changed
- **v1.4.0 Фаза 2 (KI-052)**: `ModelOverride` + `SystemPromptOverride` в суб-агентах.
  - `ILmStudioClient.CompleteAsync(..., string model = null)` — перегрузка с явной моделью (обратносовместимо).
  - `LmStudioClient`: если `model` передан — используется он; иначе `LmStudio:Model`.
  - `SubAgentService`: `BuildSystemMessage(maxSteps, overridePrompt)` — `SystemPromptOverride` имеет приоритет над `SubAgent:SystemPrompt`.
  - Основной цикл, финальное резюме и reviewer — все используют `request.ModelOverride`.
- **v1.4.0 Фаза 3 (KI-052)**: `AgentToolBase` + 6 инструментов-обёрток.
  - `AgentToolBase` — единый базовый класс (params, resolve дескриптора, вызов `SubAgentService` через `Func<ISubAgentService>` — разрыв DI-цикла).
  - 6 наследников: `FileSystemAgentTool`, `CodeAgentTool`, `WebAgentTool`, `GitAgentTool`, `GitHubAgentTool`, `PlannerAgentTool`.
  - `RequiresApprovalByDefault` резолвится из дескриптора (`SubAgents:X.RequiresApproval`).
  - `SubAgentService`: расширена защита от рекурсии — запрет `consult_secondary_agent` + любого `*_agent` внутри суб-агента.
- **v1.4.0 Фаза 4 (KI-052)**: усилены system-промпты всех 6 агентов.
  - `web_agent`: обязательно использовать инструменты перед ответом, указывать источник.
  - `file_system_agent`: проверять `list_directory` перед операциями.
  - `code_agent`: обязательно проверять код запуском.
  - `git_agent`: начинать с `git_status`, не делать force-push.
  - `github_agent`: начинать с `gh_auth_status`.
  - `planner_agent`: `save_memory` / `get_system_info` по назначению.
- **v1.4.0 Фаза 5 (KI-052)**: Chat использует `SubAgentRegistry` вместо `SubAgent:DefaultAllowedTools`.
  - `ChatStreamService`: в конструктор добавлен `ISubAgentRegistry`.
  - Список tools = `SubAgentRegistry.GetEnabled()` (6 агентов) + `consult_secondary_agent` (fallback).
  - **Эффект:** Chat видит **7 инструментов** вместо 12. LLM вызывает `file_system_agent` вместо `list_directory` + `read_file` + `save_file` по отдельности.

### Fixed
- **KI-073 (Fixed)**: первый `dotnet test` на Windows после cold build занимал ~44-60 с. Диагностика: **Defender — не главная причина** — виноват **testhost boot** (30+ DLL из API, PuppeteerSharp). Решено: `scripts/setup/configure-defender.ps1` + отключение Dev Drive protection + reboot → **1.4 с**.
- **KI-075**: `ToolRegistry` логировал «инициализирован» на `LogInformation` при каждом scope — спам в проде. Понижено до `LogDebug`.

### Documented
- **KI-076** (Deferred, v1.4.x): статистика по агентам (TotalRuns, AvgTime, SuccessRate) в админке.
- **KI-077** (Documented): `model: null` при PUT агента = «сбросить на default», а не «не менять».
- **KI-078, KI-079, KI-080** (Deferred, v1.4.x): по мотивам DeepSeek — внутричатовый поиск + подсветка, collapse sidebar, поле ввода на всю ширину.

---

## [1.3.1] — 2026-09-24

### Added
- **KI-068**: новый метод `IChatService.SearchUserChatsAsync(userId, search)` — регистронезависимый поиск (кросс-провайдерно). Пустой запрос эквивалентен `GetUserChatsAsync`.

### Changed
- **KI-069**: переименование чата в sidebar теперь через **inline-edit** (ChatGPT-style) вместо `prompt()`. Двойной клик по названию → `<input>`, **Enter** = сохранить, **Esc** = отмена, **blur** = сохранить. Кнопка ✏️ вызывает тот же inline-edit. Backend (`PATCH /api/chats/{id}`) без изменений.
- **KI-068**: поиск по чатам теперь **server-side** (по названию + содержимому сообщений). Запрос `GET /api/chats?search={q}`. Frontend — debounce 300ms, убран клиентский фильтр.

### Fixed
- **KI-068 (регрессия)**: поиск по чатам возвращал неполный список при кириллице. Причина: SQLite `LOWER()` не обрабатывает не-ASCII — `LOWER('Привет') = 'Привет'`, поэтому `LIKE '%прив%'` не матчил. Решение: фильтрация в памяти через `string.Contains(term, StringComparison.OrdinalIgnoreCase)`. См. RULES § 4.26.
- **KI-071**: даты в JSON сериализовались без суффикса `Z` (Sqlite + EF Core возвращают `DateTime` с `Kind=Unspecified`). JS `new Date()` парсил их как local → только что созданные чаты показывались как «3 ч назад» (UTC+3). Решение: `AddNewtonsoftJson(o => o.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc)` + то же для `SseJsonSettings`. См. RULES § 4.27.
- **KI-072**: при активном поиске новый чат попадал в отфильтрованный sidebar. Решение: `createChat()` сбрасывает поиск и перезагружает полный список перед созданием.
- **KI-064**: `wikipedia_search` через корпоративный прокси зависал на ~43 секунды. Решение: явный `Timeout = 15s` + retry 1 раз с задержкой 1s. См. RULES § 4.28.
- **KI-043**: утечка памяти в `RateLimitingMiddleware`. Решение: `Timer` каждые 2 минуты удаляет `LimiterEntry` с `LastUsedUtc` > 5 минут. Middleware реализует `IDisposable`.

---

## [1.3.0] — 2026-09-23

### Added
- **Chat UI (v1.3 Фаза 1.1)**: сущности `Chat` и `ChatMessage` + миграция `AddChatAndChatMessages`. Таблицы `Chats`, `ChatMessages` в БД; индексы `IX_Chats_UserId_UpdatedAt`, `IX_ChatMessages_ChatId_CreatedAt`.
- **ChatService (v1.3 Фаза 1.2)**: `IChatService` + `ChatService` — CRUD чатов и сообщений, проверка владения (`userId`), `DeleteOldChatsAsync` для retention.
- **LM Studio SSE (v1.3 Фаза 1.3)**: `ILmStudioClient.ChatStreamAsync` — `IAsyncEnumerable<ChatCompletionChunk>` для стриминга. DTO `ChatCompletionChunk` (DeltaContent, DeltaReasoning, DeltaToolCall, FinishReason, Usage, IsDone). Парсер SSE-формата (`data: {...}`, `[DONE]`, tool_calls частями). Сохранён `CompleteAsync` для `SubAgentService`.
- **Design doc v1.3**: `docs/development/v1.3/DESIGN.md` — Chat UI (sidebar, SSE, инлайн tool calls), API-контракты, схема данных, план работ.
- **Unit-тесты**: 5 новых на парсинг SSE (`LmStudioSseParseTests`). Всего: **24/24**.
- **ChatController (v1.3 Фаза 1.4)**: REST API чатов — `GET /api/chats` (список), `GET /api/chats/{id}` (детали + история), `POST /api/chats` (создать), `PATCH /api/chats/{id}` (обновить title/model/systemPrompt), `DELETE /api/chats/{id}` (удалить). DTO: `ChatListItemDto`, `ChatDetailDto`, `ChatMessageDto`, `CreateChatRequest`, `UpdateChatRequest`. Проверка владения (`userId` из claims), `[Authorize]` на всех методах.
- **Tool calling infrastructure (v1.3 Фаза 1.6.A)**: `ToolDefinitionsBuilder` — единый построитель JSON-схем инструментов в формате OpenAI Function Calling. Используется Chat (10 инструментов из `SubAgent:DefaultAllowedTools`) и SubAgent. Поддерживает белый список и список исключений.
- **Tool calling — SSE events (v1.3 Фаза 1.6.A.2.1)**: `ChatStreamEvent.ToolCall` и `ChatStreamEvent.ToolResult`. DTO `ChatToolCallDto` (`id`, `name`, `arguments`, `requiresApproval`) и `ChatToolResultDto` (`id`, `name`, `success`, `content`, `message`). Готовит почву для multi-turn loop в `ChatStreamService`.
- **Tool calling — аккумулятор (v1.3 Фаза 1.6.A.2.2)**: `ToolCallsAccumulator` — накопление инкрементальных `delta.tool_calls` из SSE-стрима LM Studio. Складывает `arguments` по `index`, собирает завершённые вызовы формата OpenAI.
- **Tool calling — multi-turn loop (v1.3 Фаза 1.6.A.2.3)**: `ChatStreamService.StreamAsync` расширен до multi-turn цикла (до 5 итераций). Стрим → накопление `tool_calls` → выполнение через `IToolRegistry` → SSE-события `tool_call` / `tool_result` → сохранение в БД → повторный стрим. При `Request.UseTools == false` — старое поведение (без tools). Инструменты с `RequiresApprovalByDefault == true` возвращают `ToolResult.Fail` (полная реализация — Фаза 1.7).
- **Тесты tool calling (v1.3 Фаза 1.6.B)**: 2 новых интеграционных теста в `ChatStreamServiceTests` — успешный multi-turn loop (list_directory → результат → финальный ответ) и approval-инструмент (save_file → ToolResult.Fail без выполнения). FakeLmStudioClient расширен — поддержка итераций. Всего: **29/29**.
- **Approvals в чате — каркас (v1.3 Фаза 1.7.1)**: `ChatApprovalDecision` (enum), `ChatApprovalRequiredDto`, `ChatApprovalResolvedDto`, `IChatApprovalCoordinator`. Дополнены `ChatStreamEvent.ToolApprovalRequired` и `.ToolApprovalResolved`. Готовит почву для Singleton-координатора (Шаг 1.7.2).
- **Approvals в чате — REST-endpoints (v1.3 Фаза 1.7.4)**: `POST /api/chat/approvals/{callId}/approve` и `POST /api/chat/approvals/{callId}/reject`. Вызывают `IChatApprovalCoordinator.ResolveAsync`, будят ожидающий SSE-стрим. `[Authorize]` — только аутентифицированные. Если ожидающий не найден (таймаут) — `{ success: false }`.
- **Approvals в чате (v1.3 Фаза 1.7 — полная реализация)**: 
  - `POST /api/chat/approvals/{callId}/approve` — подтвердить вызов инструмента.
  - `POST /api/chat/approvals/{callId}/reject` — отклонить.
  - SSE-события `tool_approval_required` / `tool_approval_resolved`.
  - Координатор Singleton + cleanup (KI-043 учтён).
  - camelCase в SSE-событиях (единый стиль).
  - Smoke-тест end-to-end: `save_file` → approval_required → reject → tool_result(fail) → финал.
- **Фаза 1.7 v1.3 закрыта.** Backend полностью готов к Chat UI.
- **Chat UI — каркас страницы `/chat` (v1.3 Фаза 2.0.1)**: 
  - `ChatViewController` + Razor-страница `Views/Chat/Index.cshtml`.
  - Layout: sidebar (список чатов) + область сообщений + поле ввода.
  - `wwwroot/css/chat.css` — стили чата.
  - `wwwroot/js/modules/chat.js` — заглушка (инициализация без логики).
  - `_Layout.cshtml`: пункт меню «Чат» + `@RenderSection("Styles")`.
  - **Chat UI — локализация (v1.3 Фаза 2.0.1)**: 16 новых ключей в `SharedResources.resx` (EN) и `SharedResources.ru.resx` (RU). Ключи для страницы `/chat`: `Чат`, `Новый чат`, `Удалить чат`, `Выберите чат или создайте новый`, `Введите сообщение…`, `Отправить`. Резерв для Шагов 2.0.2–2.0.4 (sidebar CRUD, SSE-ошибки). Соответствует правилу 1.14.
- **Chat UI — endpoint моделей (v1.3 Фаза 2.0.2a)**: `GET /api/models` — возвращает список моделей LM Studio (`/v1/models`) + модель по умолчанию из `LmStudio:Model` (всегда первая, `isDefault: true`). Fallback: если LM Studio недоступен — возвращается только default (UI работает в offline-режиме). `[Authorize]`. DTO `ModelInfoDto` (`id`, `name`, `isDefault`). Готовит почву для UI-селектора модели (DESIGN.md § 13.1).
- **Chat UI — sidebar (v1.3 Фаза 2.0.2b)**: `chat.js` реализован полностью для sidebar:
  - `loadChats()` / `loadModels()` — загрузка списка чатов и моделей.
  - `createChat()` — создание (модель по умолчанию из `/api/models`).
  - `selectChat(id)` — переключение с загрузкой истории.
  - `deleteChat(id)` — удаление с подтверждением.
  - `renderChatList()` / `renderMessages()` — рендер sidebar и ленты.
  - Синхронизация активного чата с URL через `history.replaceState` (`?chatId=N`).
  - Относительные даты (`только что`, `5 мин назад`, `вчера`, `22.09`).
  - Минимальное отображение tool calls (серые блоки с именем инструмента).
  - Автоскролл к последнему сообщению.
  - `chat.css` дополнен стилями сообщений и tool-блоков.
- **Chat UI — SSE-стриминг (v1.3 Фаза 2.0.3)**: `chat.js` умеет отправлять сообщения и стримить ответ:
  - `sendMessage()` — `POST /api/chat/stream` с `useTools: true`.
  - `readSseStream()` — чтение SSE через `fetch` + `ReadableStream`.
  - `handleSseEvent()` — обработка `start`/`delta`/`done`/`error`/`tool_call`/`tool_result`.
  - `tool_approval_required` / `tool_approval_resolved` — заглушки (полная обработка — Фаза 2.0.4).
  - Оптимистичное отображение user-пузыря, индикатор «Печатает…» (три точки), потоковая отрисовка delta.
  - Блокировка input/кнопки на время стрима, автоскролл (только если пользователь был внизу).
  - Enter — отправка, Shift+Enter — новая строка, автоувеличение textarea.
  - Локальное обновление `UpdatedAt` в sidebar после `done`.
  - `chat.css`: стили `chat-typing-indicator`, `chat-message-streaming`, `chat-message-error`, `chat-tool-result.success/error`.
- **Chat UI — Approvals (v1.3 Фаза 2.0.4)**: интеграция `_ApprovalModal` в чат.
  - `approvals.js`: рефакторинг — общая логика вынесена в `_showApprovalModal({ approveUrl, rejectUrl, askReason })`.
  - Новый экспорт `requestChatApproval(callId, ...)` — для flow `/api/chat/approvals/{callId}/...` (без prompt причины, см. Q6 Фазы 1.7).
  - `requestApproval(actionId, ...)` — сохранён (обратная совместимость с `/test`).
  - `chat.js`: обработка SSE-события `tool_approval_required` — показ модалки (fire-and-forget, не блокирует SSE reader).
  - `chat.js`: `tool_approval_resolved` — логирование (UI-индикатор — v1.3.x).
  - Стрим продолжается автоматически после решения: `tool_approval_resolved` → `tool_result` → `delta` → `done`.
- **Chat UI — переименование + авто-нумерация (v1.3 Фаза 2.0.5a)**:
  - Кнопка ✏️ в sidebar рядом с 🗑 (появляется при hover / на активном чате).
  - `renameChat(id)` — `prompt()` с текущим именем → `PATCH /api/chats/{id}` (`{ title }`). Пустое имя отклоняется; при совпадении с текущим — no-op.
  - Локальное обновление `state.chats` + синхронизация с header для активного чата.
  - `generateNextChatTitle()` — авто-нумерация «Новый чат», «Новый чат 2», «Новый чат 3», … (учитывает максимальный существующий N).
  - `chat.css`: `.chat-list-item-delete` → `.chat-list-item-action` (общий класс для edit/delete), разные цвета hover (edit — синий, delete — красный).
- **Chat UI — ChatGPT-style скроллинг (v1.3 Фаза 2.0.5b)**:
  - `state.autoScroll` — флаг прилипания к низу ленты.
  - Scroll listener на `#chat-messages` — если пользователь отскроллил > 80px от низа → `autoScroll = false`, вернулся к низу → `autoScroll = true`.
  - `scrollToBottom(force)` — при `force === true` скроллит принудительно и включает autoScroll; иначе — только при `autoScroll === true`.
  - Кнопка «↓ Вниз» (`.chat-scroll-down`) — появляется при `autoScroll === false`, клик → принудительный скролл. Создаётся динамически в JS (Razor не тронут).
  - При переключении чата (`selectChat`) `autoScroll` сбрасывается в `true`.
  - `chat.css`: `.chat-main` → `position: relative`, стили `.chat-scroll-down`.
- **Chat UI — Фаза 2.0 закрыта (v1.3)**: Chat UI реализован end-to-end.
  - `/chat` — Razor-страница с sidebar, лентой сообщений, полем ввода.
  - Sidebar: список чатов (относительные даты), создание, удаление, переименование (✏️), авто-нумерация «Новый чат N».
  - SSE-стриминг: `POST /api/chat/stream`, потоковая отрисовка delta с мигающим курсором.
  - Tool calling: до 5 итераций, SSE-события `tool_call` / `tool_result`.
  - Approvals: модалка `_ApprovalModal` с drag-and-drop, X=Reject, countdown 5 минут.
  - ChatGPT-style скроллинг: кнопка «↓ Вниз», автоскролл отключается при ручной прокрутке вверх.
  - Enter — отправка, Shift+Enter — новая строка, автоувеличение textarea.
  - Синхронизация активного чата с URL (`?chatId=N`).
  - Локализация RU/EN.
  - Скриншоты и подробности — `docs/development/v1.3/DESIGN.md` § 14.
- **Chat UI — Copy message (v1.3 Фаза 2.1.1)**: на каждом сообщении (user/assistant) — кнопка 📋 под контентом. Появляется при hover, копирует текст в clipboard. Inline-фидбек (✅ на 1.5 сек). Работает и во время стриминга (dataset.copyText синхронизируется с накопленным текстом). Fallback — `execCommand('copy')` для HTTP / старых браузеров. Tool-блоки не копируются.
- **Chat UI — Markdown + code blocks (v1.3 Фаза 2.1.4)**: рендеринг ответов LLM через `marked` + `DOMPurify` (bundled локально в `wwwroot/lib/marked/` и `wwwroot/lib/dompurify/`).
  - `renderMarkdown(text)` — `DOMPurify.sanitize(marked.parse(text))` (XSS-защита обязательна; LLM-вывод никогда не вставляется без sanitize).
  - Поддерживаются: `**bold**`, `*italic*`, `## headings`, списки, `code`, ` ```code blocks``` `, ссылки, таблицы.
  - Во время стрима — plain-text (без мерцания); после `done` — финальный Markdown-рендер (`finalizeAssistantBubble`).
  - **Code blocks** — ChatGPT-style: wrapper `.chat-code-block` с шапкой (язык + кнопки Copy/Download).
    - Copy — копирует код, inline ✅ на 1.5 сек.
    - Download — скачивает как файл с расширением по языку (`langToExtension`).
- `chat.css`: стили `.chat-markdown` (GitHub-like) и `.chat-code-block`.
- **Chat UI — подсветка синтаксиса (v1.3 Фаза 2.1.4.1)**: `highlight.js` 11.9.0 (UMD, полный бандл) + тема `github.min.css`, bundled локально в `wwwroot/lib/highlight/`. Подсветка срабатывает в `enhanceCodeBlocks()` через `hljs.highlightElement(code)`. Автодетект языка по `class="language-xxx"` или auto-detect. Фон/паддинг темы нейтрализованы, чтобы wrapper `.chat-code-block` (#f6f8fa) оставался единым — работает только палитра токенов. Fallback: если `hljs` не загружен — code block без подсветки, Copy/Download работают.
- **Chat UI — Regenerate endpoint (v1.3 Фаза 2.1.2.2)**:
  - `ChatStreamRequest.Regenerate` (bool) — если `true`, стрим не сохраняет новое user-сообщение, а удаляет последний assistant-exchange (assistant + tool) и генерирует ответ заново от последнего user.
  - `POST /api/chat/regenerate` — принимает `{ chatId }`, эмулирует `StreamAsync` с `Regenerate = true` и `UseTools = true`. Возвращает SSE-поток, идентичный `/api/chat/stream`.
  - `ChatStreamController`: общая логика вынесена в private `StreamInternalAsync` (используется и `stream`, и `regenerate`).
  - `ChatStreamService.StreamAsync`: при `Regenerate = true` вызывает `DeleteLastAssistantExchangeAsync`, находит последний user-message и стартует общий multi-turn loop.
- **Chat UI — Regenerate (v1.3 Фаза 2.1.2.3)**: кнопка 🔄 в `.chat-message-actions` на **последнем** assistant-сообщении (рядом с 📋). Клик: удаляет последний assistant-пузырь из DOM, вызывает `POST /api/chat/regenerate`, стримит новый ответ через существующий `readSseStream`. Кнопка 🔄 автоматически перевешивается на новый bubble в `finalizeAssistantBubble()` и снимается со всех остальных.
- **Chat UI — Stop (v1.3 Фаза 2.1.3.2)**: кнопка ⏹ рядом с input для прерывания стрима.
  - `#btn-stop` — отдельный элемент в `.input-group`; во время стрима `#btn-send` скрыта, `#btn-stop` показана.
  - `state.abortController` + `fetch(..., { signal })` — при Stop рвётся SSE-соединение.
  - `AbortError` — частичный assistant-пузырь удаляется из DOM; **частичный ответ не сохраняется в БД**.
  - Работает для обычного стрима и Regenerate.
  - **Audit при Stop (2.1.3.3)**: при отмене в `AuditLogs` пишется запись `Status = "Cancelled"`, `ToolName = chat_stream | chat_regenerate`, `DurationMs` (Stopwatch), `ClientIp`. Текст сообщения не логируется (без PII).
- **Chat UI — Retry после Stop (v1.3 Фаза 2.1.3.5, KI-065)**:
  - Кнопка «🔄 Повторить» под последним user-сообщением — если ответ был прерван через Stop или не сгенерирован.
  - Backend: `ChatStreamService.StreamAsync` (Regenerate) — убрана жёсткая проверка `deleted == 0`; best-effort удаление; работает при последнем user (когда ответ не сохранён).
  - Frontend: `regenerateLastMessage({ allowNoAssistant: true })` — переиспользован для Retry; `showRetryOnLastUser()` вызывается из `AbortError`; `renderMessages` показывает кнопку при загрузке истории, если последнее — user.
  - CSS: `.chat-message-action-with-text`, `.chat-message-actions-always-visible`. 
- **Chat UI — Audit Stop (v1.3 Фаза 2.1.3.3)**: `ChatStreamController` инжектит `IAuditService`; при `OperationCanceledException` (клиент нажал Stop) пишется запись в `AuditLogs`:
  - `ToolName`: `chat_stream` (обычный) или `chat_regenerate` (Regenerate).
  - `Status`: `Cancelled`.
  - `ParametersJson`: `{ chatId, regenerate, hasMessage }` — **без текста сообщения** (правило 5.x — без PII).
  - `ResultJson`: `{ reason: "user_stop" }`.
  - `DurationMs`: длительность стрима (Stopwatch).
  - `ClientIp`: из `HttpContext.Connection.RemoteIpAddress`.
- **Chat UI — AI-title frontend (v1.3 Фаза 2.2.1b)**:
  - `maybeGenerateTitle()` — фоновый `POST /api/chats/{id}/generate-title` после **первого** успешного ответа в пустом чате.
  - Триггер: `state.activeChatMessageCount === 0` до отправки + `dataset.streamCompleted === '1'` после `done`.
  - Защита от повторного вызова: title должен матчить `/^Новый чат(\s+\d+)?$/` — иначе пропускаем (пользователь переименовал вручную).
  - Race-safe: `chatIdAtRequest` фиксируется — если пользователь переключится, пока идёт запрос, обновится правильный чат.
  - При успехе: `renderChatList()` + `renderChatHeader()` — без F5.
  - `state.activeChatMessageCount` — новый счётчик для активного чата (init в `selectChat`).
- **Chat UI — селектор модели (v1.3 Фаза 2.2.4, DESIGN § 13.1)**: `<select id="chat-model-select">` в header чата (вместо `<small>`).
  - Заполняется из `state.models` (уже загружено в `initChatPage` → `loadModels`).
  - Формат: `<id>` + ` (по умолчанию)` у `isDefault: true`. Если текущей модели нет в списке — disabled option `<id> (недоступна)`.
  - `onChatModelChanged`: `PATCH /api/chats/{id}` с `{ model }`; оптимистично обновляет `state.activeChat.model` + `state.chats[i].model`; при ошибке — откат.
  - Селектор **disabled во время стрима** (`setStreamingUI`). Попытка смены в этот момент игнорируется.
  - `showEmptyState`: очистка селекта (нет активного чата).
  - Локализация: 4 новых ключа (`Модель`, `по умолчанию`, `недоступна`, `Нет моделей`) — передаются в JS через `data-*`-атрибуты.
  - `chat.css`: `.chat-model-select` (моноширинный, компактный, hover/focus/disabled).
- **Chat retention (v1.3 Фаза 2.2.5, DESIGN § 13.2)**: фоновый `ChatRetentionService` (по образцу `AuditRetentionService`).
  - Удаляет чаты старше `Chat:Retention:DefaultDays` (по умолчанию — 30 дней) через `IChatService.DeleteOldChatsAsync` (bulk DELETE через `ExecuteDeleteAsync`).
  - Первый запуск — через 2 минуты после старта; далее раз в `CleanupIntervalHours` (по умолчанию — 24 ч).
  - Защита от опечаток: `DefaultDays` ограничивается сверху `MaxDays` (365).
  - `Chat:Retention:Enabled = false` — полностью отключает retention (рекомендуется в dev).
  - Метрика Prometheus: `iichattools_chat_cleanup_total` (label `reason="retention"`).
  - Логирование: `Retention чатов: удалено {Count} чатов старше {Cutoff}`.
  - Per-user override — отложено в v1.3.x (**KI-067**).
- **Chat UI — поиск по чатам (v1.3 Фаза 2.2.3)**: input `#chat-search` в sidebar (между кнопкой «+ Новый чат» и списком).
  - Клиентский фильтр по `chat.title` (case-insensitive, `includes`).
  - `state.searchQuery` сохраняется при createChat/deleteChat/renameChat — фильтр не сбрасывается.
  - «Ничего не найдено» — если ни один чат не матчит (локализация через `data-label-no-results`).
  - Поиск по содержимому сообщений — отложено в v1.3.x (**KI-068**).
- **Chat UI — Backend edit user-message (v1.3 Фаза 2.2.6a)**:
  - `IChatService.EditUserMessageAsync(messageId, userId, newContent)` — обновляет `Content` user-сообщения + удаляет все сообщения после (ассистент + tool + последующие) + обновляет `Chat.UpdatedAt`.
  - `POST /api/chat/messages/{id}/edit` — принимает `{ content }`. Возвращает `{ success, data: { messageId, deletedCount } }`.
  - Валидация: не пустое, role == "user", владение через `chat.UserId`. Обобщённый ответ «Сообщение не найдено» — не палим чужие чаты.
  - `ChatStreamController`: + `IChatService` в конструктор.
  - DTO: `EditUserMessageRequest` (request), `EditUserMessageResult` (сервис).
- **Chat UI — inline-edit user-message (v1.3 Фаза 2.2.6b)**:
  - Кнопка ✏️ в `.chat-message-actions` на **user**-сообщениях (при hover).
  - Для свежих сообщений ✏️ добавляется в SSE-событии `start` (после optimistic-рендера id ещё не известен). Для истории (F5) — сразу в `renderMessage`.
  - Клик по ✏️ → `.chat-message-content` заменяется на `<textarea>` + кнопки Save/Cancel.
  - **Enter** = Save, **Shift+Enter** = новая строка, **Esc** = Cancel.
  - Save: `POST /api/chat/messages/{id}/edit` (Фаза 2.2.6a) → обновление UI → удаление DOM-сообщений после → `regenerateLastMessage({ allowNoAssistant: true })`.
  - Edit доступен только в `state.isStreaming === false`.
  - `chat.css`: `.chat-message-edit` (textarea), `.chat-message-edit-actions`.
  
### Changed
- **ChatStreamService (v1.3 Фаза 1.6.A.2.4)**: добавлена зависимость `IWorkspaceResolver`. `ToolExecutionContext.WorkspaceRoot` теперь реально резолвится (было `null`) — FS-инструменты в чате работают.
- **Config (v1.3 Фаза 1.6.A.2.4)**: `SubAgent:DefaultAllowedTools` обновлён — только **read-only** инструменты без approval: `read_file`, `find_files`, `get_file_metadata`, `fuzzy_find_local_files`, `git_status`, `git_log`, `git_diff`, `web_search`, `wikipedia_search`, `get_system_info`. Mutating-инструменты (`list_directory`, `save_file`, `replace_text_in_file`, `execute_command`, `git_add`, …) требуют approval и станут доступны в чате после Фазы 1.7.
- **`appsettings.Development.json`**: добавлен `SubAgent:DefaultAllowedTools` (ранее отсутствовал — все 40 инструментов попадали в чат).
- **`ListDirectoryTool` (v1.3 Фаза 1.6.A.2.4)**: `RequiresApprovalByDefault` → `false`. `list_directory` — read-only операция (возвращает список файлов, ничего не меняет), не требует подтверждения. Добавлен обратно в `SubAgent:DefaultAllowedTools`.
- **Документация (v1.3 Фаза 1.6.B)**: обновлены `docs/KNOWN_ISSUES.md` — KI-051 → Resolved, KI-049 дополнен, добавлены KI-054 (approvals в чате, Фаза 1.7) и KI-055 (tool calling реализован). KI-048 дополнен планом автоматизации git config.
- **Тест `StreamAsync_ToolCalling_RequiresApproval_DoesNotExecute` (v1.3 Фаза 1.7.3)**: обновлён под новую логику — проверяет SSE-события `tool_approval_required` и `tool_approval_resolved` + сообщение «Пользователь отклонил вызов инструмента» (ранее — «Требуется подтверждение»).
- **`.gitignore` (v1.3 Фаза 2.0.2b)**: добавлены правила для `cookies.txt` и `.commit-msg.txt` — артефакты локальных smoke-тестов и here-string-коммитов.
- **Chat UI — sidebar (v1.3 Фаза 2.0.2b)**: кнопка удаления чата добавлена в каждый элемент sidebar (появляется при hover / на активном чате). `<a>` → `<div role="button" tabindex="0">` — позволяет вкладывать `<button>` без семантических конфликтов. Кнопка 🗑 в header (`#btn-delete-chat`) сохранена для активного чата.
- **Chat UI — AI-title cleanup (v1.3 Фаза 2.2.1b)**: удалены диагностические `console.log` из `sendMessage`/`maybeGenerateTitle` после подтверждения работы. Оставлен один информативный `[chat] AI-title: "..."` + `console.warn` на реальные проблемы (нет чата, regex не матчит, response failed).

### Fixed
- **v1.3 Фаза 1.1**: warnings CS0108 (Chat.UpdatedAt скрывает BaseEntity.UpdatedAt — намеренно, `new`), CS0618 (HasName → HasDatabaseName в EF Core 10).
- **v1.3 Фаза 1.3**: warnings CS1574 (cref ArgumentNullException и др. — добавлен `using System;`), CA2024 (reader.EndOfStream в async — заменён на проверку `line == null`).
- **KI-057 (v1.3 Фаза 2.0.2a)**: `GET /api/models` — embedding-модели (например, `text-embedding-nomic-embed-text-v1.5`) исключаются из списка heuristic-фильтром по подстроке `embed`. Причина: эти модели не поддерживают `/v1/chat/completions` и приводят к 400 при выборе в чате.
- **Chat UI — approvals UX (v1.3 Фаза 2.0.4)**: две проблемы, выявленные на smoke-тесте:
- **Модалку нельзя было подвинуть** — добавлен drag-and-drop по `.modal-header` (курсор `move`, `position: fixed` на время drag, сброс стилей при `hidden.bs.modal`).
- **Закрытие крестиком (X) подвешивало UI** — модалка закрывалась, но reject на сервер не отправлялся; SSE-стрим ждал 5 минут, input/btn-send оставались заблокированными. Теперь **X = Reject**: при `hidden.bs.modal` без решения автоматически отправляется reject на сервер (в /chat — без причины, в /test — с reason «Закрыто пользователем»).
- Исправлена утечка listener'ов `hidden.bs.modal` — `{ once: true }`.
- `getOrCreateInstance` вместо `new bootstrap.Modal(...)` — нет warning'ов при повторных открытиях.
- **KI-046 (v1.3 Фаза 2.0.6a + hotfix)**: `MessageCount` в `ChatListItemDto` — реализован подсчёт через `IChatService.GetMessageCountsAsync(userId)` (один SQL `GROUP BY ChatId`). Sidebar отображает meta-строку в формате «<дата> · N сообщ.» через `formatChatMeta(chat)` (пустые чаты — только дата).
- **KI-059**: placeholder `CHANGE_ME_VIA_USER_SECRETS` в `Browser:ProxyServer` трактовался как реальный прокси. Из-за этого `web_search` и `wikipedia_search` падали с `SocketException 11001` (хост `change_me_via_user_secrets:80` неизвестен). Исправлено: helper `IsRealProxyUrl()` в `Startup.cs` и `Program.cs` — placeholder / пустые / невалидные URL игнорируются, прокси не устанавливается.
- **KI-060**: user-сообщения рендерились как Markdown (Фаза 2.1.4). Если пользователь писал ` ``` ` или `**bold**` в обычном тексте, они интерпретировались как разметка → пустой code block с шапкой «без» в user-пузыре. Теперь user-сообщения — plain text (`renderUserContent`), Markdown применяется только к assistant.
- **KI-061**: кнопка «Отправить» перекрывала textarea при одном ряде текста. Фикс: `min-height` для textarea (стандарт Bootstrap `.form-control`), `align-items: stretch` в `.input-group`, `.btn { align-self: stretch; height: auto }`.
- **KI-062**: отсутствовал автофокус на поле ввода при создании/выборе чата. Фикс: `input.focus()` в `selectChat()` (покрывает оба сценария, т.к. `createChat` вызывает `selectChat`).
- **KI-061a**: box-shadow фокуса на textarea перекрывал кнопку «Отправить». Focus-ring перенесён на `.input-group:focus-within`; `textarea:focus` и `.btn:focus` → `box-shadow: none`.
- **KI-066**: кнопка Copy (📋) пропадала под ответом ассистента до F5. Причина: `renderMessageActions('')` в `appendAssistantBubble()` не создаёт кнопку при пустом тексте (правка 2.2.1a). Фикс: `finalizeAssistantBubble()` пересоздаёт `.chat-message-actions` с финальным текстом после Markdown-рендера.

### Security
- Н/Д

### Documented
- **KI-064**: `wikipedia_search` иногда падает с `SSL connection could not be established` (SocketException 10054) через корпоративный прокси — внешняя сетевая проблема, не баг приложения. LLM переключается на `web_search`. Планируется уменьшение таймаута + retry в v1.3.x.

---

## [1.2.0] — 2026-09-21

### Added
- **Docker**: `Dockerfile` (multi-stage, SDK 10.0 → ASP.NET Runtime 10.0), `docker-compose.yml` (prod + опциональный SQL Server), `docker-compose.override.yml` (dev), `.dockerignore`, `.env.example`. Опциональный Chromium через build-arg `INSTALL_BROWSER`.
- **CI/CD**: GitHub Actions — `ci.yml` (build + test + coverage artifacts), `docker-publish.yml` (образ в ghcr.io на main и теги v*), `dependabot.yml` (авто-обновления NuGet + Actions).
- **Health checks**: `/health/live` (liveness), `/health/ready` (БД + Workspace), `/health` (полный JSON-отчёт). Анонимные endpoints.
- **Rate limiting**: собственный `RateLimitingMiddleware` на `System.Threading.RateLimiting`. Политики: per-user (100/min), tools-execute (30/min), auth (5/min). JSON-ответ 429 с `retryAfterSeconds`. `/health/*` без лимита.
- **Prometheus метрики**: `/metrics` (анонимный). Стандартные `http_requests_received_total`, `http_request_duration_seconds`, `dotnet_collection_count_total`, `process_*`. Кастомные: `iichattools_tool_executions_total`, `iichattools_tool_execution_duration_seconds`, `iichattools_pending_approvals`, `iichattools_active_users`, `iichattools_audit_entries_total`, `iichattools_lmstudio_requests_total`, `iichattools_audit_cleanup_total`.
- **Audit retention**: `AuditRetentionService` — фоновый BackgroundService чистит `AuditLogs` (ExecuteDeleteAsync) и `logs/audit/*.jsonl` по retention policy. Настраивается через `Audit:CleanupIntervalHours`, `Audit:DatabaseRetentionDays`, `Audit:FileRetentionDays`.
- **MSSQL migrations**: `IIChatTools.Data/Migrations/SqlServer/20260921102238_InitialSqlServer` — версионирование схемы для SQL Server. `__EFMigrationsHistory` в БД.
- **Скрипты setup**: `scripts/setup/enable-online-restore.ps1` (создаёт `NuGet.Config.online`), `scripts/setup/fill-local-packages.ps1` (наполняет `LocalPackages`).
- **Config**: `NuGet.Config.example.online` — шаблон онлайн-конфига.

### Changed
- **Directory.Build.props**: версии Microsoft-пакетов синхронизированы с ref-pack SDK 10.0.401 → **10.0.12**.
- **`AppMetrics`** перенесён в `IIChatTools.Services/Metrics` — восстановлена слоистость (Data → Services → API).
- **`MetricsRefreshBackgroundService`** перенесён в `IIChatTools.API/BackgroundServices`.
- **`Startup.cs`**: `UseMiddleware<RateLimitingMiddleware>()` вместо `UseRateLimiter()` (см. KI-042).

### Fixed
- **KI-042**: `Microsoft.AspNetCore.RateLimiting` недоступен в SDK 10.0.401 — реализован собственный `RateLimitingMiddleware`.
- **KI-045**: HELP-описания метрик переведены на английский (устранены кракозябры в PowerShell с CP866).
- **Auth**: `[FromForm]` для `LoginAsync` и `RegisterAsync` — устранён HTTP 415 при работе с Razor-формой (побочный эффект `[ApiController]`).

### Documented
- **KI-043**: утечка памяти в `RateLimitingMiddleware` (лимитеры не очищаются при истечении окна) — запланировано на v1.2.x.
- **KI-044**: `iichattools_audit_entries_total` и `iichattools_lmstudio_requests_total` пока не инкрементируются — запланировано на v1.2.x.

---

## [1.1.1] — 2026-09-18

### Changed
- **KI-037**: удалён legacy-ключ `ConnectionStrings:DefaultConnection`. Единственный источник строки SqlServer — `Database:SqlServerConnectionString`. Fallback в `DbContextOptionsExtensions` заменён на явную ошибку.
- **KI-038**: `Workspace:RootPath` вынесен в User Secrets. `WorkspaceResolver` раскрывает env-переменные через `Environment.ExpandEnvironmentVariables`. Устранены мержи локальных путей.
- **KI-005** (API-изменение): `git_add` требует явного `all: true` для добавления всех изменений или непустой `files`. Устранён неявный `git add -A` при пустом `files`.

### Fixed
- **KI-001**: на `/test` при отклонении действия в поле «Результат» отображается причина отклонения. `requestApproval` возвращает `{ decision, reason }`; `ToolsController` при `Rejected` передаёт `data.rejectionReason`. Отмена диалога причины (`Cancel`) больше не вешает модалку.
- **KI-005**: `git_add` — исправлено формирование CLI: `--` добавляется один раз перед списком файлов (а не перед каждым).

### Security
- **KI-022**: обновлён `SQLitePCLRaw.bundle_e_sqlite3` до 2.1.13 (GHSA-2m69-gcr7-jv3q, High).
- **KI-036**: удалён хардкод прокси-credentials из `Program.cs` и скриптов. Значения — только через env/User Secrets.
- **KI-039**: устранена утечка прокси-credentials и email автора в git-истории. `git filter-repo --replace-text` + `--mailmap` + `--invert-paths`, force-push всех веток и тегов. GitHub Secret Scanning: «No secrets found».

### Removed
- Папка `UPDATES/` (устаревшие копии).
- Дубли `configs/NuGet1.Config`, `configs/NuGet111.Config`.
- Ad-hoc скрипты `scripts/setup/download-missing*.ps1`, `downloader*.ps1`.
- Локальные SQLite-БД `IIChatTools.API/Data/*.db` (не коммитились).

---

## [1.1.0] — 2026-09-18

### Added
- **Native LM Studio tool calling** — подтверждено end-to-end.
- **Subagent E2E loop** — многошаговые задачи с автоотладкой.
- **Offline-развёртывание** через `LocalPackages/` (без интернета).
- **`LmStudioTestController`** — endpoint для тестирования LM Studio.
- **`SubAgentController`** — API для суб-агентов.
- **`scripts/diagnostics/*.ps1`** — диагностические скрипты для tool calls.
- **`docs/KNOWN_ISSUES.md`**: KI-015 … KI-038 (24 новых записи).

### Changed
- **.NET Core 3.1 → .NET 10 LTS** (KI-015).
  - `TargetFramework`: `netcoreapp3.1` → `net10.0`, `LangVersion latest`.
  - EF Core 3.1.32 → **10.0.4**.
  - ASP.NET Core / Microsoft.Extensions → **10.0.4**.
  - IdentityModel 6.35.0 → **8.14.0**, HtmlAgilityPack 1.11.61 → **1.12.1**.
- **Версионирование**: `AppVersion.Current` читается из `AssemblyInformationalVersion` (источник — `<Version>` в `Directory.Build.props`).
- **Локализация**: ключ `WelcomeTitle` с плейсхолдером `{0}` вместо версии в ключе (KI-031a).
- **`global.json` и `NuGet.Config`** перенесены в корень репозитория (KI-016, KI-017).
- **`_Layout.cshtml`**: `@(AppVersion.Current)` вместо `@AppVersion.Current` (Razor-парсер путал с email).
- **`Localizer["..."]`** → **`Localizer["..."].Value`** во всех контроллерах (JSON-сериализация `LocalizedString`).
- **`README.md`** полностью переписан под .NET 10 LTS.

### Fixed
- **KI-018**: дублирование `PuppeteerSharpVersion` в `Directory.Build.props`.
- **KI-019**: избыточные ссылки на `Microsoft.Extensions.Logging.*` (NU1510).
- **KI-020**: `LocalPackages` в формате global-packages folder вместо source.
- **KI-021**: кодировка PowerShell искажала вывод `dotnet`.
- **KI-030**: двойной `©` в логе запуска.
- **KI-031**: версия `1.0.2` / `v1.0` в UI вместо `1.1.0`.
- **KI-033**: `favicon.ico` → 404.
- **KI-034**: ASP.NET Core developer certificate не доверен.
- **KI-035**: 4 интеграционных теста `ApprovalService` падали (сидирование пользователей в `TestDbContextFactory`).

### Removed
- Устаревший пакет `Microsoft.AspNetCore.Identity` 2.2.0.
- Избыточные `Microsoft.Extensions.Logging`, `.Console`, `.Debug` из API-проекта.
- Файл `IIChatTools.API/Resources/Как использовать в коде.txt`.

### Security
- Пароль прокси сменён (инцидент 2026-09-18, см. v1.1.1).

---

## [1.0.2] — 2026-09-16

### Added
- Гибридная БД: SqlServer / Sqlite / InMemory (выбор через `Database:Provider`).
- `CompositeAuditService` — аудит в БД + JSONL-файл (`logs/audit/`).
- `.gitattributes` для нормализации LF/CRLF.
- `Directory.Build.props` — централизованные версии пакетов.
- **ChatStream DTO (v1.3 Фаза 1.5)**: `ChatStreamRequest` и `ChatStreamEvent` (start/delta/done/error) — типы для SSE-стриминга ответов LLM.
- **ChatStreamService (v1.3 Фаза 1.5)**: `IChatStreamService` + `ChatStreamService` — координатор SSE-стриминга. Сохраняет user message → строит историю (JArray, OpenAI-формат) → вызывает `ILmStudioClient.ChatStreamAsync` → сохраняет assistant message с токенами → возвращает `IAsyncEnumerable<ChatStreamEvent>`.
- **ChatStreamController (v1.3 Фаза 1.5)**: `POST /api/chat/stream` — SSE-эндпоинт. Заголовки `text/event-stream`, `X-Accel-Buffering: no` (обход прокси/nginx), flush после каждого события. События: `start`, `delta`, `done`, `error`.
- **Unit-тесты ChatStreamService (v1.3 Фаза 1.5)**: 3 теста с `FakeLmStudioClient` — успешный стрим (start/delta/done + сохранение), отсутствие чата (error), пустое сообщение (error). Всего: **27/27**.

### Changed
- Git-инструменты: параметр `path` (каталог репо) + `filePath` (файл) — KI-004.
- GitHub-инструменты: параметр `path` для работы в подкаталогах — KI-006.
- Браузер: `--remote-allow-origins=*`, `--proxy-bypass-list=<-loopback>`, `--ignore-certificate-errors` (Chromium 111+).
- Реалистичный User-Agent для обхода headless-детекции (KI-003).

### Fixed
- **KI-002**: 407 Proxy Authentication Required для веб-инструментов.
- **KI-003**: 403 Forbidden от Wikipedia API.
- **KI-004**: git-инструменты не работали в подкаталогах.
- **KI-006**: gh-инструменты не работали в подкаталогах.
- **KI-050**: rate limiting на HTML-эндпоинтах (`/auth/login`, `/auth/register`) — теперь возвращает **303 redirect** с query `error=ratelimit&retryAfter=N` вместо сырого JSON. `Login.cshtml` / `Register.cshtml` показывают красный banner «Слишком много попыток входа. Попробуйте снова через N секунд». Для API-клиентов (`Accept: application/json`) сохранён прежний 429 JSON.
- **`AuthController`**: удалён атрибут `[ApiController]` — это UI-контроллер (возвращает Razor-Views), и автоматическая модель-валидация возвращала `ProblemDetails` 400 вместо формы с ошибками.

### Removed
- Бинарные артефакты из истории (`bin/`, `obj/`, `LocalPackages/`, `logs/`, `Workspace/`, `*.db`).
  - `git filter-repo --invert-paths` + `git gc --prune=now --aggressive`.
  - Размер `.git`: **154 МБ → 1.51 МБ** (KI-010).
- Gitlink `Workspace/users/1/git-test/` (KI-011).
- Множественные `obj/` из индекса (KI-012).

---

## [1.0.1] — 2026-09-14

### Changed
- Реорганизация структуры репозитория: `docs/`, `scripts/`, `configs/`, `assets/images/` (KI-013).
- Перемещение временных файлов отладки в `docs/development/archive/` (KI-014).

### Fixed
- Мелкие правки после v1.0.

---

## [1.0.0] — 2026-09-13

### Added
- Первый публичный релиз.
- **40 инструментов**: файловая система (13), выполнение кода (3), веб (3), Git (7), GitHub (7), браузер (4), суб-агенты (1), утилиты (2).
- **Мультипользовательность**: роли Admin/User, изоляция workspace.
- **Аутентификация**: cookie (Razor) + JWT (API).
- **Система подтверждений**: `PendingAction` + polling `/api/approvals/pending`.
- **Локализация RU/EN**: `SharedResources.resx` + `IStringLocalizer<SharedResources>`.
- **Суб-агенты**: `ISubAgentService` + `Func<ISubAgentService>` (разрыв DI-цикла).
- **Браузерная автоматизация**: PuppeteerSharp 7.1.0 + Edge/Chrome через прокси.
- **Аудит**: `AuditLog` в БД.
- **Razor UI**: главная, статус, админка, тест, логин/регистрация.
- **ES-модули**: `api`, `ui`, `status`, `approvals`, `admin`, `test`.

---

## Ссылки

- [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/)
- [Semantic Versioning](https://semver.org/lang/ru/)
- [GitHub Releases](https://github.com/iilmchat/IIChatTools/releases)
- [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) — реестр проблем

---

## Как обновлять

1. **В процессе работы** — добавляйте записи в секцию `[Unreleased]`.
2. **При релизе**:
   - Замените `[Unreleased]` на `[X.Y.Z] — YYYY-MM-DD`.
   - Создайте пустую `[Unreleased]` сверху.
   - Обновите `AppVersion.Current` и `<Version>` в `Directory.Build.props`.
   - Создайте git-тег: `git tag -a vX.Y.Z -m "..."`.
3. **Типы записей** — только из списка: Added / Changed / Deprecated / Removed / Fixed / Security.
4. **Ссылки на KI** — обязательно, если изменение связано с реестром проблем.
