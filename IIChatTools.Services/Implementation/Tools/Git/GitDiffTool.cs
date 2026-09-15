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
    /// Инструмент: просмотр различий (git diff) между рабочей директорией и индексом/HEAD.
    /// Поддерживает работу в подкаталогах через параметр path.
    /// </summary>
    public class GitDiffTool : BaseGitTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GitDiffTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GitDiffTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "git_diff";

        /// <inheritdoc />
        public override string Description =>
            "Показывает разницу между рабочей директорией и индексом, либо между коммитами. " +
            "Параметр path указывает каталог репозитория, filePath — файл внутри него.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "filePath",
                Type = "string",
                Description = "Относительный путь к файлу/каталогу внутри репозитория (опционально).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "staged",
                Type = "bool",
                Description = "Показать staged-изменения (--cached).",
                Required = false,
                Default = false
            },
            new ToolParameterDescriptor
            {
                Name = "fromRef",
                Type = "string",
                Description = "Коммит/ветка начала диапазона (опционально).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "toRef",
                Type = "string",
                Description = "Коммит/ветка конца диапазона (опционально).",
                Required = false
            }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGitContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

            var filePath = arguments.GetString("filePath");
            var staged = arguments.GetBool("staged");
            var fromRef = arguments.GetString("fromRef");
            var toRef = arguments.GetString("toRef");

            if (!IsValidRef(fromRef) || !IsValidRef(toRef))
                return ToolResult.Fail("Некорректная ссылка (ref)");

            try
            {
                var args = new List<string> { "diff", "--no-color" };

                if (staged) args.Add("--cached");

                if (!string.IsNullOrWhiteSpace(fromRef))
                {
                    args.Add(fromRef);
                    if (!string.IsNullOrWhiteSpace(toRef))
                        args.Add(toRef);
                }
                else if (!string.IsNullOrWhiteSpace(toRef))
                {
                    args.Add(toRef);
                }

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
                        $"git diff завершился с кодом {result.ExitCode}: {result.StdErr}");

                return ToolResult.Ok(new
                {
                    diff = result.StdOut ?? string.Empty,
                    truncated = result.Truncated,
                    durationMs = result.DurationMs
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка git diff");
                return ToolResult.Fail($"Ошибка git diff: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверяет допустимость имени ref (ветки/коммита/тега).
        /// </summary>
        /// <param name="value">Значение</param>
        /// <returns>true, если значение допустимо или пусто</returns>
        private static bool IsValidRef(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            if (value.Length > 200) return false;
            if (value.StartsWith("-")) return false;

            foreach (var ch in value)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '/' || ch == '_' || ch == '-' || ch == '.' || ch == '~' || ch == '^'))
                    return false;
            }
            return true;
        }
    }
}