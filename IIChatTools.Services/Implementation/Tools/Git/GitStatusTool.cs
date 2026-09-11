using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Git
{
    /// <summary>
    /// Инструмент: получение статуса Git-репозитория.
    /// </summary>
    public class GitStatusTool : BaseGitTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GitStatusTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GitStatusTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "git_status";

        /// <inheritdoc />
        public override string Description => "Возвращает статус Git-репозитория (branch, изменения, staged, untracked).";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => Array.Empty<ToolParameterDescriptor>();

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGitContextAsync(context);
            if (validation != null) return validation;

            try
            {
                // Короткий формат: branch + список изменений
                var shortResult = await RunGitAsync(context, new[] { "status", "--porcelain=v1", "--branch" });
                if (shortResult.ExitCode != 0)
                    return ToolResult.Fail($"git status завершился с кодом {shortResult.ExitCode}: {shortResult.StdErr}");

                var lines = (shortResult.StdOut ?? string.Empty)
                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                string branch = null;
                var changed = new List<object>();

                foreach (var line in lines)
                {
                    if (line.StartsWith("## ", StringComparison.Ordinal))
                    {
                        // ## main...origin/main [ahead 1]
                        var rest = line.Substring(3).Trim();
                        var spaceIdx = rest.IndexOf(' ');
                        branch = spaceIdx > 0 ? rest.Substring(0, spaceIdx) : rest;
                        continue;
                    }

                    if (line.Length < 3) continue;

                    var x = line[0].ToString();
                    var y = line[1].ToString();
                    var path = line.Substring(3).Trim();

                    changed.Add(new
                    {
                        indexStatus = x,
                        workTreeStatus = y,
                        path
                    });
                }

                return ToolResult.Ok(new
                {
                    branch,
                    changedCount = changed.Count,
                    changes = changed
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка git status");
                return ToolResult.Fail($"Ошибка git status: {ex.Message}");
            }
        }
    }
}