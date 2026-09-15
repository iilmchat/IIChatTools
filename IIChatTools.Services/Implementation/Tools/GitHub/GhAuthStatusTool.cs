using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.GitHub
{
    /// <summary>
    /// Инструмент: проверка статуса авторизации в GitHub CLI.
    /// Команда глобальная, не привязана к конкретному репозиторию,
    /// поэтому параметр path не поддерживается.
    /// </summary>
    public class GhAuthStatusTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GhAuthStatusTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GhAuthStatusTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "gh_auth_status";

        /// <inheritdoc />
        public override string Description =>
            "Проверяет статус авторизации gh CLI и активный аккаунт.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters =>
            Array.Empty<ToolParameterDescriptor>();

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            // Проверяем доступность gh CLI (без привязки к репозиторию)
            if (!await EnsureGhAvailableAsync(context))
                return ToolResult.Fail("GitHub CLI (gh) не установлен в системе");

            try
            {
                // Запускаем gh auth status в корне workspace.
                // Команда не привязана к репозиторию, поэтому workingDir = WorkspaceRoot.
                var result = await RunGhInDirAsync(
                    context.WorkspaceRoot,
                    new[] { "auth", "status" },
                    context.CancellationToken);

                return ToolResult.Ok(new
                {
                    authenticated = result.ExitCode == 0,
                    stdout = result.StdOut ?? string.Empty,
                    stderr = result.StdErr ?? string.Empty,
                    exitCode = result.ExitCode
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка gh auth status");
                return ToolResult.Fail($"Ошибка gh auth status: {ex.Message}");
            }
        }
    }
}