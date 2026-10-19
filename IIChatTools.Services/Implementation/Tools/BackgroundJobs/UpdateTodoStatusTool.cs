using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.BackgroundJobs
{
    /// <summary>
    /// Tool: обновить статус пункта плана (v2.1.0, Фаза 5).
    /// </summary>
    public class UpdateTodoStatusTool : ITool
    {
        private readonly ITodoService _todoService;

        /// <summary>
        /// Создаёт tool.
        /// </summary>
        /// <param name="todoService">Сервис плана задач.</param>
        public UpdateTodoStatusTool(ITodoService todoService)
        {
            _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        }

        /// <inheritdoc />
        public string Name => "update_todo_status";

        /// <inheritdoc />
        public string Description =>
            "Меняет статус пункта плана текущего чата: Pending, InProgress, " +
            "Completed. Вызывай после завершения шага.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "todoId",
                Type = "integer",
                Description = "Идентификатор пункта плана.",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "status",
                Type = "string",
                Description = "Pending | InProgress | Completed.",
                Required = true
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            if (!context.ChatId.HasValue || context.ChatId.Value <= 0)
                return ToolResult.Fail("Todo доступны только в чате.");

            var todoId = arguments["todoId"]?.Value<int?>() ?? 0;
            var status = arguments.GetString("status");

            if (todoId <= 0)
                return ToolResult.Fail("Параметр 'todoId' обязателен.");
            if (string.IsNullOrWhiteSpace(status))
                return ToolResult.Fail("Параметр 'status' обязателен.");

            try
            {
                // Todo должен принадлежать именно текущему чату,
                // а не просто пользователю (у пользователя много чатов).
                var todos = await _todoService.GetForChatAsync(
                    context.ChatId.Value, context.UserId);
                if (todos.All(t => t.Id != todoId))
                    return ToolResult.Fail("Задача не найдена в текущем чате.");

                await _todoService.UpdateStatusAsync(todoId, context.UserId, status);

                return ToolResult.Ok(
                    new { todoId, status },
                    $"Статус задачи {todoId}: {status}.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return ToolResult.Fail(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return ToolResult.Fail(ex.Message);
            }
        }
    }
}
