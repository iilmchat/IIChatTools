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
            Assert.Equal(2, tool.Parameters.Count);   // query + topK
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
    }
}