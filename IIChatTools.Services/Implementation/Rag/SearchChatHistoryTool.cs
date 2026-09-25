using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Rag
{
    /// <summary>
    /// Инструмент LLM: поиск по истории чатов текущего пользователя
    /// (v1.5.0, KI-083, Шаг 5B).
    ///
    /// <para>
    /// Использует индекс <c>chat_history</c> (per-user).
    /// <b>UserId берётся из <see cref="ToolExecutionContext"/> — обязателен.</b>
    /// Опциональный <c>chatId</c> ограничивает поиск конкретным чатом.
    /// </para>
    ///
    /// <para>
    /// Read-only, <c>RequiresApprovalByDefault = false</c>.
    /// </para>
    /// </summary>
    public sealed class SearchChatHistoryTool : ITool
    {
        /// <summary>Имя индекса chat_history (per-user).</summary>
        private const string ChatHistoryIndex = "chat_history";

        /// <summary>Верхняя граница topK (защита от чрезмерного контекста).</summary>
        private const int MaxTopK = 20;

        private readonly IRetrievalService _retrievalService;
        private readonly ILogger<SearchChatHistoryTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="retrievalService">Сервис поиска (Scoped)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public SearchChatHistoryTool(
            IRetrievalService retrievalService,
            ILogger<SearchChatHistoryTool> logger)
        {
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "search_chat_history";

        /// <inheritdoc />
        public string Description =>
            "Ищет релевантные фрагменты в истории чатов текущего пользователя " +
            "(все диалоги с LLM). Используй, когда пользователь ссылается на прошлые " +
            "обсуждения: «мы говорили про…», «в прошлый раз ты…».";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "query",
                Type = "string",
                Description = "Поисковый запрос на естественном языке.",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "topK",
                Type = "integer",
                Description = "Сколько чанков вернуть (1–20, по умолчанию 5).",
                Required = false,
                Default = 5
            },
            new ToolParameterDescriptor
            {
                Name = "chatId",
                Type = "integer",
                Description = "Опционально: ограничить поиск конкретным чатом (id).",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                // Изоляция по пользователю: обязательно должен быть задан.
                if (context == null || context.UserId <= 0)
                    return ToolResult.Fail("UserId не задан в контексте — поиск по истории чатов невозможен.");

                var query = arguments?["query"]?.ToString();
                if (string.IsNullOrWhiteSpace(query))
                    return ToolResult.Fail("Не указан параметр 'query'.");

                var topK = ParseTopK(arguments);
                var chatId = ParseChatId(arguments);

                var results = await _retrievalService.SearchAsync(
                    query: query,
                    indexName: ChatHistoryIndex,
                    topK: topK,
                    chatId: chatId,
                    userId: context.UserId,
                    cancellationToken: context.CancellationToken);

                var data = new
                {
                    query = query,
                    chatId = chatId,
                    count = results.Count,
                    results = results.Select((r, i) => new
                    {
                        rank = i + 1,
                        score = Math.Round(r.Score, 3),
                        source = r.DocumentPath,
                        chunkIndex = r.ChunkIndex,
                        text = r.Text
                    }).ToArray()
                };

                var message = results.Count == 0
                    ? "По запросу ничего не найдено в истории чатов."
                    : $"Найдено {results.Count} релевантных фрагментов.";

                return ToolResult.Ok(data, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SearchChatHistoryTool: ошибка поиска (userId={UserId}, query={Query})",
                    context?.UserId, arguments?["query"]?.ToString());
                return ToolResult.Fail($"Ошибка поиска: {ex.Message}");
            }
        }

        /// <summary>
        /// Парсит topK: default=5, clamp 1..20.
        /// </summary>
        private static int ParseTopK(JObject arguments)
        {
            var raw = arguments?["topK"];
            if (raw == null || raw.Type == JTokenType.Null)
                return 5;

            int topK;
            if (raw.Type == JTokenType.Integer)
                topK = raw.Value<int>();
            else if (!int.TryParse(raw.ToString(), out topK))
                return 5;

            return Math.Clamp(topK, 1, MaxTopK);
        }

        /// <summary>
        /// Парсит chatId: null, если не задан или невалиден.
        /// </summary>
        private static int? ParseChatId(JObject arguments)
        {
            var raw = arguments?["chatId"];
            if (raw == null || raw.Type == JTokenType.Null)
                return null;

            if (raw.Type == JTokenType.Integer)
                return raw.Value<int>();

            return int.TryParse(raw.ToString(), out var chatId) ? chatId : (int?)null;
        }
    }
}