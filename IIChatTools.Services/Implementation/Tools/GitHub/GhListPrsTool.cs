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
    /// Поддерживает работу в подкаталогах через параметр path.
    /// </summary>
    public class GhListPrsTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
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
        public override string Description =>
            "Возвращает список pull request'ов с фильтром по состоянию. " +
            "Параметр path указывает каталог репозитория внутри workspace.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "state",
                Type = "string",
                Description = "open | closed | merged | all (по умолчанию open).",
                Required = false,
                Default = "open"
            },
            new ToolParameterDescriptor
            {
                Name = "limit",
                Type = "integer",
                Description = "Максимум (1–100, по умолчанию 20).",
                Required = false,
                Default = 20
            }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

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

                var result = await RunGhInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                    return ToolResult.Fail(
                        $"gh pr list завершился с кодом {result.ExitCode}: {result.StdErr}");

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