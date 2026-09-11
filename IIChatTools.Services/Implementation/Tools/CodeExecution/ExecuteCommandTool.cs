using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.CodeExecution
{
    /// <summary>
    /// Инструмент: выполнение shell-команды через передачу имени команды и списка аргументов.
    /// </summary>
    public class ExecuteCommandTool : ITool
    {
        private static readonly HashSet<string> AllowedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "git", "gh", "dotnet", "node", "npm", "npx", "python3", "pip3"
        };

        private readonly IProcessRunner _processRunner;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        public ExecuteCommandTool(IProcessRunner processRunner, IConfiguration configuration)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <inheritdoc />
        public string Name => "execute_command";

        /// <inheritdoc />
        public string Description => "Выполняет разрешённую команду в рабочем пространстве. Разрешены: git, gh, dotnet, node, npm, npx, python3, pip3.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "command", Type = "string", Description = "Имя исполняемой команды (без аргументов).", Required = true },
            new ToolParameterDescriptor { Name = "args", Type = "array", Description = "Массив аргументов команды.", Required = false },
            new ToolParameterDescriptor { Name = "timeoutSeconds", Type = "integer", Description = "Таймаут в секундах (по умолчанию 60).", Required = false, Default = 60 }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            if (_configuration["Security:EnableShellCommands"]?.ToLowerInvariant() == "false")
                return ToolResult.Fail("Выполнение shell-команд отключено в настройках");

            var command = arguments.GetString("command");
            if (string.IsNullOrWhiteSpace(command))
                return ToolResult.Fail("Не указана команда");

            // Отбрасываем путь — сверяем только имя
            var commandName = System.IO.Path.GetFileName(command);
            if (!AllowedCommands.Contains(commandName))
                return ToolResult.Fail($"Команда '{commandName}' не входит в белый список разрешённых");

            var args = arguments.GetStringArray("args").ToArray();
            var timeout = arguments.GetInt("timeoutSeconds", 60);
            if (timeout <= 0 || timeout > 600) timeout = 60;

            var maxOutputBytes = 1024L * 1024;
            var rawMax = _configuration["Workspace:MaxCommandOutputBytes"];
            if (!string.IsNullOrWhiteSpace(rawMax) && long.TryParse(rawMax, out var maxOut) && maxOut > 0)
                maxOutputBytes = maxOut;

            var result = await _processRunner.RunAsync(
                commandName,
                args,
                context.WorkspaceRoot,
                timeout,
                maxOutputBytes,
                context.CancellationToken);

            if (result.TimedOut)
                return ToolResult.Fail($"Команда превысила таймаут ({timeout} c)");

            return ToolResult.Ok(new
            {
                command = commandName,
                exitCode = result.ExitCode,
                stdout = result.StdOut ?? string.Empty,
                stderr = result.StdErr ?? string.Empty,
                durationMs = result.DurationMs,
                truncated = result.Truncated
            });
        }
    }
}