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
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
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
        public override string Description =>
            "Добавляет указанные пути (или все изменения) в индекс Git.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "files",
                Type = "array",
                Description = "Список относительных путей файлов внутри репозитория. Пустой — добавить все.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "all",
                Type = "bool",
                Description = "Добавить все изменения (git add -A).",
                Required = false,
                Default = false
            }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGitContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

            var files = arguments.GetStringArray("files");
            var all = arguments.GetBool("all");

            try
            {
                var args = new List<string> { "add" };

                if (all || files.Count == 0)
                {
                    args.Add("-A");
                }
                else
                {
                    foreach (var f in files)
                    {
                        if (string.IsNullOrWhiteSpace(f))
                            continue;

                        if (!PathHelper.TryGetSafeFullPath(f, workingDir, out _))
                            return ToolResult.Fail($"Недопустимый путь файла: {f}");

                        args.Add("--");
                        args.Add(f);
                    }
                }

                var result = await RunGitInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                    return ToolResult.Fail(
                        $"git add завершился с кодом {result.ExitCode}: {result.StdErr}");

                return ToolResult.Ok(new
                {
                    added = true,
                    stdout = result.StdOut ?? string.Empty,
                    files = all ? new[] { "*" } : files
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