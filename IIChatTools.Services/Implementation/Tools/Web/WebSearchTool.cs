using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Web
{
    /// <summary>
    /// Инструмент: веб-поиск через DuckDuckGo HTML-интерфейс.
    /// </summary>
    public class WebSearchTool : ITool
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WebSearchTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов</param>
        /// <param name="logger">Логгер</param>
        public WebSearchTool(IHttpClientFactory httpClientFactory, ILogger<WebSearchTool> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "web_search";

        /// <inheritdoc />
        public string Description => "Выполняет поиск в интернете через DuckDuckGo и возвращает список результатов (заголовок, ссылка, сниппет).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "query", Type = "string", Description = "Поисковый запрос.", Required = true },
            new ToolParameterDescriptor { Name = "limit", Type = "integer", Description = "Максимум результатов (по умолчанию 10).", Required = false, Default = 10 }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var query = arguments.GetString("query");
            if (string.IsNullOrWhiteSpace(query))
                return ToolResult.Fail("Не указан поисковый запрос");

            var limit = arguments.GetInt("limit", 10);
            if (limit <= 0 || limit > 30) limit = 10;

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) IIChatTools/1.0");

                var url = $"https://html.duckduckgo.com/html/?q={HttpUtility.UrlEncode(query)}";
                var html = await client.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var results = new List<object>();
                var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'result')]");

                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        if (results.Count >= limit) break;

                        var linkNode = node.SelectSingleNode(".//a[contains(@class,'result__a')]");
                        var snippetNode = node.SelectSingleNode(".//a[contains(@class,'result__snippet')]");

                        if (linkNode == null) continue;

                        var title = HtmlEntity.DeEntitize(linkNode.InnerText)?.Trim();
                        var href = linkNode.GetAttributeValue("href", string.Empty);

                        // DuckDuckGo редиректит через /l/?uddg=
                        var decoded = DecodeDuckLink(href);

                        results.Add(new
                        {
                            title,
                            url = decoded,
                            snippet = snippetNode != null
                                ? HtmlEntity.DeEntitize(snippetNode.InnerText)?.Trim()
                                : null
                        });
                    }
                }

                return ToolResult.Ok(new { query, count = results.Count, results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка веб-поиска: {Query}", query);
                return ToolResult.Fail($"Ошибка поиска: {ex.Message}");
            }
        }

        /// <summary>
        /// Извлекает настоящий URL из редиректной ссылки DuckDuckGo.
        /// </summary>
        /// <param name="href">Значение атрибута href</param>
        /// <returns>Развёрнутый URL</returns>
        private static string DecodeDuckLink(string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return href;
            if (href.StartsWith("//duckduckgo.com/l/?", StringComparison.OrdinalIgnoreCase))
                href = "https:" + href;

            try
            {
                var uri = new Uri(href);
                var q = HttpUtility.ParseQueryString(uri.Query);
                var uddg = q["uddg"];
                return string.IsNullOrEmpty(uddg) ? href : HttpUtility.UrlDecode(uddg);
            }
            catch
            {
                return href;
            }
        }
    }
}