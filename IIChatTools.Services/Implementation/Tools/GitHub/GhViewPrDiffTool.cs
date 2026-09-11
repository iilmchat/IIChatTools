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
    /// Инструмент: просмотр diff Pull Request.
    /// </summary>
    public class GhViewPrDiffTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        public GhViewPrDiffTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GhViewPrDiffTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "gh_view_pr_diff";

        /// <inheritdoc />
        public override string Description => "Возвращает diff указанного Pull Request.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "number", Type = "integer", Description = "Номер PR.", Required = true }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhAsync(context);
            if (validation != null) return validation;

            var number = arguments.GetInt("number");
            if (number <= 0)
                return ToolResult.Fail("Некорректный номер PR");

            try
            {
                var args = new List<string> { "pr", "diff", number.ToString() };
                var result = await RunGhAsync(context, args);
                if (result.ExitCode != 0)
                    return ToolResult.Fail($"gh pr diff завершился с кодом {result.ExitCode}: {result.StdErr}");

                return ToolResult.Ok(new
                {
                    number,
                    diff = result.StdOut ?? string.Empty,
                    truncated = result.Truncated
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка gh pr diff");
                return ToolResult.Fail($"Ошибка gh pr diff: {ex.Message}");
            }
        }
    }
}