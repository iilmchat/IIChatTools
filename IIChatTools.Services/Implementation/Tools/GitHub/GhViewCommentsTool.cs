using System;
using System.Linq;
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
    /// Инструмент: просмотр комментариев к issue или pull request.
    /// </summary>
    public class GhViewCommentsTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GhViewCommentsTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GhViewCommentsTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "gh_view_comments";

        /// <inheritdoc />
        public override string Description =>
            "Возвращает комментарии к issue или pull request по номеру. " +
            "Параметр path указывает каталог репозитория внутри workspace.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "number",
                Type = "integer",
                Description = "Номер issue или PR.",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "type",
                Type = "string",
                Description = "issue | pr (по умолчанию issue).",
                Required = false,
                Default = "issue"
            }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

            var number = arguments.GetInt("number");
            var type = arguments.GetString("type", "issue");

            if (number <= 0)
                return ToolResult.Fail("Некорректный номер");
            if (type != "issue" && type != "pr")
                type = "issue";

            try
            {
                var subCommand = type == "pr" ? "pr" : "issue";
                var args = new List<string>
                {
                    subCommand, "view", number.ToString(),
                    "--json", "comments"
                };

                var result = await RunGhInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                    return ToolResult.Fail(
                        $"gh view завершился с кодом {result.ExitCode}: {result.StdErr}");

                var comments = new List<object>();
                try
                {
                    var json = JObject.Parse(result.StdOut ?? "{}");
                    var commentsArr = json["comments"] as JArray;
                    if (commentsArr != null)
                    {
                        foreach (var c in commentsArr)
                        {
                            comments.Add(new
                            {
                                author = c["author"]?["login"]?.ToString(),
                                body = c["body"]?.ToString(),
                                createdAt = c["createdAt"]?.ToString(),
                                url = c["url"]?.ToString()
                            });
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    Logger.LogWarning(parseEx, "Не удалось распарсить JSON из gh view");
                }

                return ToolResult.Ok(new { number, type, count = comments.Count, comments });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка gh view comments");
                return ToolResult.Fail($"Ошибка gh view: {ex.Message}");
            }
        }
    }
}