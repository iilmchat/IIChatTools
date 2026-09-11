using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.GitHub
{
    /// <summary>
    /// Инструмент: список issues репозитория через gh.
    /// </summary>
    public class GhListIssuesTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        public GhListIssuesTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GhListIssuesTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "gh_list_issues";

        /// <inheritdoc />
        public override string Description => "Возвращает список issues текущего репозитория с фильтрами состояния и метки.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "state", Type = "string", Description = "open | closed | all (по умолчанию open).", Required = false, Default = "open" },
            new ToolParameterDescriptor { Name = "label", Type = "string", Description = "Фильтр по метке.", Required = false },
            new ToolParameterDescriptor { Name = "limit", Type = "integer", Description = "Максимум (1–100, по умолчанию 20).", Required = false, Default = 20 }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhAsync(context);
            if (validation != null) return validation;

            var state = arguments.GetString("state", "open");
            var label = arguments.GetString("label");
            var limit = arguments.GetInt("limit", 20);

            if (state != "open" && state != "closed" && state != "all")
                state = "open";
            if (limit <= 0 || limit > 100) limit = 20;

            try
            {
                var args = new List<string>
                {
                    "issue", "list",
                    "--state", state,
                    "--limit", limit.ToString(),
                    "--json", "number,title,state,author,labels,createdAt,url"
                };

                if (!string.IsNullOrWhiteSpace(label))
                {
                    args.Add("--label");
                    args.Add(label);
                }

                var result = await RunGhAsync(context, args);
                if (result.ExitCode != 0)
                    return ToolResult.Fail($"gh issue list завершился с кодом {result.ExitCode}: {result.StdErr}");

                var issues = new List<object>();
                try
                {
                    var json = JArray.Parse(result.StdOut ?? "[]");
                    foreach (var item in json)
                    {
                        issues.Add(new
                        {
                            number = item["number"]?.Value<int>(),
                            title = item["title"]?.ToString(),
                            state = item["state"]?.ToString(),
                            author = item["author"]?["login"]?.ToString(),
                            url = item["url"]?.ToString(),
                            createdAt = item["createdAt"]?.ToString(),
                            labels = item["labels"] is JArray labelsArr
                                ? labelsArr.Select(l => l["name"]?.ToString()).ToArray()
                                : Array.Empty<string>()
                        });
                    }
                }
                catch (Exception parseEx)
                {
                    Logger.LogWarning(parseEx, "Не удалось распарсить JSON из gh issue list");
                }

                return ToolResult.Ok(new { count = issues.Count, issues });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка gh issue list");
                return ToolResult.Fail($"Ошибка gh issue list: {ex.Message}");
            }
        }
    }
}