using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.Web
{
    /// <summary>
    /// Инструмент: поиск в Wikipedia через официальный REST API.
    /// </summary>
    public class WikipediaSearchTool : ITool
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WikipediaSearchTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов</param>
        /// <param name="logger">Логгер</param>
        public WikipediaSearchTool(IHttpClientFactory httpClientFactory, ILogger<WikipediaSearchTool> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "wikipedia_search";

        /// <inheritdoc />
        public string Description => "Ищет статьи в Wikipedia и возвращает краткое описание и ссылку.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "query", Type = "string", Description = "Поисковый запрос.", Required = true },
            new ToolParameterDescriptor { Name = "language", Type = "string", Description = "Код языка (ru, en и т.д.). По умолчанию ru.", Required = false, Default = "ru" },
            new ToolParameterDescriptor { Name = "limit", Type = "integer", Description = "Максимум результатов (по умолчанию 5).", Required = false, Default = 5 }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var query = arguments.GetString("query");
            var lang = arguments.GetString("language", "ru");
            var limit = arguments.GetInt("limit", 5);

            if (string.IsNullOrWhiteSpace(query))
                return ToolResult.Fail("Не указан поисковый запрос");
            if (string.IsNullOrWhiteSpace(lang) || lang.Length > 8) lang = "ru";
            if (limit <= 0 || limit > 20) limit = 5;

            try
            {
                var client = _httpClientFactory.CreateClient();

                // Wikipedia требует информативный User-Agent с контактом разработчика.
                // Без этого запрос блокируется с 403 (политика User-Agent).
                // https://meta.wikimedia.org/wiki/User-Agent_policy
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "IIChatTools/1.0 (https://github.com/RuChating/IIChatTools; iilmchat@localhost)");

                var url = $"https://{lang}.wikipedia.org/w/api.php?action=query&list=search&srsearch={HttpUtility.UrlEncode(query)}&format=json&srlimit={limit}";
                var json = await client.GetStringAsync(url);

                var root = JObject.Parse(json);
                var results = new List<object>();

                var hits = root["query"]?["search"] as JArray;
                if (hits != null)
                {
                    foreach (var hit in hits)
                    {
                        var title = hit["title"]?.ToString();
                        var snippet = hit["snippet"]?.ToString();
                        var pageId = hit["pageid"]?.Value<long>() ?? 0;

                        results.Add(new
                        {
                            title,
                            pageId,
                            url = $"https://{lang}.wikipedia.org/?curid={pageId}",
                            snippet = System.Text.RegularExpressions.Regex.Replace(snippet ?? "", "<.*?>", "")
                        });
                    }
                }

                return ToolResult.Ok(new { query, language = lang, count = results.Count, results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка поиска в Wikipedia: {Query}", query);
                return ToolResult.Fail($"Ошибка поиска: {ex.Message}");
            }
        }
    }
}