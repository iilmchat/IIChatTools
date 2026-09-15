using System;
using System.Linq;
using System.Collections.Generic;
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
    /// Инструмент: создание Pull Request через gh.
    /// Поддерживает работу в подкаталогах через параметр path.
    /// </summary>
    public class GhCreatePrTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GhCreatePrTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GhCreatePrTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "gh_create_pr";

        /// <inheritdoc />
        public override string Description =>
            "Создаёт Pull Request в GitHub из текущей ветки. " +
            "Параметр path указывает каталог репозитория внутри workspace.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor
            {
                Name = "title",
                Type = "string",
                Description = "Заголовок PR (максимум 500 символов).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "body",
                Type = "string",
                Description = "Тело PR (Markdown, максимум 60 000 символов).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "base",
                Type = "string",
                Description = "Целевая ветка (например, main).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "head",
                Type = "string",
                Description = "Исходная ветка (по умолчанию текущая).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "draft",
                Type = "bool",
                Description = "Создать как черновик.",
                Required = false,
                Default = false
            }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

            var title = arguments.GetString("title");
            var body = arguments.GetString("body");
            var baseBranch = arguments.GetString("base");
            var headBranch = arguments.GetString("head");
            var draft = arguments.GetBool("draft");

            if (string.IsNullOrWhiteSpace(title))
                return ToolResult.Fail("Не указан заголовок PR");
            if (title.Length > 500)
                return ToolResult.Fail("Заголовок превышает 500 символов");
            if (body != null && body.Length > 60000)
                return ToolResult.Fail("Тело PR превышает 60 000 символов");

            try
            {
                var args = new List<string> { "pr", "create", "--title", title };

                if (!string.IsNullOrWhiteSpace(body))
                {
                    args.Add("--body");
                    args.Add(body);
                }
                if (!string.IsNullOrWhiteSpace(baseBranch))
                {
                    if (!IsValidBranch(baseBranch))
                        return ToolResult.Fail("Некорректное имя базовой ветки");
                    args.Add("--base");
                    args.Add(baseBranch);
                }
                if (!string.IsNullOrWhiteSpace(headBranch))
                {
                    if (!IsValidBranch(headBranch))
                        return ToolResult.Fail("Некорректное имя исходной ветки");
                    args.Add("--head");
                    args.Add(headBranch);
                }
                if (draft) args.Add("--draft");

                var result = await RunGhInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                    return ToolResult.Fail(
                        $"gh pr create завершился с кодом {result.ExitCode}: {result.StdErr}");

                return ToolResult.Ok(new
                {
                    created = true,
                    url = result.StdOut?.Trim(),
                    stdout = result.StdOut ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка gh pr create");
                return ToolResult.Fail($"Ошибка создания PR: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверяет корректность имени ветки.
        /// </summary>
        /// <param name="value">Имя ветки</param>
        /// <returns>true, если допустимо</returns>
        private static bool IsValidBranch(string value)
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