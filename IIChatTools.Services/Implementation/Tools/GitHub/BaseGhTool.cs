using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.GitHub
{
    /// <summary>
    /// Базовый класс для всех инструментов GitHub CLI.
    /// </summary>
    public abstract class BaseGhTool : ITool
    {
        /// <summary>
        /// Исполнитель процессов.
        /// </summary>
        protected readonly IProcessRunner ProcessRunner;

        /// <summary>
        /// Конфигурация.
        /// </summary>
        protected readonly IConfiguration Configuration;

        /// <summary>
        /// Логгер.
        /// </summary>
        protected readonly ILogger Logger;

        /// <summary>
        /// Создаёт экземпляр базового класса.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        protected BaseGhTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger logger)
        {
            ProcessRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract string Description { get; }

        /// <inheritdoc />
        public abstract bool RequiresApprovalByDefault { get; }

        /// <inheritdoc />
        public abstract IReadOnlyList<ToolParameterDescriptor> Parameters { get; }

        /// <inheritdoc />
        public abstract Task<ToolResult> ExecuteAsync(ToolExecutionContext context, Newtonsoft.Json.Linq.JObject arguments);

        /// <summary>
        /// Возвращает таймаут для gh-команд (по умолчанию 90 секунд).
        /// </summary>
        /// <returns>Таймаут в секундах</returns>
        protected int GetGhTimeoutSeconds()
        {
            var raw = Configuration["Security:GhTimeoutSeconds"];
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var v) && v > 0)
                return v;
            return 90;
        }

        /// <summary>
        /// Возвращает лимит размера вывода.
        /// </summary>
        /// <returns>Лимит в байтах</returns>
        protected long GetMaxOutputBytes()
        {
            var raw = Configuration["Workspace:MaxCommandOutputBytes"];
            if (!string.IsNullOrWhiteSpace(raw) && long.TryParse(raw, out var v) && v > 0)
                return v;
            return 1024L * 1024;
        }

        /// <summary>
        /// Запускает gh-команду.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="args">Аргументы gh (без слова "gh")</param>
        /// <returns>Результат выполнения</returns>
        protected async Task<ProcessResult> RunGhAsync(ToolExecutionContext context, IReadOnlyList<string> args)
        {
            return await ProcessRunner.RunAsync(
                "gh",
                args,
                context.WorkspaceRoot,
                GetGhTimeoutSeconds(),
                GetMaxOutputBytes(),
                context.CancellationToken);
        }

        /// <summary>
        /// Проверяет доступность gh CLI.
        /// </summary>
        /// <param name="context">Контекст</param>
        /// <returns>true, если gh доступен</returns>
        protected async Task<bool> EnsureGhAvailableAsync(ToolExecutionContext context)
        {
            var result = await ProcessRunner.RunAsync(
                "gh",
                new[] { "--version" },
                context.WorkspaceRoot,
                10,
                64 * 1024,
                context.CancellationToken);
            return result.ExitCode == 0;
        }

        /// <summary>
        /// Проверяет наличие gh. При отсутствии возвращает ToolResult с ошибкой.
        /// </summary>
        /// <param name="context">Контекст</param>
        /// <returns>ToolResult с ошибкой или null</returns>
        protected async Task<ToolResult> ValidateGhAsync(ToolExecutionContext context)
        {
            if (!await EnsureGhAvailableAsync(context))
                return ToolResult.Fail("GitHub CLI (gh) не установлен в системе");
            return null;
        }
    }
}