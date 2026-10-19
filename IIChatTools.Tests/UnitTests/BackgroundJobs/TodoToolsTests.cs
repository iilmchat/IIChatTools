using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Implementation.Tools.BackgroundJobs;
using IIChatTools.Services.Interfaces;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.BackgroundJobs
{
    /// <summary>
    /// Unit-тесты Todo-tools (v2.1.0, Фаза 5).
    /// </summary>
    public class TodoToolsTests
    {
        /// <summary>Todo-tools без ChatId → Fail.</summary>
        [Fact]
        public async Task AddTodo_WithoutChatId_Fails()
        {
            var service = new Mock<ITodoService>();
            var tool = new AddTodoTool(service.Object);
            var context = new ToolExecutionContext { UserId = 1, ChatId = null };

            var result = await tool.ExecuteAsync(context, new JObject { ["title"] = "План" });

            Assert.False(result.Success);
            Assert.Contains("чате", result.Message);
        }

        /// <summary>AddTodo возвращает todoId/status/sortOrder.</summary>
        [Fact]
        public async Task AddTodo_WithChat_ReturnsCreatedTodo()
        {
            var service = new Mock<ITodoService>();
            service.Setup(s => s.CreateAsync(7, 1, "План", null))
                .ReturnsAsync(new ChatTodo
                {
                    Id = 11,
                    ChatId = 7,
                    Title = "План",
                    Status = "Pending",
                    SortOrder = 0
                });

            var tool = new AddTodoTool(service.Object);
            var context = new ToolExecutionContext { UserId = 1, ChatId = 7 };
            var result = await tool.ExecuteAsync(context, new JObject { ["title"] = "План" });

            Assert.True(result.Success);
            var json = JObject.FromObject(result.Data);
            Assert.Equal(11, json["todoId"]?.Value<int>());
            Assert.Equal("Pending", json["status"]?.ToString());
        }

        /// <summary>ListTodos с недопустимым статусом → Fail.</summary>
        [Fact]
        public async Task ListTodos_InvalidStatus_Fails()
        {
            var service = new Mock<ITodoService>();
            var tool = new ListTodosTool(service.Object);
            var context = new ToolExecutionContext { UserId = 1, ChatId = 7 };

            var result = await tool.ExecuteAsync(context, new JObject { ["status"] = "Done" });

            Assert.False(result.Success);
            Assert.Contains("Недопустимый статус", result.Message);
        }

        /// <summary>UpdateTodoStatus проверяет принадлежность todo текущему чату.</summary>
        [Fact]
        public async Task UpdateTodoStatus_ForeignTodo_Fails()
        {
            var service = new Mock<ITodoService>();
            service.Setup(s => s.GetForChatAsync(7, 1))
                .ReturnsAsync(new List<ChatTodo>
                {
                    new ChatTodo { Id = 1, ChatId = 7, Title = "Чужой", Status = "Pending" }
                });

            var tool = new UpdateTodoStatusTool(service.Object);
            var context = new ToolExecutionContext { UserId = 1, ChatId = 7 };

            var result = await tool.ExecuteAsync(context, new JObject
            {
                ["todoId"] = 999,
                ["status"] = "Completed"
            });

            Assert.False(result.Success);
            Assert.Contains("не найдена", result.Message);
        }
    }
}
