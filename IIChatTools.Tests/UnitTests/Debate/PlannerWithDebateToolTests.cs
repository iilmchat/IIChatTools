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
    /// Unit-тесты <see cref="PlannerWithDebateTool"/>
    /// (v1.14.0, KI-126, Фаза 2, Шаг 2E).
    ///
    /// <para>
    /// По образцу <see cref="CodeAgentWithReviewToolTests"/> — покрываем
    /// 3-ролевой цикл (Pro → Contra → Judge), валидацию, persist,
    /// Human-in-the-loop, эскалацию при Uncertain.
    /// </para>
    /// </summary>
    public class PlannerWithDebateToolTests
    {
        private const string ProToolName = "planner_pro_agent";
        private const string ContraToolName = "planner_contra_agent";
        private const string JudgeToolName = "planner_judge_agent";

        /// <summary>
        /// Конфиг по умолчанию — HumanApproval = Never (чтобы не ловить
        /// лишние вызовы coordinator.WaitForFeedbackAsync в happy-path тестах).
        /// Эскалация отключена (пустой реестр провайдеров) — для её тестов
        /// есть отдельные кейсы.
        /// </summary>
        private static IConfiguration BuildConfig(
            int maxRounds = 3,
            string humanApproval = "Never",
            bool allowEscalation = true,
            string escalationProvider = "deepseek")
        {
            var dict = new Dictionary<string, string>
            {
                ["SubAgents:planner_with_debate:MaxRounds"] = maxRounds.ToString(),
                ["SubAgents:planner_with_debate:TokenBudget"] = "50000",
                ["SubAgents:planner_with_debate:AllowEscalation"] = allowEscalation.ToString(),
                ["SubAgents:planner_with_debate:HumanApproval"] = humanApproval,
                ["SubAgents:planner_with_debate:EscalationProvider"] = escalationProvider,
                ["SubAgents:planner_pro_agent:Model"] = "qwen/qwen3-4b-2507",
                ["SubAgents:planner_contra_agent:Model"] = "qwen/qwen3-4b-2507",
                ["SubAgents:planner_judge_agent:Model"] = "qwen/qwen3-4b-2507"
            };
            return new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();
        }

        /// <summary>
        /// Fake-реестр инструментов: очереди ответов на каждое имя tool'а.
        /// Копия из <c>CodeAgentWithReviewToolTests</c> (приватный вложенный
        /// класс — не переиспользуется между тестами).
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

        private static ToolResult AgentOk(string finalAnswer) =>
            ToolResult.Ok(new { finalAnswer, completed = true }, message: null);

        /// <summary>
        /// Хелпер: сформировать валидный JSON ответа Pro-агента.
        /// </summary>
        private static string ProJson(string conclusion = "Предложение обосновано.")
            => $"{{\"arguments\":[\"аргумент 1\",\"аргумент 2\"]," +
               $"\"coveredCases\":[\"case A\"],\"conclusion\":\"{conclusion}\"}}";

        /// <summary>
        /// Хелпер: сформировать валидный JSON ответа Contra-агента.
        /// </summary>
        private static string ContraJson(string conclusion = "Есть риски.")
            => $"{{\"issues\":[{{\"severity\":\"Major\",\"description\":\"проблема\"," +
               $"\"suggestion\":\"исправить\"}}],\"risks\":[\"риск 1\"],\"conclusion\":\"{conclusion}\"}}";

        /// <summary>
        /// Хелпер: сформировать валидный JSON ответа Judge-агента.
        /// </summary>
        private static string JudgeJson(string verdict, string reasoning = "Обоснование.")
            => $"{{\"verdict\":\"{verdict}\",\"reasoning\":\"{reasoning}\"," +
               $"\"issues\":[],\"summary\":\"Краткое резюме.\"}}";

        /// <summary>
        /// Создаёт <see cref="PlannerWithDebateTool"/> с дефолтными mock-сервисами.
        /// </summary>
        private static PlannerWithDebateTool CreateTool(
            FakeToolRegistry registry,
            int maxRounds = 3,
            string humanApproval = "Never",
            bool allowEscalation = true,
            Mock<IAgentDebateSessionService> sessionMock = null,
            Mock<IAgentDebateCoordinator> coordinatorMock = null,
            Mock<IExternalLlmClient> externalLlmMock = null,
            Mock<IExternalProviderRegistry> externalProviderRegistryMock = null,
            IConfiguration config = null,
            Action<AgentDebateRoundDto> onRoundAdded = null)
        {
            sessionMock ??= new Mock<IAgentDebateSessionService>();
            sessionMock.Setup(s => s.StartAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(777);
            sessionMock.Setup(s => s.MarkInProgressAsync(
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            sessionMock.Setup(s => s.AddRoundAsync(
                    It.IsAny<int>(), It.IsAny<AgentDebateRoundDto>(),
                    It.IsAny<CancellationToken>()))
                .Callback<int, AgentDebateRoundDto, CancellationToken>(
                    (_, round, _) => onRoundAdded?.Invoke(round))
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

            // Пустой реестр провайдеров (эскалация пропускается) —
            // только если mock не передан извне. Иначе Setup(It.IsAny<string>)
            // перебьёт точечный Setup("deepseek") (Moq: последний matching wins).
            if (externalProviderRegistryMock == null)
            {
                externalProviderRegistryMock = new Mock<IExternalProviderRegistry>();
                externalProviderRegistryMock
                    .Setup(p => p.Get(It.IsAny<string>()))
                    .Returns((ExternalProviderOptions)null);
            }

            return new PlannerWithDebateTool(
                () => registry,
                sessionMock.Object,
                coordinatorMock.Object,
                externalLlmMock.Object,
                externalProviderRegistryMock.Object,
                config ?? BuildConfig(maxRounds, humanApproval, allowEscalation),
                NullLogger<PlannerWithDebateTool>.Instance);
        }

        private static ToolExecutionContext CtxWithChat(int chatId = 1) =>
            new ToolExecutionContext { UserId = 1, ChatId = chatId };

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

        // ============ Структура ============

        [Fact]
        public void Name_ReturnsPlannerWithDebate()
        {
            var tool = CreateTool(new FakeToolRegistry());
            Assert.Equal("planner_with_debate", tool.Name);
        }

        [Fact]
        public void RequiresApprovalByDefault_IsTrue()
        {
            var tool = CreateTool(new FakeToolRegistry());
            Assert.True(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public void Description_MentionsProContraJudgeAndApproval()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var description = tool.Description;

            Assert.Contains("Pro", description, StringComparison.Ordinal);
            Assert.Contains("Contra", description, StringComparison.Ordinal);
            Assert.Contains("Judge", description, StringComparison.Ordinal);
            Assert.Contains("approval", description, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parameters_ContainsTaskContextMaxRounds()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var names = tool.Parameters.Select(p => p.Name).ToList();

            Assert.Contains("task", names);
            Assert.Contains("context", names);
            Assert.Contains("maxRounds", names);
        }

        // ============ Валидация ============

        [Fact]
        public async Task ExecuteAsync_EmptyTask_ReturnsFail()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "   " });

            Assert.False(result.Success);
            Assert.Contains("предложение", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_TaskTooLong_ReturnsFail()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var longTask = new string('x', 8001);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = longTask });

            Assert.False(result.Success);
            Assert.Contains("8000", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_ContextTooLong_ReturnsFail()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var longCtx = new string('x', 20001);
            var result = await tool.ExecuteAsync(
                CtxWithChat(),
                new JObject { ["task"] = "test", ["context"] = longCtx });

            Assert.False(result.Success);
            Assert.Contains("20000", result.Message);
        }

        // ============ Happy path ============

        [Fact]
        public async Task ExecuteAsync_JudgeApprovedFirstRound_StopsAfterOneRound()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson())
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson())
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Approved"))
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Использовать REST для API" });

            Assert.True(result.Success);
            Assert.Equal(3, registry.CalledTools.Count);
            Assert.Equal(ProToolName, registry.CalledTools[0]);
            Assert.Equal(ContraToolName, registry.CalledTools[1]);
            Assert.Equal(JudgeToolName, registry.CalledTools[2]);

            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_RejectedThenApproved_StopsAfterTwoRounds()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson("v1")),
                AgentOk(ProJson("v2"))
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson("v1")),
                AgentOk(ContraJson("v2"))
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Rejected")),
                AgentOk(JudgeJson("Approved"))
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            Assert.Equal(6, registry.CalledTools.Count);   // 2 раунда × 3 роли
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(2, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_MaxRoundsReached_ReturnsMaxRoundsReached()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson("v1")), AgentOk(ProJson("v2")), AgentOk(ProJson("v3"))
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson()), AgentOk(ContraJson()), AgentOk(ContraJson())
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Rejected")),
                AgentOk(JudgeJson("Rejected")),
                AgentOk(JudgeJson("Rejected"))
            });

            var tool = CreateTool(registry, maxRounds: 3);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            Assert.Equal(9, registry.CalledTools.Count);   // 3 раунда × 3 роли
            var data = JObject.FromObject(result.Data);
            Assert.Equal("MaxRoundsReached", data["verdict"]?.ToString());
            Assert.Equal(3, data["totalRounds"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_JudgeUncertain_StopsEarly()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson())
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson())
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Uncertain"))
            });

            var tool = CreateTool(registry, allowEscalation: false);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            Assert.Equal(3, registry.CalledTools.Count);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());
        }

        // ============ Persist ============

        [Fact]
        public async Task ExecuteAsync_PersistRound_SavesProContraJudgeFields()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson("ПроЗаключение"))
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson("КонтраЗаключение"))
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Approved", "Обоснование судьи"))
            });

            var capturedRounds = new List<AgentDebateRoundDto>();
            var tool = CreateTool(registry, onRoundAdded: r => capturedRounds.Add(r));

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            Assert.Single(capturedRounds);

            var round = capturedRounds[0];
            Assert.Equal(1, round.RoundNumber);
            Assert.Contains("ПроЗаключение", round.ProOutput);
            Assert.Contains("КонтраЗаключение", round.ContraOutput);
            Assert.Equal("Approved", round.JudgeVerdict);
            Assert.Contains("Обоснование судьи", round.JudgeFeedbackJson);

            // ActorCritic-поля должны быть null (семантика Debate).
            Assert.Null(round.ActorOutput);
            Assert.Null(round.CriticVerdict);
            Assert.Null(round.CriticFeedbackJson);

            Assert.False(round.WasEscalated);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutChatId_NoPersistence()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[] { AgentOk(JudgeJson("Approved")) });

            var sessionMock = new Mock<IAgentDebateSessionService>();
            var tool = CreateTool(registry, sessionMock: sessionMock);

            var ctx = new ToolExecutionContext { UserId = 1, ChatId = null };
            var result = await tool.ExecuteAsync(ctx, new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            sessionMock.Verify(s => s.StartAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ============ SSE-события ============

        [Fact]
        public async Task ExecuteAsync_EmitsDebateEvents()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[] { AgentOk(JudgeJson("Approved")) });

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

            Assert.Contains(events, e => e.Type == "debate_started");
            Assert.Contains(events, e => e.Type == "debate_round");
            Assert.Contains(events, e => e.Type == "debate_completed");

            // debate_started содержит PatternType=Debate + 3 роли.
            //
            // RULES § 4.43: `JObject.FromObject(DTO)` использует дефолтный
            // Newtonsoft-контракт → PascalCase ключи. CamelCase — только
            // в MVC/SSE-ответах (см. SseJsonSettings).
            var started = events.First(e => e.Type == "debate_started");
            var startedJson = JObject.FromObject(started.Data);
            Assert.Equal("Debate", startedJson["PatternType"]?.ToString());
            Assert.Equal(ProToolName, startedJson["ProAgent"]?.ToString());
            Assert.Equal(ContraToolName, startedJson["ContraAgent"]?.ToString());
            Assert.Equal(JudgeToolName, startedJson["JudgeAgent"]?.ToString());
        }

        // ============ Human-in-the-loop ============

        [Fact]
        public async Task ExecuteAsync_JudgeRejectedBetweenRounds_CallsCoordinator()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson("v1")), AgentOk(ProJson("v2"))
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson()), AgentOk(ContraJson())
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Rejected")),
                AgentOk(JudgeJson("Approved"))
            });

            var coordinatorMock = new Mock<IAgentDebateCoordinator>();
            coordinatorMock.Setup(c => c.WaitForFeedbackAsync(
                    It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Пользовательский feedback");

            var tool = CreateTool(
                registry,
                humanApproval: "BetweenRounds",
                coordinatorMock: coordinatorMock);

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            // Coordinator вызван ровно 1 раз (после Judge Rejected в Round 1).
            coordinatorMock.Verify(c => c.WaitForFeedbackAsync(
                It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_HumanApprovalNever_DoesNotCallCoordinator()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson("v1")), AgentOk(ProJson("v2"))
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson()), AgentOk(ContraJson())
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Rejected")),
                AgentOk(JudgeJson("Approved"))
            });

            var coordinatorMock = new Mock<IAgentDebateCoordinator>();
            var tool = CreateTool(
                registry,
                humanApproval: "Never",
                coordinatorMock: coordinatorMock);

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            coordinatorMock.Verify(c => c.WaitForFeedbackAsync(
                It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ============ Эскалация (Шаг 2C.2) ============

        [Fact]
        public async Task ExecuteAsync_JudgeUncertain_EscalatesAndReconsiders()
        {
            var registry = new FakeToolRegistry();
            // Pro/Contra — по 1 ответу (1 раунд).
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson())
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson())
            });
            // Judge — 2 ответа: Uncertain → Approved (после эскалации).
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Uncertain", "Не уверен про SQL injection")),
                AgentOk(JudgeJson("Approved", "Внешняя модель подтвердила"))
            });

            var externalMock = new Mock<IExternalLlmClient>();
            externalMock.Setup(e => e.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExternalLlmResponse
                {
                    Provider = "deepseek",
                    Content = "Критик прав — нужно учесть SQL injection.",
                    PromptTokens = 150,
                    CompletionTokens = 80,
                    CostUsd = 0.002m,
                    DurationMs = 800
                });

            var capturedRounds = new List<AgentDebateRoundDto>();
            var tool = CreateTool(
                registry,
                externalLlmMock: externalMock,
                externalProviderRegistryMock: RegistryWithDeepSeek(),
                onRoundAdded: r => capturedRounds.Add(r));

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());

            // Внешняя LLM вызвана 1 раз.
            externalMock.Verify(e => e.CompleteAsync(
                It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);

            // Judge вызван 2 раза (round 1 + second round после эскалации).
            var judgeCalls = registry.CalledTools.Count(t => t == JudgeToolName);
            Assert.Equal(2, judgeCalls);

            // Раунд был эскалирован.
            Assert.Single(capturedRounds);
            Assert.True(capturedRounds[0].WasEscalated);
            Assert.Equal("deepseek", capturedRounds[0].EscalationProvider);
            Assert.Equal(150, capturedRounds[0].TokensIn);
            Assert.Equal(80, capturedRounds[0].TokensOut);
            Assert.Equal(0.002m, capturedRounds[0].CostUsd);
        }

        [Fact]
        public async Task ExecuteAsync_JudgeUncertain_AllowEscalationFalse_Skips()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[] { AgentOk(JudgeJson("Uncertain")) });

            var externalMock = new Mock<IExternalLlmClient>();
            var tool = CreateTool(
                registry,
                allowEscalation: false,
                externalLlmMock: externalMock,
                externalProviderRegistryMock: RegistryWithDeepSeek());

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());

            externalMock.Verify(e => e.CompleteAsync(
                It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_EscalationHttpFails_KeepsUncertain()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[] { AgentOk(JudgeJson("Uncertain")) });

            var externalMock = new Mock<IExternalLlmClient>();
            externalMock.Setup(e => e.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Daily budget exceeded"));

            var capturedRounds = new List<AgentDebateRoundDto>();
            var tool = CreateTool(
                registry,
                externalLlmMock: externalMock,
                externalProviderRegistryMock: RegistryWithDeepSeek(),
                onRoundAdded: r => capturedRounds.Add(r));

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());

            // Judge вызван 1 раз (эскалация упала → второй раунд не делается).
            var judgeCalls = registry.CalledTools.Count(t => t == JudgeToolName);
            Assert.Equal(1, judgeCalls);

            Assert.Single(capturedRounds);
            Assert.False(capturedRounds[0].WasEscalated);
        }

        [Fact]
        public async Task ExecuteAsync_EscalationProviderNotRegistered_Skips()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[] { AgentOk(JudgeJson("Uncertain")) });

            var externalMock = new Mock<IExternalLlmClient>();
            var emptyRegistry = new Mock<IExternalProviderRegistry>();
            emptyRegistry.Setup(p => p.Get(It.IsAny<string>()))
                .Returns((ExternalProviderOptions)null);

            var tool = CreateTool(
                registry,
                externalLlmMock: externalMock,
                externalProviderRegistryMock: emptyRegistry);

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());

            externalMock.Verify(e => e.CompleteAsync(
                It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        // ============ Failure-сценарии ============

        [Fact]
        public async Task ExecuteAsync_ProFails_ReturnsFail()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                ToolResult.Fail("Pro: timeout")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.False(result.Success);
            Assert.Contains("Pro-агент", result.Message);
            Assert.DoesNotContain(ContraToolName, registry.CalledTools);
            Assert.DoesNotContain(JudgeToolName, registry.CalledTools);
        }

        [Fact]
        public async Task ExecuteAsync_ContraFails_JudgeStillRunsWithFallback()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                ToolResult.Fail("Contra: timeout")
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[] { AgentOk(JudgeJson("Approved")) });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());

            // Judge всё равно вызван (Contra fail → JSON с error, не ломает цикл).
            var judgeCalls = registry.CalledTools.Count(t => t == JudgeToolName);
            Assert.Equal(1, judgeCalls);
        }

        [Fact]
        public async Task ExecuteAsync_JudgeFails_ReturnsUncertain()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                ToolResult.Fail("Judge: timeout")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
        }

        // ============ Парсер Judge JSON ============

        [Fact]
        public async Task ExecuteAsync_JudgeMarkdownJson_ParsesCorrectly()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk("```json\n" + JudgeJson("Approved") + "\n```")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
        }

        [Fact]
        public async Task ExecuteAsync_JudgeFreeText_FallsBackToUncertain()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk("Предложение выглядит нормально, но я не до конца уверен.")
            });

            var tool = CreateTool(registry, allowEscalation: false);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
        }

        // ============ Последовательность ============

        [Fact]
        public async Task ExecuteAsync_RoundsHaveSequentialNumbers()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ProJson("v1")), AgentOk(ProJson("v2")), AgentOk(ProJson("v3"))
            });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(ContraJson()), AgentOk(ContraJson()), AgentOk(ContraJson())
            });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(JudgeJson("Rejected")),
                AgentOk(JudgeJson("Rejected")),
                AgentOk(JudgeJson("Approved"))
            });

            var capturedRounds = new List<AgentDebateRoundDto>();
            var tool = CreateTool(registry, onRoundAdded: r => capturedRounds.Add(r));

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Тест" });

            Assert.True(result.Success);
            Assert.Equal(3, capturedRounds.Count);
            Assert.Equal(1, capturedRounds[0].RoundNumber);
            Assert.Equal(2, capturedRounds[1].RoundNumber);
            Assert.Equal(3, capturedRounds[2].RoundNumber);
        }

        [Fact]
        public async Task ExecuteAsync_WithChatId_PersistsSessionAndRounds()
        {
            const int SessionId = 777;

            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[ProToolName] = new Queue<ToolResult>(new[] { AgentOk(ProJson()) });
            registry.ResponsesByTool[ContraToolName] = new Queue<ToolResult>(new[] { AgentOk(ContraJson()) });
            registry.ResponsesByTool[JudgeToolName] = new Queue<ToolResult>(new[] { AgentOk(JudgeJson("Approved")) });

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

            // StartAsync вызван с chatId=42 + PatternType=Debate в snapshot.
            sessionMock.Verify(s => s.StartAsync(
                It.Is<int>(id => id == 42),
                It.IsAny<int>(), It.IsAny<string>(),
                It.Is<AgentDebateConfigSnapshot>(snap => snap.PatternType == "Debate"),
                It.IsAny<CancellationToken>()),
                Times.Once);

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