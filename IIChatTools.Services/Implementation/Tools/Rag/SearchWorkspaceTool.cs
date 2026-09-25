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
    /// Инструмент LLM: семантический поиск по файлам workspace пользователя
    /// (v1.5.0, KI-083, Шаг 5C).
    ///
    /// <para>
    /// Использует индекс <c>workspace</c> (per-user, opt-in).
    /// <b>UserId берётся из <see cref="ToolExecutionContext"/> — обязателен.</b>
    /// Должен быть явно включён пользователем в
    /// <c>/profile → Workspace index</c>: если выключен — возвращает Fail.
    /// </para>
    ///
    /// <para>
    /// Read-only, <c>RequiresApprovalByDefault = false</c>.
    /// </para>
    /// </summary>
    public sealed class SearchWorkspaceTool : ITool
    {
        /// <summary>Имя индекса workspace (per-user, opt-in).</summary>
        private const string WorkspaceIndex = "workspace";

        /// <summary>Ключ per-user настройки: включён ли Workspace index.</summary>
        private const string EnabledSettingKey = "Workspace.Index.Enabled";

        /// <summary>Верхняя граница topK (защита от чрезмерного контекста).</summary>
        private const int MaxTopK = 20;

        private readonly IRetrievalService _retrievalService;
        private readonly IUserSettingsService _userSettings;
        private readonly ILogger<SearchWorkspaceTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="retrievalService">Сервис поиска (Scoped)</param>
        /// <param name="userSettings">Сервис per-user настроек (Scoped)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public SearchWorkspaceTool(
            IRetrievalService retrievalService,
            IUserSettingsService userSettings,
            ILogger<SearchWorkspaceTool> logger)
        {
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "search_workspace";

        /// <inheritdoc />
        public string Description =>
            "Ищет фрагменты в файлах рабочей области (workspace) пользователя. " +
            "Используй для вопросов вида «где у меня в проекте X?», «найди код, " +
            "который делает Y». НЕ используй, если известен точный путь — для этого " +
            "есть read_file.";

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
                Name = "filePattern",
                Type = "string",
                Description = "Опционально: подстрока пути файла для фильтрации " +
                              "(например, '.cs' или 'src/Services'). Регистронезависимо.",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                // Изоляция по пользователю.
                if (context == null || context.UserId <= 0)
                    return ToolResult.Fail("UserId не задан в контексте — поиск по workspace невозможен.");

                var query = arguments?["query"]?.ToString();
                if (string.IsNullOrWhiteSpace(query))
                    return ToolResult.Fail("Не указан параметр 'query'.");

                // Проверка opt-in: пользователь должен явно включить индексацию workspace.
                var enabled = await _userSettings.GetBoolAsync(
                    context.UserId, EnabledSettingKey, defaultValue: false);

                if (!enabled)
                {
                    return ToolResult.Fail(
                        "Workspace index отключён. Включите его в /profile → «Workspace index», " +
                        "затем дождитесь завершения индексации.");
                }

                var topK = ParseTopK(arguments);
                var filePattern = arguments?["filePattern"]?.ToString();

                // Over-fetch: если задан filePattern, берём больше, чтобы после
                // фильтрации по пути хватило на topK.
                var effectiveTopK = string.IsNullOrWhiteSpace(filePattern) ? topK : topK * 3;

                var results = await _retrievalService.SearchAsync(
                    query: query,
                    indexName: WorkspaceIndex,
                    topK: effectiveTopK,
                    chatId: null,
                    userId: context.UserId,
                    cancellationToken: context.CancellationToken);

                // Post-filter по path (простой Contains, case-insensitive).
                if (!string.IsNullOrWhiteSpace(filePattern))
                {
                    results = results
                        .Where(r => r.DocumentPath != null
                            && r.DocumentPath.Contains(filePattern, StringComparison.OrdinalIgnoreCase))
                        .Take(topK)
                        .ToList();
                }

                var data = new
                {
                    query = query,
                    filePattern = filePattern,
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
                    ? "По запросу ничего не найдено в файлах workspace."
                    : $"Найдено {results.Count} релевантных фрагментов.";

                return ToolResult.Ok(data, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SearchWorkspaceTool: ошибка поиска (userId={UserId}, query={Query})",
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
    }
}