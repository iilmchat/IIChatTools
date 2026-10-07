using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Реализация <see cref="IChromeCdpSession"/> через PuppeteerSharp CDP-attach.
    /// Подключается к <b>уже запущенному</b> Chrome
    /// (с флагом <c>--remote-debugging-port=9222</c>), ищет DOM-элементы
    /// по эвристикам (label / position / type).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161). См. DESIGN § 2.1, § 4.3.
    /// </para>
    /// <para>
    /// <b>Соединение не убивает Chrome.</b> <see cref="DisposeAsync"/> вызывает
    /// <c>IBrowser.Disconnect()</c> — только разрывает CDP-канал.
    /// Процессом Chrome владеет <c>LocalHarnessVisionBackend</c>
    /// (он его запустил и он его же закрывает через <c>Process.Kill</c>).
    /// </para>
    /// <para>
    /// <b>Не бросает</b> из <see cref="ConnectAsync"/> / <see cref="FindElementAsync"/> /
    /// <see cref="GetViewportInfoAsync"/> — при любой ошибке логирует и
    /// возвращает результат с <c>Found=false</c> (или <c>false</c> для
    /// <see cref="ConnectAsync"/>). Единственное исключение — <c>OperationCanceledException</c>
    /// от внешнего <c>CancellationToken</c>.
    /// </para>
    /// <para>
    /// <b>Координаты JS</b> (<c>getBoundingClientRect().left/top</c>) — в <b>viewport CSS px</b>,
    /// отсчёт от верхнего левого угла <b>viewport</b> (ниже chrome UI, правее рамки).
    /// Конвертация VL-bounds (screenshot-space) → viewport-css — внутри
    /// <see cref="FindElementAsync"/> (использует <see cref="CdpViewportInfo"/>
    /// из <see cref="GetViewportInfoAsync"/>).
    /// </para>
    /// </remarks>
    public sealed class PuppeteerSharpCdpSession : IChromeCdpSession
    {
        /// <summary>
        /// JS-функция поиска элемента в DOM.
        /// <para>
        /// Вход (<c>q</c>): <c>{ label, type, vlCx?, vlCy?, vlW?, vlH?, tolerancePx }</c>
        /// в <b>viewport CSS px</b>.
        /// </para>
        /// <para>
        /// Выход: <c>{ found, x, y, w, h, matchedBy, selector, error }</c>.
        /// </para>
        /// </summary>
        private const string JsFindElement = @"
(q) => {
    const label = ((q.label || '') + '').trim().toLowerCase();
    const type = (q.type || 'other').toLowerCase();
    const tolerancePx = q.tolerancePx || 200;
    const hasBounds = typeof q.vlCx === 'number' && typeof q.vlCy === 'number';
    const vlCx = q.vlCx, vlCy = q.vlCy;

    const selectors = {
        button:     'button, [role=\""button\""], input[type=\""submit\""], input[type=\""button\""]',
        text_input: 'input[type=\""text\""], input[type=\""search\""], input[type=\""email\""], input[type=\""url\""], textarea, [role=\""textbox\""]',
        link:       'a[href]',
        checkbox:   'input[type=\""checkbox\""], [role=\""checkbox\""]',
        radio:      'input[type=\""radio\""], [role=\""radio\""]',
        dropdown:   'select, [role=\""combobox\""], [role=\""listbox\""]',
        other:      'button, [role=\""button\""], input, textarea, select, a[href], [role=\""textbox\""]'
    };

    const selector = selectors[type] || selectors.other;
    const all = Array.from(document.querySelectorAll(selector));

    const isVisible = (el) => {
        const r = el.getBoundingClientRect();
        if (r.width <= 0 || r.height <= 0) return false;
        const s = window.getComputedStyle(el);
        if (s.display === 'none' || s.visibility === 'hidden') return false;
        const op = parseFloat(s.opacity || '1');
        if (op === 0) return false;
        return true;
    };

    const cssPath = (el) => {
        if (el.id) return '#' + el.id;
        const nm = el.getAttribute && el.getAttribute('name');
        if (nm) return el.tagName.toLowerCase() + '[name=\""' + nm + '\""]';
        const parts = [];
        let node = el;
        let depth = 0;
        while (node && node.nodeType === 1 && depth < 6) {
            const tag = node.tagName.toLowerCase();
            if (node.id) { parts.unshift('#' + node.id); break; }
            let nth = 1;
            let sib = node;
            while (sib.previousElementSibling) {
                if (sib.previousElementSibling.tagName === node.tagName) nth++;
                sib = sib.previousElementSibling;
            }
            parts.unshift(nth === 1 ? tag : tag + ':nth-of-type(' + nth + ')');
            node = node.parentElement;
            depth++;
        }
        return parts.join(' > ');
    };

    const getLabel = (el) => {
        const aria = el.getAttribute && el.getAttribute('aria-label');
        if (aria) return aria.trim();
        const ph = el.getAttribute && el.getAttribute('placeholder');
        if (ph) return ph.trim();
        const val = el.value;
        if (typeof val === 'string' && val.trim().length > 0) return val.trim();
        return (el.textContent || '').trim();
    };

    const getRect = (el) => {
        const r = el.getBoundingClientRect();
        return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height };
    };

    const dist = (c) => {
        if (!hasBounds) return Infinity;
        const dx = c.x - vlCx, dy = c.y - vlCy;
        return Math.sqrt(dx * dx + dy * dy);
    };

    const visible = all.filter(isVisible);

    const result = (el, by) => {
        const c = getRect(el);
        return { found: true, x: c.x, y: c.y, w: c.w, h: c.h, matchedBy: by, selector: cssPath(el) };
    };

    // Matcher 1: label (если непустой).
    if (label.length > 0) {
        const labelMatches = visible.filter(el => {
            const l = getLabel(el).toLowerCase();
            if (!l) return false;
            return l.includes(label) || label.includes(l);
        });

        if (labelMatches.length === 1) {
            return result(labelMatches[0], 'label');
        }
        if (labelMatches.length > 1) {
            if (hasBounds) {
                let best = null, bestD = Infinity;
                for (const el of labelMatches) {
                    const d = dist(getRect(el));
                    if (d < bestD) { bestD = d; best = el; }
                }
                if (best) {
                    return result(best, bestD <= tolerancePx ? 'label+position' : 'label+position-far');
                }
            }
            return result(labelMatches[0], 'label-first');
        }
    }

    // Matcher 2: position.
    if (hasBounds && visible.length > 0) {
        let best = null, bestD = Infinity;
        for (const el of visible) {
            const d = dist(getRect(el));
            if (d < bestD) { bestD = d; best = el; }
        }
        if (best && bestD <= tolerancePx) {
            return result(best, 'position');
        }
    }

    // Matcher 3: type (fallback — первый видимый).
    if (visible.length > 0) {
        return result(visible[0], 'type-first');
    }

    return {
        found: false,
        error: 'Нет подходящих элементов. Всего кандидатов: ' + all.length + ', видимых: ' + visible.length
    };
}";

        /// <summary>
        /// JS-функция сбора viewport-метрик страницы.
        /// Выход: <c>{ dpr, screenX, screenY, chromeUiWidth, chromeUiHeight }</c>.
        /// </summary>
        private const string JsGetViewportInfo = @"
() => {
    const dpr = window.devicePixelRatio || 1;
    const screenX = window.screenX || 0;
    const screenY = window.screenY || 0;
    const outerW = window.outerWidth || 0;
    const outerH = window.outerHeight || 0;
    const innerW = window.innerWidth || 0;
    const innerH = window.innerHeight || 0;
    return {
        dpr: dpr,
        screenX: screenX,
        screenY: screenY,
        chromeUiWidth: Math.max(0, outerW - innerW),
        chromeUiHeight: Math.max(0, outerH - innerH)
    };
}";

        private readonly ILogger<PuppeteerSharpCdpSession> _logger;

        /// <summary>
        /// Подключённый браузер. <c>null</c> — CDP не подключён.
        /// <para>
        /// Тип — конкретный класс <see cref="PuppeteerSharp.Browser"/>, а не
        /// интерфейс: в PuppeteerSharp 7.1 интерфейсы <c>IBrowser</c> /
        /// <c>IPage</c> ещё не существуют (добавлены в v10+).
        /// </para>
        /// </summary>
        private PuppeteerSharp.Browser _browser;

        /// <summary>
        /// Активная страница (для Evaluate-скриптов).
        /// Выбирается один раз при <see cref="ConnectAsync"/>.
        /// <para>
        /// Тип — конкретный класс <see cref="PuppeteerSharp.Page"/> (см. выше).
        /// </para>
        /// </summary>
        private PuppeteerSharp.Page _activePage;

        /// <summary>
        /// Кэш viewport-метрик. Очищается при <see cref="DisposeAsync"/>.
        /// </summary>
        private CdpViewportInfo _cachedViewportInfo;

        private bool _disposed;

        /// <summary>
        /// Создаёт CDP-сессию. Соединение не устанавливается — нужно вызвать
        /// <see cref="ConnectAsync"/>.
        /// </summary>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="logger"/> = null.</exception>
        public PuppeteerSharpCdpSession(ILogger<PuppeteerSharpCdpSession> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsConnected => !_disposed && _browser != null && _browser.IsConnected;

        /// <inheritdoc />
        public async Task<bool> ConnectAsync(
            string browserUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(browserUrl))
            {
                _logger.LogDebug("CDP: пустой browserUrl — не подключаемся");
                return false;
            }

            if (IsConnected)
            {
                return true;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("CDP: подключение к {Url}...", browserUrl);

                // DefaultViewport = null — PuppeteerSharp НЕ должен эмулировать
                // свой viewport (иначе getBoundingClientRect вернёт координаты
                // фейкового окна, а не реального Chrome).
                //
                // Timeout: ConnectOptions.Timeout отсутствует в PuppeteerSharp 7.1.
                // На localhost (127.0.0.1:9222) неработающий порт падает сам за ~1-2 с
                // (TCP RST) → catch ниже вернёт false. Внешний таймаут — через
                // cancellationToken (проверка в начале try). ConnectTimeoutMs из
                // конфига не используется в этой реализации — при необходимости
                // добавим Task.WhenAny-обёртку в v1.13.x.
                _browser = await Puppeteer.ConnectAsync(
                    new ConnectOptions
                    {
                        BrowserURL = browserUrl,
                        DefaultViewport = null
                    }).ConfigureAwait(false);

                if (_browser == null || !_browser.IsConnected)
                {
                    _logger.LogWarning("CDP: ConnectAsync вернул не-подключённый браузер");
                    _browser = null;
                    return false;
                }

                // Активная страница — первая не about:blank, иначе первая.
                var pages = await _browser.PagesAsync().ConfigureAwait(false);
                _activePage = pages
                        .FirstOrDefault(p => !string.IsNullOrEmpty(p.Url)
                                             && !p.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                    ?? pages.FirstOrDefault();

                if (_activePage == null)
                {
                    _logger.LogWarning(
                        "CDP подключён к {Url}, но активной страницы нет " +
                        "(все about:blank). FindElement вернёт ошибку.",
                        browserUrl);
                }
                else
                {
                    _logger.LogInformation(
                        "CDP подключён: {Url} — активная страница: {PageUrl}",
                        browserUrl, _activePage.Url);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CDP ConnectAsync упал ({Type}) — DOM-режим отключён для этой задачи",
                    ex.GetType().Name);
                _browser = null;
                _activePage = null;
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<CdpViewportInfo> GetViewportInfoAsync(
            CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _activePage == null)
            {
                return new CdpViewportInfo();
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var raw = await _activePage
                    .EvaluateFunctionAsync<JObject>(JsGetViewportInfo)
                    .ConfigureAwait(false);

                if (raw == null)
                {
                    return _cachedViewportInfo ?? new CdpViewportInfo();
                }

                var info = new CdpViewportInfo
                {
                    DevicePixelRatio = raw["dpr"]?.Value<double>() ?? 1.0,
                    WindowScreenX = raw["screenX"]?.Value<double>() ?? 0,
                    WindowScreenY = raw["screenY"]?.Value<double>() ?? 0,
                    ChromeUiWidth = raw["chromeUiWidth"]?.Value<double>() ?? 0,
                    ChromeUiHeight = raw["chromeUiHeight"]?.Value<double>() ?? 0
                };

                _cachedViewportInfo = info;
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CDP GetViewportInfoAsync упал — используем кэш/дефолт");
                return _cachedViewportInfo ?? new CdpViewportInfo();
            }
        }

        /// <inheritdoc />
        public async Task<CdpElementResult> FindElementAsync(
            CdpElementQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            if (!IsConnected || _activePage == null)
            {
                return new CdpElementResult
                {
                    Found = false,
                    Error = "CDP не подключён"
                };
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Конвертация VL-bounds (screenshot-space) → viewport-css.
                double? vlCx = null, vlCy = null, vlW = null, vlH = null;
                if (query.Bounds != null && query.Bounds.W > 0 && query.Bounds.H > 0)
                {
                    var info = _cachedViewportInfo
                        ?? await GetViewportInfoAsync(cancellationToken).ConfigureAwait(false);
                    var dpr = info.DevicePixelRatio > 0 ? info.DevicePixelRatio : 1.0;

                    // VL-bounds (screenshot-space) → screen-px.
                    var cx_screen_px = (query.Bounds.X + query.Bounds.W / 2.0) * query.ScreenshotScaleX;
                    var cy_screen_px = (query.Bounds.Y + query.Bounds.H / 2.0) * query.ScreenshotScaleY;

                    // screen-px → CSS-px.
                    var cx_css = cx_screen_px / dpr;
                    var cy_css = cy_screen_px / dpr;

                    // CSS-px (window-space) → viewport-css.
                    // Слева chrome UI симметричен (border-left = border-right);
                    // сверху — заголовок + табы + адресная строка (chromeUiHeight).
                    vlCx = cx_css - info.WindowScreenX - info.ChromeUiWidth / 2.0;
                    vlCy = cy_css - info.WindowScreenY - info.ChromeUiHeight;

                    vlW = (query.Bounds.W * query.ScreenshotScaleX) / dpr;
                    vlH = (query.Bounds.H * query.ScreenshotScaleY) / dpr;
                }

                // 2. Формирование JS-запроса.
                var jsQuery = new JObject
                {
                    ["label"] = query.Label ?? string.Empty,
                    ["type"] = (query.Type ?? "other").ToLowerInvariant(),
                    ["tolerancePx"] = query.PositionTolerancePx
                };
                if (vlCx.HasValue) jsQuery["vlCx"] = vlCx.Value;
                if (vlCy.HasValue) jsQuery["vlCy"] = vlCy.Value;
                if (vlW.HasValue) jsQuery["vlW"] = vlW.Value;
                if (vlH.HasValue) jsQuery["vlH"] = vlH.Value;

                // 3. JS-скрипт.
                var raw = await _activePage
                    .EvaluateFunctionAsync<JObject>(JsFindElement, jsQuery)
                    .ConfigureAwait(false);

                if (raw == null)
                {
                    return new CdpElementResult
                    {
                        Found = false,
                        Error = "JS вернул null"
                    };
                }

                var found = raw["found"]?.Value<bool>() ?? false;
                if (!found)
                {
                    return new CdpElementResult
                    {
                        Found = false,
                        Error = raw["error"]?.Value<string>() ?? "элемент не найден"
                    };
                }

                return new CdpElementResult
                {
                    Found = true,
                    ViewportX = raw["x"]?.Value<double>() ?? 0,
                    ViewportY = raw["y"]?.Value<double>() ?? 0,
                    Width = raw["w"]?.Value<double>() ?? 0,
                    Height = raw["h"]?.Value<double>() ?? 0,
                    MatchedBy = raw["matchedBy"]?.Value<string>(),
                    Selector = raw["selector"]?.Value<string>()
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CDP FindElementAsync упал");
                return new CdpElementResult
                {
                    Found = false,
                    Error = $"JS-ошибка: {ex.Message}"
                };
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;

            try
            {
                if (_browser != null && _browser.IsConnected)
                {
                    // Disconnect — не закрывает Chrome. Разрывает CDP-канал.
                    // Chrome-процессом владеет LocalHarnessVisionBackend.
                    _browser.Disconnect();
                    _logger.LogDebug("CDP: соединение разорвано (Chrome не закрыт)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CDP Disconnect упал");
            }
            finally
            {
                _browser = null;
                _activePage = null;
                _cachedViewportInfo = null;
            }

            return ValueTask.CompletedTask;
        }
    }
}