using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Implementation.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Rag
{
    /// <summary>
    /// Инструмент LLM: поиск по документам проекта и вложениям чата
    /// (v1.5.0, KI-083, Шаг 5B; v1.11.0, KI-130 — параметр <c>indexName</c>).
    ///
    /// <para>
    /// <b>Два индекса:</b>
    /// <list type="bullet">
    ///   <item><c>project_docs</c> (default) — глобальный: README, RULES,
    ///     KNOWN_ISSUES, CHANGELOG, RELEASES;</item>
    ///   <item><c>my_rag_docs</c> — per-chat: файлы, приложенные к текущему
    ///     чату через 📎. Требует активного <c>ChatId</c> в контексте.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Возвращает top-K релевантных чанков в формате
    /// <c>{ query, indexName, count, results: [...] }</c>.
    /// Read-only, <c>RequiresApprovalByDefault = false</c>.
    /// </para>
    /// </summary>
    public sealed class SearchKnowledgeBaseTool : ITool
    {
        /// <summary>Имя индекса project_docs (глобальный).</summary>
        private const string ProjectDocsIndex = "project_docs";

        /// <summary>Имя индекса my_rag_docs (per-chat, вложения).</summary>
        private const string MyRagDocsIndex = "my_rag_docs";

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
            "(README.md, RULES.md, KNOWN_ISSUES.md, CHANGELOG.md, RELEASES.md, " +
            "ARCHITECTURE.md, DESIGN.md и др.) ИЛИ в файлах, приложенных к текущему чату. " +
            "Параметр `indexName`:\n" +
            "  • 'project_docs' (по умолчанию) — документация проекта;\n" +
            "  • 'my_rag_docs' — файлы, приложенные к текущему чату через 📎 " +
            "(code-*.py, *.pdf, *.docx, *.md и т.п.).\n" +
            "ОБЯЗАТЕЛЬНО используй ЭТОТ инструмент:\n" +
            "  – для вопросов «что у нас в RULES про…», «правила разработки», " +
            "«что в KNOWN_ISSUES», «архитектура проекта» → indexName='project_docs';\n" +
            "  – для задач «исправь / прочитай / доработай код в файле code-XXXX.py», " +
            "«что в приложенном файле X» → indexName='my_rag_docs'.\n" +
            "НЕ пытайся искать приложенные файлы через file_system_agent или " +
            "read_file — они лежат не в workspace, а в RAG-индексе чата. " +
            "НЕ используй для внешних тем (для них — web_search).";

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
                Name = "indexName",
                Type = "string",
                Description =
                    "Какой индекс искать: 'project_docs' (default) — документация " +
                    "проекта; 'my_rag_docs' — файлы, приложенные к текущему чату " +
                    "(📎). Для 'my_rag_docs' chatId подставляется автоматически.",
                Required = false,
                Default = ProjectDocsIndex
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
                var indexName = ParseIndexName(arguments);

                // v1.11.0 (KI-130): my_rag_docs требует активного чата.
                // ChatId автоматически подставляется из контекста (пробрасывается
                // ChatStreamService → CodeAgentWithReviewTool → AgentToolBase →
                // SubAgentService → сюда).
                int? chatId = null;
                int? userId = null;
                if (string.Equals(indexName, MyRagDocsIndex, StringComparison.OrdinalIgnoreCase))
                {
                    if (!context.ChatId.HasValue || context.ChatId.Value <= 0)
                    {
                        return ToolResult.Fail(
                            "indexName='my_rag_docs' требует активного чата, но ChatId отсутствует в контексте.");
                    }
                    chatId = context.ChatId.Value;

                    // Изоляция по пользователю (per-user индекс chat_history / my_rag_docs).
                    if (context.UserId > 0)
                        userId = context.UserId;
                }

                var results = await _retrievalService.SearchAsync(
                    query: query,
                    indexName: indexName,
                    topK: topK,
                    chatId: chatId,
                    userId: userId,
                    cancellationToken: context.CancellationToken);

                var data = new
                {
                    query = query,
                    indexName = indexName,
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
                    ? (string.Equals(indexName, MyRagDocsIndex, StringComparison.OrdinalIgnoreCase)
                        ? "По запросу ничего не найдено среди приложенных к чату файлов."
                        : "По запросу ничего не найдено в документации проекта.")
                    : $"Найдено {results.Count} релевантных фрагментов.";

                // v1.6.0 (KI-086): проброс sources для блока «Источники» в UI.
                var sources = RagSourceBuilder.Build(results);

                return ToolResult.Ok(data, message, sources);
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

        /// <summary>
        /// Парсит indexName из аргументов: default = 'project_docs',
        /// допустимые значения: 'project_docs' | 'my_rag_docs'.
        /// </summary>
        /// <param name="arguments">Аргументы вызова</param>
        /// <returns>Имя индекса (валидное; при неизвестном — fallback на project_docs)</returns>
        private static string ParseIndexName(JObject arguments)
        {
            var raw = arguments?["indexName"]?.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return ProjectDocsIndex;

            var normalized = raw.Trim().ToLowerInvariant();
            return normalized switch
            {
                "my_rag_docs" => MyRagDocsIndex,
                "project_docs" => ProjectDocsIndex,
                _ => ProjectDocsIndex   // fallback: неизвестный индекс → project_docs
            };
        }
    }
}