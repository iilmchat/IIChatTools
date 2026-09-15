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
    /// Инструмент: создание коммита (git commit -m).
    /// </summary>
    public class GitCommitTool : BaseGitTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GitCommitTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GitCommitTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "git_commit";

        /// <inheritdoc />
        public override string Description =>
            "Создаёт коммит с указанным сообщением. Опционально можно сразу добавить все изменения в индекс.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "message",
                Type = "string",
                Description = "Сообщение коммита (максимум 4000 символов).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "addAll",
                Type = "bool",
                Description = "Перед коммитом выполнить git add -A.",
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

            var message = arguments.GetString("message");
            if (string.IsNullOrWhiteSpace(message))
                return ToolResult.Fail("Не указано сообщение коммита");
            if (message.Length > 4000)
                return ToolResult.Fail("Сообщение коммита превышает 4000 символов");

            var addAll = arguments.GetBool("addAll");

            try
            {
                if (addAll)
                {
                    var addResult = await RunGitInDirAsync(
                        workingDir,
                        new[] { "add", "-A" },
                        context.CancellationToken);

                    if (addResult.ExitCode != 0)
                        return ToolResult.Fail(
                            $"git add -A завершился с кодом {addResult.ExitCode}: {addResult.StdErr}");
                }

                var commitResult = await RunGitInDirAsync(
                    workingDir,
                    new[] { "commit", "-m", message },
                    context.CancellationToken);

                if (commitResult.ExitCode != 0)
                    return ToolResult.Fail(
                        $"git commit завершился с кодом {commitResult.ExitCode}: {commitResult.StdErr}");

                var hashResult = await RunGitInDirAsync(
                    workingDir,
                    new[] { "rev-parse", "HEAD" },
                    context.CancellationToken);

                var hash = hashResult.ExitCode == 0
                    ? hashResult.StdOut?.Trim()
                    : null;

                return ToolResult.Ok(new
                {
                    committed = true,
                    hash,
                    stdout = commitResult.StdOut ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка git commit");
                return ToolResult.Fail($"Ошибка git commit: {ex.Message}");
            }
        }
    }
}