using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Git
{
    /// <summary>
    /// Инструмент: добавление файлов в индекс (git add).
    /// </summary>
    public class GitAddTool : BaseGitTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        public GitAddTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GitAddTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "git_add";

        /// <inheritdoc />
        public override string Description => "Добавляет указанные пути (или все изменения) в индекс Git.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "paths", Type = "array", Description = "Список относительных путей. Пустой — добавить все.", Required = false },
            new ToolParameterDescriptor { Name = "all", Type = "bool", Description = "Добавить все изменения (git add -A).", Required = false, Default = false }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGitContextAsync(context);
            if (validation != null) return validation;

            var paths = arguments.GetStringArray("paths");
            var all = arguments.GetBool("all");

            try
            {
                var args = new List<string> { "add" };

                if (all || paths.Count == 0)
                {
                    args.Add("-A");
                }
                else
                {
                    foreach (var p in paths)
                    {
                        if (!PathHelper.TryGetSafeFullPath(p, context.WorkspaceRoot, out _))
                            return ToolResult.Fail($"Недопустимый путь: {p}");
                        args.Add("--");
                        args.Add(p);
                    }
                }

                var result = await RunGitAsync(context, args);
                if (result.ExitCode != 0)
                    return ToolResult.Fail($"git add завершился с кодом {result.ExitCode}: {result.StdErr}");

                return ToolResult.Ok(new
                {
                    added = true,
                    stdout = result.StdOut ?? string.Empty,
                    paths = all ? new[] { "*" } : paths
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка git add");
                return ToolResult.Fail($"Ошибка git add: {ex.Message}");
            }
        }
    }
}