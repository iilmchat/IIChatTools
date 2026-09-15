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
    /// Инструмент: создание issue в GitHub через gh.
    /// </summary>
    public class GhCreateIssueTool : BaseGhTool
    {
        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="processRunner">Исполнитель процессов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public GhCreateIssueTool(
            IProcessRunner processRunner,
            IConfiguration configuration,
            ILogger<GhCreateIssueTool> logger)
            : base(processRunner, configuration, logger)
        {
        }

        /// <inheritdoc />
        public override string Name => "gh_create_issue";

        /// <inheritdoc />
        public override string Description =>
            "Создаёт issue в текущем репозитории через GitHub CLI. " +
            "Параметр path указывает каталог репозитория внутри workspace. " +
            "Метки (labels) должны быть созданы в репозитории заранее — gh не создаёт их автоматически.";

        /// <inheritdoc />
        public override bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public override IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            PathParameter,
            new ToolParameterDescriptor { Name = "title", Type = "string", Description = "Заголовок issue.", Required = true },
            new ToolParameterDescriptor { Name = "body", Type = "string", Description = "Тело issue (Markdown).", Required = false },
            new ToolParameterDescriptor { Name = "labels", Type = "array", Description = "Список меток.", Required = false }
        };

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var validation = await ValidateGhContextAsync(context, arguments);
            if (!validation.IsSuccess) return validation.Error;
            var workingDir = validation.WorkingDir;

            var title = arguments.GetString("title");
            var body = arguments.GetString("body");
            var labels = arguments.GetStringArray("labels");

            if (string.IsNullOrWhiteSpace(title))
                return ToolResult.Fail("Не указан заголовок issue");
            if (title.Length > 500)
                return ToolResult.Fail("Заголовок превышает 500 символов");
            if (body != null && body.Length > 60000)
                return ToolResult.Fail("Тело issue превышает 60 000 символов");

            try
            {
                var args = new List<string> { "issue", "create", "--title", title };
                if (!string.IsNullOrWhiteSpace(body))
                {
                    args.Add("--body");
                    args.Add(body);
                }

                foreach (var label in labels)
                {
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    if (label.Length > 100) continue;
                    args.Add("--label");
                    args.Add(label);
                }

                var result = await RunGhInDirAsync(workingDir, args, context.CancellationToken);
                if (result.ExitCode != 0)
                {
                    var stderr = result.StdErr ?? string.Empty;

                    // Улучшаем сообщение для часто встречающихся ошибок gh
                    if (stderr.Contains("could not add label", StringComparison.OrdinalIgnoreCase))
                    {
                        return ToolResult.Fail(
                            $"Не удалось создать issue: одна из указанных меток не существует в репозитории. " +
                            $"Создайте метку заранее через веб-интерфейс GitHub или gh CLI, либо уберите параметр labels. " +
                            $"Детали: {stderr.Trim()}");
                    }

                    if (stderr.Contains("no git remotes found", StringComparison.OrdinalIgnoreCase))
                    {
                        return ToolResult.Fail(
                            "Не удалось создать issue: у репозитория нет настроенного remote origin. " +
                            "Проверьте параметр path — возможно, указан не тот каталог. " +
                            $"Детали: {stderr.Trim()}");
                    }

                    if (stderr.Contains("not logged", StringComparison.OrdinalIgnoreCase) ||
                        stderr.Contains("authentication", StringComparison.OrdinalIgnoreCase))
                    {
                        return ToolResult.Fail(
                            "Не удалось создать issue: gh CLI не авторизован. " +
                            "Выполните 'gh auth login' или проверьте 'gh_auth_status'. " +
                            $"Детали: {stderr.Trim()}");
                    }

                    return ToolResult.Fail($"gh issue create завершился с кодом {result.ExitCode}: {stderr}");
                }

                return ToolResult.Ok(new
                {
                    created = true,
                    url = result.StdOut?.Trim(),
                    stdout = result.StdOut ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка gh issue create");
                return ToolResult.Fail($"Ошибка создания issue: {ex.Message}");
            }
        }
    }
}