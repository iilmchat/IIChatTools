using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private IBrowser _browser;
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

            var sessionId = Guid.NewGuid().ToString("N");
            var page = await _browser.NewPageAsync();
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
        /// Ленивая инициализация браузера.
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

                _logger.LogInformation("Инициализация PuppeteerSharp...");
                await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultRevision);

                _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = GetHeadless(),
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu"
                    }
                });

                _logger.LogInformation("PuppeteerSharp инициализирован");
            }
            finally
            {
                _browserInitLock.Release();
            }
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
        private static async Task<string> SafeGetTitleAsync(IPage page)
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

                    case "get_content":
                    {
                        var html = await page.GetContentAsync();
                        var truncated = false;
                        const int maxLen = 200000;
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
                            truncated,
                            html
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
            public IPage Page { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime LastActivityUtc { get; set; }
        }
    }
}