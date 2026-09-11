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
    /// </summary>
    public class GhAuthStatusTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
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
        public override string Description => "Проверяет статус авторизации gh CLI и активный аккаунт.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => Array.Empty<ToolParameterDescriptor>();

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhAsync(context);
            if (validation != null) return validation;

            try
            {
                var result = await RunGhAsync(context, new[] { "auth", "status" });

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