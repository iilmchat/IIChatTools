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

        // ============================================================
        // v1.7.0 (KI-101): IToolRegistry.GetTool + ITool.RequiresApprovalForCall
        // ============================================================

        /// <summary>
        /// <c>GetTool</c> возвращает тот же экземпляр <see cref="ITool"/>,
        /// что был зарегистрирован (не копию).
        /// </summary>
        [Fact]
        public void GetTool_Registered_ReturnsSameInstance()
        {
            var tool = new FakeTool { Name = "fake_tool" };
            var registry = new ToolRegistry(new List<ITool> { tool },
                NullLogger<ToolRegistry>.Instance);

            var result = registry.GetTool("fake_tool");

            Assert.NotNull(result);
            Assert.Same(tool, result);
        }

        /// <summary>
        /// <c>GetTool</c> возвращает <c>null</c> для неизвестного имени
        /// (для пустой строки и для null).
        /// </summary>
        [Theory]
        [InlineData("no_such_tool")]
        [InlineData("")]
        [InlineData(null)]
        public void GetTool_Unknown_ReturnsNull(string name)
        {
            var registry = new ToolRegistry(
                new List<ITool> { new FakeTool() },
                NullLogger<ToolRegistry>.Instance);

            Assert.Null(registry.GetTool(name));
        }

        /// <summary>
        /// Default-реализация <see cref="ITool.RequiresApprovalForCall"/>
        /// возвращает <see cref="ITool.RequiresApprovalByDefault"/> —
        /// для инструментов без per-call логики поведение не меняется.
        /// </summary>
        [Fact]
        public void RequiresApprovalForCall_DefaultImplementation_ReturnsRequiresApprovalByDefault()
        {
            // ВАЖНО (C# 8+ default interface methods): метод, объявленный
            // в интерфейсе с default-реализацией, НЕ виден через конкретный тип
            // класса, который его не переопределил. Доступен только через
            // интерфейсную переменную. Объявляем как ITool — иначе CS1061.
            // (DatabaseAgentTool override'ит метод, поэтому там вызов на конкретном
            // типе работает — RULES § 4.46.)
            ITool toolFalse = new FakeToolWithApproval { Name = "nope", Approval = false };
            ITool toolTrue = new FakeToolWithApproval { Name = "yep", Approval = true };

            // FakeToolWithApproval не переопределяет RequiresApprovalForCall —
            // работает default interface method.
            Assert.False(toolFalse.RequiresApprovalForCall(new JObject()));
            Assert.True(toolTrue.RequiresApprovalForCall(new JObject()));
        }

        /// <summary>
        /// Fake-инструмент с настраиваемым <see cref="RequiresApprovalByDefault"/>.
        /// </summary>
        private class FakeToolWithApproval : ITool
        {
            public string Name { get; set; } = "fake_with_approval";
            public string Description => "Тестовый инструмент с настраиваемым approval";
            public bool Approval { get; set; }
            public bool RequiresApprovalByDefault => Approval;
            public IReadOnlyList<ToolParameterDescriptor> Parameters
                => new List<ToolParameterDescriptor>();

            public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
                => Task.FromResult(ToolResult.Ok(new { ok = true }));
        }
    }
}