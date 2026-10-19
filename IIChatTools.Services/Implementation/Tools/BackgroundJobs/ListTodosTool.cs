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
    /// Tool: список пунктов плана текущего чата (v2.1.0, Фаза 5).
    /// </summary>
    public class ListTodosTool : ITool
    {
        private static readonly string[] AllowedStatuses =
        {
            "Pending", "InProgress", "Completed"
        };

        private readonly ITodoService _todoService;

        /// <summary>
        /// Создаёт tool.
        /// </summary>
        /// <param name="todoService">Сервис плана задач.</param>
        public ListTodosTool(ITodoService todoService)
        {
            _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        }

        /// <inheritdoc />
        public string Name => "list_todos";

        /// <inheritdoc />
        public string Description =>
            "Возвращает план задач текущего чата (или только выбранный статус). " +
            "Вызывай перед add_todo, чтобы не дублировать пункты.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "status",
                Type = "string",
                Description = "Опциональный фильтр: Pending | InProgress | Completed.",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            if (!context.ChatId.HasValue || context.ChatId.Value <= 0)
                return ToolResult.Fail("Todo доступны только в чате.");

            var filter = arguments.GetString("status");
            string canonicalFilter = null;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                canonicalFilter = AllowedStatuses.FirstOrDefault(
                    s => string.Equals(s, filter.Trim(), StringComparison.OrdinalIgnoreCase));

                if (canonicalFilter == null)
                    return ToolResult.Fail(
                        "Недопустимый статус. Допустимые: Pending, InProgress, Completed.");
            }

            try
            {
                var todos = await _todoService.GetForChatAsync(
                    context.ChatId.Value, context.UserId);

                var items = todos
                    .Where(t => canonicalFilter == null ||
                                string.Equals(t.Status, canonicalFilter, StringComparison.Ordinal))
                    .Select(t => new
                    {
                        todoId = t.Id,
                        title = t.Title,
                        description = t.Description,
                        status = t.Status,
                        order = t.SortOrder,
                        completedAt = t.CompletedAt
                    })
                    .ToList();

                return ToolResult.Ok(items, $"Пунктов плана: {items.Count}.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return ToolResult.Fail(ex.Message);
            }
        }
    }
}
