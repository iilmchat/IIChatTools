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
    /// Инструмент: просмотр diff Pull Request.
    /// Поддерживает работу в подкаталогах через параметр path.
    /// </summary>
    public class GhViewPrDiffTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
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
        public override string Description =>
            "Возвращает diff указанного Pull Request. " +
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
                Description = "Номер PR.",
                Required = true
            }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

            var number = arguments.GetInt("number");
            if (number <= 0)
                return ToolResult.Fail("Некорректный номер PR");

            try
            {
                var args = new List<string> { "pr", "diff", number.ToString() };
                var result = await RunGhInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                    return ToolResult.Fail(
                        $"gh pr diff завершился с кодом {result.ExitCode}: {result.StdErr}");

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