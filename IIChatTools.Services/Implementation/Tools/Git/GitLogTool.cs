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
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
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
        public override string Description =>
            "Возвращает последние N коммитов в формате: hash, автор, дата, сообщение.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "limit",
                Type = "integer",
                Description = "Количество коммитов (1–200, по умолчанию 20).",
                Required = false,
                Default = 20
            },
            new ToolParameterDescriptor
            {
                Name = "filePath",
                Type = "string",
                Description = "Ограничить историю указанным файлом/каталогом внутри репозитория.",
                Required = false
            }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGitContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

            var limit = arguments.GetInt("limit", 20);
            if (limit <= 0 || limit > 200) limit = 20;

            var filePath = arguments.GetString("filePath");

            try
            {
                var format = "%H|%an|%ae|%aI|%s";
                var args = new List<string>
                {
                    "log",
                    $"--pretty=format:{format}",
                    $"-n{limit}"
                };

                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    if (!PathHelper.TryGetSafeFullPath(filePath, workingDir, out _))
                        return ToolResult.Fail("Недопустимый путь файла");

                    args.Add("--");
                    args.Add(filePath);
                }

                var result = await RunGitInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                    return ToolResult.Fail(
                        $"git log завершился с кодом {result.ExitCode}: {result.StdErr}");

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