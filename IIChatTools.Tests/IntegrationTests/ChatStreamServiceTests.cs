using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;                     // v1.5.0 (KI-083, Шаг 6C): AppDbContext
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.LmStudio;    // v1.5.0 (KI-083, Фаза 1): EmbeddingResponse
using IIChatTools.Services.DTO.Rag;         // v1.5.0 (KI-083, Шаг 6C): RetrievedChunkDto
using IIChatTools.Services.DTO.SubAgent;    // v1.4.0 Фаза 5 (KI-052)
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.Tools.SubAgent;  // KI-049
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты ChatStreamService: простой стрим, tool calling loop, approval.
    /// Используют реальный ChatService на InMemory-БД + FakeLmStudioClient.
    /// </summary>
    public class ChatStreamServiceTests
    {
        /// <summary>
        /// Fake ILmStudioClient — отдаёт чанки по итерациям.
        /// Итерация 1 → Iterations[0], итерация 2 → Iterations[1] и т. д.
        /// Если итераций меньше, чем запрошено, отдаётся последняя.
        /// </summary>
        private sealed class FakeLmStudioClient : ILmStudioClient
        {
            /// <summary>Список итераций: каждая — набор чанков.</summary>
            public List<List<ChatCompletionChunk>> Iterations { get; } = new List<List<ChatCompletionChunk>>();

            /// <summary>Счётчик запросов ChatStreamAsync (для ассертов).</summary>
            public int StreamCallCount { get; private set; }

            /// <summary>Совместимость со старыми тестами — используем Iterations[0].</summary>
            public List<ChatCompletionChunk> Chunks =>
                Iterations.Count == 0 ? null : Iterations[0];

            /// <summary>
            /// v1.5.0 (KI-083, Шаг 6C): запоминаем входящие messages каждого
            /// вызова ChatStreamAsync — для проверки auto-inject в system prompt.
            /// </summary>
            public List<JArray> CapturedMessages { get; } = new List<JArray>();

            /// <summary>
            /// Fake-заглушка: реальный вызов не выполняется (тесты стрима используют
            /// <see cref="ChatStreamAsync"/>). Сигнатура соответствует расширенному
            /// интерфейсу (v1.4.0 Фаза 2, KI-052).
            /// </summary>
            /// <param name="messages">История сообщений</param>
            /// <param name="tools">Список инструментов</param>
            /// <param name="cancellationToken">Токен отмены</param>
            /// <param name="model">Опциональная модель (v1.4.0 Фаза 2)</param>
            public Task<ChatCompletionResponse> CompleteAsync(
                JArray messages, JArray tools, CancellationToken cancellationToken, string model = null)
                => throw new NotImplementedException();

            public Task<IReadOnlyList<string>> GetModelIdsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<string>>(new List<string>());

            /// <summary>
            /// v1.5.0 (KI-083): в тестах ChatStreamService embeddings не используются —
            /// заглушка бросает <see cref="NotImplementedException"/>.
            /// </summary>
            public Task<EmbeddingResponse> GetEmbeddingsAsync(
                IReadOnlyList<string> inputs, string model, CancellationToken cancellationToken)
                => throw new NotImplementedException();

            public async IAsyncEnumerable<ChatCompletionChunk> ChatStreamAsync(
                JArray messages,
                JArray tools,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                // v1.5.0 (KI-083, Шаг 6C): фиксируем messages для ассертов.
                CapturedMessages.Add(messages);

                var index = StreamCallCount;
                StreamCallCount++;

                // Если итераций меньше, чем вызовов — отдаём последнюю (повтор для устойчивости)
                var listIndex = Math.Min(index, Iterations.Count - 1);
                if (listIndex < 0)
                {
                    yield break;
                }

                var chunks = Iterations[listIndex];
                foreach (var chunk in chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return chunk;
                    await Task.Yield();
                }
            }
        }

        /// <summary>
        /// v1.5.0 (KI-083, Шаг 6C): fake IRetrievalService для тестов auto-inject.
        /// По умолчанию — пустой результат (нет RAG-контекста).
        /// </summary>
        private sealed class FakeRetrievalService : IRetrievalService
        {
            /// <summary>Список вызовов SearchAsync — для ассертов.</summary>
            public List<(string Query, string Index, int TopK, int? ChatId, int? UserId)> Calls { get; }
                = new List<(string, string, int, int?, int?)>();

            /// <summary>Результат, который вернёт SearchAsync. По умолчанию — пустой.</summary>
            public List<RetrievedChunkDto> NextResult { get; set; } = new List<RetrievedChunkDto>();

            public Task<IReadOnlyList<RetrievedChunkDto>> SearchAsync(
                string query, string indexName, int topK = 5,
                int? chatId = null, int? userId = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add((query, indexName, topK, chatId, userId));
                return Task.FromResult<IReadOnlyList<RetrievedChunkDto>>(NextResult);
            }
        }

        /// <summary>
        /// Fake-резолвер workspace — возвращает временный путь.
        /// </summary>
        private sealed class FakeWorkspaceResolver : IWorkspaceResolver
        {
            public Task<string> GetWorkspacePathAsync(int userId)
                => Task.FromResult(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "iichattools_test_ws",
                    userId.ToString()));
        }

        /// <summary>
        /// Fake-координатор подтверждений — мгновенно отдаёт заданное решение.
        /// В тестах approval-инструментов возвращает Rejected, чтобы проверять
        /// отрицательный сценарий без ожидания.
        /// </summary>
        private sealed class FakeApprovalCoordinator : IChatApprovalCoordinator
        {
            public ChatApprovalDecision NextDecision { get; set; } = ChatApprovalDecision.Rejected;

            /// <summary>Счётчик вызовов WaitForDecisionAsync.</summary>
            public int WaitCallCount { get; private set; }

            /// <summary>Список callId, по которым было ожидание.</summary>
            public List<string> WaitedCallIds { get; } = new List<string>();

            public Task<ChatApprovalDecision> WaitForDecisionAsync(
                string callId,
                TimeSpan timeout,
                CancellationToken cancellationToken = default)
            {
                WaitCallCount++;
                WaitedCallIds.Add(callId);
                return Task.FromResult(NextDecision);
            }

            public Task<bool> ResolveAsync(string callId, ChatApprovalDecision decision)
                => Task.FromResult(true);
        }

        /// <summary>
        /// Реестр fake-инструментов: позволяет зарегистрировать инструменты
        /// с настраиваемыми Name/RequiresApprovalByDefault/Result.
        /// </summary>
        private sealed class FakeToolRegistry : IToolRegistry
        {
            private readonly Dictionary<string, FakeToolDef> _tools
                = new Dictionary<string, FakeToolDef>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Счётчик вызовов ExecuteAsync (для ассертов).</summary>
            public int ExecuteCallCount { get; private set; }

            /// <summary>Имена инструментов, которые были вызваны.</summary>
            public List<string> ExecutedTools { get; } = new List<string>();

            public void Register(string name, bool requiresApproval, ToolResult result, string description = null)
            {
                _tools[name] = new FakeToolDef
                {
                    Name = name,
                    Description = description ?? $"Fake tool: {name}",
                    RequiresApproval = requiresApproval,
                    Result = result
                };
            }

            public IReadOnlyList<ToolDescriptor> GetAllDescriptors()
                => _tools.Values
                    .Select(t => new ToolDescriptor
                    {
                        Name = t.Name,
                        Description = t.Description,
                        RequiresApprovalByDefault = t.RequiresApproval,
                        Parameters = new List<ToolParameterDescriptor>()
                    })
                    .ToList();

            public ToolDescriptor GetDescriptor(string name)
            {
                if (!_tools.TryGetValue(name, out var t)) return null;
                return new ToolDescriptor
                {
                    Name = t.Name,
                    Description = t.Description,
                    RequiresApprovalByDefault = t.RequiresApproval,
                    Parameters = new List<ToolParameterDescriptor>()
                };
            }

            public Task<ToolResult> ExecuteAsync(string toolName, ToolExecutionContext context, JObject arguments)
            {
                ExecuteCallCount++;
                ExecutedTools.Add(toolName);
                if (!_tools.TryGetValue(toolName, out var t))
                    return Task.FromResult(ToolResult.Fail($"Fake tool '{toolName}' not registered"));
                return Task.FromResult(t.Result);
            }

            private sealed class FakeToolDef
            {
                public string Name { get; set; }
                public string Description { get; set; }
                public bool RequiresApproval { get; set; }
                public ToolResult Result { get; set; }
            }
        }

        /// <summary>
        /// Пустой реестр — для тестов, где tools не нужны.
        /// </summary>
        private sealed class EmptyToolRegistry : IToolRegistry
        {
            public IReadOnlyList<ToolDescriptor> GetAllDescriptors() => new List<ToolDescriptor>();
            public ToolDescriptor GetDescriptor(string name) => null;
            public Task<ToolResult> ExecuteAsync(string toolName, ToolExecutionContext context, JObject arguments)
                => Task.FromResult(ToolResult.Fail("not implemented"));
        }

        /// <summary>
        /// Fake-реестр специализированных суб-агентов (v1.4.0 Фаза 5, KI-052).
        /// Возвращает дескрипторы с заданными именами — Chat использует их как whitelist tools.
        /// В тестах вместо реальных агентов (`file_system_agent`, ...) регистрируем
        /// имена инструментов напрямую — так проще проверить tool calling loop.
        /// </summary>
        private sealed class FakeSubAgentRegistry : ISubAgentRegistry
        {
            private readonly List<SubAgentDescriptor> _agents = new List<SubAgentDescriptor>();

            /// <summary>Зарегистрировать дескриптор с указанным именем (enabled).</summary>
            public void Register(string name) => _agents.Add(new SubAgentDescriptor
            {
                Name = name,
                Disabled = false,
                AllowedTools = new List<string>()
            });

            public IReadOnlyList<SubAgentDescriptor> GetAll() => _agents;

            public IReadOnlyList<SubAgentDescriptor> GetEnabled()
                => _agents.Where(a => !a.Disabled).ToList();

            public SubAgentDescriptor Get(string name)
                => _agents.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

            public void Update(SubAgentDescriptor descriptor) { /* not used in tests */ }

            public void Reset(string name) { /* not used in tests */ }
        }

        /// <summary>
        /// Создаёт сервис с реальным ChatService на InMemory-БД.
        /// </summary>
        /// <param name="registry">Fake-реестр инструментов (или EmptyToolRegistry).</param>
        /// <param name="model">Модель чата.</param>
        /// <param name="config">Конфигурация (устаревший параметр, оставлен для совместимости).</param>
        /// <param name="retrievalService">
        /// v1.5.0 (KI-083, Шаг 6C): fake IRetrievalService для тестов auto-inject.
        /// По умолчанию — <see cref="FakeRetrievalService"/> (пустой результат).
        /// </param>
        /// <param name="enabledTools">
        /// v1.4.0 Фаза 5 (KI-052): имена «агентов» (в реальности — инструментов),
        /// которые Chat увидит в списке tools. Пустой список = без tools.
        /// </param>
        private static (ChatStreamService Service, ChatService ChatService, int UserId, int ChatId, FakeLmStudioClient LmClient, IToolRegistry Registry, AppDbContext Db) CreateService(
            IToolRegistry registry = null,
            string model = "test-model",
            IConfiguration config = null,
            IRetrievalService retrievalService = null,
            params string[] enabledTools)
        {
            var db = TestDbContextFactory.Create();
            var chatService = new ChatService(db, NullLogger<ChatService>.Instance);
            var fakeLm = new FakeLmStudioClient();

            var effectiveRegistry = registry ?? new EmptyToolRegistry();
            var fakeResolver = new FakeWorkspaceResolver();
            var fakeApproval = new FakeApprovalCoordinator();
            var effectiveRetrieval = retrievalService ?? new FakeRetrievalService();

            // v1.4.0 Фаза 5 (KI-052): Chat теперь резолвит tools из ISubAgentRegistry.
            var fakeSubAgentRegistry = new FakeSubAgentRegistry();
            if (enabledTools != null)
            {
                foreach (var name in enabledTools)
                    fakeSubAgentRegistry.Register(name);
            }

            var effectiveConfig = config ?? new ConfigurationBuilder().Build();

            var service = new ChatStreamService(
                chatService,
                fakeLm,
                effectiveRegistry,
                fakeSubAgentRegistry,
                fakeResolver,
                fakeApproval,
                new TokenCounter(),   // KI-049
                effectiveConfig,
                NullLogger<ChatStreamService>.Instance,
                db,                   // Шаг 6C
                effectiveRetrieval);  // Шаг 6C

            var chat = chatService.CreateChatAsync(1, model, "Test Chat").GetAwaiter().GetResult();

            return (service, chatService, 1, chat.Id, fakeLm, effectiveRegistry, db);
        }

        // ============================================================
        // Существующие тесты (A.1 / A.2.1 / A.2.2 — простой стрим)
        // ============================================================

        [Fact]
        public async Task StreamAsync_ValidRequest_EmitsStartDeltaDone_AndSavesMessages()
        {
            var (service, chatService, userId, chatId, fakeLm, _, _) = CreateService();

            fakeLm.Iterations.Add(new List<ChatCompletionChunk>
            {
                new ChatCompletionChunk { DeltaContent = "Привет" },
                new ChatCompletionChunk { DeltaContent = ", мир" },
                new ChatCompletionChunk
                {
                    IsDone = true,
                    FinishReason = "stop",
                    Usage = new ChatCompletionUsage { PromptTokens = 10, CompletionTokens = 5 }
                }
            });

            var request = new ChatStreamRequest { ChatId = chatId, Message = "Тест" };

            var events = new List<ChatStreamEvent>();
            await foreach (var evt in service.StreamAsync(request, userId))
            {
                events.Add(evt);
            }

            Assert.NotEmpty(events);
            Assert.Equal("start", events[0].Type);
            Assert.Contains(events, e => e.Type == "delta");
            Assert.Equal("done", events.Last().Type);

            var messages = await chatService.GetMessagesAsync(chatId, userId, 50);
            Assert.Equal(2, messages.Count);
            Assert.Equal("user", messages[0].Role);
            Assert.Equal("Тест", messages[0].Content);
            Assert.Equal("assistant", messages[1].Role);
            Assert.Equal("Привет, мир", messages[1].Content);
            Assert.Equal(10, messages[1].TokensIn);
            Assert.Equal(5, messages[1].TokensOut);
        }

        [Fact]
        public async Task StreamAsync_ChatNotFound_EmitsError()
        {
            var (service, _, userId, _, _, _, _) = CreateService();
            var request = new ChatStreamRequest { ChatId = 99999, Message = "Тест" };

            var events = new List<ChatStreamEvent>();
            await foreach (var evt in service.StreamAsync(request, userId))
            {
                events.Add(evt);
            }

            Assert.Single(events);
            Assert.Equal("error", events[0].Type);
        }

        [Fact]
        public async Task StreamAsync_EmptyMessage_EmitsError()
        {
            var (service, _, userId, chatId, _, _, _) = CreateService();
            var request = new ChatStreamRequest { ChatId = chatId, Message = "" };

            var events = new List<ChatStreamEvent>();
            await foreach (var evt in service.StreamAsync(request, userId))
            {
                events.Add(evt);
            }

            Assert.Single(events);
            Assert.Equal("error", events[0].Type);
        }

        // ============================================================
        // Новые тесты (1.6.B — tool calling loop)
        // ============================================================

        /// <summary>
        /// Проверяет успешный multi-turn tool calling:
        /// итерация 1 — LLM вызывает инструмент; итерация 2 — финальный текст.
        /// </summary>
        [Fact]
        public async Task StreamAsync_ToolCalling_ExecutesToolAndContinues()
        {
            // Arrange
            var registry = new FakeToolRegistry();
            registry.Register(
                name: "list_directory",
                requiresApproval: false,
                result: ToolResult.Ok(new { count = 2, items = new[] { ".tmp", "git-test" } },
                                      "Найдено 2 элемента"));

            // Конфигурация с DefaultAllowedTools — чтобы tools попали в запрос
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["SubAgent:DefaultAllowedTools:0"] = "list_directory"
                })
                .Build();

            var (service, chatService, userId, chatId, fakeLm, _, _) = CreateService(
                registry, config: config, enabledTools: new[] { "list_directory" });

            // Итерация 1: LLM вызывает list_directory.
            // Строим JObject программно — экранирование JSON в verbatim-строке ломается.
            var toolCallJObject = new JObject
            {
                ["index"] = 0,
                ["id"] = "call_1",
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "list_directory",
                    // arguments — это JSON-СТРОКА (как в OpenAI API), а не объект
                    ["arguments"] = "{\"path\":\".\"}"
                }
            };

            fakeLm.Iterations.Add(new List<ChatCompletionChunk>
            {
                new ChatCompletionChunk { DeltaToolCall = toolCallJObject },
                new ChatCompletionChunk { IsDone = true, FinishReason = "tool_calls" }
            });

            // Итерация 2: LLM отдаёт финальный текст
            fakeLm.Iterations.Add(new List<ChatCompletionChunk>
            {
                new ChatCompletionChunk { DeltaContent = "В workspace " },
                new ChatCompletionChunk { DeltaContent = "2 элемента: .tmp, git-test." },
                new ChatCompletionChunk { IsDone = true, FinishReason = "stop" }
            });

            var request = new ChatStreamRequest
            {
                ChatId = chatId,
                Message = "Покажи файлы",
                UseTools = true
            };

            // Act
            var events = new List<ChatStreamEvent>();
            await foreach (var evt in service.StreamAsync(request, userId))
            {
                events.Add(evt);
            }

            // Assert: события SSE
            Assert.Equal("start", events[0].Type);
            Assert.Contains(events, e => e.Type == "tool_call");
            Assert.Contains(events, e => e.Type == "tool_result");
            Assert.Equal("done", events.Last().Type);

            // Assert: инструмент вызван ровно 1 раз
            Assert.Equal(1, registry.ExecuteCallCount);
            Assert.Contains("list_directory", registry.ExecutedTools);

            // Assert: стрим вызван 2 раза (2 итерации)
            Assert.Equal(2, fakeLm.StreamCallCount);

            // Assert: tool_call содержит requiresApproval=false
            var toolCallEvent = events.First(e => e.Type == "tool_call");
            var toolCallDto = (ChatToolCallDto)toolCallEvent.Data;
            Assert.Equal("list_directory", toolCallDto.Name);
            Assert.False(toolCallDto.RequiresApproval);

            // Assert: tool_result success=true
            var toolResultEvent = events.First(e => e.Type == "tool_result");
            var toolResultDto = (ChatToolResultDto)toolResultEvent.Data;
            Assert.True(toolResultDto.Success);
            Assert.Equal("list_directory", toolResultDto.Name);

            // Assert: в БД 4 записи: user, assistant(tool_calls), tool, assistant(final)
            var messages = await chatService.GetMessagesAsync(chatId, userId, 50);
            Assert.Equal(4, messages.Count);
            Assert.Equal("user", messages[0].Role);
            Assert.Equal("assistant", messages[1].Role);
            Assert.NotNull(messages[1].ToolCallsJson);   // assistant с tool_calls
            Assert.Equal("tool", messages[2].Role);
            Assert.Equal("list_directory", messages[2].ToolName);
            Assert.Equal("assistant", messages[3].Role);
            Assert.Contains("2 элемента", messages[3].Content);
        }

        /// <summary>
        /// Проверяет, что approval-инструмент НЕ выполняется,
        /// LLM получает ToolResult.Fail("Требуется подтверждение…").
        /// </summary>
        [Fact]
        public async Task StreamAsync_ToolCalling_RequiresApproval_DoesNotExecute()
        {
            // Arrange
            var registry = new FakeToolRegistry();
            registry.Register(
                name: "save_file",
                requiresApproval: true,
                result: ToolResult.Ok(new { saved = true }));

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["SubAgent:DefaultAllowedTools:0"] = "save_file"
                })
                .Build();

            var (service, chatService, userId, chatId, fakeLm, _, _) = CreateService(
                registry, config: config, enabledTools: new[] { "save_file" });

            // Итерация 1: LLM вызывает save_file.
            var toolCallJObject = new JObject
            {
                ["index"] = 0,
                ["id"] = "call_2",
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "save_file",
                    ["arguments"] = "{\"path\":\"test.txt\",\"content\":\"hi\"}"
                }
            };

            fakeLm.Iterations.Add(new List<ChatCompletionChunk>
            {
                new ChatCompletionChunk { DeltaToolCall = toolCallJObject },
                new ChatCompletionChunk { IsDone = true, FinishReason = "tool_calls" }
            });

            // Итерация 2: LLM «извиняется»
            fakeLm.Iterations.Add(new List<ChatCompletionChunk>
            {
                new ChatCompletionChunk { DeltaContent = "Извините, требуется подтверждение." },
                new ChatCompletionChunk { IsDone = true, FinishReason = "stop" }
            });

            var request = new ChatStreamRequest
            {
                ChatId = chatId,
                Message = "Сохрани файл test.txt",
                UseTools = true
            };

            // Act
            var events = new List<ChatStreamEvent>();
            await foreach (var evt in service.StreamAsync(request, userId))
            {
                events.Add(evt);
            }

            // Assert: инструмент НЕ вызван
            Assert.Equal(0, registry.ExecuteCallCount);

            // Assert: tool_call содержит requiresApproval=true
            var toolCallEvent = events.First(e => e.Type == "tool_call");
            var toolCallDto = (ChatToolCallDto)toolCallEvent.Data;
            Assert.Equal("save_file", toolCallDto.Name);
            Assert.True(toolCallDto.RequiresApproval);

            // Assert: SSE-события approval
            var approvalRequiredEvent = events.FirstOrDefault(e => e.Type == "tool_approval_required");
            var approvalResolvedEvent = events.FirstOrDefault(e => e.Type == "tool_approval_resolved");
            Assert.NotNull(approvalRequiredEvent);
            Assert.NotNull(approvalResolvedEvent);

            // Assert: approval_required
            var approvalRequiredDto = (ChatApprovalRequiredDto)approvalRequiredEvent.Data;
            Assert.Equal("call_2", approvalRequiredDto.Id);
            Assert.Equal("save_file", approvalRequiredDto.Name);
            Assert.NotNull(approvalRequiredDto.Arguments);

            // Assert: approval_resolved
            var approvalResolvedDto = (ChatApprovalResolvedDto)approvalResolvedEvent.Data;
            Assert.Equal("call_2", approvalResolvedDto.Id);
            Assert.Equal("rejected", approvalResolvedDto.Decision);

            // Assert: tool_result success=false + сообщение про отклонение
            var toolResultEvent = events.First(e => e.Type == "tool_result");
            var toolResultDto = (ChatToolResultDto)toolResultEvent.Data;
            Assert.False(toolResultDto.Success);
            Assert.Contains("Пользователь отклонил", toolResultDto.Message);

            // Assert: 4 сообщения в БД
            var messages = await chatService.GetMessagesAsync(chatId, userId, 50);
            Assert.Equal(4, messages.Count);
            Assert.Equal("tool", messages[2].Role);
            Assert.Equal("save_file", messages[2].ToolName);
        }

        // ============================================================
        // v1.5.0 (KI-083, Шаг 6C): auto-inject RAG-контекста в system prompt
        // ============================================================

        /// <summary>
        /// Если в my_rag_docs есть чанки для чата и Retrieval возвращает результат —
        /// в system prompt вставляется RAG-блок (DESIGN § 4.7.5).
        /// </summary>
        [Fact]
        public async Task StreamAsync_AutoInject_RagContextInSystemPrompt()
        {
            // Arrange
            var fakeRetrieval = new FakeRetrievalService
            {
                NextResult = new List<RetrievedChunkDto>
                {
                    new RetrievedChunkDto
                    {
                        ChunkId = 1,
                        Text = "RAG fragment text about yield return",
                        Score = 0.9f,
                        DocumentPath = "test-attachment.txt",
                        ChunkIndex = 0,
                        IndexName = "my_rag_docs"
                    }
                }
            };

            var (service, _, userId, chatId, fakeLm, _, db) = CreateService(
                retrievalService: fakeRetrieval);

            // Чанк в my_rag_docs — чтобы AnyAsync вернул true.
            db.DocumentChunks.Add(new DocumentChunk
            {
                IndexName = "my_rag_docs",
                ChatId = chatId,
                UserId = userId,
                DocumentPath = "test-attachment.txt",
                DocumentHash = "test-hash",
                ChunkIndex = 0,
                Text = "RAG fragment text about yield return",
                Tokens = 10
            });
            await db.SaveChangesAsync();

            fakeLm.Iterations.Add(new List<ChatCompletionChunk>
            {
                new ChatCompletionChunk { DeltaContent = "OK" },
                new ChatCompletionChunk { IsDone = true, FinishReason = "stop" }
            });

            var request = new ChatStreamRequest { ChatId = chatId, Message = "Расскажи про yield return" };

            // Act
            await foreach (var _ in service.StreamAsync(request, userId)) { }

            // Assert: retrieval вызван 1 раз с правильными параметрами.
            Assert.Single(fakeRetrieval.Calls);
            var call = fakeRetrieval.Calls[0];
            Assert.Equal("my_rag_docs", call.Index);
            Assert.Equal(chatId, call.ChatId);
            Assert.Equal(userId, call.UserId);
            Assert.Equal("Расскажи про yield return", call.Query);

            // Assert: system prompt содержит RAG-блок.
            Assert.Single(fakeLm.CapturedMessages);
            var systemMsg = fakeLm.CapturedMessages[0]
                .FirstOrDefault(m => m["role"]?.ToString() == "system")?["content"]?.ToString();
            Assert.NotNull(systemMsg);
            Assert.Contains("релевантные фрагменты", systemMsg);
            Assert.Contains("RAG fragment text about yield return", systemMsg);
            Assert.Contains("test-attachment.txt", systemMsg);
        }

        /// <summary>
        /// Если в my_rag_docs нет чанков для чата — retrieval НЕ вызывается,
        /// system prompt без RAG-блока.
        /// </summary>
        [Fact]
        public async Task StreamAsync_AutoInject_NoRagChunks_NoInjection()
        {
            var fakeRetrieval = new FakeRetrievalService();
            // NextResult пустой, в БД тоже пусто.

            var (service, _, userId, chatId, fakeLm, _, _) = CreateService(
                retrievalService: fakeRetrieval);

            fakeLm.Iterations.Add(new List<ChatCompletionChunk>
            {
                new ChatCompletionChunk { DeltaContent = "OK" },
                new ChatCompletionChunk { IsDone = true, FinishReason = "stop" }
            });

            var request = new ChatStreamRequest { ChatId = chatId, Message = "Привет" };

            await foreach (var _ in service.StreamAsync(request, userId)) { }

            // Retrieval не вызывался (нет чанков).
            Assert.Empty(fakeRetrieval.Calls);

            // В messages нет system-сообщения (у чата пустой SystemPrompt).
            Assert.Single(fakeLm.CapturedMessages);
            var systemMsg = fakeLm.CapturedMessages[0]
                .FirstOrDefault(m => m["role"]?.ToString() == "system");
            Assert.Null(systemMsg);
        }

        /// <summary>
        /// Если все чанки ниже MinScore — RAG-блок не вставляется.
        /// </summary>
        [Fact]
        public async Task StreamAsync_AutoInject_LowScoreFiltered()
        {
            var fakeRetrieval = new FakeRetrievalService
            {
                NextResult = new List<RetrievedChunkDto>
                {
                    new RetrievedChunkDto
                    {
                        ChunkId = 1,
                        Text = "irrelevant text",
                        Score = 0.1f,   // ниже MinScore (0.35)
                        DocumentPath = "test.txt",
                        ChunkIndex = 0,
                        IndexName = "my_rag_docs"
                    }
                }
            };

            var (service, _, userId, chatId, fakeLm, _, db) = CreateService(
                retrievalService: fakeRetrieval);

            db.DocumentChunks.Add(new DocumentChunk
            {
                IndexName = "my_rag_docs",
                ChatId = chatId,
                UserId = userId,
                DocumentPath = "test.txt",
                DocumentHash = "h",
                ChunkIndex = 0,
                Text = "irrelevant text",
                Tokens = 5
            });
            await db.SaveChangesAsync();

            fakeLm.Iterations.Add(new List<ChatCompletionChunk>
            {
                new ChatCompletionChunk { DeltaContent = "OK" },
                new ChatCompletionChunk { IsDone = true, FinishReason = "stop" }
            });

            var request = new ChatStreamRequest { ChatId = chatId, Message = "Что-то" };

            await foreach (var _ in service.StreamAsync(request, userId)) { }

            // Retrieval был вызван (есть чанки), но после фильтра — пусто.
            Assert.Single(fakeRetrieval.Calls);

            // System-сообщения нет (фильтр отсёк низкий score).
            Assert.Single(fakeLm.CapturedMessages);
            var systemMsg = fakeLm.CapturedMessages[0]
                .FirstOrDefault(m => m["role"]?.ToString() == "system");
            Assert.Null(systemMsg);
        }
    }
}