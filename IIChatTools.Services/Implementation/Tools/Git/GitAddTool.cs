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
    /// Требует явного указания <c>files</c> или <c>all: true</c> — неявный
    /// <c>git add -A</c> при пустом списке файлов запрещён (KI-005).
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
            "Добавляет указанные пути в индекс Git. " +
            "Для добавления ВСЕХ изменений требуется явно передать all: true. " +
            "Пустой files без all: true недопустим — предотвращает случайный git add -A.";

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
                Description = "Список относительных путей файлов внутри репозитория. " +
                              "Обязателен, если all != true. Пустой массив недопустим.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "all",
                Type = "bool",
                Description = "Явно добавить все изменения (git add -A). " +
                              "Взаимоисключающий с files. По умолчанию false.",
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

            var hasFiles = files != null && files.Count > 0;

            // Взаимоисключающие параметры
            if (all && hasFiles)
            {
                return ToolResult.Fail(
                    "Параметры 'files' и 'all: true' взаимоисключающие. " +
                    "Укажите либо список файлов, либо all: true, но не оба.");
            }

            // Пустой files без all: true — отказ (KI-005)
            if (!all && !hasFiles)
            {
                return ToolResult.Fail(
                    "Не указан список файлов. " +
                    "Передайте 'files' (массив относительных путей) " +
                    "или явно 'all: true' для добавления всех изменений.");
            }

            try
            {
                var args = new List<string> { "add" };

                if (all)
                {
                    // Явное намерение добавить все изменения
                    args.Add("-A");
                }
                else
                {
                    // Один `--` перед всем списком, затем — файлы
                    args.Add("--");

                    foreach (var f in files)
                    {
                        if (string.IsNullOrWhiteSpace(f))
                            continue;

                        if (!PathHelper.TryGetSafeFullPath(f, workingDir, out _))
                            return ToolResult.Fail($"Недопустимый путь файла: {f}");

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