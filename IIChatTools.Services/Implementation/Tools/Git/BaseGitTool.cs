using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.Git
{
    /// <summary>
    /// Базовый класс для всех инструментов Git.
    /// Содержит общую логику проверки репозитория и запуска git-команд.
    /// </summary>
    public abstract class BaseGitTool : ITool
    {
        /// <summary>
        /// Исполнитель процессов.
        /// </summary>
        protected readonly IProcessRunner ProcessRunner;

        /// <summary>
        /// Конфигурация приложения.
        /// </summary>
        protected readonly IConfiguration Configuration;

        /// <summary>
        /// Логгер инструмента.
        /// </summary>
        protected readonly ILogger Logger;

        /// <summary>
        /// Создаёт экземпляр базового класса.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        protected BaseGitTool(
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
        /// Проверяет, что в рабочем пространстве есть Git-репозиторий.
        /// </summary>
        /// <param name="workspaceRoot">Корень рабочего пространства</param>
        /// <returns>true, если найден каталог .git</returns>
        protected static bool IsGitRepository(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot)) return false;
            return Directory.Exists(Path.Combine(workspaceRoot, ".git"));
        }

        /// <summary>
        /// Возвращает значение таймаута для git-команд из конфигурации.
        /// </summary>
        /// <returns>Таймаут в секундах</returns>
        protected int GetGitTimeoutSeconds()
        {
            var raw = Configuration["Security:GitTimeoutSeconds"];
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var v) && v > 0)
                return v;
            return 60;
        }

        /// <summary>
        /// Возвращает лимит размера вывода команды из конфигурации.
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
        /// Запускает git-команду с указанными аргументами в рабочем пространстве.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="args">Аргументы git-команды (без слова "git")</param>
        /// <returns>Результат выполнения</returns>
        /// <exception cref="ArgumentNullException">Если context или args равны null</exception>
        protected async Task<ProcessResult> RunGitAsync(ToolExecutionContext context, IReadOnlyList<string> args)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (args == null) throw new ArgumentNullException(nameof(args));

            return await ProcessRunner.RunAsync(
                "git",
                args,
                context.WorkspaceRoot,
                GetGitTimeoutSeconds(),
                GetMaxOutputBytes(),
                context.CancellationToken);
        }

        /// <summary>
        /// Проверяет, что git CLI доступен в системе.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <returns>true, если git доступен</returns>
        protected async Task<bool> EnsureGitAvailableAsync(ToolExecutionContext context)
        {
            var result = await ProcessRunner.RunAsync(
                "git",
                new[] { "--version" },
                context.WorkspaceRoot,
                10,
                64 * 1024,
                context.CancellationToken);
            return result.ExitCode == 0;
        }

        /// <summary>
        /// Проверяет репозиторий и доступность git. Возвращает ошибку ToolResult при неудаче или null при успехе.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <returns>ToolResult с ошибкой или null</returns>
        protected async Task<ToolResult> ValidateGitContextAsync(ToolExecutionContext context)
        {
            if (!await EnsureGitAvailableAsync(context))
                return ToolResult.Fail("git CLI не установлен в системе");

            if (!IsGitRepository(context.WorkspaceRoot))
                return ToolResult.Fail("В рабочем пространстве не найден Git-репозиторий (отсутствует каталог .git)");

            return null;
        }
    }
}