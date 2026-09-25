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
    /// Инструмент LLM: поиск по глобальным документам проекта
    /// (v1.5.0, KI-083, Шаг 5B).
    ///
    /// <para>
    /// Использует индекс <c>project_docs</c>: README, RULES, KNOWN_ISSUES,
    /// CHANGELOG, RELEASES. Возвращает top-K релевантных чанков
    /// в формате <c>{ query, count, results: [...] }</c>.
    /// </para>
    ///
    /// <para>
    /// Read-only, <c>RequiresApprovalByDefault = false</c>.
    /// </para>
    /// </summary>
    public sealed class SearchKnowledgeBaseTool : ITool
    {
        /// <summary>Имя индекса project_docs (глобальный).</summary>
        private const string ProjectDocsIndex = "project_docs";

        /// <summary>Верхняя граница topK (защита от чрезмерного контекста).</summary>
        private const int MaxTopK = 20;

        private readonly IRetrievalService _retrievalService;
        private readonly ILogger<SearchKnowledgeBaseTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="retrievalService">Сервис поиска (Scoped)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public SearchKnowledgeBaseTool(
            IRetrievalService retrievalService,
            ILogger<SearchKnowledgeBaseTool> logger)
        {
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "search_knowledge_base";

        /// <inheritdoc />
        public string Description =>
            "Ищет релевантные фрагменты в документации проекта IIChatTools " +
            "(README, RULES, KNOWN_ISSUES, CHANGELOG, RELEASES). " +
            "Используй для вопросов про правила разработки, известные проблемы, " +
            "архитектуру, API, конфигурацию. НЕ используй для вопросов по внешним темам " +
            "(для них — web_search).";

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
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                var query = arguments?["query"]?.ToString();
                if (string.IsNullOrWhiteSpace(query))
                    return ToolResult.Fail("Не указан параметр 'query'.");

                var topK = ParseTopK(arguments);

                var results = await _retrievalService.SearchAsync(
                    query: query,
                    indexName: ProjectDocsIndex,
                    topK: topK,
                    chatId: null,
                    userId: null,
                    cancellationToken: context.CancellationToken);

                var data = new
                {
                    query = query,
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
                    ? "По запросу ничего не найдено в документации проекта."
                    : $"Найдено {results.Count} релевантных фрагментов.";

                return ToolResult.Ok(data, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SearchKnowledgeBaseTool: ошибка поиска (query={Query})",
                    arguments?["query"]?.ToString());
                return ToolResult.Fail($"Ошибка поиска: {ex.Message}");
            }
        }

        /// <summary>
        /// Парсит topK из аргументов: default=5, clamp 1..20.
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
    }
}