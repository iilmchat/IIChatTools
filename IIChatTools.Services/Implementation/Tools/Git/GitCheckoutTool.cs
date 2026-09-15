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
    /// Инструмент: переключение веток и восстановление файлов (git checkout).
    /// </summary>
    public class GitCheckoutTool : BaseGitTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GitCheckoutTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GitCheckoutTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "git_checkout";

        /// <inheritdoc />
        public override string Description =>
            "Переключает ветку или восстанавливает файлы из указанного коммита.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "target",
                Type = "string",
                Description = "Ветка или ref (например, main, feature/x, HEAD~1).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "createBranch",
                Type = "bool",
                Description = "Создать новую ветку (git checkout -b).",
                Required = false,
                Default = false
            },
            new ToolParameterDescriptor
            {
                Name = "files",
                Type = "array",
                Description = "Опционально: восстановить только указанные пути.",
                Required = false
            }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGitContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

            var target = arguments.GetString("target");
            if (string.IsNullOrWhiteSpace(target))
                return ToolResult.Fail("Не указана цель (branch/ref)");
            if (!IsValidRef(target))
                return ToolResult.Fail("Некорректное имя ветки/ref");

            var createBranch = arguments.GetBool("createBranch");
            var files = arguments.GetStringArray("files");

            try
            {
                var args = new List<string> { "checkout" };

                if (createBranch) args.Add("-b");
                args.Add(target);

                if (files.Count > 0)
                {
                    args.Add("--");
                    foreach (var f in files)
                    {
                        if (string.IsNullOrWhiteSpace(f)) continue;

                        if (!PathHelper.TryGetSafeFullPath(f, workingDir, out _))
                            return ToolResult.Fail($"Недопустимый путь файла: {f}");

                        args.Add(f);
                    }
                }

                var result = await RunGitInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                    return ToolResult.Fail(
                        $"git checkout завершился с кодом {result.ExitCode}: {result.StdErr}");

                return ToolResult.Ok(new
                {
                    checkedOut = true,
                    target,
                    stdout = result.StdOut ?? string.Empty,
                    stderr = result.StdErr ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка git checkout");
                return ToolResult.Fail($"Ошибка git checkout: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверяет допустимость имени ref.
        /// </summary>
        /// <param name="value">Значение</param>
        /// <returns>true, если значение допустимо</returns>
        private static bool IsValidRef(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
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