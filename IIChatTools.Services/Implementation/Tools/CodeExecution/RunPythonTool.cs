using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.CodeExecution
{
    /// <summary>
    /// Инструмент: выполнение Python-скрипта через python3.
    /// </summary>
    public class RunPythonTool : ITool
    {
        private readonly IProcessRunner _processRunner;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        public RunPythonTool(IProcessRunner processRunner, IConfiguration configuration)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <inheritdoc />
        public string Name => "run_python";

        /// <inheritdoc />
        public string Description => "Выполняет Python-скрипт через python3 в рабочем пространстве. Возвращает stdout/stderr.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "code", Type = "string", Description = "Python-код для выполнения.", Required = true },
            new ToolParameterDescriptor { Name = "timeoutSeconds", Type = "integer", Description = "Таймаут в секундах (по умолчанию 30).", Required = false, Default = 30 }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            if (_configuration["Security:EnableCodeExecution"]?.ToLowerInvariant() == "false")
                return ToolResult.Fail("Выполнение кода отключено в настройках");

            var code = arguments.GetString("code");
            if (string.IsNullOrWhiteSpace(code))
                return ToolResult.Fail("Не указан код");

            var timeout = arguments.GetInt("timeoutSeconds", 30);
            if (timeout <= 0 || timeout > 300) timeout = 30;

            var maxOutputBytes = 1024L * 1024;
            var rawMax = _configuration["Workspace:MaxCommandOutputBytes"];
            if (!string.IsNullOrWhiteSpace(rawMax) && long.TryParse(rawMax, out var maxOut) && maxOut > 0)
                maxOutputBytes = maxOut;

            var tempDir = Path.Combine(context.WorkspaceRoot, ".tmp");
            if (!Directory.Exists(tempDir))
                Directory.CreateDirectory(tempDir);

            var tempFile = Path.Combine(tempDir, $"run_{Guid.NewGuid():N}.py");
            await File.WriteAllTextAsync(tempFile, code);

            try
            {
                var args = new[] { tempFile };
                var result = await _processRunner.RunAsync(
                    "python3",
                    args,
                    context.WorkspaceRoot,
                    timeout,
                    maxOutputBytes,
                    context.CancellationToken);

                if (result.TimedOut)
                    return ToolResult.Fail($"Выполнение превысило таймаут ({timeout} c)");

                return ToolResult.Ok(new
                {
                    exitCode = result.ExitCode,
                    stdout = result.StdOut ?? string.Empty,
                    stderr = result.StdErr ?? string.Empty,
                    durationMs = result.DurationMs,
                    truncated = result.Truncated
                });
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                catch { /* игнорируем */ }
            }
        }
    }
}