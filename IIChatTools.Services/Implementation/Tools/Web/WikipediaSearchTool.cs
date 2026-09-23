using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
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
    ///
    /// KI-064: Wikipedia через корпоративный прокси иногда обрывает TLS-handshake
    /// (SocketException 10054). Дефолтный <c>HttpClient.Timeout</c> = 100s
    /// приводил к зависанию запроса на ~43 секунды. Решение:
    /// - явный <c>Timeout = 15s</c>;
    /// - retry 1 раз с задержкой 1s при transient-ошибках (SSL/сеть);
    /// - внешняя отмена (от Chat / SubAgent) — не retry, пробрасываем.
    /// </summary>
    public class WikipediaSearchTool : ITool
    {
        /// <summary>Явный таймаут HTTP-запроса (сек).</summary>
        private const int HttpTimeoutSeconds = 15;

        /// <summary>Всего попыток (включая первую).</summary>
        private const int MaxAttempts = 2;

        /// <summary>Задержка перед повтором (сек).</summary>
        private const int RetryDelaySeconds = 1;

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

            // CancellationToken из ToolExecutionContext (пробрасывается из Chat/SubAgent).
            var ct = context.CancellationToken;

            try
            {
                // KI-064: явный таймаут 15s — иначе дефолт 100s приводит
                // к 40+ секундному зависанию при SSL-обрыве через прокси.
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

                // Wikipedia требует информативный User-Agent с контактом разработчика.
                // Без этого запрос блокируется с 403 (политика User-Agent).
                // https://meta.wikimedia.org/wiki/User-Agent_policy
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "IIChatTools/1.0 (https://github.com/RuChating/IIChatTools; iilmchat@localhost)");

                var url = $"https://{lang}.wikipedia.org/w/api.php?action=query&list=search&srsearch={HttpUtility.UrlEncode(query)}&format=json&srlimit={limit}";

                // KI-064: retry 1 раз при transient-ошибках (SSL/сеть).
                var json = await SendWithRetryAsync(client, url, query, ct);

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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Внешняя отмена (пользователь нажал Stop, чат закрылся) — не наша ошибка.
                _logger.LogInformation("Wikipedia запрос отменён: {Query}", query);
                return ToolResult.Fail("Запрос отменён");
            }
            catch (TaskCanceledException ex)
            {
                // Timeout (не отмена) — 15s не хватило (SSL/сеть).
                _logger.LogWarning(ex,
                    "Wikipedia запрос превысил таймаут {Timeout}s: {Query}",
                    HttpTimeoutSeconds, query);
                return ToolResult.Fail(
                    $"Wikipedia не ответила за {HttpTimeoutSeconds} секунд. " +
                    "Возможны проблемы с сетью или прокси. Попробуйте позже или используйте web_search.");
            }
            catch (HttpRequestException ex)
            {
                // SSL/сеть — после retry всё ещё ошибка.
                _logger.LogWarning(ex,
                    "Wikipedia недоступна после {Attempts} попыток: {Query}",
                    MaxAttempts, query);
                return ToolResult.Fail(
                    "Wikipedia недоступна (сетевая ошибка или проблема с прокси). " +
                    "Попробуйте позже или используйте web_search.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка поиска в Wikipedia: {Query}", query);
                return ToolResult.Fail($"Ошибка поиска: {ex.Message}");
            }
        }

        /// <summary>
        /// KI-064: выполняет GET с одной повторной попыткой при transient-ошибках
        /// (SSL/сеть/timeout). Внешняя отмена <paramref name="ct"/> — НЕ retry,
        /// пробрасывается немедленно.
        /// </summary>
        /// <param name="client">HTTP-клиент (уже с таймаутом и User-Agent)</param>
        /// <param name="url">URL запроса</param>
        /// <param name="query">Исходный поисковый запрос (для логов)</param>
        /// <param name="ct">Внешний токен отмены</param>
        /// <returns>Тело ответа как строка</returns>
        /// <exception cref="HttpRequestException">Если все попытки неудачны</exception>
        /// <exception cref="TaskCanceledException">Если таймаут исчерпан</exception>
        private async Task<string> SendWithRetryAsync(
            HttpClient client,
            string url,
            string query,
            CancellationToken ct)
        {
            Exception lastException = null;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    return await client.GetStringAsync(url, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Внешняя отмена — сразу пробрасываем, не retry.
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    lastException = ex;

                    if (attempt < MaxAttempts)
                    {
                        _logger.LogWarning(ex,
                            "Wikipedia запрос не удался (попытка {Attempt}/{Max}), повтор через {Delay}s: {Query}",
                            attempt, MaxAttempts, RetryDelaySeconds, query);

                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                    }
                    else
                    {
                        _logger.LogWarning(ex,
                            "Wikipedia запрос не удался после {Max} попыток: {Query}",
                            MaxAttempts, query);
                    }
                }
            }

            // Все попытки неудачны.
            throw lastException ?? new HttpRequestException("Wikipedia запрос не удался");
        }
    }
}