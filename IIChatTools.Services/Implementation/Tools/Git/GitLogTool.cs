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
    /// Инструмент: просмотр истории коммитов (git log).
    /// </summary>
    public class GitLogTool : BaseGitTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        public GitLogTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GitLogTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "git_log";

        /// <inheritdoc />
        public override string Description => "Возвращает последние N коммитов в формате: hash, автор, дата, сообщение.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "limit", Type = "integer", Description = "Количество коммитов (1–200, по умолчанию 20).", Required = false, Default = 20 },
            new ToolParameterDescriptor { Name = "path", Type = "string", Description = "Ограничить историю файлом/каталогом.", Required = false }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGitContextAsync(context);
            if (validation != null) return validation;

            var limit = arguments.GetInt("limit", 20);
            if (limit <= 0 || limit > 200) limit = 20;

            var pathArg = arguments.GetString("path");

            try
            {
                var format = "%H|%an|%ae|%aI|%s";
                var args = new List<string>
                {
                    "log",
                    $"--pretty=format:{format}",
                    $"-n{limit}"
                };

                if (!string.IsNullOrWhiteSpace(pathArg))
                {
                    if (!PathHelper.TryGetSafeFullPath(pathArg, context.WorkspaceRoot, out _))
                        return ToolResult.Fail("Недопустимый путь");
                    args.Add("--");
                    args.Add(pathArg);
                }

                var result = await RunGitAsync(context, args);
                if (result.ExitCode != 0)
                    return ToolResult.Fail($"git log завершился с кодом {result.ExitCode}: {result.StdErr}");

                var commits = new List<object>();
                var lines = (result.StdOut ?? string.Empty)
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var parts = line.Split('|', 5);
                    if (parts.Length < 5) continue;

                    commits.Add(new
                    {
                        hash = parts[0],
                        authorName = parts[1],
                        authorEmail = parts[2],
                        dateIso = parts[3],
                        subject = parts[4]
                    });
                }

                return ToolResult.Ok(new { count = commits.Count, commits });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка git log");
                return ToolResult.Fail($"Ошибка git log: {ex.Message}");
            }
        }
    }
}