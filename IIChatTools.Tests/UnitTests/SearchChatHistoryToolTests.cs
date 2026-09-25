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
    /// Тесты <see cref="SearchChatHistoryTool"/>
    /// (v1.5.0, KI-083, Шаг 5B).
    /// </summary>
    public class SearchChatHistoryToolTests
    {
        /// <summary>
        /// Fake-сервис поиска: запоминает вызовы, возвращает заданный результат.
        /// </summary>
        private sealed class FakeRetrievalService : IRetrievalService
        {
            public List<(string Query, string Index, int TopK, int? ChatId, int? UserId)> Calls { get; }
                = new List<(string, string, int, int?, int?)>();

            public List<RetrievedChunkDto> NextResult { get; set; } = new List<RetrievedChunkDto>();

            public Task<IReadOnlyList<RetrievedChunkDto>> SearchAsync(
                string query,
                string indexName,
                int topK = 5,
                int? chatId = null,
                int? userId = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add((query, indexName, topK, chatId, userId));
                return Task.FromResult<IReadOnlyList<RetrievedChunkDto>>(NextResult);
            }
        }

        private static SearchChatHistoryTool CreateTool(FakeRetrievalService fake)
            => new SearchChatHistoryTool(fake, NullLogger<SearchChatHistoryTool>.Instance);

        private static ToolExecutionContext Ctx(int userId = 1)
            => new ToolExecutionContext { UserId = userId, WorkspaceRoot = "/tmp", CancellationToken = CancellationToken.None };

        // ============================================================
        // Тесты
        // ============================================================

        /// <summary>
        /// Метаданные инструмента.
        /// </summary>
        [Fact]
        public void Name_IsSearchChatHistory()
        {
            var tool = CreateTool(new FakeRetrievalService());

            Assert.Equal("search_chat_history", tool.Name);
            Assert.False(tool.RequiresApprovalByDefault);
            Assert.Equal(3, tool.Parameters.Count);   // query + topK + chatId
        }

        /// <summary>
        /// Пустой query → Fail.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_EmptyQuery_ReturnsFail()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            var result = await tool.ExecuteAsync(Ctx(), new JObject { ["query"] = "" });

            Assert.False(result.Success);
            Assert.Empty(fake.Calls);
        }

        /// <summary>
        /// UserId из context передаётся в сервис.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_UsesUserIdFromContext()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            await tool.ExecuteAsync(Ctx(userId: 7), new JObject { ["query"] = "тест" });

            Assert.Single(fake.Calls);
            Assert.Equal(7, fake.Calls[0].UserId);
            Assert.Equal("chat_history", fake.Calls[0].Index);
        }

        /// <summary>
        /// UserId = 0 → Fail (защита от утечки между юзерами).
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_UserIdZero_ReturnsFail()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            var result = await tool.ExecuteAsync(Ctx(userId: 0), new JObject { ["query"] = "test" });

            Assert.False(result.Success);
            Assert.Contains("UserId", result.Message);
            Assert.Empty(fake.Calls);
        }

        /// <summary>
        /// Опциональный chatId передаётся в сервис как фильтр.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_OptionalChatIdFilter()
        {
            var fake = new FakeRetrievalService();
            var tool = CreateTool(fake);

            await tool.ExecuteAsync(Ctx(), new JObject
            {
                ["query"] = "test",
                ["chatId"] = 42
            });

            Assert.Single(fake.Calls);
            Assert.Equal(42, fake.Calls[0].ChatId);
        }
    }
}