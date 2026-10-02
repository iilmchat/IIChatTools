using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.Tools.Debate;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Debate
{
    /// <summary>
    /// Unit-тесты <see cref="CodeAgentWithReviewTool"/>
    /// (v1.11.0, KI-126, Шаг 1E-part2 — обновлено).
    /// </summary>
    public class CodeAgentWithReviewToolTests
    {
        private static IConfiguration BuildConfig(int maxRounds = 3)
        {
            var dict = new Dictionary<string, string>
            {
                ["SubAgents:code_agent_with_review:MaxRounds"] = maxRounds.ToString(),
                ["SubAgents:code_agent_with_review:TokenBudget"] = "50000",
                ["SubAgents:code_agent_with_review:AllowEscalation"] = "true",
                ["SubAgents:code_agent_with_review:HumanApproval"] = "Never",
                ["SubAgents:code_agent:Model"] = "qwen/qwen3-4b-2507",
                ["SubAgents:code_reviewer_agent:Model"] = "qwen/qwen3-4b-2507"
            };
            return new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();
        }

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

        private static ToolResult AgentOk(string finalAnswer) =>
            ToolResult.Ok(new { finalAnswer, completed = true }, message: null);

        /// <summary>
        /// Создаёт tool с моками <see cref="IAgentDebateSessionService"/>,
        /// <see cref="IAgentDebateCoordinator"/>, <see cref="IExternalLlmClient"/>
        /// и <see cref="IExternalProviderRegistry"/>.
        ///
        /// <para>
        /// По умолчанию реестр провайдеров пустой — эскалация пропускается
        /// (Шаг 1F). Чтобы включить — передайте <paramref name="externalProviderRegistryMock"/>
        /// с зарегистрированным провайдером.
        /// </para>
        /// </summary>
        private static CodeAgentWithReviewTool CreateTool(
            FakeToolRegistry registry,
            int maxRounds = 3,
            Mock<IAgentDebateSessionService> sessionMock = null,
            Mock<IAgentDebateCoordinator> coordinatorMock = null,
            Mock<IExternalLlmClient> externalLlmMock = null,
            Mock<IExternalProviderRegistry> externalProviderRegistryMock = null,
            IConfiguration config = null)
        {
            sessionMock ??= new Mock<IAgentDebateSessionService>();
            sessionMock.Setup(s => s.StartAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(999);
            sessionMock.Setup(s => s.MarkInProgressAsync(
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            sessionMock.Setup(s => s.AddRoundAsync(
                    It.IsAny<int>(), It.IsAny<AgentDebateRoundDto>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            sessionMock.Setup(s => s.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            coordinatorMock ??= new Mock<IAgentDebateCoordinator>();
            coordinatorMock.Setup(c => c.WaitForFeedbackAsync(
                    It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string)null);   // таймаут по умолчанию

            externalLlmMock ??= new Mock<IExternalLlmClient>();

            // v1.11.0 (KI-126, Шаг 1F): настраиваем "пустой реестр" ТОЛЬКО если
            // mock не был передан извне. Иначе Setup(It.IsAny<string>) → null
            // перебьёт ранее настроенный Setup("deepseek") → options
            // (Moq: последний matching setup wins).
            if (externalProviderRegistryMock == null)
            {
                externalProviderRegistryMock = new Mock<IExternalProviderRegistry>();
                externalProviderRegistryMock
                    .Setup(p => p.Get(It.IsAny<string>()))
                    .Returns((ExternalProviderOptions)null);
            }

            return new CodeAgentWithReviewTool(
                () => registry,
                sessionMock.Object,
                coordinatorMock.Object,
                externalLlmMock.Object,
                externalProviderRegistryMock.Object,
                config ?? BuildConfig(maxRounds),
                NullLogger<CodeAgentWithReviewTool>.Instance);
        }

        private static ToolExecutionContext CtxWithChat(int chatId = 1) =>
            new ToolExecutionContext { UserId = 1, ChatId = chatId };

        // ============ Структура ============

        [Fact]
        public void Name_ReturnsCodeAgentWithReview()
        {
            var tool = CreateTool(new FakeToolRegistry());
            Assert.Equal("code_agent_with_review", tool.Name);
        }

        [Fact]
        public void RequiresApprovalByDefault_IsTrue()
        {
            var tool = CreateTool(new FakeToolRegistry());
            Assert.True(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public void Description_MentionsActorCriticAndApproval()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var description = tool.Description;

            Assert.Contains("Actor-Critic", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("code_agent", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("code_reviewer_agent", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("approval", description, StringComparison.OrdinalIgnoreCase);
        }

        // ============ Валидация ============

        [Fact]
        public async Task ExecuteAsync_EmptyTask_ReturnsFail()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "   " });

            Assert.False(result.Success);
            Assert.Contains("задача", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutChatId_NoPersistence()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def f(): pass")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            var sessionMock = new Mock<IAgentDebateSessionService>();
            var tool = CreateTool(registry, sessionMock: sessionMock);

            var ctx = new ToolExecutionContext { UserId = 1, ChatId = null };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            // sessionService не должен вызываться.
            sessionMock.Verify(s => s.StartAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ============ Основные сценарии ============

        [Fact]
        public async Task ExecuteAsync_ApprovedFirstRound_StopsAfterOneRound()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def is_palindrome(s): return s == s[::-1]")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Палиндром" });

            Assert.True(result.Success);
            Assert.Equal(2, registry.CalledTools.Count);

            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_RejectedThenApproved_StopsAfterTwoRounds()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def v1(): pass"),
                AgentOk("def v2(): return 42")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[{\"severity\":\"Major\"}],\"summary\":\"Нет обработки\"}"),
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            Assert.Equal(4, registry.CalledTools.Count);

            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(2, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_MaxRoundsReached_ReturnsMaxRoundsReached()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1"), AgentOk("code v2"), AgentOk("code v3")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}"),
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}"),
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}")
            });

            var tool = CreateTool(registry, maxRounds: 3);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("MaxRoundsReached", data["verdict"]?.ToString());
            Assert.Equal(3, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_CriticUncertain_StopsEarly()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Uncertain\",\"issues\":[],\"summary\":\"Не уверен\"}")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_CriticReturnsMarkdownJson_ParsesCorrectly()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def foo(): pass")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("```json\n{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"хорошо\"}\n```")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
        }

        [Fact]
        public async Task ExecuteAsync_CriticReturnsFreeText_FallsBackToUncertain()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def foo(): pass")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("Код выглядит нормально, но я не совсем уверен.")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
        }

        // ============ SSE-события (EventWriter) ============

        [Fact]
        public async Task ExecuteAsync_EmitsDebateEvents()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def foo(): pass")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            var tool = CreateTool(registry);

            var channel = Channel.CreateUnbounded<ChatStreamEvent>();
            var ctx = new ToolExecutionContext
            {
                UserId = 1,
                ChatId = 1,
                EventWriter = channel.Writer
            };

            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Test" });
            Assert.True(result.Success);

            channel.Writer.TryComplete();

            var events = new List<ChatStreamEvent>();
            await foreach (var evt in channel.Reader.ReadAllAsync())
            {
                events.Add(evt);
            }

            // Ожидаем: debate_started + debate_round + debate_completed
            Assert.Contains(events, e => e.Type == "debate_started");
            Assert.Contains(events, e => e.Type == "debate_round");
            Assert.Contains(events, e => e.Type == "debate_completed");
        }

        // ============ Human-in-the-loop ============

        [Fact]
        public async Task ExecuteAsync_HumanApprovalBetweenRounds_CallsCoordinator()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1"),
                AgentOk("code v2")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}"),
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            var coordinatorMock = new Mock<IAgentDebateCoordinator>();
            coordinatorMock.Setup(c => c.WaitForFeedbackAsync(
                    It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("пользовательский feedback");

            // HumanApproval: BetweenRounds
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["SubAgents:code_agent_with_review:MaxRounds"] = "3",
                    ["SubAgents:code_agent_with_review:HumanApproval"] = "BetweenRounds",
                    ["SubAgents:code_agent:Model"] = "qwen/qwen3-4b-2507",
                    ["SubAgents:code_reviewer_agent:Model"] = "qwen/qwen3-4b-2507"
                })
                .Build();

            var sessionMock = new Mock<IAgentDebateSessionService>();
            sessionMock.Setup(s => s.StartAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(999);
            sessionMock.Setup(s => s.MarkInProgressAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            sessionMock.Setup(s => s.AddRoundAsync(It.IsAny<int>(), It.IsAny<AgentDebateRoundDto>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            sessionMock.Setup(s => s.CompleteAsync(It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // v1.11.0 (KI-126, Шаг 1F): конструктор расширен —
            // +IExternalLlmClient +IExternalProviderRegistry.
            // Здесь используем пустой mock реестра — эскалация пропускается
            // (этот тест про Human-in-the-loop, не про эскалацию).
            var externalLlmMock = new Mock<IExternalLlmClient>();
            var externalRegistryMock = new Mock<IExternalProviderRegistry>();
            externalRegistryMock.Setup(p => p.Get(It.IsAny<string>()))
                .Returns((ExternalProviderOptions)null);

            var tool = new CodeAgentWithReviewTool(
                () => registry,
                sessionMock.Object,
                coordinatorMock.Object,
                externalLlmMock.Object,
                externalRegistryMock.Object,
                config,
                NullLogger<CodeAgentWithReviewTool>.Instance);

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            // Coordinator должен быть вызван ровно 1 раз (после Rejected, перед Round 2).
            coordinatorMock.Verify(c => c.WaitForFeedbackAsync(
                It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ============ Эскалация на внешнюю LLM (Шаг 1F) ============

        /// <summary>
        /// Фабрика: mock-реестр с зарегистрированным "deepseek".
        /// </summary>
        private static Mock<IExternalProviderRegistry> RegistryWithDeepSeek()
        {
            var mock = new Mock<IExternalProviderRegistry>();
            mock.Setup(p => p.Get("deepseek")).Returns(new ExternalProviderOptions
            {
                DisplayName = "DeepSeek",
                BaseUrl = "https://api.deepseek.com/v1",
                Model = "deepseek-chat"
            });
            mock.Setup(p => p.Get(It.IsNotIn("deepseek")))
                .Returns((ExternalProviderOptions)null);
            mock.Setup(p => p.GetNames()).Returns(new List<string> { "deepseek" });
            mock.Setup(p => p.DefaultProvider).Returns("deepseek");
            return mock;
        }

        [Fact]
        public async Task ExecuteAsync_UncertainWithEscalation_EscalatesAndRechecks()
        {
            var registry = new FakeToolRegistry();
            // Actor — 1 ответ (Uncertain после critic-1, потом escalation, потом critic-2 Approved)
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def is_palindrome(s): return s == s[::-1]")
            });
            // Critic — 2 ответа: Uncertain → Approved (после эскалации).
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Uncertain\",\"issues\":[],\"summary\":\"Не уверен про Unicode\"}"),
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"Внешняя модель подтвердила\"}")
            });

            var externalMock = new Mock<IExternalLlmClient>();
            externalMock.Setup(e => e.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExternalLlmResponse
                {
                    Provider = "deepseek",
                    Content = "Да, критик прав — нужно учесть Unicode-нормализацию.",
                    PromptTokens = 100,
                    CompletionTokens = 50,
                    CostUsd = 0.001m,
                    DurationMs = 500
                });

            var tool = CreateTool(
                registry,
                externalLlmMock: externalMock,
                externalProviderRegistryMock: RegistryWithDeepSeek());

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Палиндром" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());

            // Внешняя LLM вызвана ровно 1 раз.
            externalMock.Verify(e => e.CompleteAsync(
                It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);

            // Critic вызван 2 раза (round 1 + second round после эскалации).
            var criticCalls = registry.CalledTools.Where(t => t == "code_reviewer_agent").Count();
            Assert.Equal(2, criticCalls);

            // round в ответе: был Escalated.
            var rounds = data["rounds"] as JArray;
            Assert.NotNull(rounds);
            Assert.Single(rounds);
            Assert.True(rounds[0]["wasEscalated"]?.Value<bool>());
        }

        [Fact]
        public async Task ExecuteAsync_UncertainWithoutEscalation_Skips()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("def foo(): pass")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Uncertain\",\"issues\":[],\"summary\":\"n/a\"}")
            });

            var externalMock = new Mock<IExternalLlmClient>();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["SubAgents:code_agent_with_review:MaxRounds"] = "3",
                    ["SubAgents:code_agent_with_review:AllowEscalation"] = "false",
                    ["SubAgents:code_agent_with_review:HumanApproval"] = "Never"
                })
                .Build();

            var tool = CreateTool(
                registry,
                externalLlmMock: externalMock,
                externalProviderRegistryMock: RegistryWithDeepSeek(),
                config: config);

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());

            // Внешняя LLM НЕ вызвана (AllowEscalation=false).
            externalMock.Verify(e => e.CompleteAsync(
                It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_EscalationHttpFails_KeepsUncertain()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Uncertain\",\"issues\":[],\"summary\":\"n/a\"}")
            });

            var externalMock = new Mock<IExternalLlmClient>();
            externalMock.Setup(e => e.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Daily budget exceeded"));

            var tool = CreateTool(
                registry,
                externalLlmMock: externalMock,
                externalProviderRegistryMock: RegistryWithDeepSeek());

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());

            // Второй раунд критика НЕ вызывался (эскалация упала).
            var criticCalls = registry.CalledTools.Where(t => t == "code_reviewer_agent").Count();
            Assert.Equal(1, criticCalls);

            // round.wasEscalated == false (эскалация не удалась).
            var rounds = data["rounds"] as JArray;
            Assert.False(rounds[0]["wasEscalated"]?.Value<bool>());
        }

        [Fact]
        public async Task ExecuteAsync_EscalationProviderNotRegistered_Skips()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Uncertain\",\"issues\":[],\"summary\":\"n/a\"}")
            });

            var externalMock = new Mock<IExternalLlmClient>();
            // Пустой реестр — провайдер не зарегистрирован.
            var emptyRegistry = new Mock<IExternalProviderRegistry>();
            emptyRegistry.Setup(p => p.Get(It.IsAny<string>()))
                .Returns((ExternalProviderOptions)null);

            var tool = CreateTool(
                registry,
                externalLlmMock: externalMock,
                externalProviderRegistryMock: emptyRegistry);

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());

            // Внешняя LLM НЕ вызвана (провайдер не зарегистрирован).
            externalMock.Verify(e => e.CompleteAsync(
                It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        // ============ v1.11.0 (KI-126, Шаг 1I): failure-сценарии ============

        [Fact]
        public async Task ExecuteAsync_ActorFails_ReturnsFail()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                ToolResult.Fail("code_agent: не удалось запустить Python")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Задача" });

            Assert.False(result.Success);
            Assert.Contains("actor", result.Message, StringComparison.OrdinalIgnoreCase);

            // Critic не вызывался (actor упал на первом раунде).
            Assert.DoesNotContain("code_reviewer_agent", registry.CalledTools);
        }

        [Fact]
        public async Task ExecuteAsync_CriticFails_ReturnsUncertain()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                ToolResult.Fail("critic: timeout")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Задача" });

            // Actor отработал, но critic упал → verdict=Uncertain.
            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
        }

        [Fact]
        public async Task ExecuteAsync_RoundsHaveSequentialNumbers()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("v1"), AgentOk("v2"), AgentOk("v3")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}"),
                AgentOk("{\"verdict\":\"Rejected\",\"issues\":[],\"summary\":\"no\"}"),
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Задача" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            var rounds = data["rounds"] as JArray;
            Assert.NotNull(rounds);
            Assert.Equal(3, rounds.Count);

            Assert.Equal(1, rounds[0]["roundNumber"]?.Value<int>());
            Assert.Equal(2, rounds[1]["roundNumber"]?.Value<int>());
            Assert.Equal(3, rounds[2]["roundNumber"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_WithChatId_PersistsSessionAndRounds()
        {
            // v1.11.0 (KI-126, Шаг 1I): sessionId=999 — дефолт из фабрики
            // CreateTool. Moq: последний matching Setup wins — если в тесте
            // переопределить ReturnsAsync(777) до вызова CreateTool, то
            // фабричный Setup(→999) перебьёт пользовательский.
            const int SessionId = 999;

            var registry = new FakeToolRegistry();
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("code v1")
            });
            registry.ResponsesByTool["code_reviewer_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"verdict\":\"Approved\",\"issues\":[],\"summary\":\"OK\"}")
            });

            // Используем mock из CreateTool (sessionId=999), но переопределим
            // StartAsync только чтобы проверить chatId в аргументе.
            var sessionMock = new Mock<IAgentDebateSessionService>();
            sessionMock.Setup(s => s.StartAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SessionId);
            sessionMock.Setup(s => s.MarkInProgressAsync(
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            sessionMock.Setup(s => s.AddRoundAsync(
                    It.IsAny<int>(), It.IsAny<AgentDebateRoundDto>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            sessionMock.Setup(s => s.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var tool = CreateTool(registry, sessionMock: sessionMock);
            var result = await tool.ExecuteAsync(
                CtxWithChat(chatId: 42), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);

            // StartAsync вызван с chatId=42.
            sessionMock.Verify(s => s.StartAsync(
                It.Is<int>(id => id == 42),
                It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()),
                Times.Once);

            // MarkInProgress + AddRound + Complete вызваны с тем же sessionId,
            // что вернул StartAsync (999).
            sessionMock.Verify(s => s.MarkInProgressAsync(
                SessionId, It.IsAny<CancellationToken>()), Times.Once);
            sessionMock.Verify(s => s.AddRoundAsync(
                SessionId, It.IsAny<AgentDebateRoundDto>(), It.IsAny<CancellationToken>()),
                Times.Once);
            sessionMock.Verify(s => s.CompleteAsync(
                SessionId, "Approved", It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
