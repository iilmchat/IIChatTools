using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Web
{
    /// <summary>
    /// Инструмент: загрузка текстового содержимого веб-страницы.
    /// </summary>
    public class FetchWebContentTool : ITool
    {
        private const int MaxTextLength = 50000;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FetchWebContentTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public FetchWebContentTool(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<FetchWebContentTool> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "fetch_web_content";

        /// <inheritdoc />
        public string Description => "Загружает веб-страницу по URL и возвращает очищенный текст (без HTML-тегов).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "url", Type = "string", Description = "URL страницы.", Required = true },
            new ToolParameterDescriptor { Name = "extractText", Type = "bool", Description = "Извлекать только текст (true) или вернуть HTML (false).", Required = false, Default = true }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var url = arguments.GetString("url");
            if (string.IsNullOrWhiteSpace(url))
                return ToolResult.Fail("Не указан URL");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                return ToolResult.Fail("Некорректный URL (разрешены только http/https)");

            var extractText = arguments.GetBool("extractText", true);

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) IIChatTools/1.0");

                var html = await client.GetStringAsync(url);

                if (!extractText)
                    return ToolResult.Ok(new { url, htmlLength = html.Length, html });

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Удаляем скрипты и стили
                var toRemove = doc.DocumentNode.SelectNodes("//script|//style|//noscript");
                if (toRemove != null)
                    foreach (var n in toRemove) n.Remove();

                var text = doc.DocumentNode.InnerText ?? string.Empty;
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                var truncated = false;
                if (text.Length > MaxTextLength)
                {
                    text = text.Substring(0, MaxTextLength);
                    truncated = true;
                }

                var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();

                return ToolResult.Ok(new
                {
                    url,
                    title,
                    textLength = text.Length,
                    truncated,
                    text
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки веб-страницы: {Url}", url);
                return ToolResult.Fail($"Ошибка загрузки: {ex.Message}");
            }
        }
    }
}