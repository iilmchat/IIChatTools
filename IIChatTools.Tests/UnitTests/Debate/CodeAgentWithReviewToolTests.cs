using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Implementation.Tools.Debate;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Debate
{
    /// <summary>
    /// Unit-тесты <see cref="CodeAgentWithReviewTool"/>
    /// (v1.11.0, KI-126, Шаг 1D).
    ///
    /// <para>
    /// Проверяют: структуру (Name / Description / Parameters), валидацию аргументов,
    /// корректность парсинга verdict критика (без реального вызова LM Studio).
    /// Полный e2e-цикл actor-critic — в интеграционных тестах Шага 1I.
    /// </para>
    /// </summary>
    public class CodeAgentWithReviewToolTests
    {
        /// <summary>
        /// Минимальный in-memory конфиг для тестов.
        /// </summary>
        private static IConfiguration BuildConfig(int maxRounds = 3)
        {
            var dict = new Dictionary<string, string>
            {
                ["SubAgents:code_agent_with_review:MaxRounds"] = maxRounds.ToString(),
                ["SubAgents:code_agent_with_review:TokenBudget"] = "50000"
            };
            return new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();
        }

        /// <summary>
        /// Fake-реестр с одним настроенным tool'ом (или с цепочкой ответов).
        /// </summary>
        private sealed class FakeToolRegistry : IToolRegistry
        {
            public Dictionary<string, Queue<ToolResult>> ResponsesByTool { get; }
                = new Dictionary<string, Queue<ToolResult>>(StringComparer.OrdinalIgnoreCase);

            public List<string> CalledTools { get; } = new List<string>();

            public IReadOnlyList<ToolDescriptor> GetAllDescriptors() => Array.Empty<ToolDescriptor>();

            public ToolDescriptor GetDescriptor(string name) => null;

            public ITool GetTool(string name) => null;

            public Task<ToolResult> ExecuteAsync(
                string toolName, ToolExecutionContext context, JObject arguments)
            {
                CalledTools.Add(toolName);
                if (ResponsesByTool.TryGetValue(toolName, out var q) && q.Count > 0)
                {
                    return Task.FromResult(q.Dequeue());
                }
                return Task.FromResult(ToolResult.Fail($"Нет ответа для '{toolName}'"));
            }
        }

        /// <summary>
        /// Хелпер: сформировать успешный ответ агента с заданным finalAnswer.
        /// </summary>
        private static ToolResult AgentOk(string finalAnswer) =>
            ToolResult.Ok(new { finalAnswer, completed = true }, message: null);

        [Fact]
        public void Name_ReturnsCodeAgentWithReview()
        {
            var tool = new CodeAgentWithReviewTool(
                new FakeToolRegistry(),
                BuildConfig(),
                NullLogger<CodeAgentWithReviewTool>.Instance);

            Assert.Equal("code_agent_with_review", tool.Name);
        }

        [Fact]
        public void RequiresApprovalByDefault_IsTrue()
        {
            var tool = new CodeAgentWithReviewTool(
                new FakeToolRegistry(),
                BuildConfig(),
                NullLogger<CodeAgentWithReviewTool>.Instance);

            Assert.True(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public void Description_MentionsActorCriticAndApproval()
        {
            var tool = new CodeAgentWithReviewTool(
                new FakeToolRegistry(),
                BuildConfig(),
                NullLogger<CodeAgentWithReviewTool>.Instance);

            var description = tool.Description;
            Assert.Contains("Actor-Critic", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("code_agent", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("code_reviewer_agent", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("approval", description, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_EmptyTask_ReturnsFail()
        {
            var tool = new CodeAgentWithReviewTool(
                new FakeToolRegistry(),
                BuildConfig(),
                NullLogger<CodeAgentWithReviewTool>.Instance);

            var ctx = new ToolExecutionContext { UserId = 1 };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "   " });

            Assert.False(result.Success);
            Assert.Contains("задача", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_ApprovedFirstRound_StopsAfterOneRound()
        {
            // Actor: 1 ответ; Critic: 1 ответ = Approved.
            var fakeRegistry = new FakeToolRegistry();
            fakeRegistry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def is_palindrome(s): return s == s[::-1]")
            });
            fakeRegistry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            var tool = new CodeAgentWithReviewTool(
                fakeRegistry, BuildConfig(), NullLogger<CodeAgentWithReviewTool>.Instance);

            var ctx = new ToolExecutionContext { UserId = 1 };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Палиндром" });

            Assert.True(result.Success);
            Assert.Equal(2, fakeRegistry.CalledTools.Count);   // actor + critic
            Assert.Equal("code_agent", fakeRegistry.CalledTools[0]);
            Assert.Equal("code_reviewer_agent", fakeRegistry.CalledTools[1]);

            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_RejectedThenApproved_StopsAfterTwoRounds()
        {
            var fakeRegistry = new FakeToolRegistry();
            fakeRegistry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def v1(): pass"),  // раунд 1
                AgentOk("def v2(): return 42")  // раунд 2 (учитывает feedback)
            });
            fakeRegistry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[{\"severity\":\"Major\"}],\"summary\":\"Нет обработки\"}"),
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            var tool = new CodeAgentWithReviewTool(
                fakeRegistry, BuildConfig(), NullLogger<CodeAgentWithReviewTool>.Instance);

            var ctx = new ToolExecutionContext { UserId = 1 };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            Assert.Equal(4, fakeRegistry.CalledTools.Count);   // actor+critic × 2

            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(2, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_MaxRoundsReached_ReturnsMaxRoundsReached()
        {
            var fakeRegistry = new FakeToolRegistry();

            // 3 раунда — все Rejected.
            fakeRegistry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1"), AgentOk("code v2"), AgentOk("code v3")
            });
            fakeRegistry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}"),
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}"),
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}")
            });

            var tool = new CodeAgentWithReviewTool(
                fakeRegistry, BuildConfig(maxRounds: 3), NullLogger<CodeAgentWithReviewTool>.Instance);

            var ctx = new ToolExecutionContext { UserId = 1 };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("MaxRoundsReached", data["verdict"]?.ToString());
            Assert.Equal(3, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_CriticUncertain_StopsEarly()
        {
            var fakeRegistry = new FakeToolRegistry();
            fakeRegistry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1")
            });
            fakeRegistry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Uncertain\",\"issues\":[],\"summary\":\"Не уверен\"}")
            });

            var tool = new CodeAgentWithReviewTool(
                fakeRegistry, BuildConfig(), NullLogger<CodeAgentWithReviewTool>.Instance);

            var ctx = new ToolExecutionContext { UserId = 1 };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_CriticReturnsMarkdownJson_ParsesCorrectly()
        {
            // qwen3-4b может вернуть JSON в markdown-блоке.
            var fakeRegistry = new FakeToolRegistry();
            fakeRegistry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def foo(): pass")
            });
            fakeRegistry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("```json\n{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"хорошо\"}\n```")
            });

            var tool = new CodeAgentWithReviewTool(
                fakeRegistry, BuildConfig(), NullLogger<CodeAgentWithReviewTool>.Instance);

            var ctx = new ToolExecutionContext { UserId = 1 };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
        }

        [Fact]
        public async Task ExecuteAsync_CriticReturnsFreeText_FallsBackToUncertain()
        {
            // Если критик вернул не-JSON и без явного вердикта — fallback на Uncertain.
            var fakeRegistry = new FakeToolRegistry();
            fakeRegistry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def foo(): pass")
            });
            fakeRegistry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("Код выглядит нормально, но я не совсем уверен в требованиях.")
            });

            var tool = new CodeAgentWithReviewTool(
                fakeRegistry, BuildConfig(), NullLogger<CodeAgentWithReviewTool>.Instance);

            var ctx = new ToolExecutionContext { UserId = 1 };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
        }
    }
}