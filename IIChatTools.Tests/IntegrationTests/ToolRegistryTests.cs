using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты реестра инструментов.
    /// </summary>
    public class ToolRegistryTests
    {
        /// <summary>
        /// Фиктивный инструмент для тестов.
        /// </summary>
        private class FakeTool : ITool
        {
            public string Name { get; set; } = "fake_tool";
            public string Description => "Тестовый инструмент";
            public bool RequiresApprovalByDefault => false;
            public IReadOnlyList<ToolParameterDescriptor> Parameters => new List<ToolParameterDescriptor>();
            public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
                => Task.FromResult(ToolResult.Ok(new { ok = true }));
        }

        /// <summary>
        /// Проверяет, что инструменты корректно регистрируются.
        /// </summary>
        [Fact]
        public void GetAllDescriptors_ReturnsRegisteredTools()
        {
            var tools = new List<ITool>
            {
                new FakeTool { Name = "tool_a" },
                new FakeTool { Name = "tool_b" }
            };

            var registry = new ToolRegistry(tools, NullLogger<ToolRegistry>.Instance);
            var descriptors = registry.GetAllDescriptors();

            Assert.Equal(2, descriptors.Count);
        }

        /// <summary>
        /// Проверяет, что дубликаты имён игнорируются.
        /// </summary>
        [Fact]
        public void Constructor_DuplicateNames_KeepsFirst()
        {
            var tools = new List<ITool>
            {
                new FakeTool { Name = "dup" },
                new FakeTool { Name = "dup" }
            };

            var registry = new ToolRegistry(tools, NullLogger<ToolRegistry>.Instance);
            Assert.Single(registry.GetAllDescriptors());
        }

        /// <summary>
        /// Проверяет выполнение известного инструмента.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_KnownTool_ReturnsResult()
        {
            var tools = new List<ITool> { new FakeTool() };
            var registry = new ToolRegistry(tools, NullLogger<ToolRegistry>.Instance);

            var context = new ToolExecutionContext { UserId = 1, WorkspaceRoot = "/tmp" };
            var result = await registry.ExecuteAsync("fake_tool", context, new JObject());

            Assert.True(result.Success);
        }

        /// <summary>
        /// Проверяет ошибку при неизвестном инструменте.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_UnknownTool_ReturnsFail()
        {
            var registry = new ToolRegistry(new List<ITool>(), NullLogger<ToolRegistry>.Instance);
            var result = await registry.ExecuteAsync("no_such_tool",
                new ToolExecutionContext { UserId = 1, WorkspaceRoot = "/tmp" },
                new JObject());

            Assert.False(result.Success);
            Assert.Contains("не зарегистрирован", result.Message);
        }
    }
}