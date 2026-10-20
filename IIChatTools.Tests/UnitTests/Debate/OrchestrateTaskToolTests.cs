using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.DTO.Orchestrator;
using IIChatTools.Services.Implementation.Orchestrator;
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
    /// Unit-тесты <see cref="OrchestrateTaskTool"/>
    /// (v2.0.0, KI-126, Фаза 3, Шаг 3J).
    ///
    /// <para>
    /// По образцу <see cref="PlannerWithDebateToolTests"/>. Покрываем:
    /// структуру, валидацию, декомпозицию, whitelist, параллелизм,
    /// External-detection, persist, SSE, failure-сценарии.
    /// </para>
    /// </summary>
    public class OrchestrateTaskToolTests
    {
        private const string PlannerToolName = "orchestrator_planner_agent";

        // ============================================================
        // Конфиг
        // ============================================================

        /// <summary>
        /// Конфиг по умолчанию: MaxSubtasks=5, MaxParallel=3, whitelist =
        /// {web_agent, code_agent, file_system_agent}, HumanApproval=Never.
        /// </summary>
        private static IConfiguration BuildConfig(
            int maxSubtasks = 5,
            int maxParallel = 3,
            string[] allowedWorkers = null)
        {
            var dict = new Dictionary<string, string>
            {
                ["SubAgents:orchestrate_task:MaxSubtasks"] = maxSubtasks.ToString(),
                ["SubAgents:orchestrate_task:MaxParallel"] = maxParallel.ToString(),
                ["SubAgents:orchestrate_task:TokenBudget"] = "50000",
                ["SubAgents:orchestrate_task:HumanApproval"] = "Never",
                ["SubAgents:orchestrate_task:EscalationProvider"] = "deepseek"
            };

            // Whitelist через индексы (ConfigurationBinder понимает
            // Key:0, Key:1, ... → string[]).
            var workers = allowedWorkers ?? new[] { "web_agent", "code_agent", "file_system_agent" };
            for (var i = 0; i < workers.Length; i++)
            {
                dict[$"SubAgents:orchestrate_task:AllowedWorkerAgents:{i}"] = workers[i];
            }

            return new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();
        }

        // ============================================================
        // Fake-реестр инструментов
        // ============================================================

        private sealed class FakeToolRegistry : IToolRegistry
        {
            public Dictionary<string, Queue<ToolResult>> ResponsesByTool { get; }
                = new Dictionary<string, Queue<ToolResult>>(StringComparer.OrdinalIgnoreCase);

            public List<string> CalledTools { get; } = new List<string>();

            /// <summary>
            /// v2.1.x (KI-258): опциональный хук — вызывается перед выдачей
            /// ответа (например, чтобы отменить CTS между подзадачами).
            /// </summary>
            public Action<string> OnExecute { get; set; }

            /// <summary>
            /// v2.1.x (KI-258): инструменты, для которых <c>ExecuteAsync</c>
            /// бросает <see cref="OperationCanceledException"/> (эмуляция
            /// отмены worker'а).
            /// </summary>
            public HashSet<string> ThrowCancellationFor { get; }
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyList<ToolDescriptor> GetAllDescriptors() => Array.Empty<ToolDescriptor>();
            public ToolDescriptor GetDescriptor(string name) => null;
            public ITool GetTool(string name) => null;

            public Task<ToolResult> ExecuteAsync(
                string toolName, ToolExecutionContext context, JObject arguments)
            {
                CalledTools.Add(toolName);
                OnExecute?.Invoke(toolName);

                // KI-258: эмуляция отмены внутри worker'а.
                if (ThrowCancellationFor.Contains(toolName))
                {
                    throw new OperationCanceledException();
                }

                if (ResponsesByTool.TryGetValue(toolName, out var q) && q.Count > 0)
                {
                    return Task.FromResult(q.Dequeue());
                }
                return Task.FromResult(ToolResult.Fail($"Нет ответа для '{toolName}'"));
            }
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static ToolResult AgentOk(string finalAnswer) =>
            ToolResult.Ok(new { finalAnswer, completed = true }, message: null);

        /// <summary>
        /// Формирует JSON плана от декомпозитора.
        /// </summary>
        private static string PlanJson(
            bool parallel,
            params (string Id, string Agent, string Task)[] subtasks)
        {
            var subs = string.Join(",", subtasks.Select(s =>
                $"{{\"id\":\"{s.Id}\",\"agent\":\"{s.Agent}\",\"task\":\"{s.Task}\"}}"));
            return $"{{\"parallel\":{(parallel ? "true" : "false")}," +
                   $"\"reasoning\":\"test\",\"subtasks\":[{subs}]}}";
        }

        private static OrchestrateTaskTool CreateTool(
            FakeToolRegistry registry,
            int maxSubtasks = 5,
            int maxParallel = 3,
            string[] allowedWorkers = null,
            Mock<IAgentDebateSessionService> sessionMock = null,
            Mock<IAgentBlackboard> blackboardMock = null,
            Action<AgentDebateRoundDto> onRoundAdded = null,
            IOrchestratorOptionsProvider optionsProvider = null)
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

            // v2.1.x (KI-258): отмена сессии (CancelAsync) — дефолт true,
            // чтобы тесты без отмены не падали при неожиданном вызове.
            sessionMock.Setup(s => s.CancelAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            blackboardMock ??= new Mock<IAgentBlackboard>();
            blackboardMock.Setup(b => b.GetOrCreate(It.IsAny<int>()))
                .Returns(() => new JObject());
            blackboardMock.Setup(b => b.Set(
                    It.IsAny<int>(), It.IsAny<string>(), It.IsAny<JToken>()));
            blackboardMock.Setup(b => b.Get(
                    It.IsAny<int>(), It.IsAny<string>()))
                .Returns((JToken)null);
            blackboardMock.Setup(b => b.Snapshot(It.IsAny<int>()))
                .Returns(() => new JObject());
            blackboardMock.Setup(b => b.Clear(It.IsAny<int>()));

            return new OrchestrateTaskTool(
                () => registry,
                sessionMock.Object,
                blackboardMock.Object,
                optionsProvider ?? new OrchestratorOptionsProvider(
                    BuildConfig(maxSubtasks, maxParallel, allowedWorkers),
                    NullLogger<OrchestratorOptionsProvider>.Instance),
                NullLogger<OrchestrateTaskTool>.Instance);
        }

        private static ToolExecutionContext CtxWithChat(int chatId = 1) =>
            new ToolExecutionContext { UserId = 1, ChatId = chatId };

        // ============================================================
        // Структура
        // ============================================================

        [Fact]
        public void Name_ReturnsOrchestrateTask()
        {
            var tool = CreateTool(new FakeToolRegistry());
            Assert.Equal("orchestrate_task", tool.Name);
        }

        [Fact]
        public void RequiresApprovalByDefault_IsTrue()
        {
            var tool = CreateTool(new FakeToolRegistry());
            Assert.True(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public void Description_MentionsOrchestratorAndApproval()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var description = tool.Description;

            Assert.Contains("оркестратор", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("декомпозиц", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("approval", description, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parameters_ContainsTaskAndParallel()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var names = tool.Parameters.Select(p => p.Name).ToList();

            Assert.Contains("task", names);
            Assert.Contains("parallel", names);
        }

        // ============================================================
        // Валидация
        // ============================================================

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
        public async Task ExecuteAsync_TaskTooLong_ReturnsFail()
        {
            var tool = CreateTool(new FakeToolRegistry());
            var longTask = new string('x', 8001);

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = longTask });

            Assert.False(result.Success);
            Assert.Contains("8000", result.Message);
        }

        // ============================================================
        // Декомпозиция
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_PlannerFails_ReturnsFail()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                ToolResult.Fail("Планировщик упал")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.False(result.Success);
            Assert.Contains("Планировщик упал", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_PlannerReturnsInvalidJson_ReturnsFail()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk("это не JSON, а просто текст без скобок")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.False(result.Success);
            Assert.Contains("невалидный JSON", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_PlannerReturnsEmptySubtasks_ReturnsFail()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk("{\"parallel\":false,\"reasoning\":\"n/a\",\"subtasks\":[]}")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.False(result.Success);
            Assert.Contains("пустой", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        // Happy path (последовательно)
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_ThreeSubtasks_Success()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "web_agent", "Найди X"),
                    ("2", "web_agent", "Найди Y"),
                    ("3", "code_agent", "Сравни")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("Результат X"),
                AgentOk("Результат Y")
            });
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("Сравнение X vs Y")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Исследуй" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal(3, data["totalSubtasks"]?.Value<int>());
            Assert.Equal(3, data["completedSubtasks"]?.Value<int>());
            Assert.Equal(0, data["failedSubtasks"]?.Value<int>());
            Assert.Contains("Результаты оркестрации", data["finalArtifact"]?.ToString());

            // Порядок вызовов: Planner → web → web → code.
            Assert.Equal(4, registry.CalledTools.Count);
            Assert.Equal(PlannerToolName, registry.CalledTools[0]);
            Assert.Equal("web_agent", registry.CalledTools[1]);
            Assert.Equal("web_agent", registry.CalledTools[2]);
            Assert.Equal("code_agent", registry.CalledTools[3]);
        }

        // ============================================================
        // Обрезка MaxSubtasks
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_MoreSubtasksThanMax_Truncates()
        {
            var registry = new FakeToolRegistry();

            // Планировщик вернёт 7 подзадач, MaxSubtasks=3.
            var manySubtasks = new (string, string, string)[]
            {
                ("1", "web_agent", "t1"),
                ("2", "web_agent", "t2"),
                ("3", "web_agent", "t3"),
                ("4", "web_agent", "t4"),
                ("5", "web_agent", "t5"),
                ("6", "web_agent", "t6"),
                ("7", "web_agent", "t7")
            };

            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false, manySubtasks))
            });

            // 3 ответа для web_agent.
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("r1"), AgentOk("r2"), AgentOk("r3")
            });

            var tool = CreateTool(registry, maxSubtasks: 3);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal(3, data["totalSubtasks"]?.Value<int>());
            Assert.True(data["truncated"]?.Value<bool>());

            // Planner + 3 web_agent = 4.
            Assert.Equal(4, registry.CalledTools.Count);
        }

        // ============================================================
        // Whitelist
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_AgentNotInWhitelist_Rejected()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "web_agent", "ok"),
                    ("2", "git_agent", "не в whitelist")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("Результат 1")
            });

            // whitelist: только web_agent.
            var tool = CreateTool(registry, allowedWorkers: new[] { "web_agent" });
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal(2, data["totalSubtasks"]?.Value<int>());
            Assert.Equal(1, data["completedSubtasks"]?.Value<int>());
            Assert.Equal(1, data["failedSubtasks"]?.Value<int>());

            // git_agent НЕ вызывался — отклонён до вызова.
            Assert.DoesNotContain("git_agent", registry.CalledTools);

            var subtasks = data["subtasks"] as JArray;
            var rejectedSub = subtasks.First(s => s["id"]?.ToString() == "2");
            Assert.Equal("rejected", rejectedSub["status"]?.ToString());
            Assert.Contains("whitelist", rejectedSub["error"]?.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_RuntimeOverride_IsAppliedOnNextCall()
        {
            // v2.2.2 (KI-245): настройки читаются из провайдера при каждом
            // вызове — изменение whitelist в runtime (как из админки) должно
            // применяться к следующей оркестрации без пересоздания tool'а.
            var provider = new OrchestratorOptionsProvider(
                BuildConfig(allowedWorkers: new[] { "web_agent" }),
                NullLogger<OrchestratorOptionsProvider>.Instance);

            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false, ("1", "code_agent", "первый"))),
                AgentOk(PlanJson(false, ("1", "code_agent", "второй")))
            });
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("Результат")
            });

            var tool = CreateTool(registry, optionsProvider: provider);

            // 1-й вызов: code_agent не в whitelist → rejected, worker не вызван.
            var first = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });
            Assert.True(first.Success);
            var firstData = JObject.FromObject(first.Data);
            Assert.Equal(1, firstData["failedSubtasks"]?.Value<int>());
            Assert.DoesNotContain("code_agent", registry.CalledTools);

            // Runtime-override: теперь code_agent разрешён.
            provider.Update(new OrchestratorSettingsDto
            {
                MaxSubtasks = 5,
                MaxParallel = 3,
                TokenBudget = 50_000,
                HumanApproval = "Never",
                AllowedWorkerAgents = new List<string> { "code_agent" }
            });

            // 2-й вызов: override применён без пересоздания tool'а.
            var second = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });
            Assert.True(second.Success);
            var secondData = JObject.FromObject(second.Data);
            Assert.Equal(1, secondData["completedSubtasks"]?.Value<int>());
            Assert.Contains("code_agent", registry.CalledTools);
        }

        [Fact]
        public async Task ExecuteAsync_RecursiveOrchestrateTask_Rejected()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "orchestrate_task", "рекурсия")))
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal(1, data["failedSubtasks"]?.Value<int>());

            var subtasks = data["subtasks"] as JArray;
            Assert.Equal("rejected", subtasks[0]["status"]?.ToString());
            Assert.Contains("рекурсивный", subtasks[0]["error"]?.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        // Failure Worker'а
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_WorkerFails_StatusIsFailed_ContinueOthers()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "web_agent", "упадёт"),
                    ("2", "web_agent", "пройдёт")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                ToolResult.Fail("web_agent: timeout"),
                AgentOk("Результат 2")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal(2, data["totalSubtasks"]?.Value<int>());
            Assert.Equal(1, data["completedSubtasks"]?.Value<int>());
            Assert.Equal(1, data["failedSubtasks"]?.Value<int>());

            var subtasks = data["subtasks"] as JArray;
            var failed = subtasks.First(s => s["id"]?.ToString() == "1");
            Assert.Equal("failed", failed["status"]?.ToString());
            Assert.Contains("timeout", failed["error"]?.ToString());

            var ok = subtasks.First(s => s["id"]?.ToString() == "2");
            Assert.Equal("completed", ok["status"]?.ToString());
        }

        // ============================================================
        // Persist
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_WithChatId_PersistsSessionAndRounds()
        {
            const int SessionId = 777;

            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "web_agent", "t1"),
                    ("2", "code_agent", "t2")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[] { AgentOk("r1") });
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[] { AgentOk("r2") });

            var capturedRounds = new List<AgentDebateRoundDto>();

            // StartAsync возвращает SessionId — переопределяем дефолт фабрики (777 = тот же).
            var sessionMock = new Mock<IAgentDebateSessionService>();
            sessionMock.Setup(s => s.StartAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SessionId);

            // ВАЖНО (Moq: последний matching setup wins): НЕ настраиваем
            // AddRoundAsync / MarkInProgressAsync / CompleteAsync здесь —
            // иначе перебьём factory-настройку с onRoundAdded callback'ом.
            // Раунды перехватываем через onRoundAdded.

            var tool = CreateTool(
                registry,
                sessionMock: sessionMock,
                onRoundAdded: r => capturedRounds.Add(r));

            var result = await tool.ExecuteAsync(
                CtxWithChat(chatId: 42), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);

            // StartAsync с chatId=42 + PatternType=Orchestrator.
            sessionMock.Verify(s => s.StartAsync(
                It.Is<int>(id => id == 42),
                It.IsAny<int>(), It.IsAny<string>(),
                It.Is<AgentDebateConfigSnapshot>(snap => snap.PatternType == "Orchestrator"),
                It.IsAny<CancellationToken>()),
                Times.Once);

            sessionMock.Verify(s => s.MarkInProgressAsync(
                SessionId, It.IsAny<CancellationToken>()), Times.Once);

            // 2 раунда (по одной на подзадачу).
            Assert.Equal(2, capturedRounds.Count);
            Assert.Equal(1, capturedRounds[0].RoundNumber);
            Assert.Equal("1", capturedRounds[0].SubtaskId);
            Assert.Equal("web_agent", capturedRounds[0].SubtaskAgent);
            Assert.Equal("completed", capturedRounds[0].SubtaskStatus);
            Assert.Equal("r1", capturedRounds[0].SubtaskResult);

            Assert.Equal(2, capturedRounds[1].RoundNumber);
            Assert.Equal("2", capturedRounds[1].SubtaskId);
            Assert.Equal("code_agent", capturedRounds[1].SubtaskAgent);

            sessionMock.Verify(s => s.CompleteAsync(
                SessionId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutChatId_NoPersistence()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false, ("1", "web_agent", "t1")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[] { AgentOk("r1") });

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

        // ============================================================
        // Blackboard
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_ClearsBlackboardInFinally()
        {
            const int SessionId = 777;

            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false, ("1", "web_agent", "t1")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[] { AgentOk("r1") });

            var bbMock = new Mock<IAgentBlackboard>();
            bbMock.Setup(b => b.GetOrCreate(It.IsAny<int>())).Returns(new JObject());
            bbMock.Setup(b => b.Snapshot(It.IsAny<int>())).Returns(new JObject());

            var tool = CreateTool(registry, blackboardMock: bbMock);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            bbMock.Verify(b => b.GetOrCreate(SessionId), Times.Once);
            bbMock.Verify(b => b.Clear(SessionId), Times.Once);
        }

        // ============================================================
        // SSE-события
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_EmitsOrchestratorEvents()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "web_agent", "t1"),
                    ("2", "code_agent", "t2")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[] { AgentOk("r1") });
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[] { AgentOk("r2") });

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

            // RULES § 4.43: JObject.FromObject(DTO) → PascalCase.
            Assert.Contains(events, e => e.Type == "orchestrator_started");
            Assert.Contains(events, e => e.Type == "orchestrator_round");
            Assert.Contains(events, e => e.Type == "orchestrator_completed");

            // orchestrator_started: MaxSubtasks + AllowedWorkerAgents.
            var started = events.First(e => e.Type == "orchestrator_started");
            var startedJson = JObject.FromObject(started.Data);
            Assert.Equal(5, startedJson["MaxSubtasks"]?.Value<int>());
            Assert.NotNull(startedJson["AllowedWorkerAgents"]);

            // Два orchestrator_round (по одной на подзадачу).
            var rounds = events.Where(e => e.Type == "orchestrator_round").ToList();
            Assert.Equal(2, rounds.Count);

            var round1 = JObject.FromObject(rounds[0].Data);
            Assert.Equal("1", round1["SubtaskId"]?.ToString());
            Assert.Equal("web_agent", round1["Agent"]?.ToString());
            Assert.Equal("completed", round1["Status"]?.ToString());

            // orchestrator_completed: totalSubtasks=2.
            var completed = events.First(e => e.Type == "orchestrator_completed");
            var completedJson = JObject.FromObject(completed.Data);
            Assert.Equal(2, completedJson["TotalSubtasks"]?.Value<int>());
            Assert.Equal(2, completedJson["CompletedSubtasks"]?.Value<int>());
        }

        // ============================================================
        // Параллелизм + External-detection
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_ParallelTrue_NoExternal_ParallelPath()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(true,
                    ("1", "web_agent", "t1"),
                    ("2", "web_agent", "t2")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("r1"), AgentOk("r2")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.True(data["parallel"]?.Value<bool>());
            Assert.False(data["hasExternalWorker"]?.Value<bool>());

            // Порядок вызовов при параллелизме недетерминирован
            // (оба web_agent — параллельно), но оба должны быть вызваны.
            Assert.Equal(3, registry.CalledTools.Count);
            Assert.Equal(PlannerToolName, registry.CalledTools[0]);
            Assert.Equal(2, registry.CalledTools.Count(t => t == "web_agent"));
        }

        [Fact]
        public async Task ExecuteAsync_ParallelTrue_WithExternal_SequentialFallback()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(true,
                    ("1", "web_agent", "t1"),
                    ("2", "external_llm_agent", "t2")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[] { AgentOk("r1") });
            registry.ResponsesByTool["external_llm_agent"] = new Queue<ToolResult>(new[] { AgentOk("r2") });

            // whitelist с external_llm_agent.
            var tool = CreateTool(registry,
                allowedWorkers: new[] { "web_agent", "external_llm_agent" });

            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);

            // parallel запрошено, но из-за external — фактически последовательно.
            Assert.True(data["parallelRequested"]?.Value<bool>());
            Assert.False(data["parallel"]?.Value<bool>());
            Assert.True(data["hasExternalWorker"]?.Value<bool>());
            Assert.Equal(1, data["maxParallel"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_ParallelOverrideFalse_IgnoresPlannerParallel()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(true,   // planner говорит parallel=true
                    ("1", "web_agent", "t1"),
                    ("2", "web_agent", "t2")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("r1"), AgentOk("r2")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(),
                new JObject { ["task"] = "Test", ["parallel"] = false });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.False(data["parallel"]?.Value<bool>());
        }

        // ============================================================
        // Сборка финального артефакта
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_FinalArtifact_ContainsAllSubtaskResults()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "web_agent", "Найди A"),
                    ("2", "web_agent", "Найди B")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("Результат A"),
                AgentOk("Результат B")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Исследуй" });

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            var artifact = data["finalArtifact"]?.ToString();

            Assert.Contains("Результаты оркестрации", artifact);
            Assert.Contains("Результат A", artifact);
            Assert.Contains("Результат B", artifact);
            Assert.Contains("web_agent", artifact);
        }

        // ============================================================
        // Кооперативная отмена (v2.1.x, KI-258)
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_CancelledBeforeStart_ReturnsFail()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false, ("1", "web_agent", "t1")))
            });

            var sessionMock = new Mock<IAgentDebateSessionService>();
            var tool = CreateTool(registry, sessionMock: sessionMock);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var ctx = new ToolExecutionContext
            {
                UserId = 1,
                ChatId = 1,
                CancellationToken = cts.Token
            };

            var result = await tool.ExecuteAsync(
                ctx, new JObject { ["task"] = "Test" });

            Assert.False(result.Success);
            Assert.Contains("Операция отменена", result.Message);

            // Планировщик и сессия даже не начинали работу.
            Assert.Empty(registry.CalledTools);
            sessionMock.Verify(s => s.StartAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<AgentDebateConfigSnapshot>(), It.IsAny<CancellationToken>()),
                Times.Never);
            sessionMock.Verify(s => s.CancelAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_CancellationBetweenSubtasks_StopsAndMarksCancelled()
        {
            const int SessionId = 777;

            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "web_agent", "t1"),
                    ("2", "web_agent", "t2"),
                    ("3", "web_agent", "t3")))
            });
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("r1"), AgentOk("r2"), AgentOk("r3")
            });

            using var cts = new CancellationTokenSource();

            // Отменяем токен при первом же вызове worker'а: вторая и третья
            // подзадачи не должны запуститься.
            registry.OnExecute = toolName =>
            {
                if (string.Equals(toolName, "web_agent", StringComparison.OrdinalIgnoreCase))
                {
                    cts.Cancel();
                }
            };

            var sessionMock = new Mock<IAgentDebateSessionService>();
            var tool = CreateTool(registry, sessionMock: sessionMock);

            var ctx = new ToolExecutionContext
            {
                UserId = 1,
                ChatId = 1,
                CancellationToken = cts.Token
            };

            var result = await tool.ExecuteAsync(
                ctx, new JObject { ["task"] = "Test" });

            Assert.False(result.Success);
            Assert.Contains("Операция отменена", result.Message);

            var data = JObject.FromObject(result.Data);
            Assert.Equal(3, data["totalSubtasks"]?.Value<int>());
            Assert.Equal(1, data["completedSubtasks"]?.Value<int>());
            Assert.Equal(0, data["failedSubtasks"]?.Value<int>());
            Assert.Equal(2, data["cancelledSubtasks"]?.Value<int>());
            Assert.True(data["cancelled"]?.Value<bool>());

            var subtasks = data["subtasks"] as JArray;
            Assert.NotNull(subtasks);
            Assert.Equal("completed",
                subtasks.First(s => s["id"]?.ToString() == "1")["status"]?.ToString());
            Assert.Equal("cancelled",
                subtasks.First(s => s["id"]?.ToString() == "2")["status"]?.ToString());
            Assert.Equal("cancelled",
                subtasks.First(s => s["id"]?.ToString() == "3")["status"]?.ToString());

            // Planner + один worker; 2-я и 3-я подзадачи не запускались.
            Assert.Equal(2, registry.CalledTools.Count);
            Assert.Equal(1, registry.CalledTools.Count(t => t == "web_agent"));

            // Сессия отменена через CancelAsync (НЕ CompleteAsync).
            sessionMock.Verify(s => s.CancelAsync(
                SessionId, 1, It.IsAny<CancellationToken>()), Times.Once);
            sessionMock.Verify(s => s.CompleteAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WorkerThrowsOperationCanceled_MarksCancelledNotFailed()
        {
            var registry = new FakeToolRegistry();
            registry.ResponsesByTool[PlannerToolName] = new Queue<ToolResult>(new[]
            {
                AgentOk(PlanJson(false,
                    ("1", "web_agent", "t1"),
                    ("2", "code_agent", "t2")))
            });
            registry.ThrowCancellationFor.Add("web_agent");
            registry.ResponsesByTool["web_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("r1")
            });
            registry.ResponsesByTool["code_agent"] = new Queue<ToolResult>(new[]
            {
                AgentOk("r2")
            });

            var tool = CreateTool(registry);
            var result = await tool.ExecuteAsync(
                CtxWithChat(), new JObject { ["task"] = "Test" });

            Assert.False(result.Success);
            Assert.Contains("Операция отменена", result.Message);

            var data = JObject.FromObject(result.Data);
            Assert.Equal(1, data["cancelledSubtasks"]?.Value<int>());
            Assert.Equal(0, data["failedSubtasks"]?.Value<int>());
            Assert.True(data["cancelled"]?.Value<bool>());

            var subtasks = data["subtasks"] as JArray;
            Assert.NotNull(subtasks);
            var cancelledSub = subtasks.First(s => s["id"]?.ToString() == "1");
            Assert.Equal("cancelled", cancelledSub["status"]?.ToString());
            Assert.NotEqual("failed", cancelledSub["status"]?.ToString());
            Assert.Contains("Операция отменена пользователем",
                cancelledSub["error"]?.ToString());
        }
    }
}
