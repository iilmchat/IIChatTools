using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Implementation.Tools.Debate;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;
using Microsoft.Extensions.Configuration;   // <--- IConfiguration
using Microsoft.Extensions.Primitives;      // <--- IChangeToken

namespace IIChatTools.Tests.UnitTests.Debate
{
    /// <summary>
    /// Unit-тесты <see cref="ReflectTaskTool"/> (v2.2.0, KI-244, Фаза 5).
    ///
    /// <para>
    /// Покрывают валидацию, happy-path (1 и 2 раунда), MaxRoundsReached,
    /// edge-cases (Uncertain / Rejected без issues / duplicate-guard / ошибки
    /// SubAgentService), persistence в <c>AgentDebateRoundDto</c>, отмену и
    /// SSE-события <c>reflect_*</c>.
    /// </para>
    ///
    /// <para>
    /// Все зависимости — fake-реализации (без Moq): тесты проверяют контракт
    /// инструмента, а не поведение LM Studio.
    /// </para>
    /// </summary>
    public class ReflectTaskToolTests
    {
        private const string AgentName = "reflector_agent";
        private const string DraftPrompt = "DRAFT-PROMPT";
        private const string CritiquePrompt = "CRITIQUE-PROMPT";
        private const int UserId = 1;
        private const int ChatId = 1;
        private const int SessionId = 777;

        // ============================================================
        // Fakes
        // ============================================================

        /// <summary>
        /// Fake <see cref="ISubAgentService"/>: очередь предсказуемых ответов
        /// (draft / critique / rewrite) + лог запросов.
        /// </summary>
        private sealed class FakeSubAgentService : ISubAgentService
        {
            private readonly Queue<Func<SubAgentTaskRequest, SubAgentTaskResult>> _responses
                = new Queue<Func<SubAgentTaskRequest, SubAgentTaskResult>>();

            public List<SubAgentTaskRequest> Requests { get; } = new List<SubAgentTaskRequest>();

            /// <summary>Хук перед выдачей ответа (например, отмена CTS после draft).</summary>
            public Action<ToolExecutionContext, SubAgentTaskRequest> OnExecute { get; set; }

            public void Enqueue(string answer)
                => _responses.Enqueue(_ => new SubAgentTaskResult
                {
                    FinalAnswer = answer,
                    Completed = true,
                    Steps = 1,
                    DurationMs = 1
                });

            public void EnqueueNull()
                => _responses.Enqueue(_ => null);

            public Task<SubAgentTaskResult> ExecuteTaskAsync(
                ToolExecutionContext context, SubAgentTaskRequest request)
            {
                Requests.Add(request);
                OnExecute?.Invoke(context, request);

                if (_responses.Count == 0)
                    return Task.FromResult<SubAgentTaskResult>(null);

                return Task.FromResult(_responses.Dequeue()(request));
            }
        }

        /// <summary>
        /// Fake <see cref="ISubAgentRegistry"/>: один дескриптор
        /// <c>reflector_agent</c> (или <c>null</c> — «агент не зарегистрирован»).
        /// </summary>
        private sealed class FakeSubAgentRegistry : ISubAgentRegistry
        {
            public SubAgentDescriptor Descriptor { get; set; }

            public IReadOnlyList<SubAgentDescriptor> GetAll()
                => Descriptor == null
                    ? Array.Empty<SubAgentDescriptor>()
                    : new[] { Descriptor };

            public IReadOnlyList<SubAgentDescriptor> GetEnabled() => GetAll();

            public SubAgentDescriptor Get(string name) => Descriptor;

            public void Update(SubAgentDescriptor descriptor) => Descriptor = descriptor;

            public void Reset(string name) { }
        }

        /// <summary>
        /// Fake <see cref="IAgentDebateSessionService"/>: запоминает вызовы
        /// StartAsync / AddRoundAsync / CompleteAsync / CancelAsync.
        /// </summary>
        private sealed class FakeAgentDebateSessionService : IAgentDebateSessionService
        {
            public int NextSessionId { get; set; } = SessionId;

            public Exception StartException { get; set; }

            public List<(int ChatId, int UserId, string Task, AgentDebateConfigSnapshot Config)>
                StartCalls { get; } = new List<(int, int, string, AgentDebateConfigSnapshot)>();

            public List<(int SessionId, AgentDebateRoundDto Round)> AddedRounds { get; }
                = new List<(int, AgentDebateRoundDto)>();

            public List<(int SessionId, string Verdict, string Artifact)> Completed { get; }
                = new List<(int, string, string)>();

            public List<(int SessionId, int UserId)> Cancelled { get; }
                = new List<(int, int)>();

            public Task<int> StartAsync(
                int chatId,
                int userId,
                string task,
                AgentDebateConfigSnapshot config,
                CancellationToken cancellationToken = default)
            {
                StartCalls.Add((chatId, userId, task, config));
                if (StartException != null) throw StartException;
                return Task.FromResult(NextSessionId);
            }

            public Task<bool> AddRoundAsync(
                int sessionId,
                AgentDebateRoundDto round,
                CancellationToken cancellationToken = default)
            {
                AddedRounds.Add((sessionId, round));
                return Task.FromResult(true);
            }

            public Task<bool> CompleteAsync(
                int sessionId,
                string finalVerdict,
                string finalArtifactJson,
                CancellationToken cancellationToken = default)
            {
                Completed.Add((sessionId, finalVerdict, finalArtifactJson));
                return Task.FromResult(true);
            }

            public Task<bool> CancelAsync(
                int sessionId,
                int userId,
                CancellationToken cancellationToken = default)
            {
                Cancelled.Add((sessionId, userId));
                return Task.FromResult(true);
            }

            public Task<bool> MarkInProgressAsync(
                int sessionId, CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task<AgentDebateStatusDto> GetStatusAsync(
                int sessionId, int userId, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<IReadOnlyList<AgentDebateStatusDto>> GetSessionsByChatAsync(
                int chatId, int userId, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<bool> InjectFeedbackAsync(
                int sessionId, int userId, string feedback,
                CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
        }

        /// <summary>
        /// Fake <see cref="ChannelWriter{T}"/>: складывает SSE-события в список.
        /// </summary>
        private sealed class FakeEventWriter : ChannelWriter<ChatStreamEvent>
        {
            public List<ChatStreamEvent> Events { get; } = new List<ChatStreamEvent>();

            public override bool TryWrite(ChatStreamEvent item)
            {
                Events.Add(item);
                return true;
            }

            public override bool TryComplete(Exception error = null) => true;

            public override ValueTask<bool> WaitToWriteAsync(
                CancellationToken cancellationToken = default)
                => new ValueTask<bool>(true);

            public override ValueTask WriteAsync(
                ChatStreamEvent item, CancellationToken cancellationToken = default)
            {
                Events.Add(item);
                return default;
            }
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static SubAgentDescriptor CreateDescriptor(
            bool disabled = false,
            string draftPrompt = DraftPrompt,
            string critiquePrompt = CritiquePrompt)
        {
            return new SubAgentDescriptor
            {
                Name = AgentName,
                DisplayName = "Рефлектор",
                Description = "Тестовый reflector_agent",
                Model = "qwen/qwen3-4b-2507",
                MaxSteps = 3,
                SystemPrompt = draftPrompt,
                SelfCritiquePrompt = critiquePrompt,
                Internal = true,
                Disabled = disabled
            };
        }

        private static ReflectTaskTool CreateTool(
            FakeSubAgentService subAgent,
            FakeSubAgentRegistry registry = null,
            FakeAgentDebateSessionService sessions = null)
        {
            registry ??= new FakeSubAgentRegistry { Descriptor = CreateDescriptor() };
            sessions ??= new FakeAgentDebateSessionService();

            // Канонический ConfigurationBuilder вместо FakeConfiguration:
            // поддерживает и indexer, и GetSection — устраняет риск рецидива
            // (FakeConfiguration бросал NotImplementedException в GetSection).
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "SubAgents:reflect_task:MaxRounds", "2" },
                    { "SubAgents:reflect_task:TokenBudget", "50000" }
                })
                .Build();

            return new ReflectTaskTool(
                () => subAgent,
                registry,
                sessions,
                config,
                NullLogger<ReflectTaskTool>.Instance);
        }

        private static ToolExecutionContext CreateContext(
            CancellationToken cancellationToken = default,
            FakeEventWriter eventWriter = null,
            int? chatId = ChatId)
        {
            return new ToolExecutionContext
            {
                UserId = UserId,
                ChatId = chatId,
                CancellationToken = cancellationToken,
                EventWriter = eventWriter
            };
        }

        private static JObject CreateArguments(string task = "Напиши ТЗ на фичу", int? maxRounds = null)
        {
            var args = new JObject { ["task"] = task };
            if (maxRounds.HasValue)
                args["maxRounds"] = maxRounds.Value;
            return args;
        }

        /// <summary>JSON-ответ самокритики.</summary>
        private static string CritiqueJson(string verdict, bool withIssues = true)
        {
            var issues = withIssues
                ? """[{"severity":"Major","description":"issue","suggestion":"fix"}]"""
                : "[]";

            return $$"""{"verdict":"{{verdict}}","issues":{{issues}},"summary":"test summary"}""";
        }

        /// <summary>Ответ для «2 раунда: Rejected → Approved».</summary>
        private static void ScriptRejectedThenApproved(FakeSubAgentService subAgent)
        {
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Rejected"));
            subAgent.Enqueue("DRAFT-2");
            subAgent.Enqueue(CritiqueJson("Approved"));
        }

        // ============================================================
        // 1. Структура инструмента
        // ============================================================

        [Fact]
        public void Name_ReturnsReflectTask()
        {
            var tool = CreateTool(new FakeSubAgentService());

            Assert.Equal("reflect_task", tool.Name);
        }

        [Fact]
        public void RequiresApprovalByDefault_IsTrue()
        {
            var tool = CreateTool(new FakeSubAgentService());

            Assert.True(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public void Parameters_ContainsTaskAndMaxRounds()
        {
            var tool = CreateTool(new FakeSubAgentService());
            var names = tool.Parameters.Select(p => p.Name).ToList();

            Assert.Contains("task", names);
            Assert.Contains("maxRounds", names);
            Assert.True(tool.Parameters.Single(p => p.Name == "task").Required);
            Assert.False(tool.Parameters.Single(p => p.Name == "maxRounds").Required);
        }

        // ============================================================
        // 2. Валидация
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_EmptyTask_ReturnsFail()
        {
            var subAgent = new FakeSubAgentService();
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(
                CreateContext(),
                new JObject { ["task"] = "   " });

            Assert.False(result.Success);
            Assert.Contains("task", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(subAgent.Requests);
            Assert.Empty(sessions.StartCalls);
        }

        [Fact]
        public async Task ExecuteAsync_TaskTooLong_ReturnsFail()
        {
            var subAgent = new FakeSubAgentService();
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(
                CreateContext(),
                CreateArguments(new string('x', 8001)));

            Assert.False(result.Success);
            Assert.Contains("8000", result.Message);
            Assert.Empty(subAgent.Requests);
            Assert.Empty(sessions.StartCalls);
        }

        [Fact]
        public async Task ExecuteAsync_AgentMissing_ReturnsFail()
        {
            var subAgent = new FakeSubAgentService();
            var sessions = new FakeAgentDebateSessionService();
            var registry = new FakeSubAgentRegistry { Descriptor = null };
            var tool = CreateTool(subAgent, registry, sessions);

            var result = await tool.ExecuteAsync(CreateContext(), CreateArguments());

            Assert.False(result.Success);
            Assert.Contains(AgentName, result.Message);
            Assert.Empty(sessions.StartCalls);
            Assert.Empty(subAgent.Requests);
        }

        [Fact]
        public async Task ExecuteAsync_AgentDisabled_ReturnsFail()
        {
            var subAgent = new FakeSubAgentService();
            var registry = new FakeSubAgentRegistry
            {
                Descriptor = CreateDescriptor(disabled: true)
            };
            var tool = CreateTool(subAgent, registry);

            var result = await tool.ExecuteAsync(CreateContext(), CreateArguments());

            Assert.False(result.Success);
            Assert.Contains("отключ", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(subAgent.Requests);
        }

        [Fact]
        public async Task ExecuteAsync_SelfCritiquePromptMissing_ReturnsFail()
        {
            var subAgent = new FakeSubAgentService();
            var registry = new FakeSubAgentRegistry
            {
                Descriptor = CreateDescriptor(critiquePrompt: null)
            };
            var tool = CreateTool(subAgent, registry);

            var result = await tool.ExecuteAsync(CreateContext(), CreateArguments());

            Assert.False(result.Success);
            Assert.Contains("SelfCritiquePrompt", result.Message);
            Assert.Empty(subAgent.Requests);
        }

        [Fact]
        public async Task ExecuteAsync_MaxRoundsZero_ClampsToOne()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Approved"));
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(
                CreateContext(), CreateArguments(maxRounds: 0));

            Assert.True(result.Success);
            Assert.Single(sessions.StartCalls);
            Assert.Equal(1, sessions.StartCalls[0].Config.MaxRounds);
            Assert.Equal("Approved", sessions.Completed.Single().Verdict);
        }

        [Fact]
        public async Task ExecuteAsync_MaxRoundsTen_ClampsToFive_AndAliasWorks()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Approved"));
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);
            var args = new JObject { ["task"] = "test", ["max_rounds"] = 10 };

            var result = await tool.ExecuteAsync(CreateContext(), args);

            Assert.True(result.Success);
            Assert.Equal(5, sessions.StartCalls[0].Config.MaxRounds);
        }

        // ============================================================
        // 3. Happy path
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_ApprovedFirstRound_ReturnsOk()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Approved"));
            var sessions = new FakeAgentDebateSessionService();
            var writer = new FakeEventWriter();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(
                CreateContext(eventWriter: writer), CreateArguments());

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());
            Assert.Equal("DRAFT-1", data["finalArtifact"]?.ToString());
            Assert.Equal(SessionId, data["sessionId"]?.Value<int>());

            Assert.Single(sessions.AddedRounds);
            Assert.Equal("Approved", sessions.Completed.Single().Verdict);
            Assert.Equal(2, subAgent.Requests.Count);   // draft + critique
        }

        [Fact]
        public async Task ExecuteAsync_RejectedThenApproved_ReturnsApproved()
        {
            var subAgent = new FakeSubAgentService();
            ScriptRejectedThenApproved(subAgent);
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(CreateContext(), CreateArguments());

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Approved", data["verdict"]?.ToString());
            Assert.Equal(2, data["totalRounds"]?.Value<int>());
            Assert.Equal("DRAFT-2", data["finalArtifact"]?.ToString());

            Assert.Equal(2, sessions.AddedRounds.Count);
            Assert.Equal("Rejected", sessions.AddedRounds[0].Round.CriticVerdict);
            Assert.Equal("Approved", sessions.AddedRounds[1].Round.CriticVerdict);
            Assert.Equal("Approved", sessions.Completed.Single().Verdict);

            // rewrite идёт с draft-промптом; critique — с critique-промптом.
            Assert.Equal(4, subAgent.Requests.Count);
            Assert.Equal(DraftPrompt, subAgent.Requests[2].SystemPromptOverride);
            Assert.Equal(CritiquePrompt, subAgent.Requests[3].SystemPromptOverride);
        }

        [Fact]
        public async Task ExecuteAsync_AllRejected_ReturnsMaxRoundsReached()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Rejected"));
            subAgent.Enqueue("DRAFT-2");
            subAgent.Enqueue(CritiqueJson("Rejected"));
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(
                CreateContext(), CreateArguments(maxRounds: 2));

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("MaxRoundsReached", data["verdict"]?.ToString());
            Assert.Equal(2, data["totalRounds"]?.Value<int>());
            Assert.Equal("MaxRoundsReached", sessions.Completed.Single().Verdict);
            Assert.Equal(2, sessions.AddedRounds.Count);
        }

        // ============================================================
        // 4. Edge cases
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_CritiqueUncertain_StopsWithUncertain()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Uncertain"));
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(CreateContext(), CreateArguments());

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
            Assert.Equal(1, data["totalRounds"]?.Value<int>());
            Assert.Equal("Uncertain", sessions.Completed.Single().Verdict);
            Assert.Equal(2, subAgent.Requests.Count);   // без rewrite
        }

        [Fact]
        public async Task ExecuteAsync_RejectedWithoutIssues_TreatedAsUncertain()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Rejected", withIssues: false));
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(CreateContext(), CreateArguments());

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
            Assert.Equal("Uncertain", sessions.Completed.Single().Verdict);

            // Раунд сохранён с исходным Rejected-вердиктом, rewrite не вызывался.
            Assert.Single(sessions.AddedRounds);
            Assert.Equal("Rejected", sessions.AddedRounds[0].Round.CriticVerdict);
            Assert.Equal(2, subAgent.Requests.Count);
        }

        [Fact]
        public async Task ExecuteAsync_DuplicateDraft_StopsWithUncertain()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("SAME-DRAFT");
            subAgent.Enqueue(CritiqueJson("Rejected"));
            subAgent.Enqueue("SAME-DRAFT");   // rewrite не изменил текст
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(
                CreateContext(), CreateArguments(maxRounds: 2));

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
            Assert.Equal("Uncertain", sessions.Completed.Single().Verdict);
            Assert.Single(sessions.AddedRounds);   // второй раунд не записан
            Assert.Equal(3, subAgent.Requests.Count);
        }

        [Fact]
        public async Task ExecuteAsync_InitialDraftNull_ReturnsFail()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.EnqueueNull();
            var sessions = new FakeAgentDebateSessionService();
            var writer = new FakeEventWriter();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(
                CreateContext(eventWriter: writer), CreateArguments());

            Assert.False(result.Success);
            Assert.Contains("черновик", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(sessions.Cancelled);
            Assert.Empty(sessions.Completed);
            Assert.Empty(sessions.AddedRounds);

            // reflect_started успел уйти, reflect_completed — нет.
            Assert.Single(writer.Events, e => e.Type == "reflect_started");
            Assert.DoesNotContain(writer.Events, e => e.Type == "reflect_completed");
        }

        [Fact]
        public async Task ExecuteAsync_CritiqueNull_StopsWithUncertain()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.EnqueueNull();
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(CreateContext(), CreateArguments());

            Assert.True(result.Success);
            var data = JObject.FromObject(result.Data);
            Assert.Equal("Uncertain", data["verdict"]?.ToString());
            Assert.Equal(0, data["totalRounds"]?.Value<int>());
            Assert.Equal("Uncertain", sessions.Completed.Single().Verdict);
            Assert.Empty(sessions.AddedRounds);
        }

        // ============================================================
        // 5. Persistence
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_AddRoundAsync_PersistsRoundFields()
        {
            var subAgent = new FakeSubAgentService();
            ScriptRejectedThenApproved(subAgent);
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            await tool.ExecuteAsync(CreateContext(), CreateArguments());

            Assert.Equal(2, sessions.AddedRounds.Count);

            var first = sessions.AddedRounds[0];
            Assert.Equal(SessionId, first.SessionId);
            Assert.Equal(1, first.Round.RoundNumber);
            Assert.Equal("DRAFT-1", first.Round.ActorOutput);
            Assert.Equal("Rejected", first.Round.CriticVerdict);
            Assert.NotNull(first.Round.CriticFeedbackJson);
            Assert.Contains("issues", first.Round.CriticFeedbackJson);

            var second = sessions.AddedRounds[1];
            Assert.Equal(2, second.Round.RoundNumber);
            Assert.Equal("DRAFT-2", second.Round.ActorOutput);
            Assert.Equal("Approved", second.Round.CriticVerdict);
        }

        // ============================================================
        // 6. Отмена
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_PreCancelledToken_ReturnsFail()
        {
            var subAgent = new FakeSubAgentService();
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await tool.ExecuteAsync(
                CreateContext(cts.Token), CreateArguments());

            Assert.False(result.Success);
            Assert.Contains("отменена", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(sessions.StartCalls);
            Assert.Empty(subAgent.Requests);
        }

        [Fact]
        public async Task ExecuteAsync_CancelledAfterDraft_CancelsSessionAndFails()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Approved"));
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);
            using var cts = new CancellationTokenSource();

            // Отменяем токен во время первого вызова (draft).
            subAgent.OnExecute = (_, _) => cts.Cancel();

            var result = await tool.ExecuteAsync(
                CreateContext(cts.Token), CreateArguments());

            Assert.False(result.Success);
            Assert.Single(sessions.Cancelled);
            Assert.Empty(sessions.Completed);
        }

        // ============================================================
        // 7. SSE-события
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_ReflectStarted_EmittedOnce()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Approved"));
            var writer = new FakeEventWriter();
            var tool = CreateTool(subAgent);

            await tool.ExecuteAsync(CreateContext(eventWriter: writer), CreateArguments());

            var started = writer.Events.Where(e => e.Type == "reflect_started").ToList();
            Assert.Single(started);

            var dto = Assert.IsType<ChatReflectStartedDto>(started[0].Data);
            Assert.Equal(SessionId, dto.SessionId);
            Assert.Equal("Reflection", dto.PatternType);
            Assert.Equal(AgentName, dto.Agent);
        }

        [Fact]
        public async Task ExecuteAsync_ReflectRound_EmittedPerRound()
        {
            var subAgent = new FakeSubAgentService();
            ScriptRejectedThenApproved(subAgent);
            var writer = new FakeEventWriter();
            var tool = CreateTool(subAgent);

            await tool.ExecuteAsync(CreateContext(eventWriter: writer), CreateArguments());

            var rounds = writer.Events.Where(e => e.Type == "reflect_round").ToList();
            Assert.Equal(2, rounds.Count);

            var first = Assert.IsType<ChatReflectRoundDto>(rounds[0].Data);
            Assert.Equal(1, first.RoundNumber);
            Assert.Equal("DRAFT-1", first.Draft);
            Assert.Equal("Rejected", first.Verdict);
            Assert.NotNull(first.Issues);

            var second = Assert.IsType<ChatReflectRoundDto>(rounds[1].Data);
            Assert.Equal(2, second.RoundNumber);
            Assert.Equal("Approved", second.Verdict);
        }

        [Fact]
        public async Task ExecuteAsync_ReflectCompleted_EmittedOnce()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Approved"));
            var writer = new FakeEventWriter();
            var tool = CreateTool(subAgent);

            await tool.ExecuteAsync(CreateContext(eventWriter: writer), CreateArguments());

            var completed = writer.Events.Where(e => e.Type == "reflect_completed").ToList();
            Assert.Single(completed);

            var dto = Assert.IsType<ChatReflectCompletedDto>(completed[0].Data);
            Assert.Equal(SessionId, dto.SessionId);
            Assert.Equal("Approved", dto.Verdict);
            Assert.Equal(1, dto.TotalRounds);
            Assert.Equal("DRAFT-1", dto.FinalArtifact);
        }

        // ============================================================
        // 8. Контекст без ChatId
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_NoChatId_PassesZeroToStartAsync()
        {
            var subAgent = new FakeSubAgentService();
            subAgent.Enqueue("DRAFT-1");
            subAgent.Enqueue(CritiqueJson("Approved"));
            var sessions = new FakeAgentDebateSessionService();
            var tool = CreateTool(subAgent, sessions: sessions);

            var result = await tool.ExecuteAsync(
                CreateContext(chatId: null), CreateArguments());

            Assert.True(result.Success);
            Assert.Single(sessions.StartCalls);
            Assert.Equal(0, sessions.StartCalls[0].ChatId);
            Assert.Equal(UserId, sessions.StartCalls[0].UserId);
            Assert.Equal("Reflection", sessions.StartCalls[0].Config.PatternType);
        }
    }    
}
