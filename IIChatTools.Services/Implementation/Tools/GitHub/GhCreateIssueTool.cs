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
    /// Инструмент: создание issue в GitHub через gh.
    /// </summary>
    public class GhCreateIssueTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        public GhCreateIssueTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GhCreateIssueTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "gh_create_issue";

        /// <inheritdoc />
        public override string Description => "Создаёт issue в текущем репозитории через GitHub CLI.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "title", Type = "string", Description = "Заголовок issue.", Required = true },
            new ToolParameterDescriptor { Name = "body", Type = "string", Description = "Тело issue (Markdown).", Required = false },
            new ToolParameterDescriptor { Name = "labels", Type = "array", Description = "Список меток.", Required = false }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhAsync(context);
            if (validation != null) return validation;

            var title = arguments.GetString("title");
            var body = arguments.GetString("body");
            var labels = arguments.GetStringArray("labels");

            if (string.IsNullOrWhiteSpace(title))
                return ToolResult.Fail("Не указан заголовок issue");
            if (title.Length > 500)
                return ToolResult.Fail("Заголовок превышает 500 символов");
            if (body != null && body.Length > 60000)
                return ToolResult.Fail("Тело issue превышает 60 000 символов");

            try
            {
                var args = new List<string> { "issue", "create", "--title", title };
                if (!string.IsNullOrWhiteSpace(body))
                {
                    args.Add("--body");
                    args.Add(body);
                }

                foreach (var label in labels)
                {
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    if (label.Length > 100) continue;
                    args.Add("--label");
                    args.Add(label);
                }

                var result = await RunGhAsync(context, args);
                if (result.ExitCode != 0)
                    return ToolResult.Fail($"gh issue create завершился с кодом {result.ExitCode}: {result.StdErr}");

                return ToolResult.Ok(new
                {
                    created = true,
                    url = result.StdOut?.Trim(),
                    stdout = result.StdOut ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка gh issue create");
                return ToolResult.Fail($"Ошибка создания issue: {ex.Message}");
            }
        }
    }
}