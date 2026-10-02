using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Tools.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты <see cref="SearchKnowledgeBaseTool"/>
    /// (v1.5.0, KI-083, Шаг 5B).
    /// </summary>
    public class SearchKnowledgeBaseToolTests
    {
        /// <summary>
        /// Fake-сервис поиска: запоминает вызовы, возвращает заданный результат.
        /// </summary>
        private sealed class FakeRetrievalService : IRetrievalService
        {
            /// <summary>Зафиксированные вызовы SearchAsync.</summary>
            public List<(string Query, string Index, int TopK, int? ChatId, int? UserId)> Calls { get; }
                = new List<(string, string, int, int?, int?)>();

            /// <summary>Что вернуть. По умолчанию — пустой список.</summary>
            public List<RetrievedChunkDto> NextResult { get; set; } = new List<RetrievedChunkDto>();

            /// <summary>Если задано — SearchAsync бросит это исключение.</summary>
            public Exception NextException { get; set; }

            public Task<IReadOnlyList<RetrievedChunkDto>> SearchAsync(
                string query,
                string indexName,
                int topK = 5,
                int? chatId = null,
                int? userId = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add((query, indexName, topK, chatId, userId));

                if (NextException != null)
                    throw NextException;

                return Task.FromResult<IReadOnlyList<RetrievedChunkDto>>(NextResult);
            }
        }

        private static SearchKnowledgeBaseTool CreateTool(FakeRetrievalService fake)
            => new SearchKnowledgeBaseTool(fake, NullLogger<SearchKnowledgeBaseTool>.Instance);

        private static ToolExecutionContext Ctx()
            => new ToolExecutionContext { UserId = 1, WorkspaceRoot = "/tmp", CancellationToken = CancellationToken.None };

        // ============================================================
        // Тесты
        // ============================================================

        /// <summary>
        /// Метаданные инструмента: Name и RequiresApprovalByDefault.
        /// </summary>
        [Fact]
        public void Name_And_RequiresApproval_Are_Correct()
        {
            var tool = CreateTool(new FakeRetrievalService());

            Assert.Equal("search_knowledge_base", tool.Name);
            Assert.False(tool.RequiresApprovalByDefault);
            Assert.Equal(3, tool.Parameters.Count);   // query + topK + indexName
        }

        /// <summary>
        /// Пустой query → Fail, сервис не вызван.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_EmptyQuery_ReturnsFail()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            var result = await tool.ExecuteAsync(Ctx(), new JObject { ["query"] = "" });

            Assert.False(result.Success);
            Assert.Contains("query", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(fake.Calls);
        }

        /// <summary>
        /// Валидный query → сервис вызван с правильными параметрами.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ValidQuery_CallsRetrievalService()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            var result = await tool.ExecuteAsync(Ctx(), new JObject
            {
                ["query"] = "yield return scope",
                ["topK"] = 3
            });

            Assert.True(result.Success);
            Assert.Single(fake.Calls);
            var call = fake.Calls[0];
            Assert.Equal("yield return scope", call.Query);
            Assert.Equal("project_docs", call.Index);
            Assert.Equal(3, call.TopK);
            Assert.Null(call.ChatId);   // project_docs — global
            Assert.Null(call.UserId);   // project_docs — global
        }

        /// <summary>
        /// topK &gt; 20 → clamp до 20.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_TopK_ClampedToMax()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            await tool.ExecuteAsync(Ctx(), new JObject
            {
                ["query"] = "test",
                ["topK"] = 100
            });

            Assert.Single(fake.Calls);
            Assert.Equal(20, fake.Calls[0].TopK);
        }

        /// <summary>
        /// Ошибка в RetrievalService → Fail (не throw).
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_RetrievalServiceThrows_ReturnsFail()
        {
            var fake = new FakeRetrievalService
            {
                NextException = new InvalidOperationException("LM Studio недоступен")
            };
            var tool = CreateTool(fake);

            var result = await tool.ExecuteAsync(Ctx(), new JObject { ["query"] = "test" });

            Assert.False(result.Success);
            Assert.Contains("LM Studio недоступен", result.Message);
        }

        // ============================================================
        // v1.11.0 (KI-130): тесты параметра indexName
        // ============================================================

        /// <summary>
        /// KI-130: indexName='my_rag_docs' → используется context.ChatId,
        /// userId передаётся для per-user изоляции.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_IndexName_MyRagDocs_UsesChatIdFromContext()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            var ctx = new ToolExecutionContext
            {
                UserId = 42,
                WorkspaceRoot = "/tmp",
                ChatId = 777,
                CancellationToken = CancellationToken.None
            };

            var result = await tool.ExecuteAsync(ctx, new JObject
            {
                ["query"] = "code-12345.py",
                ["indexName"] = "my_rag_docs"
            });

            Assert.True(result.Success);
            Assert.Single(fake.Calls);
            var call = fake.Calls[0];
            Assert.Equal("my_rag_docs", call.Index);
            Assert.Equal(777, call.ChatId);
            Assert.Equal(42, call.UserId);
        }

        /// <summary>
        /// KI-130: indexName='my_rag_docs' без ChatId в контексте → Fail,
        /// сервис не вызывается.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_IndexName_MyRagDocs_WithoutChatId_ReturnsFail()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            var ctx = new ToolExecutionContext
            {
                UserId = 42,
                WorkspaceRoot = "/tmp",
                ChatId = null,   // нет активного чата
                CancellationToken = CancellationToken.None
            };

            var result = await tool.ExecuteAsync(ctx, new JObject
            {
                ["query"] = "code-12345.py",
                ["indexName"] = "my_rag_docs"
            });

            Assert.False(result.Success);
            Assert.Contains("my_rag_docs", result.Message);
            Assert.Empty(fake.Calls);
        }

        /// <summary>
        /// KI-130: indexName='project_docs' явно → глобальный поиск (chatId/userId = null).
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_IndexName_ProjectDocs_Explicit_UsesGlobalSearch()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            var ctx = new ToolExecutionContext
            {
                UserId = 42,
                WorkspaceRoot = "/tmp",
                ChatId = 777,   // есть чат, но indexName='project_docs' — global
                CancellationToken = CancellationToken.None
            };

            var result = await tool.ExecuteAsync(ctx, new JObject
            {
                ["query"] = "yield return",
                ["indexName"] = "project_docs"
            });

            Assert.True(result.Success);
            Assert.Single(fake.Calls);
            var call = fake.Calls[0];
            Assert.Equal("project_docs", call.Index);
            Assert.Null(call.ChatId);
            Assert.Null(call.UserId);
        }

        /// <summary>
        /// KI-130: неизвестный indexName → fallback на 'project_docs'.
        /// </summary>
        [Theory]
        [InlineData("unknown_index")]
        [InlineData("MY_RAG_DOCS_TYPO")]
        [InlineData("chat_history")]   // не поддерживается в этом tool'е
        [InlineData("workspace")]      // не поддерживается в этом tool'е
        public async Task ExecuteAsync_IndexName_Unknown_FallsBackToProjectDocs(string badIndex)
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            await tool.ExecuteAsync(Ctx(), new JObject
            {
                ["query"] = "test",
                ["indexName"] = badIndex
            });

            Assert.Single(fake.Calls);
            Assert.Equal("project_docs", fake.Calls[0].Index);
        }
    }
}