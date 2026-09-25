using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
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
    /// Тесты <see cref="SearchWorkspaceTool"/>
    /// (v1.5.0, KI-083, Шаг 5C).
    /// </summary>
    public class SearchWorkspaceToolTests
    {
        // ============================================================
        // Fake-зависимости
        // ============================================================

        /// <summary>
        /// Fake IRetrievalService — запоминает вызовы, возвращает заданный результат.
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

        /// <summary>
        /// Fake IUserSettingsService — только GetBoolAsync для SearchWorkspaceTool.
        /// Остальные методы бросают NotImplementedException (не используются в тестах).
        /// Сигнатуры — с CancellationToken (актуальный контракт IUserSettingsService).
        /// </summary>
        private sealed class FakeUserSettingsService : IUserSettingsService
        {
            /// <summary>Значения bool-настроек по (userId, key).</summary>
            public Dictionary<(int UserId, string Key), bool> BoolValues { get; }
                = new Dictionary<(int, string), bool>();

            public Task<bool> GetBoolAsync(
                int userId, string key, bool defaultValue = false,
                CancellationToken cancellationToken = default)
            {
                if (BoolValues.TryGetValue((userId, key), out var value))
                    return Task.FromResult(value);
                return Task.FromResult(defaultValue);
            }

            // Не используются в тестах SearchWorkspaceTool.
            public Task<string> GetStringAsync(
                int userId, string key, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<int> GetIntAsync(
                int userId, string key, int defaultValue = 0,
                CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<IReadOnlyList<UserSetting>> GetAllForUserAsync(
                int userId, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<IReadOnlyList<UserSetting>> GetAllWithKeyPrefixAsync(
                string keyPrefix, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task SetAsync(
                int userId, string key, string value, string type,
                CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<bool> DeleteAsync(
                int userId, string key, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
        }

        // ============================================================
        // Хелперы
        // ============================================================

        private static SearchWorkspaceTool CreateTool(
            FakeRetrievalService retrieval,
            FakeUserSettingsService settings)
            => new SearchWorkspaceTool(retrieval, settings, NullLogger<SearchWorkspaceTool>.Instance);

        private static ToolExecutionContext Ctx(int userId = 1)
            => new ToolExecutionContext { UserId = userId, WorkspaceRoot = "/tmp", CancellationToken = CancellationToken.None };

        // ============================================================
        // Тесты
        // ============================================================

        /// <summary>
        /// Метаданные инструмента.
        /// </summary>
        [Fact]
        public void Name_IsSearchWorkspace()
        {
            var tool = CreateTool(new FakeRetrievalService(), new FakeUserSettingsService());

            Assert.Equal("search_workspace", tool.Name);
            Assert.False(tool.RequiresApprovalByDefault);
            Assert.Equal(3, tool.Parameters.Count);   // query + topK + filePattern
        }

        /// <summary>
        /// Workspace index отключён → Fail, сервис не вызван.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WorkspaceDisabled_ReturnsFail()
        {
            var retrieval = new FakeRetrievalService();
            var settings = new FakeUserSettingsService();
            // По умолчанию false (не задано) — то же самое.

            var tool = CreateTool(retrieval, settings);
            var result = await tool.ExecuteAsync(Ctx(), new JObject { ["query"] = "test" });

            Assert.False(result.Success);
            Assert.Contains("Workspace index отключён", result.Message);
            Assert.Empty(retrieval.Calls);
        }

        /// <summary>
        /// Workspace index включён → поиск выполняется с правильными параметрами.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WorkspaceEnabled_Searches()
        {
            var retrieval = new FakeRetrievalService();
            var settings = new FakeUserSettingsService();
            settings.BoolValues[(1, "Workspace.Index.Enabled")] = true;

            var tool = CreateTool(retrieval, settings);
            var result = await tool.ExecuteAsync(Ctx(userId: 1), new JObject { ["query"] = "test" });

            Assert.True(result.Success);
            Assert.Single(retrieval.Calls);
            var call = retrieval.Calls[0];
            Assert.Equal("test", call.Query);
            Assert.Equal("workspace", call.Index);
            Assert.Equal(1, call.UserId);
            Assert.Null(call.ChatId);
        }

        /// <summary>
        /// UserId из context передаётся в сервис и в проверку opt-in.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_UsesUserIdFromContext()
        {
            var retrieval = new FakeRetrievalService();
            var settings = new FakeUserSettingsService();
            // Включено только у пользователя 7, у 1 — нет.
            settings.BoolValues[(7, "Workspace.Index.Enabled")] = true;

            var tool = CreateTool(retrieval, settings);

            // Для пользователя 1 (не включено) — Fail.
            var result1 = await tool.ExecuteAsync(Ctx(userId: 1), new JObject { ["query"] = "test" });
            Assert.False(result1.Success);

            // Для пользователя 7 (включено) — OK, UserId=7.
            var result7 = await tool.ExecuteAsync(Ctx(userId: 7), new JObject { ["query"] = "test" });
            Assert.True(result7.Success);
            Assert.Single(retrieval.Calls);
            Assert.Equal(7, retrieval.Calls[0].UserId);
        }

        /// <summary>
        /// filePattern фильтрует результаты по подстроке в DocumentPath.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_FilePatternFilters()
        {
            var retrieval = new FakeRetrievalService
            {
                NextResult = new List<RetrievedChunkDto>
                {
                    new RetrievedChunkDto { ChunkId = 1, Text = "a", Score = 0.9f, DocumentPath = "src/Service.cs" },
                    new RetrievedChunkDto { ChunkId = 2, Text = "b", Score = 0.8f, DocumentPath = "docs/README.md" },
                    new RetrievedChunkDto { ChunkId = 3, Text = "c", Score = 0.7f, DocumentPath = "src/Helper.cs" }
                }
            };
            var settings = new FakeUserSettingsService();
            settings.BoolValues[(1, "Workspace.Index.Enabled")] = true;

            var tool = CreateTool(retrieval, settings);
            var result = await tool.ExecuteAsync(Ctx(), new JObject
            {
                ["query"] = "test",
                ["filePattern"] = ".cs"
            });

            Assert.True(result.Success);
            // Ожидаем 2 .cs-файла (Service.cs + Helper.cs), README.md отфильтрован.
            var data = result.Data as dynamic;
            // Проверяем через сериализацию (dynamic в тестах — неудобно).
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result.Data);
            Assert.Contains("Service.cs", json);
            Assert.Contains("Helper.cs", json);
            Assert.DoesNotContain("README.md", json);
        }
    }
}