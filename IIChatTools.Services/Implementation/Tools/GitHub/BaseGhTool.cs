using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.GitHub
{
    /// <summary>
    /// Результат проверки контекста GitHub-инструмента.
    /// Содержит рабочий каталог при успехе или ошибку.
    /// </summary>
    public class GhContextValidationResult
    {
        /// <summary>
        /// Рабочий каталог (абсолютный путь).
        /// Заполнен только при успешной валидации.
        /// </summary>
        public string WorkingDir { get; set; }

        /// <summary>
        /// Результат с ошибкой (null при успехе).
        /// </summary>
        public ToolResult Error { get; set; }

        /// <summary>
        /// Признак успешной валидации.
        /// </summary>
        public bool IsSuccess => Error == null;
    }

    /// <summary>
    /// Базовый класс для всех инструментов GitHub CLI.
    /// Поддерживает работу в подкаталогах через параметр path.
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
        public abstract Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments);

        /// <summary>
        /// Описание общего параметра path для GitHub-инструментов.
        /// </summary>
        protected static ToolParameterDescriptor PathParameter => new ToolParameterDescriptor
        {
            Name = "path",
            Type = "string",
            Description = "Относительный путь к каталогу репозитория внутри workspace (по умолчанию корень workspace).",
            Required = false
        };

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
        /// Разрешает рабочий каталог из аргументов (параметр path) и проверяет наличие .git.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="arguments">Аргументы вызова</param>
        /// <param name="workingDir">Результирующий абсолютный путь каталога</param>
        /// <param name="error">Сообщение об ошибке</param>
        /// <returns>true, если каталог валиден</returns>
        protected bool TryResolveWorkingDirectory(
            ToolExecutionContext context,
            JObject arguments,
            out string workingDir,
            out string error)
        {
            workingDir = null;
            error = null;

            if (context == null)
            {
                error = "Контекст выполнения не задан";
                return false;
            }

            var pathArg = arguments?.GetString("path");

            if (string.IsNullOrWhiteSpace(pathArg))
            {
                workingDir = context.WorkspaceRoot;
            }
            else
            {
                if (!PathHelper.TryGetSafeFullPath(pathArg, context.WorkspaceRoot, out var safePath))
                {
                    error = "Недопустимый путь или выход за пределы рабочего пространства";
                    return false;
                }

                if (!Directory.Exists(safePath))
                {
                    error = $"Каталог не найден: {pathArg}";
                    return false;
                }

                workingDir = safePath;
            }

            return true;
        }

        /// <summary>
        /// Запускает gh-команду в указанной рабочей директории.
        /// </summary>
        /// <param name="workingDir">Рабочая директория</param>
        /// <param name="args">Аргументы gh (без слова "gh")</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Результат выполнения</returns>
        /// <exception cref="ArgumentNullException">Если args равен null</exception>
        protected async Task<ProcessResult> RunGhInDirAsync(
            string workingDir,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            return await ProcessRunner.RunAsync(
                "gh",
                args,
                workingDir,
                GetGhTimeoutSeconds(),
                GetMaxOutputBytes(),
                cancellationToken);
        }

        /// <summary>
        /// Проверяет доступность gh и разрешает рабочий каталог.
        /// </summary>
        /// <param name="context">Контекст</param>
        /// <param name="arguments">Аргументы</param>
        /// <returns>Результат валидации</returns>
        protected async Task<GhContextValidationResult> ValidateGhContextAsync(
            ToolExecutionContext context,
            JObject arguments)
        {
            var result = new GhContextValidationResult();

            if (!await EnsureGhAvailableAsync(context))
            {
                result.Error = ToolResult.Fail("GitHub CLI (gh) не установлен в системе");
                return result;
            }

            if (!TryResolveWorkingDirectory(context, arguments, out var workingDir, out var error))
            {
                result.Error = ToolResult.Fail(error);
                return result;
            }

            result.WorkingDir = workingDir;
            return result;
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
    }
}