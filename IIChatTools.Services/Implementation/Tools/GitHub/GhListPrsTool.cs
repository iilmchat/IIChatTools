using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Инструмент: список Pull Request'ов текущего репозитория.
    /// </summary>
    public class GhListPrsTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        public GhListPrsTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GhListPrsTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "gh_list_prs";

        /// <inheritdoc />
        public override string Description => "Возвращает список pull request'ов с фильтром по состоянию.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "state", Type = "string", Description = "open | closed | merged | all (по умолчанию open).", Required = false, Default = "open" },
            new ToolParameterDescriptor { Name = "limit", Type = "integer", Description = "Максимум (1–100, по умолчанию 20).", Required = false, Default = 20 }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhAsync(context);
            if (validation != null) return validation;

            var state = arguments.GetString("state", "open");
            var limit = arguments.GetInt("limit", 20);

            var allowed = new[] { "open", "closed", "merged", "all" };
            if (!allowed.Contains(state)) state = "open";
            if (limit <= 0 || limit > 100) limit = 20;

            try
            {
                var args = new List<string>
                {
                    "pr", "list",
                    "--state", state,
                    "--limit", limit.ToString(),
                    "--json", "number,title,state,author,headRefName,baseRefName,createdAt,url,isDraft"
                };

                var result = await RunGhAsync(context, args);
                if (result.ExitCode != 0)
                    return ToolResult.Fail($"gh pr list завершился с кодом {result.ExitCode}: {result.StdErr}");

                var prs = new List<object>();
                try
                {
                    var json = JArray.Parse(result.StdOut ?? "[]");
                    foreach (var item in json)
                    {
                        prs.Add(new
                        {
                            number = item["number"]?.Value<int>(),
                            title = item["title"]?.ToString(),
                            state = item["state"]?.ToString(),
                            author = item["author"]?["login"]?.ToString(),
                            headRefName = item["headRefName"]?.ToString(),
                            baseRefName = item["baseRefName"]?.ToString(),
                            isDraft = item["isDraft"]?.Value<bool>() ?? false,
                            createdAt = item["createdAt"]?.ToString(),
                            url = item["url"]?.ToString()
                        });
                    }
                }
                catch (Exception parseEx)
                {
                    Logger.LogWarning(parseEx, "Не удалось распарсить JSON из gh pr list");
                }

                return ToolResult.Ok(new { count = prs.Count, prs });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка gh pr list");
                return ToolResult.Fail($"Ошибка gh pr list: {ex.Message}");
            }
        }
    }
}