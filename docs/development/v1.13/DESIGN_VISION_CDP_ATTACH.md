# DESIGN v1.13 — Vision Agent: PuppeteerSharp CDP-attach (KI-161)

**Версия:** 1.0 (Draft)
**Дата:** 2026-10-07
**Статус:** Draft — ожидает согласования
**Связанные KI:** KI-161 (PuppeteerSharp DOM+Vision — этот документ), KI-131 (Vision Agent), KI-162 (Coordinate-then-Verify), KI-190 (bounds-center), KI-194 (Planner `done` без проверки)
**Целевой релиз:** v1.13.x
**Связанные документы:** [DESIGN_VISION_AGENT.md](../v1.12/DESIGN_VISION_AGENT.md) § 2.4, § 9.1 · [RULES.md](../RULES.md) § 4.34, § 4.44, § 4.46, § 4.48, § 4.51 · [KNOWN_ISSUES.md](../../KNOWN_ISSUES.md)

---

## § 1. Контекст

### § 1.1. Проблема: VL-координаты и ±20-30 px

Smoke-прогоны v1.13.x (KI-195/196) показали три категории ошибок клика:

| Источник координат | Ошибка | Симптом |
|---|---|---|
| `center` от VL (первый describe) | ±30 px | Промах в левый край кнопки (chatId=43) |
| `bounds-center` (KI-190) | ±3-5 px на крупных, ±20 px на мелких | ChatId=46 — клик по `search_button` не привёл к переходу |
| `Verify` (KI-162, crop ×2) | ±20 px (не улучшает) | chatId=37/38: bounds-center попадает, verify — нет |

**Причина:** Qwen2.5-VL-7B на полном скриншоте (даже с downscale до 1280×720) даёт ошибку grounding ±20-30 px. Crop ×2 не помогает — второй VL-вызов даёт ту же ошибку, только с ложной `confidence: 0.90` (KI-162-fix2).

**Следствие:** для browser-задач клик по VL-координатам ненадёжен. HOTKEY-first (KI-195) закрыл submit-формы, но клики по произвольным кнопкам / ссылкам остаются проблемой.

### § 1.2. Industry best practice: DOM+Vision hybrid

Playwright, Puppeteer, Anthropic Computer Use и другие фреймворки для browser-автоматизации используют **DOM-координаты** там, где элемент доступен через селектор, и **fallback на VL** только для canvas / WebGL / shadow-DOM / desktop-приложений.

**Ключевая идея:** у PuppeteerSharp есть `elementHandle.BoundingBoxAsync()` — координаты элемента в viewport, **0 px ошибки**. Chrome, запущенный с `--remote-debugging-port=9222`, отдаёт эти координаты через CDP любому клиенту, включая наш backend.

### § 1.3. Что уже есть в проекте

- **`BrowserSessionManager`** (`Services/Implementation/Browser/`): запускает свой Chromium через `Puppeteer.LaunchAsync`, держит сессии в `ConcurrentDictionary<string, SessionHolder>`, умеет `page.ClickAsync(selector)` / `page.TypeAsync(selector, text)` / `page.ScreenshotDataAsync()`.
- **`LocalHarnessVisionBackend`** (`Services/Implementation/VisionAgent/`): запускает Chrome с fresh-профилем (`--user-data-dir=%TEMP%\vision-profile-{id}`), делает GDI-скриншоты, отправляет мышь/клавиатуру через Win32 `SendInput`.
- **`VisionAgentService.ResolveCoordinatesAsync`**: превращает семантический `target` (id из `ui_elements`) в `(x, y)`. Сейчас — bounds-center (KI-190) + опциональный Verify (KI-162).

**Ключевое различие:** `BrowserSessionManager` — инфраструктура для `browser_*`-инструментов (пользователь сам вызывает `goto` / `click(selector)`). `LocalHarnessVisionBackend` — для `vision_agent` (LLM решает, что кликнуть). Они запускают **свой** Chrome каждый; в KI-161 нужен третий режим — **attach к уже запущенному Chrome**.

### § 1.4. Цели v1.13.x

| # | Цель | Метрика |
|---|---|---|
| 1 | Vision Agent кликает по DOM-элементам с 0 px ошибки | Попадание 100% для DOM-доступных элементов |
| 2 | Fallback на VL-координаты сохраняется | Canvas / WebGL / shadow-DOM работают |
| 3 | Опциональное отключение через конфиг | `CoordinateProvider:Mode = "vision"` → старое поведение |
| 4 | Обратная совместимость | `BrowserSessionManager`, `browser_*` — не меняются |

### § 1.5. Что НЕ входит

- **Автоматический выбор провайдера по типу задачи** (LLM сам решает «это canvas») — не в v1.13.x.
- **Set-of-Mark (KI-163)** — отдельная итерация.
- **Sandbox / RemoteVnc backends** — DOM-доступ через MCP/VNC — отложено вместе с Ф3/Ф4 KI-131.
- **Полная замена VL на DOM** — VL остаётся основным провайдером «что на экране», DOM только уточняет «где кликнуть».

---

## § 2. Решение

### § 2.1. Архитектура

```
┌────────────────────────────────────────────────────────────────┐
│ VisionAgentService.ResolveCoordinatesAsync(action)             │
│   1. action.Target != null                                     │
│   2. element = screen.UiElements.FirstOrDefault(id == target)  │
└─────────────────────┬──────────────────────────────────────────┘
                      │
                      ▼
┌────────────────────────────────────────────────────────────────┐
│ var domProvider = _backend.GetCoordinateProvider()             │
│ if (domProvider != null && Mode ∈ {dom, auto}):                │
│                                                                │
│   CoordinateRequest                                            │
│   ├── TargetId: "search_button"                                │
│   ├── TargetLabel: "Найти"                                     │
│   ├── TargetType: "button"                                     │
│   ├── TargetBounds: {x,y,w,h}   (VL fallback)                  │
│   └── ScreenshotScaleX/Y: 1.875  (для конвертации)             │
│                                                                │
│   ↓ DomCoordinateProvider.ResolveAsync()                       │
│                                                                │
│   IChromeCdpSession → PuppeteerSharp → JS в page:              │
│     - candidates = querySelectorAll('button, [role=button], …')│
│     - match by label (case-insensitive substring)              │
│     - match by position (getBoundingClientRect ∩ bounds)       │
│     - return center in viewport coords (CSS px)                │
│                                                                │
│   → конвертация CSS px → screen px → screenshot-space:         │
│     screenX = (window.screenX + vpX) * DPR                     │
│     shotX   = screenX / ScreenshotScaleX                       │
└─────────────────────┬──────────────────────────────────────────┘
                      │ Found=true
                      ▼
┌────────────────────────────────────────────────────────────────┐
│ return (result.X, result.Y)  ← 0 px error (для DOM-элементов)  │
└────────────────────────────────────────────────────────────────┘

                      │ Found=false / Mode=vision / CDP недоступен
                      ▼
┌────────────────────────────────────────────────────────────────┐
│ Fallback: bounds-center (KI-190) + опциональный Verify (KI-162)│
│   return (bounds.X + bounds.W/2, bounds.Y + bounds.H/2)        │
└────────────────────────────────────────────────────────────────┘
```

### § 2.2. Два провайдера

| Провайдер | Когда используется | Точность |
|---|---|---|
| **`DomCoordinateProvider`** | Backend подключён к Chrome через CDP, элемент найден в DOM | 0 px |
| **`VisionCoordinateProvider`** | Backend не поддерживает CDP, элемент не найден в DOM, `Mode=vision` | ±20-30 px (bounds-center) |

**`VisionCoordinateProvider`** — обёртка над существующим кодом (bounds-center + Verify из KI-162). Не требует нового поведения, только формализует интерфейс.

### § 2.3. Запуск Chrome с CDP

`LocalHarnessVisionBackend.OpenAsync` добавляет в args:
```
--remote-debugging-port=9222
```

**Ограничения:**
- Если Chrome уже запущен с другим `--user-data-dir` — флаг игнорируется (Chrome не переиспользует порт). Мы всегда используем **fresh profile** (KI-155), так что коллизий нет.
- Порт **занят** другим процессом → Chrome падает. `OpenAsync` логирует Warning, `IChromeCdpSession` не подключается, DOM-режим автоматически отключается для этой задачи (без падения).
- Порт **hardcoded** в конфиге `VisionAgent:CoordinateProvider:Cdp:BrowserUrl` (default `http://127.0.0.1:9222`). При занятости — можно сменить на 9223 в appsettings.

### § 2.4. Интеграция в `IVisionBackend`

Два новых default-метода (RULES § 4.46 — default interface method виден только через интерфейсную переменную):

```csharp
public interface IVisionBackend : IAsyncDisposable
{
    // ... существующие 11 методов ...

    /// <summary>
    /// Возвращает DOM-провайдер координат для этого backend'а, если он
    /// поддерживается. <c>null</c> — используется VL-fallback.
    /// <para>
    /// Default: <c>null</c> (для backend'ов без DOM — Sandbox, VncMcp).
    /// <c>LocalHarnessVisionBackend</c> переопределяет: возвращает
    /// <c>DomCoordinateProvider</c>, если CDP подключён и элемент доступен.
    /// </para>
    /// </summary>
    ICoordinateProvider GetCoordinateProvider() => null;

    /// <summary>
    /// Текущий коэффициент масштабирования «screen / screenshot».
    /// 1.0 — если скриншот не downscale'ится.
    /// Используется для конвертации DOM-координат (в физических px экрана)
    /// в screenshot-space, в котором работает <c>VisionAgentService</c>.
    /// </summary>
    (double X, double Y) GetScreenshotScale() => (1.0, 1.0);
}
```

**RULES § 4.34:** только один не-default `IVisionBackend` в проде (`LocalHarnessVisionBackend`) + fakes в тестах. Все fakes продолжат работать без изменений (default-методы).

### § 2.5. Кто владеет `IChromeCdpSession`

**Вариант выбран:** `LocalHarnessVisionBackend` (Scoped, per-task) создаёт и владеет `IChromeCdpSession` **лениво** — при первом обращении `GetCoordinateProvider()`.

**Почему не Singleton:**
- Каждая vision-задача запускает **свой** Chrome (fresh profile, KI-155). Singleton-CDPSession не знает, к какому Chrome подключаться.
- Параллельные задачи (два пользователя) имеют разные Chrome на разных портах. Singleton бы конфликтовал.

**Почему не отдельный сервис:**
- Жизненный цикл CDP-сессии привязан к жизненному циклу Chrome, а Chrome — к `LocalHarnessVisionBackend` (закрывается в `DisposeAsync`).
- Backend естественно «владеет» своей поверхностью.

### § 2.6. Heuristics для поиска DOM-элемента

VL возвращает `ui_elements[].type` (button / text_input / link / ...) и `label` (видимый текст). У нас нет CSS-селектора.

**Порядок матчинга:**

1. **By `label`** (приоритет): ищем в `document.querySelectorAll(...)` кандидатов, у которых `textContent` / `value` / `placeholder` / `aria-label` **содержит** `label` (case-insensitive) или наоборот.
2. **By position** (fallback): для каждого кандидата считаем его центр в **screen-space** (через `window.screenX/Y + DPR`) и сравниваем с `TargetBounds`. Берём ближайший с расстоянием < 200 px (порог — эвристика: VL-bounds от того же элемента, но со сдвигом; при расстоянии > 200 px — это уже другой элемент).
3. **По типу** (когда label пуст): `type=button` → `button, [role=button], input[type=submit]`; `type=text_input` → `input[type=text|search], textarea, [role=textbox]`; `type=link` → `a[href]`; etc. Возвращаем первого кандидата (best-effort).

**Ограничения:**
- Не работает для **shadow-DOM** (для доступа нужен `element.shadowRoot` — рекурсивный обход, отдельная задача).
- Не работает для **iframe** (нужен `frame` перебор в PuppeteerSharp — отдельная задача).
- Не работает для **canvas / WebGL** (нет DOM-структуры — сразу VL-fallback).
- **Кириллица** в `textContent` — ОК, JS `includes()` работает с unicode.

**Защита от ложных срабатываний:** если найденный элемент невидим (`display: none`, `visibility: hidden`, `opacity: 0`, нулевые размеры) — считать ненайденным.

---

## § 3. Архитектура: файлы

### § 3.1. Новые файлы

**Интерфейсы** (`IIChatTools.Services/Interfaces/`):

| Файл | Назначение |
|---|---|
| `IChromeCdpSession.cs` | Сессия CDP к Chrome: connect / disconnect / find element / get viewport info |
| `ICoordinateProvider.cs` | Провайдер координат: `ResolveAsync(request) → CoordinateResult` |

**DTO** (`IIChatTools.Services/DTO/VisionAgent/`):

| Файл | Назначение |
|---|---|
| `CoordinateRequest.cs` | Вход провайдера: target id/label/type, VL bounds, scale |
| `CoordinateResult.cs` | Выход: x/y, Found, FromDom, Selector (для логов), Error |
| `VisionCoordinateProviderOptions.cs` | Опции: Mode, Cdp |
| `VisionCdpOptions.cs` | Cdp: Enabled, BrowserUrl, ConnectTimeoutMs, ElementSearchTimeoutMs |
| `CdpElementQuery.cs` | Запрос к CDP: label, type, bounds |
| `CdpElementResult.cs` | Ответ CDP: x/y (viewport), w/h, Found, MatchedBy |

**Реализации** (`IIChatTools.Services/Implementation/VisionAgent/`):

| Файл | Назначение |
|---|---|
| `PuppeteerSharpCdpSession.cs` | `IChromeCdpSession` через `Puppeteer.ConnectAsync` |
| `DomCoordinateProvider.cs` | `ICoordinateProvider` — DOM-координаты (использует `IChromeCdpSession`) |
| `VisionCoordinateProvider.cs` | `ICoordinateProvider` — fallback: bounds-center (KI-190) |

**Документация**:

| Файл | Назначение |
|---|---|
| `docs/development/v1.13/DESIGN_VISION_CDP_ATTACH.md` | Этот документ |

### § 3.2. Изменяемые файлы

| Файл | Что меняется |
|---|---|
| `Interfaces/IVisionBackend.cs` | +`GetCoordinateProvider()` (default → null), +`GetScreenshotScale()` (default → (1,1)) |
| `Implementation/VisionAgent/LocalHarnessVisionBackend.cs` | (а) `--remote-debugging-port=9222` в Chrome args; (б) lazy `IChromeCdpSession`; (в) override `GetCoordinateProvider`; (г) override `GetScreenshotScale` |
| `Implementation/VisionAgent/VisionAgentService.cs` | `ResolveCoordinatesAsync` — сначала DOM, потом VL |
| `DTO/VisionAgent/VisionAgentOptions.cs` | +`CoordinateProvider` (секция `VisionCoordinateProviderOptions`) |
| `IIChatTools.API/Startup.cs` | DI: `IChromeCdpSession` (Scoped), `DomCoordinateProvider` (Scoped), `VisionCoordinateProvider` (Scoped) |
| `IIChatTools.API/appsettings.json` + `.Development.json` | +`VisionAgent:CoordinateProvider` |
| `IIChatTools.Tests/` | Новые тесты (`PuppeteerSharpCdpSessionTests`, `DomCoordinateProviderTests`) |
| `CHANGELOG.md`, `KNOWN_ISSUES.md`, `README.md` | После реализации |

---

## § 4. Контракты

### § 4.1. `ICoordinateProvider`

```csharp
public interface ICoordinateProvider
{
    /// <summary>Имя провайдера для логов. <c>"dom"</c> / <c>"vision"</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Резолвит координаты целевого элемента.
    /// </summary>
    /// <param name="request">Запрос (target id/label/type, VL bounds, scale).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// <see cref="CoordinateResult"/> с <c>Found=true</c> при успехе.
    /// При любой ошибке — <c>Found=false</c>, <c>Error</c> заполнен.
    /// Никогда не бросает.
    /// </returns>
    Task<CoordinateResult> ResolveAsync(
        CoordinateRequest request,
        CancellationToken cancellationToken = default);
}
```

### § 4.2. `CoordinateRequest` / `CoordinateResult`

```csharp
public sealed class CoordinateRequest
{
    /// <summary>Семантический id из <c>ui_elements</c> (например, <c>search_button</c>).</summary>
    public string TargetId { get; set; }

    /// <summary>Видимый label (например, «Найти»). Может быть пустым.</summary>
    public string TargetLabel { get; set; }

    /// <summary>Тип элемента из VL: <c>button</c> / <c>text_input</c> / <c>link</c> / ...</summary>
    public string TargetType { get; set; }

    /// <summary>VL-bounds (для fallback и position-matching).</summary>
    public UiElementBoundsDto TargetBounds { get; set; }

    /// <summary>
    /// Scale «screen / screenshot». 1.875 — если скриншот 1024 → screen 1920.
    /// <c>DomCoordinateProvider</c> делит свой результат на этот scale.
    /// </summary>
    public double ScreenshotScaleX { get; set; } = 1.0;

    /// <inheritdoc cref="ScreenshotScaleX"/>
    public double ScreenshotScaleY { get; set; } = 1.0;
}

public sealed class CoordinateResult
{
    /// <summary>X в screenshot-space (VL-PNG-координаты).</summary>
    public int X { get; set; }

    /// <inheritdoc cref="X"/>
    public int Y { get; set; }

    /// <summary>true — элемент найден в DOM, координаты валидны.</summary>
    public bool Found { get; set; }

    /// <summary>true — координаты из DOM; false — из VL (fallback).</summary>
    public bool FromDom { get; set; }

    /// <summary>Найденный CSS-селектор или JS-описание (для логов).</summary>
    public string Selector { get; set; }

    /// <summary>Причина неудачи, если <c>Found=false</c>.</summary>
    public string Error { get; set; }
}
```

### § 4.3. `IChromeCdpSession`

```csharp
public interface IChromeCdpSession : IAsyncDisposable
{
    /// <summary>true — сессия подключена и браузер доступен.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Подключается к уже запущенному Chrome через CDP.
    /// </summary>
    /// <param name="browserUrl">URL CDP-эндпоинта (например, <c>http://127.0.0.1:9222</c>).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true — подключение успешно; false — Chrome недоступен (не бросает).</returns>
    Task<bool> ConnectAsync(string browserUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ищет элемент в DOM активной страницы по эвристикам (label / position / type).
    /// </summary>
    /// <param name="query">Запрос (label / type / bounds).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Результат с <c>Found=true</c> и координатами в <b>viewport CSS px</b>.
    /// При любой ошибке — <c>Found=false</c>, <c>Error</c> заполнен.
    /// </returns>
    Task<CdpElementResult> FindElementAsync(
        CdpElementQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает viewport-метрики страницы: DPR, screen offset, chrome UI offset.
    /// Нужно для конвертации viewport-координат в screen-координаты.
    /// </summary>
    Task<CdpViewportInfo> GetViewportInfoAsync(CancellationToken cancellationToken = default);
}
```

### § 4.4. `CdpElementQuery` / `CdpElementResult` / `CdpViewportInfo`

```csharp
public sealed class CdpElementQuery
{
    /// <summary>Видимый label (ищется в textContent / value / placeholder / aria-label).</summary>
    public string Label { get; set; }

    /// <summary>Тип: <c>button</c> / <c>text_input</c> / <c>link</c> / <c>checkbox</c> / <c>radio</c>.</summary>
    public string Type { get; set; }

    /// <summary>VL-bounds (для position-matching). Может быть null.</summary>
    public UiElementBoundsDto Bounds { get; set; }

    /// <summary>Таймаут поиска в DOM, мс (для позиционного матчинга).</summary>
    public int SearchTimeoutMs { get; set; } = 2000;
}

public sealed class CdpElementResult
{
    /// <summary>Центр элемента в viewport CSS px. Валидно при <c>Found=true</c>.</summary>
    public double ViewportX { get; set; }

    /// <inheritdoc cref="ViewportX"/>
    public double ViewportY { get; set; }

    /// <summary>Ширина / высота элемента в CSS px.</summary>
    public double Width { get; set; }

    /// <inheritdoc cref="Width"/>
    public double Height { get; set; }

    /// <summary>true — элемент найден и видим (не display:none).</summary>
    public bool Found { get; set; }

    /// <summary>Как нашли: <c>"label"</c> / <c>"position"</c> / <c>"type"</c>.</summary>
    public string MatchedBy { get; set; }

    /// <summary>CSS-селектор или tag+index найденного элемента (для логов).</summary>
    public string Selector { get; set; }

    /// <summary>Причина неудачи.</summary>
    public string Error { get; set; }
}

public sealed class CdpViewportInfo
{
    /// <summary>devicePixelRatio (1.0 / 1.25 / 1.5 / 2.0).</summary>
    public double DevicePixelRatio { get; set; } = 1.0;

    /// <summary>screenX браузерного окна (CSS px).</summary>
    public double WindowScreenX { get; set; }

    /// <inheritdoc cref="WindowScreenX"/>
    public double WindowScreenY { get; set; }

    /// <summary>Высота chrome UI (tabs + URL bar) в CSS px.</summary>
    public double ChromeUiHeight { get; set; }

    /// <summary>Ширина левой/правой рамки окна, CSS px.</summary>
    public double ChromeUiWidth { get; set; }
}
```

### § 4.5. `VisionAgentOptions` — новое поле

```csharp
public class VisionAgentOptions
{
    // ... существующие поля ...

    /// <summary>Конфигурация провайдера координат (DOM / Vision).</summary>
    public VisionCoordinateProviderOptions CoordinateProvider { get; set; } = new();
}

public class VisionCoordinateProviderOptions
{
    /// <summary>
    /// Режим: <c>"dom"</c> — только DOM (fallback на VL при неудаче),
    /// <c>"vision"</c> — только VL (старое поведение, KI-190),
    /// <c>"auto"</c> — DOM если доступен, иначе VL. Default: <c>"auto"</c>.
    /// </summary>
    public string Mode { get; set; } = "auto";

    /// <summary>Конфигурация CDP-подключения.</summary>
    public VisionCdpOptions Cdp { get; set; } = new();
}

public class VisionCdpOptions
{
    /// <summary>Включено ли CDP-подключение. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>URL CDP-эндпоинта. Default: http://127.0.0.1:9222.</summary>
    public string BrowserUrl { get; set; } = "http://127.0.0.1:9222";

    /// <summary>Таймаут подключения к CDP, мс. Default: 5000.</summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>Таймаут поиска элемента в DOM, мс. Default: 2000.</summary>
    public int ElementSearchTimeoutMs { get; set; } = 2000;
}
```

---

## § 5. Изменения в `LocalHarnessVisionBackend`

### § 5.1. Chrome args

`OpenAsync` — добавляем `--remote-debugging-port=9222`:

```csharp
var args = new List<string>
{
    $"--user-data-dir={_chromeProfileDir}",
    "--no-first-run",
    "--no-default-browser-check",
    "--disable-blink-features=AutomationControlled",
    "--disable-features=Translate,OptimizationHints",
    "--start-maximized",
    $"--remote-debugging-port={_cdpPort}",  // ← НОВОЕ (из конфига, default 9222)
    url
};
```

После `_chromeHwnd = ...` — попытка подключения CDP:

```csharp
if (_options.CoordinateProvider?.Cdp?.Enabled == true)
{
    try
    {
        _cdpSession = new PuppeteerSharpCdpSession(_logger);
        var connected = await _cdpSession.ConnectAsync(
            _options.CoordinateProvider.Cdp.BrowserUrl,
            cancellationToken);
        if (!connected)
        {
            _logger.LogWarning("VisionAgent: CDP не подключён — DOM-режим отключён");
            await _cdpSession.DisposeAsync();
            _cdpSession = null;
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "VisionAgent: ошибка CDP-подключения — DOM отключён");
        _cdpSession = null;
    }
}
```

### § 5.2. `GetCoordinateProvider()` override

```csharp
/// <inheritdoc />
public ICoordinateProvider GetCoordinateProvider()
{
    if (_cdpSession == null || !_cdpSession.IsConnected)
    {
        return null;   // DOM недоступен → VisionAgentService использует VL-fallback
    }

    _domCoordinateProvider ??= new DomCoordinateProvider(_cdpSession, _logger);
    return _domCoordinateProvider;
}

/// <inheritdoc />
public (double X, double Y) GetScreenshotScale()
{
    return (_screenshotScaleX, _screenshotScaleY);
}
```

### § 5.3. `CloseBrowser` — закрытие CDP

```csharp
private void CloseBrowser()
{
    // ... существующий код kill Chrome ...

    try { _cdpSession?.DisposeAsync().AsTask().Wait(1000); }
    catch (Exception ex) { _logger.LogDebug(ex, "CDP dispose"); }
    finally { _cdpSession = null; _domCoordinateProvider = null; }
}
```

---

## § 6. Изменения в `VisionAgentService`

### § 6.1. `ResolveCoordinatesAsync`

```csharp
private async Task<(int x, int y)> ResolveCoordinatesAsync(
    VisionActionDto action,
    ScreenDescriptionDto screen,
    byte[] currentScreenshotPng,
    CancellationToken ct)
{
    if (!string.IsNullOrWhiteSpace(action.Target))
    {
        var element = screen?.UiElements?.FirstOrDefault(el =>
            string.Equals(el.Id, action.Target, StringComparison.Ordinal));

        if (element == null)
        {
            throw new InvalidOperationException(
                $"VisionAgentService: target '{action.Target}' не найден в ui_elements.");
        }

        // ========== KI-161: DOM-first ==========
        var mode = _options.CoordinateProvider?.Mode ?? "auto";
        var domEnabled = mode == "dom" || mode == "auto";

        if (domEnabled)
        {
            var domProvider = _backend.GetCoordinateProvider();
            if (domProvider != null)
            {
                var (scaleX, scaleY) = _backend.GetScreenshotScale();
                var request = new CoordinateRequest
                {
                    TargetId = action.Target,
                    TargetLabel = element.Label,
                    TargetType = element.Type,
                    TargetBounds = element.Bounds,
                    ScreenshotScaleX = scaleX,
                    ScreenshotScaleY = scaleY
                };

                try
                {
                    var domResult = await domProvider
                        .ResolveAsync(request, ct)
                        .ConfigureAwait(false);

                    if (domResult.Found)
                    {
                        _logger.LogInformation(
                            "VisionAgent: DOM-resolve target='{Target}' → ({X},{Y}) " +
                            "selector='{Selector}' matchedBy={By}",
                            action.Target, domResult.X, domResult.Y,
                            domResult.Selector, domResult.Error ?? "?");
                        return (domResult.X, domResult.Y);
                    }

                    _logger.LogDebug(
                        "VisionAgent: DOM-resolve target='{Target}' не нашёл — VL-fallback ({Err})",
                        action.Target, domResult.Error ?? "not found");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "VisionAgent: DOM-resolve упал — VL-fallback");
                }
            }
        }
        // ========== /KI-161 ==========

        // ====== VL-fallback (существующий код: Verify + bounds-center) ======
        if (_options.Verify?.Enabled == true
            && element.Bounds != null
            && element.Bounds.W > 0 && element.Bounds.H > 0
            && currentScreenshotPng != null && currentScreenshotPng.Length > 0)
        {
            return await VisionVerifyHelper.TryVerifyAsync(
                currentScreenshotPng, element, action.Target,
                _visionLlm, _options.Verify, _logger, ct)
                .ConfigureAwait(false);
        }

        if (element.Bounds != null && element.Bounds.W > 0 && element.Bounds.H > 0)
        {
            return (element.Bounds.X + element.Bounds.W / 2,
                    element.Bounds.Y + element.Bounds.H / 2);
        }

        if (element.Center != null)
        {
            return (element.Center.X, element.Center.Y);
        }

        throw new InvalidOperationException(
            $"VisionAgentService: target '{action.Target}' не содержит ни bounds, ни center.");
    }

    // Прямые x/y (без изменений)
    if (action.X.HasValue && action.Y.HasValue)
    {
        return (action.X.Value, action.Y.Value);
    }

    throw new InvalidOperationException(
        "VisionAgentService: не задан ни target, ни x/y для действия.");
}
```

---

## § 7. Изменения в `Startup.cs` (DI)

В `RegisterVisionAgentTools`, секция Ф7:

```csharp
// KI-161: CDP-attach. IChromeCdpSession — Scoped (per-task).
// Реальный инстанс создаётся внутри LocalHarnessVisionBackend.OpenAsync
// (лениво). В DI регистрируется только фабрика для тестов.
services.AddScoped<PuppeteerSharpCdpSession>();
services.AddScoped<IChromeCdpSession>(sp => sp.GetRequiredService<PuppeteerSharpCdpSession>());

// KI-161: Coordinate providers. VisionCoordinateProvider — Singleton
// (stateless). DomCoordinateProvider — создаётся backend'ом, не через DI
// (нужен доступ к конкретной CDP-сессии).
services.AddSingleton<VisionCoordinateProvider>();
```

`DomCoordinateProvider` в DI **не регистрируется** — создаётся через `new` внутри `LocalHarnessVisionBackend`, потому что нужен доступ к конкретной `_cdpSession`.

---

## § 8. Конфигурация

### § 8.1. `appsettings.json` (prod)

```jsonc
"VisionAgent": {
  // ... существующие поля ...

  "CoordinateProvider": {
    "Mode": "vision",                    // prod: VL-fallback (для безопасности)
    "Cdp": {
      "Enabled": false,
      "BrowserUrl": "http://127.0.0.1:9222",
      "ConnectTimeoutMs": 5000,
      "ElementSearchTimeoutMs": 2000
    }
  }
}
```

### § 8.2. `appsettings.Development.json`

```jsonc
"VisionAgent": {
  // ... существующие поля ...

  "CoordinateProvider": {
    "Mode": "auto",                      // dev: DOM если доступен
    "Cdp": {
      "Enabled": true,
      "BrowserUrl": "http://127.0.0.1:9222",
      "ConnectTimeoutMs": 5000,
      "ElementSearchTimeoutMs": 2000
    }
  }
}
```

**Логика mode:**
- `"dom"` — только DOM (при недоступности CDP — сразу VL-fallback, не падаем).
- `"auto"` — DOM если CDP подключён, иначе VL.
- `"vision"` — никогда не пытаться DOM (старое поведение, KI-190).

---

## § 9. Безопасность / ограничения

### § 9.1. Что добавляется

- **Открытие порта 9222** на localhost. Chrome --remote-debugging-port без указания `--remote-debugging-address` слушает **только loopback** (127.0.0.1). Порт недоступен извне.
- **Fresh profile** — тот же, что был (KI-155). Cookies не переносятся.
- **Whitelist доменов** — без изменений (`VisionWhitelistValidator`).
- **Whitelist процессов** — без изменений (`EnsureForegroundProcessAllowed`).

### § 9.2. Новые ограничения

- **DOM-координаты работают только для browser-задач.** Desktop-приложения (Outlook, Excel) остаются на VL.
- **Shadow-DOM / iframe / canvas** — не поддерживаются (fallback на VL).
- **Multi-monitor с окном на не-primary мониторе** — `window.screenX` может быть некорректен (Chrome-специфика). Fallback: если DOM-координаты отличаются от bounds-center на > 200 px — предпочитаем bounds-center (защита от «фантомных» координат).
- **DPI ≠ 100%** — компенсируется через `window.devicePixelRatio`, но на 150%+ может дрейфовать на 1-2 px.

### § 9.3. Сводная таблица угроз

| Угроза | Митигация |
|---|---|
| LLM получит доступ к чужому Chrome через CDP | Порт только loopback, Chrome запускается **нами** с fresh profile, CDP-сессия живёт внутри одной задачи |
| DOM-координаты «уводят» в чужое окно | Проверка: `|domCenter - vlBoundsCenter| < 200 px`, иначе fallback |
| DOM-координаты в canvas-приложении | Если элемент не найден в DOM — fallback на VL, canvas не ломается |
| CDP-порт занят другим процессом | `ConnectAsync` возвращает false, DOM-режим отключается для задачи |

---

## § 10. План фаз

| Фаза | Что | Оценка |
|---|---|---|
| **Ф1** | Контракты + DTO: `IChromeCdpSession`, `ICoordinateProvider`, `CoordinateRequest/Result`, `VisionCoordinateProviderOptions`, `CdpElementQuery/Result`, `CdpViewportInfo` | ~30 мин |
| **Ф2** | `PuppeteerSharpCdpSession` — подключение, `GetViewportInfoAsync`, JS-скрипт поиска элемента | ~1 ч |
| **Ф3** | `DomCoordinateProvider` + `VisionCoordinateProvider` | ~40 мин |
| **Ф4** | Изменения в `IVisionBackend` (default-методы), `LocalHarnessVisionBackend` (`--remote-debugging-port`, lazy CDP, overrides) | ~40 мин |
| **Ф5** | Изменения в `VisionAgentService.ResolveCoordinatesAsync` | ~20 мин |
| **Ф6** | DI в `Startup.cs`, конфиг в обоих appsettings | ~15 мин |
| **Ф7** | Тесты: `DomCoordinateProviderTests` (fake CDP), `PuppeteerSharpCdpSessionTests` (skip — реальный Chrome), `VisionCoordinateProviderTests` | ~30 мин |
| **Ф8** | Smoke + docs + CHANGELOG + KI-161 → Fixed | ~30 мин |
| **Итого** | | **~4 ч** |

---

## § 11. Definition of Done (v1.13.x)

### § 11.1. Функциональные

- [ ] `IChromeCdpSession` подключён к Chrome после `OpenAsync` (если порт 9222 доступен).
- [ ] `DomCoordinateProvider.FindElementAsync` находит кнопку `search_button` на wikipedia.org за ≤ 2 сек.
- [ ] Координаты DOM-элемента совпадают с реальным положением кнопки (проверка: клик → страница результатов открывается).
- [ ] При `Mode=vision` старое поведение (bounds-center) полностью сохранено.
- [ ] При недоступном CDP — автоматический VL-fallback (без ошибок пользователю).
- [ ] При `target` не найденном в DOM (canvas) — автоматический VL-fallback.

### § 11.2. Нефункциональные

- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — все существующие тесты + новые.
- [ ] Smoke: 3 сценария.
  - `Mode=auto`, wikipedia.org — DOM, попадание.
  - `Mode=vision`, wikipedia.org — VL, поведение как сейчас.
  - CDP-порт занят — DOM-режим отключается, VL-fallback.
- [ ] `CHANGELOG.md` обновлён.
- [ ] `README.md` — раздел Vision Agent: «CDP-attach (v1.13.x)».
- [ ] `KNOWN_ISSUES.md` — KI-161 → Fixed.

### § 11.3. Документация

- [ ] DESIGN (этот документ) — статус Draft → Implemented.
- [ ] `RULES.md` — если будут новые уроки (например, `Puppeteer.ConnectAsync` в retry-loop).

---

## § 12. Ссылки

### § 12.1. KI

- **KI-161** (этот документ) — PuppeteerSharp DOM+Vision.
- **KI-131** — Vision Agent (общий).
- **KI-162** — Coordinate-then-Verify (crop ×2 не помогает; DOM — правильное решение).
- **KI-190** — bounds-center надёжнее verify.
- **KI-194** — Planner `done` без проверки (DOM+CDP URL-check — план v1.13.x).
- **KI-137** — OCR fallback (для мелкого текста).
- **KI-163** — Set-of-Mark (следующая итерация).

### § 12.2. RULES

- **§ 4.34** — расширение интерфейса → grep по fake-заглушкам.
- **§ 4.44** — новый top-level `ITool` → `allowedNames` (не применимо: KI-161 не добавляет tool).
- **§ 4.46** — default interface method не виден через конкретный тип.
- **§ 4.48** — `HttpClient.Timeout` → `CancellationTokenSource.CancelAfter` (применимо к `ConnectAsync`).
- **§ 4.51** — `Func<IToolRegistry>` для разрыва DI-циклов (здесь не требуется).

### § 12.3. Внешние источники

- **PuppeteerSharp `Puppeteer.ConnectAsync`** — https://www.puppeteersharp.com/api/PuppeteerSharp.Puppeteer.html#PuppeteerSharp_Puppeteer_ConnectAsync_PuppeteerSharp_ConnectOptions_PuppeteerSharp_ILoggerFactory_
- **Chrome DevTools Protocol** — https://chromedevtools.github.io/devtools-protocol/
- **Playwright auto-waiting & actionability** — https://playwright.dev/docs/actionability

### § 12.4. Внутренние документы

- `docs/development/v1.12/DESIGN_VISION_AGENT.md` § 2.4 — обоснование DOM+Vision hybrid.
- `docs/development/RULES.md` — правила разработки.

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.13.1**