using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Implementation.Tools.SubAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты базового класса <see cref="AgentToolBase"/> (v1.4.0 Фаза 7, KI-052).
    ///
    /// Проверяют:
    /// - валидацию task (пустая, слишком длинная);
    /// - резолв дескриптора из реестра (unknown, disabled);
    /// - передачу <see cref="SubAgentTaskRequest"/> в <see cref="ISubAgentService"/>
    ///   с override SystemPrompt/Model/AllowedTools/MaxSteps;
    /// - clamp maxSteps к верхней границе (30).
    /// </summary>
    public class AgentToolBaseTests
    {
        // ============================================================
        // Fake-зависимости
        // ============================================================

        /// <summary>
        /// Fake <see cref="ISubAgentService"/> — запоминает последний request,
        /// возвращает заранее заданный результат. Не вызывает реальный LM Studio.
        /// </summary>
        private sealed class FakeSubAgentService : ISubAgentService
        {
            /// <summary>Счётчик вызовов (для проверки «не вызван»).</summary>
            public int CallCount { get; private set; }

            /// <summary>Последний полученный request (для ассертов).</summary>
            public SubAgentTaskRequest LastRequest { get; private set; }

            /// <summary>Последний полученный контекст (для ассертов).</summary>
            public ToolExecutionContext LastContext { get; private set; }

            /// <summary>Что вернуть на следующий вызов.</summary>
            public SubAgentTaskResult NextResult { get; set; } = new SubAgentTaskResult
            {
                SessionId = "test-session-1",
                FinalAnswer = "Test answer",
                Completed = true,
                Steps = 3,
                DurationMs = 1234,
                UsedTools = new List<string> { "list_directory" }
            };

            public Task<SubAgentTaskResult> ExecuteTaskAsync(
                ToolExecutionContext context, SubAgentTaskRequest request)
            {
                CallCount++;
                LastContext = context;
                LastRequest = request;
                return Task.FromResult(NextResult);
            }
        }

        /// <summary>
        /// Fake <see cref="ISubAgentRegistry"/> — in-memory словарь агентов.
        /// </summary>
        private sealed class FakeRegistry : ISubAgentRegistry
        {
            private readonly Dictionary<string, SubAgentDescriptor> _agents
                = new Dictionary<string, SubAgentDescriptor>(StringComparer.OrdinalIgnoreCase);

            public void Add(SubAgentDescriptor descriptor)
                => _agents[descriptor.Name] = descriptor;

            public IReadOnlyList<SubAgentDescriptor> GetAll()
                => _agents.Values.ToList();

            public IReadOnlyList<SubAgentDescriptor> GetEnabled()
                => _agents.Values.Where(a => !a.Disabled).ToList();

            public SubAgentDescriptor Get(string name)
                => _agents.TryGetValue(name ?? string.Empty, out var d) ? d : null;

            public void Update(SubAgentDescriptor descriptor)
                => _agents[descriptor.Name] = descriptor;

            public void Reset(string name) { /* not used in tests */ }
        }

        /// <summary>
        /// Тестовый наследник <see cref="AgentToolBase"/> —
        /// позволяет задать произвольный <c>AgentName</c>.
        /// </summary>
        private sealed class TestAgentTool : AgentToolBase
        {
            private readonly string _agentName;

            public TestAgentTool(
                Func<ISubAgentService> factory,
                ISubAgentRegistry registry,
                string agentName)
                : base(factory, registry, NullLogger.Instance)
            {
                _agentName = agentName;
            }

            public override string Name => "test_agent_tool";
            public override string Description => "Test agent tool (v1.4.0 Фаза 7)";
            protected override string AgentName => _agentName;
        }

        // ============================================================
        // Helpers
        // ============================================================

        /// <summary>
        /// Создаёт стандартный тестовый дескриптор агента.
        /// </summary>
        private static SubAgentDescriptor BuildDescriptor(
            string name = "file_system_agent",
            bool disabled = false,
            int maxSteps = 10)
        {
            return new SubAgentDescriptor
            {
                Name = name,
                DisplayName = "Тестовый агент",
                Description = "Test",
                SystemPrompt = "Ты — тестовый агент.",
                Model = "test-model/x",
                MaxSteps = maxSteps,
                RequiresApprovalByDefault = true,
                Disabled = disabled,
                AllowedTools = new List<string> { "list_directory", "read_file" }
            };
        }

        /// <summary>
        /// Создаёт инструмент + зависимости для теста.
        /// </summary>
        private static (TestAgentTool Tool, FakeSubAgentService SubAgent, FakeRegistry Registry)
            CreateTool(string agentName, SubAgentDescriptor descriptorOrNull)
        {
            var registry = new FakeRegistry();
            if (descriptorOrNull != null)
                registry.Add(descriptorOrNull);

            var fakeSubAgent = new FakeSubAgentService();

            // Лямбда-фабрика: возвращает тот же Fake (проверяем корректность вызова).
            Func<ISubAgentService> factory = () => fakeSubAgent;

            var tool = new TestAgentTool(factory, registry, agentName);
            return (tool, fakeSubAgent, registry);
        }

        /// <summary>
        /// Контекст выполнения с минимально необходимыми полями.
        /// </summary>
        private static ToolExecutionContext Ctx()
            => new ToolExecutionContext
            {
                UserId = 1,
                WorkspaceRoot = "/tmp/ws",
                CancellationToken = CancellationToken.None
            };

        // ============================================================
        // Тесты
        // ============================================================

        /// <summary>
        /// Пустой task → <see cref="ToolResult.Fail"/>, SubAgent не вызван.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_EmptyTask_ReturnsFail()
        {
            var (tool, subAgent, _) = CreateTool(
                agentName: "file_system_agent",
                descriptorOrNull: BuildDescriptor());

            var args = new JObject { ["task"] = "" };

            var result = await tool.ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("Не указана задача", result.Message);
            Assert.Equal(0, subAgent.CallCount);
        }

        /// <summary>
        /// Task длиной &gt; 8000 символов → <see cref="ToolResult.Fail"/>.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_TooLongTask_ReturnsFail()
        {
            var (tool, subAgent, _) = CreateTool(
                agentName: "file_system_agent",
                descriptorOrNull: BuildDescriptor());

            var args = new JObject { ["task"] = new string('x', 8001) };

            var result = await tool.ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("8000", result.Message);
            Assert.Equal(0, subAgent.CallCount);
        }

        /// <summary>
        /// Агент не зарегистрирован в реестре → <see cref="ToolResult.Fail"/>.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_UnknownAgent_ReturnsFail()
        {
            var (tool, subAgent, _) = CreateTool(
                agentName: "no_such_agent",
                descriptorOrNull: null);

            var args = new JObject { ["task"] = "Сделай что-нибудь" };

            var result = await tool.ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("не зарегистрирован", result.Message);
            Assert.Equal(0, subAgent.CallCount);
        }

        /// <summary>
        /// Агент отключён администратором → <see cref="ToolResult.Fail"/>.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_DisabledAgent_ReturnsFail()
        {
            var (tool, subAgent, _) = CreateTool(
                agentName: "file_system_agent",
                descriptorOrNull: BuildDescriptor(disabled: true));

            var args = new JObject { ["task"] = "Сделай что-нибудь" };

            var result = await tool.ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("отключён", result.Message);
            Assert.Equal(0, subAgent.CallCount);
        }

        /// <summary>
        /// Валидный task → SubAgent вызван, дескриптор передан в request
        /// (SystemPromptOverride, ModelOverride, AllowedTools, MaxSteps).
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ValidTask_PassesDescriptorToSubAgent()
        {
            var descriptor = BuildDescriptor(maxSteps: 7);
            var (tool, subAgent, _) = CreateTool(
                agentName: "file_system_agent",
                descriptorOrNull: descriptor);

            var args = new JObject
            {
                ["task"] = "Покажи список файлов",
                ["context"] = "test-context"
            };

            var result = await tool.ExecuteAsync(Ctx(), args);

            // 1) Успешный результат
            Assert.True(result.Success);

            // 2) SubAgent вызван ровно 1 раз
            Assert.Equal(1, subAgent.CallCount);

            // 3) Request содержит override'ы из дескриптора
            var req = subAgent.LastRequest;
            Assert.NotNull(req);
            Assert.Equal("Покажи список файлов", req.Task);
            Assert.Equal("test-context", req.Context);
            Assert.Equal("Ты — тестовый агент.", req.SystemPromptOverride);
            Assert.Equal("test-model/x", req.ModelOverride);
            Assert.Equal(7, req.MaxSteps);                  // из дескриптора (не переопределён)
            Assert.NotNull(req.AllowedTools);
            Assert.Equal(2, req.AllowedTools.Count);
            Assert.Contains("list_directory", req.AllowedTools);
            Assert.Contains("read_file", req.AllowedTools);
        }

        /// <summary>
        /// maxSteps в аргументах &gt; 30 → clamp до 30.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_MaxSteps_ClampsToLimit()
        {
            var descriptor = BuildDescriptor(maxSteps: 10);
            var (tool, subAgent, _) = CreateTool(
                agentName: "file_system_agent",
                descriptorOrNull: descriptor);

            var args = new JObject
            {
                ["task"] = "Задача",
                ["maxSteps"] = 50   // превышает лимит 30
            };

            var result = await tool.ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Equal(1, subAgent.CallCount);
            Assert.Equal(30, subAgent.LastRequest.MaxSteps);
        }

        /// <summary>
        /// maxSteps в аргументах = 0 → используется значение из дескриптора.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_MaxStepsZero_UsesDescriptorDefault()
        {
            var descriptor = BuildDescriptor(maxSteps: 7);
            var (tool, subAgent, _) = CreateTool(
                agentName: "file_system_agent",
                descriptorOrNull: descriptor);

            var args = new JObject { ["task"] = "Задача" };   // maxSteps не передан

            var result = await tool.ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Equal(7, subAgent.LastRequest.MaxSteps);
        }
    }
}