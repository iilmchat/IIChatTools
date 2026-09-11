using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;

namespace IIChatTools.Services.Implementation.Tools.Browser
{
    /// <summary>
    /// Инструмент: однократное открытие страницы (stateless).
    /// </summary>
    public class BrowserOpenPageTool : ITool
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<BrowserOpenPageTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        public BrowserOpenPageTool(IConfiguration configuration, ILogger<BrowserOpenPageTool> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "browser_open_page";

        /// <inheritdoc />
        public string Description => "Открывает страницу в headless-браузере, возвращает HTML и текст без сохранения сессии.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "url", Type = "string", Description = "URL страницы (http/https).", Required = true },
            new ToolParameterDescriptor { Name = "timeoutSeconds", Type = "integer", Description = "Таймаут загрузки (по умолчанию 30).", Required = false, Default = 30 }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            if (string.Equals(_configuration["Security:EnableBrowserAutomation"], "false", StringComparison.OrdinalIgnoreCase))
                return ToolResult.Fail("Браузерная автоматизация отключена в настройках");

            var url = arguments.GetString("url");
            if (string.IsNullOrWhiteSpace(url))
                return ToolResult.Fail("Не указан URL");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                return ToolResult.Fail("Некорректный URL (разрешены только http/https)");

            var timeout = arguments.GetInt("timeoutSeconds", 30);
            if (timeout <= 0 || timeout > 120) timeout = 30;

            PuppeteerSharp.Browser browser = null;
            Page page = null;
            try
            {
                //await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultRevision);
                await new BrowserFetcher().DownloadAsync();

                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = !string.Equals(_configuration["Security:BrowserHeadless"], "false", StringComparison.OrdinalIgnoreCase),
                    Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage", "--disable-gpu" }
                });

                page = await browser.NewPageAsync();
                page.DefaultNavigationTimeout = timeout * 1000;

                var resp = await page.GoToAsync(uri.ToString());

                var html = await page.GetContentAsync() ?? string.Empty;
                string text;
                try { text = await page.EvaluateExpressionAsync<string>("document.body ? document.body.innerText : ''"); }
                catch { text = string.Empty; }

                const int maxText = 50000;
                var truncated = false;
                if (text.Length > maxText) { text = text.Substring(0, maxText); truncated = true; }

                return ToolResult.Ok(new
                {
                    url = page.Url,
                    title = await SafeGetTitleAsync(page),
                    status = resp?.Status,
                    htmlLength = html.Length,
                    textLength = text.Length,
                    truncated,
                    text
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка browser_open_page для {Url}", url);
                return ToolResult.Fail($"Ошибка открытия страницы: {ex.Message}");
            }
            finally
            {
                try { if (page != null) await page.CloseAsync(); } catch { /* ignore */ }
                try { if (browser != null) { await browser.CloseAsync(); await browser.DisposeAsync(); } } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Безопасно получает заголовок страницы.
        /// </summary>
        /// <param name="page">Страница</param>
        /// <returns>Заголовок или null</returns>
        private static async Task<string> SafeGetTitleAsync(Page page)  // ← было IPage
        {
            try { return page?.IsClosed == false ? await page.GetTitleAsync() : null; }
            catch { return null; }
        }
    }
}