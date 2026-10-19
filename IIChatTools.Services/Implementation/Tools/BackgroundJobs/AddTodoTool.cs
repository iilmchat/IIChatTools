using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.BackgroundJobs
{
    /// <summary>
    /// Tool: добавить пункт плана в текущий чат (v2.1.0, Фаза 5).
    /// </summary>
    public class AddTodoTool : ITool
    {
        private readonly ITodoService _todoService;

        /// <summary>
        /// Создаёт tool.
        /// </summary>
        /// <param name="todoService">Сервис плана задач.</param>
        public AddTodoTool(ITodoService todoService)
        {
            _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        }

        /// <inheritdoc />
        public string Name => "add_todo";

        /// <inheritdoc />
        public string Description =>
            "Добавляет пункт плана в текущий чат. Используй для сложных задач " +
            "(3+ шагов), чтобы пользователь видел прогресс. НЕ добавляй todo " +
            "для простых одношаговых вопросов.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "title",
                Type = "string",
                Description = "Короткий заголовок задачи (1..200).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "description",
                Type = "string",
                Description = "Опциональное описание (≤ 2000).",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            if (!context.ChatId.HasValue || context.ChatId.Value <= 0)
                return ToolResult.Fail("Todo доступны только в чате.");

            var title = arguments.GetString("title");
            var description = arguments.GetString("description");

            try
            {
                var todo = await _todoService.CreateAsync(
                    context.ChatId.Value, context.UserId, title, description);

                return ToolResult.Ok(new
                {
                    todoId = todo.Id,
                    title = todo.Title,
                    status = todo.Status,
                    sortOrder = todo.SortOrder
                }, $"Добавлен пункт плана: «{todo.Title}».");
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
