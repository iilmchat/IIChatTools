using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Browser;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Менеджер браузерных сессий PuppeteerSharp.
    /// Хранит активные сессии в памяти, автоматически закрывает «протухшие» по TTL.
    /// </summary>
    public class BrowserSessionManager : IBrowserSessionManager, IAsyncDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<BrowserSessionManager> _logger;

        // _browser инициализируется лениво — при первом открытии сессии
        //private Browser _browser;
        private PuppeteerSharp.Browser _browser;
        private readonly SemaphoreSlim _browserInitLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);

        private readonly ConcurrentDictionary<string, SessionHolder> _sessions =
            new ConcurrentDictionary<string, SessionHolder>(StringComparer.Ordinal);

        private readonly Timer _cleanupTimer;

        /// <summary>
        /// Создаёт менеджер сессий.
        /// </summary>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public BrowserSessionManager(IConfiguration configuration, ILogger<BrowserSessionManager> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Запускаем таймер очистки раз в минуту
            _cleanupTimer = new Timer(_ => _ = CleanupExpiredAsync(), null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <inheritdoc />
        public async Task<BrowserSessionInfo> OpenAsync(int userId, string initialUrl, CancellationToken cancellationToken)
        {
            if (IsDisabled())
                throw new InvalidOperationException("Браузерная автоматизация отключена в настройках");

            await EnsureBrowserAsync(cancellationToken);

            var maxSessionsPerUser = GetMaxSessionsPerUser();
            var current = _sessions.Values.Count(s => s.UserId == userId);
            if (current >= maxSessionsPerUser)
                throw new InvalidOperationException(
                    $"Достигнут лимит одновременных сессий для пользователя ({maxSessionsPerUser})");

            /*
            var sessionId = Guid.NewGuid().ToString("N");
            var page = await _browser.NewPageAsync();
            await page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 800 });
            */

            var sessionId = Guid.NewGuid().ToString("N");
            var page = await _browser.NewPageAsync();

            var userAgent = _configuration["Browser:UserAgent"];
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                            "AppleWebKit/537.36 (KHTML, like Gecko) " +
                            "Chrome/120.0.0.0 Safari/537.36";
            }
            await page.SetUserAgentAsync(userAgent);
            await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
            await page.EvaluateExpressionOnNewDocumentAsync(@"
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            ");

            // Подписка на запрос авторизации прокси
            var proxyUser = _configuration["Browser:ProxyUsername"];
            var proxyPass = _configuration["Browser:ProxyPassword"];
            if (!string.IsNullOrWhiteSpace(proxyUser) && !string.IsNullOrWhiteSpace(proxyPass))
            {
                await page.AuthenticateAsync(new Credentials
                {
                    Username = proxyUser,
                    Password = proxyPass
                });
            }

            await page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 800 });

            var defaultTimeout = GetPageTimeoutMs();
            page.DefaultTimeout = defaultTimeout;
            page.DefaultNavigationTimeout = defaultTimeout;

            if (!string.IsNullOrWhiteSpace(initialUrl))
            {
                var safeUrl = NormalizeUrl(initialUrl);
                await page.GoToAsync(safeUrl);
            }

            var holder = new SessionHolder
            {
                SessionId = sessionId,
                UserId = userId,
                Page = page,
                CreatedAtUtc = DateTime.UtcNow,
                LastActivityUtc = DateTime.UtcNow
            };

            _sessions[sessionId] = holder;

            _logger.LogInformation("Открыта браузерная сессия {SessionId} для пользователя {UserId}",
                sessionId, userId);

            return new BrowserSessionInfo
            {
                SessionId = sessionId,
                UserId = userId,
                CurrentUrl = page.Url,
                CurrentTitle = await SafeGetTitleAsync(page),
                CreatedAt = holder.CreatedAtUtc,
                LastActivityAt = holder.LastActivityUtc
            };
        }

        /// <inheritdoc />
        public async Task<BrowserSessionInfo> GetInfoAsync(string sessionId, int userId)
        {
            if (!_sessions.TryGetValue(sessionId, out var holder))
                return null;
            if (holder.UserId != userId)
                return null;

            holder.LastActivityUtc = DateTime.UtcNow;

            return await Task.FromResult(new BrowserSessionInfo
            {
                SessionId = holder.SessionId,
                UserId = holder.UserId,
                CurrentUrl = holder.Page.Url,
                CurrentTitle = await SafeGetTitleAsync(holder.Page),
                CreatedAt = holder.CreatedAtUtc,
                LastActivityAt = holder.LastActivityUtc
            });
        }

        /// <inheritdoc />
        public async Task<bool> CloseAsync(string sessionId, int userId)
        {
            if (!_sessions.TryRemove(sessionId, out var holder))
                return false;

            if (holder.UserId != userId)
            {
                // Возвращаем обратно, раз не владелец
                _sessions[sessionId] = holder;
                return false;
            }

            await CloseHolderAsync(holder);
            _logger.LogInformation("Браузерная сессия {SessionId} закрыта", sessionId);
            return true;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<BrowserSessionInfo>> ListForUserAsync(int userId)
        {
            var list = _sessions.Values
                .Where(s => s.UserId == userId)
                .Select(s => new BrowserSessionInfo
                {
                    SessionId = s.SessionId,
                    UserId = s.UserId,
                    CurrentUrl = s.Page.Url,
                    CurrentTitle = null,
                    CreatedAt = s.CreatedAtUtc,
                    LastActivityAt = s.LastActivityUtc
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<BrowserSessionInfo>>(list);
        }

        /// <inheritdoc />
        public async Task CloseAllForUserAsync(int userId)
        {
            var ids = _sessions.Values.Where(s => s.UserId == userId).Select(s => s.SessionId).ToList();
            foreach (var id in ids)
                await CloseAsync(id, userId);
        }

        // -------- Внутренние --------

        /// <summary>
        /// Ленивая инициализация браузера через системный Edge/Chrome.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Асинхронная задача</returns>
        private async Task EnsureBrowserAsync(CancellationToken cancellationToken)
        {
            if (_browser != null && _browser.IsConnected)
                return;

            await _browserInitLock.WaitAsync(cancellationToken);
            try
            {
                if (_browser != null && _browser.IsConnected)
                    return;

                // Ищем локальный Edge/Chrome — не используем BrowserFetcher (он не умеет прокси)
                var executablePath = BrowserLocator.Resolve(_configuration);
                if (string.IsNullOrEmpty(executablePath))
                    throw new InvalidOperationException(
                        "Не найден Chromium-совместимый браузер (Edge или Chrome). " +
                        "Укажите путь в Browser:ExecutablePath.");

                _logger.LogInformation("Инициализация PuppeteerSharp с браузером: {Path}", executablePath);
                _logger.LogInformation("Используется браузер: {Path}", executablePath);
                _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = GetHeadless(),
                    ExecutablePath = executablePath,
                    Args = BuildChromiumArgs()
                });

                _logger.LogInformation("PuppeteerSharp инициализирован");
            }
            finally
            {
                _browserInitLock.Release();
            }
        }

        /// <summary>
        /// Формирует аргументы запуска Chromium с учётом прокси.
        /// </summary>
        /// <returns>Массив аргументов</returns>
        private string[] BuildChromiumArgs()
        {
            var args = new List<string>
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--remote-allow-origins=*",   // критично для новых версий Chromium

                // v1.13.6 (KI-218): anti-detection + размер окна — всегда,
                // не только при заданном прокси. Раньше эти аргументы
                // ошибочно добавлялись только внутри `if (proxy != null)`.
                "--disable-blink-features=AutomationControlled",
                "--window-size=1920,1080"
            };

            if (string.Equals(_configuration["Browser:IgnoreCertificateErrors"], "true", StringComparison.OrdinalIgnoreCase))
                args.Add("--ignore-certificate-errors");

            // v1.13.6 (KI-218): placeholder 'CHANGE_ME_VIA_USER_SECRETS'
            // не должен попадать в --proxy-server (регрессия KI-059).
            var proxy = _configuration["Browser:ProxyServer"];
            if (BrowserProxyHelper.IsRealProxyUrl(proxy))
            {
                _logger.LogInformation("PuppeteerSharp: используется прокси {Proxy}", proxy);
                args.Add($"--proxy-server={proxy}");
                args.Add("--proxy-bypass-list=<-loopback>");
            }
            else if (!string.IsNullOrWhiteSpace(proxy))
            {
                _logger.LogDebug(
                    "PuppeteerSharp: Browser:ProxyServer игнорируется " +
                    "(placeholder или невалидный URL): {Proxy}",
                    proxy);
            }

            return args.ToArray();
        }

        /// <summary>
        /// Закрывает конкретную сессию.
        /// </summary>
        /// <param name="holder">Сессия</param>
        /// <returns>Асинхронная задача</returns>
        private async Task CloseHolderAsync(SessionHolder holder)
        {
            try
            {
                if (holder.Page != null && !holder.Page.IsClosed)
                    await holder.Page.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка закрытия страницы {SessionId}", holder.SessionId);
            }

            try
            {
                if (holder.Page != null)
                    await holder.Page.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка dispose страницы {SessionId}", holder.SessionId);
            }
        }

        /// <summary>
        /// Периодическая очистка «протухших» сессий.
        /// </summary>
        /// <returns>Асинхронная задача</returns>
        private async Task CleanupExpiredAsync()
        {
            try
            {
                var ttl = GetSessionTtlMinutes();
                var threshold = DateTime.UtcNow.AddMinutes(-ttl);

                var expired = _sessions.Values
                    .Where(s => s.LastActivityUtc < threshold)
                    .ToList();

                foreach (var holder in expired)
                {
                    if (_sessions.TryRemove(holder.SessionId, out var removed))
                    {
                        await CloseHolderAsync(removed);
                        _logger.LogInformation(
                            "Сессия {SessionId} закрыта по TTL ({Ttl} мин без активности)",
                            holder.SessionId, ttl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка очистки просроченных браузерных сессий");
            }
        }

        /// <summary>
        /// Безопасно получает заголовок страницы.
        /// </summary>
        /// <param name="page">Страница</param>
        /// <returns>Заголовок или null</returns>
        private static async Task<string> SafeGetTitleAsync(Page page)
        {
            try
            {
                if (page == null || page.IsClosed) return null;
                return await page.GetTitleAsync();
            }
            catch { return null; }
        }

        /// <summary>
        /// Приводит URL к безопасному виду.
        /// </summary>
        /// <param name="url">Входной URL</param>
        /// <returns>Нормализованный URL</returns>
        /// <exception cref="ArgumentException">Если URL некорректен</exception>
        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL не может быть пустым", nameof(url));

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException("Некорректный URL", nameof(url));

            if (uri.Scheme != "http" && uri.Scheme != "https")
                throw new ArgumentException("Разрешены только http и https", nameof(url));

            return uri.ToString();
        }

        /// <summary>
        /// Возвращает признак отключения браузерной автоматизации из конфигурации.
        /// </summary>
        /// <returns>true, если автоматизация отключена</returns>
        private bool IsDisabled()
        {
            var raw = _configuration["Security:EnableBrowserAutomation"];
            return string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Режим headless из конфигурации (по умолчанию true).
        /// </summary>
        /// <returns>true — headless</returns>
        private bool GetHeadless()
        {
            var raw = _configuration["Security:BrowserHeadless"];
            return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Лимит одновременных сессий на пользователя.
        /// </summary>
        /// <returns>Количество сессий</returns>
        private int GetMaxSessionsPerUser()
        {
            var raw = _configuration["Security:MaxBrowserSessionsPerUser"];
            return int.TryParse(raw, out var v) && v > 0 ? v : 3;
        }

        /// <summary>
        /// TTL сессии в минутах.
        /// </summary>
        /// <returns>TTL</returns>
        private int GetSessionTtlMinutes()
        {
            var raw = _configuration["Security:BrowserSessionTtlMinutes"];
            return int.TryParse(raw, out var v) && v > 0 ? v : 15;
        }

        /// <summary>
        /// Таймаут операций на странице.
        /// </summary>
        /// <returns>Таймаут в миллисекундах</returns>
        private int GetPageTimeoutMs()
        {
            var raw = _configuration["Security:BrowserPageTimeoutSeconds"];
            var seconds = int.TryParse(raw, out var v) && v > 0 ? v : 30;
            return seconds * 1000;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _cleanupTimer?.Dispose();

            foreach (var holder in _sessions.Values)
                await CloseHolderAsync(holder);

            _sessions.Clear();

            try
            {
                if (_browser != null)
                {
                    await _browser.CloseAsync();
                    await _browser.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка закрытия PuppeteerSharp-браузера");
            }
        }

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteCommandAsync(
            ToolExecutionContext context,
            string sessionId,
            string command,
            JObject arguments,
            int timeoutMs)
        {
            if (!_sessions.TryGetValue(sessionId, out var holder))
                return ToolResult.Fail("Сессия не найдена");

            if (holder.UserId != context.UserId)
                return ToolResult.Fail("Доступ к сессии запрещён");

            var page = holder.Page;
            if (page == null || page.IsClosed)
                return ToolResult.Fail("Страница закрыта");

            holder.LastActivityUtc = DateTime.UtcNow;

            var originalTimeout = page.DefaultTimeout;
            page.DefaultTimeout = timeoutMs;

            try
            {
                switch (command)
                {
                    case "goto":
                    {
                        var url = arguments.GetString("url");
                        if (string.IsNullOrWhiteSpace(url))
                            return ToolResult.Fail("Не указан URL для goto");

                        var safeUrl = NormalizeUrl(url);
                        var resp = await page.GoToAsync(safeUrl);
                        return ToolResult.Ok(new
                        {
                            url = page.Url,
                            title = await SafeGetTitleAsync(page),
                            status = resp?.Status
                        });
                    }

                    case "go_back":
                        await page.GoBackAsync();
                        return ToolResult.Ok(new { url = page.Url, title = await SafeGetTitleAsync(page) });

                    case "go_forward":
                        await page.GoForwardAsync();
                        return ToolResult.Ok(new { url = page.Url, title = await SafeGetTitleAsync(page) });

                    case "reload":
                        await page.ReloadAsync();
                        return ToolResult.Ok(new { url = page.Url, title = await SafeGetTitleAsync(page) });

                    case "click":
                    {
                        var sel = arguments.GetString("selector");
                        if (string.IsNullOrWhiteSpace(sel))
                            return ToolResult.Fail("Не указан селектор для click");
                        await page.ClickAsync(sel);
                        return ToolResult.Ok(new { clicked = sel, url = page.Url });
                    }

                    case "type":
                    {
                        var sel = arguments.GetString("selector");
                        var text = arguments.GetString("text", string.Empty);
                        if (string.IsNullOrWhiteSpace(sel))
                            return ToolResult.Fail("Не указан селектор для type");
                        await page.TypeAsync(sel, text);
                        return ToolResult.Ok(new { typed = text.Length, selector = sel });
                    }

                    case "wait_for_selector":
                    {
                        var sel = arguments.GetString("selector");
                        if (string.IsNullOrWhiteSpace(sel))
                            return ToolResult.Fail("Не указан селектор для wait_for_selector");
                        await page.WaitForSelectorAsync(sel);
                        return ToolResult.Ok(new { found = sel });
                    }

                    case "evaluate":
                    {
                        var script = arguments.GetString("script");
                        if (string.IsNullOrWhiteSpace(script))
                            return ToolResult.Fail("Не указан JS-скрипт для evaluate");
                        if (script.Length > 20000)
                            return ToolResult.Fail("Скрипт превышает 20 000 символов");

                        var evalResult = await page.EvaluateExpressionAsync<object>(script);
                        return ToolResult.Ok(new { result = evalResult });
                    }

                    // KI-219 (v1.13.7): лимит HTML снижен с 200 000 до 10 000 символов.
                    // Причина: RZD возвращает ~200 000 символов (56k токенов) → 400
                    // exceed_context_size_error на следующем шаге. Параметр maxLength
                    // позволяет поднять лимит осознанно (до 100 000).
                    // Для поиска селекторов — использовать get_selectors (ниже).
                    case "get_content":
                    {
                        var maxLen = arguments.GetInt("maxLength", 10000);
                        if (maxLen <= 0 || maxLen > 100000) maxLen = 10000;

                        var html = await page.GetContentAsync();
                        var truncated = false;
                        if (html != null && html.Length > maxLen)
                        {
                            html = html.Substring(0, maxLen);
                            truncated = true;
                        }
                        return ToolResult.Ok(new
                        {
                            url = page.Url,
                            title = await SafeGetTitleAsync(page),
                            htmlLength = html?.Length ?? 0,
                            maxLength = maxLen,
                            truncated,
                            hint = truncated
                                ? "HTML обрезан. Для поиска селекторов используй get_selectors (эффективнее)."
                                : null,
                            html
                        });
                    }

                    // KI-219 (v1.13.7): список интерактивных элементов с готовыми
                    // CSS-селекторами. Возвращает ~20-30 элементов (~3k символов,
                    // ~800 токенов) вместо 200k символов HTML (56k токенов).
                    // Это основной инструмент для поиска селекторов в browser_agent.
                    case "get_selectors":
                    {
                        const string selectorScript = @"(() => {
                            const seen = new Set();
                            const out = [];
                            const MAX = 30;
                            const els = document.querySelectorAll('input, button, textarea, select, a[href]');
                            for (const el of els) {
                                if (out.length >= MAX) break;
                                const rect = el.getBoundingClientRect();
                                if (rect.width === 0 || rect.height === 0) continue;
                                const style = window.getComputedStyle(el);
                                if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') continue;
                                const id = el.id || null;
                                const name = el.getAttribute('name') || null;
                                let selector = null;
                                if (id) selector = '#' + CSS.escape(id);
                                else if (name) selector = el.tagName.toLowerCase() + '[name=""' + name + '""]';
                                const key = selector || (el.tagName + '|' + (el.textContent || '').trim().slice(0, 50));
                                if (seen.has(key)) continue;
                                seen.add(key);
                                out.push({
                                    tag: el.tagName.toLowerCase(),
                                    id: id,
                                    name: name,
                                    type: el.getAttribute('type') || null,
                                    text: ((el.textContent || '').trim().slice(0, 100)) || null,
                                    placeholder: el.getAttribute('placeholder') || null,
                                    ariaLabel: el.getAttribute('aria-label') || null,
                                    selector: selector
                                });
                            }
                            return out;
                        })()";

                        var elements = await page.EvaluateExpressionAsync<object>(selectorScript);
                        var count = (elements as System.Collections.IList)?.Count ?? 0;

                        return ToolResult.Ok(new
                        {
                            url = page.Url,
                            title = await SafeGetTitleAsync(page),
                            count,
                            hint = count == 0
                                ? "Интерактивных элементов не найдено. Возможно, страница ещё грузится — вызови wait (evaluate('new Promise(r => setTimeout(r, 2000))'))."
                                : null,
                            elements
                        });
                    }

                    case "screenshot":
                    {
                        var bytes = await page.ScreenshotDataAsync(new ScreenshotOptions
                        {
                            Type = ScreenshotType.Png,
                            FullPage = false
                        });
                        var base64 = Convert.ToBase64String(bytes);
                        return ToolResult.Ok(new
                        {
                            format = "png",
                            sizeBytes = bytes.Length,
                            base64
                        });
                    }

                    // KI-128 (v1.13.6): скриншот с сохранением в workspace пользователя.
                    // Отличие от "screenshot": не отдаёт base64 в LLM (экономит контекст),
                    // возвращает { path, sizeBytes, url, title }.
                    case "screenshot_to_file":
                    {
                        var relativePath = arguments.GetString("path");
                        if (string.IsNullOrWhiteSpace(relativePath))
                            return ToolResult.Fail("Не указан path для screenshot_to_file");

                        if (string.IsNullOrWhiteSpace(context.WorkspaceRoot))
                            return ToolResult.Fail(
                                "context.WorkspaceRoot пуст — некуда сохранять файл. " +
                                "Инструмент должен вызываться из Chat (там WorkspaceRoot заполняется автоматически).");

                        // RULES § 1.9: все пути — через PathHelper.TryGetSafeFullPath
                        // (защита от traversal: "..", абсолютные пути, backslash на Linux).
                        if (!PathHelper.TryGetSafeFullPath(relativePath, context.WorkspaceRoot, out var safePath))
                        {
                            return ToolResult.Fail(
                                $"Путь '{relativePath}' выходит за пределы workspace или некорректен. " +
                                $"Используйте относительный путь, например: 'minsk.png' или 'screens/minsk.png'.");
                        }

                        // Создаём подкаталог, если указан составной путь.
                        var directory = Path.GetDirectoryName(safePath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        var bytes = await page.ScreenshotDataAsync(new ScreenshotOptions
                        {
                            Type = ScreenshotType.Png,
                            FullPage = false
                        });

                        await File.WriteAllBytesAsync(safePath, bytes, context.CancellationToken);

                        _logger.LogInformation(
                            "browser screenshot_to_file: сессия {SessionId}, путь {Path}, {Bytes} байт",
                            sessionId, relativePath, bytes.Length);

                        return ToolResult.Ok(new
                        {
                            path = relativePath,
                            sizeBytes = bytes.Length,
                            url = page.Url,
                            title = await SafeGetTitleAsync(page)
                        });
                    }

                    case "get_url":
                        return ToolResult.Ok(new { url = page.Url, title = await SafeGetTitleAsync(page) });

                    default:
                        return ToolResult.Fail($"Неизвестная команда: {command}");
                }
            }
            finally
            {
                try { page.DefaultTimeout = originalTimeout; } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Внутренний контейнер сессии.
        /// </summary>
        private class SessionHolder
        {
            public string SessionId { get; set; }
            public int UserId { get; set; }
            public Page Page { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime LastActivityUtc { get; set; }
        }
    }
}