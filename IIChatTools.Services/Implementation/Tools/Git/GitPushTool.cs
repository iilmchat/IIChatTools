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
    /// Инструмент: отправка изменений в удалённый репозиторий (git push).
    /// </summary>
    public class GitPushTool : BaseGitTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GitPushTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GitPushTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "git_push";

        /// <inheritdoc />
        public override string Description =>
            "Отправляет локальные коммиты в удалённый репозиторий.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "remote",
                Type = "string",
                Description = "Имя remote (по умолчанию origin).",
                Required = false,
                Default = "origin"
            },
            new ToolParameterDescriptor
            {
                Name = "branch",
                Type = "string",
                Description = "Имя ветки (по умолчанию текущая).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "setUpstream",
                Type = "bool",
                Description = "Установить upstream (-u).",
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

            var remote = arguments.GetString("remote", "origin");
            var branch = arguments.GetString("branch");
            var setUpstream = arguments.GetBool("setUpstream");

            if (!IsValidName(remote))
                return ToolResult.Fail("Некорректное имя remote");
            if (!string.IsNullOrWhiteSpace(branch) && !IsValidName(branch))
                return ToolResult.Fail("Некорректное имя ветки");

            try
            {
                var args = new List<string> { "push" };
                if (setUpstream) args.Add("-u");
                args.Add(remote);
                if (!string.IsNullOrWhiteSpace(branch)) args.Add(branch);

                var result = await RunGitInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                    return ToolResult.Fail(
                        $"git push завершился с кодом {result.ExitCode}: {result.StdErr}");

                return ToolResult.Ok(new
                {
                    pushed = true,
                    remote,
                    branch,
                    stdout = result.StdOut ?? string.Empty,
                    stderr = result.StdErr ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка git push");
                return ToolResult.Fail($"Ошибка git push: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверяет допустимость имени remote/ветки.
        /// </summary>
        /// <param name="value">Значение</param>
        /// <returns>true, если значение допустимо</returns>
        private static bool IsValidName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.Length > 200) return false;
            if (value.StartsWith("-")) return false;

            foreach (var ch in value)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '/' || ch == '_' || ch == '-' || ch == '.'))
                    return false;
            }
            return true;
        }
    }
}