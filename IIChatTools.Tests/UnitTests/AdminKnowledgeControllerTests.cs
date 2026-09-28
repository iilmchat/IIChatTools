using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Controllers;
using IIChatTools.API.Resources;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Unit-тесты <see cref="AdminKnowledgeController"/>
    /// (v1.5.0, KI-083, Шаг 7D.2).
    ///
    /// <para>
    /// Через fake <see cref="IAdminKnowledgeService"/> (без HTTP).
    /// Авторизация (<c>[Authorize(Policy = "AdminOnly")]</c>) — middleware,
    /// не проверяется в unit-тестах. Полная HTTP-интеграция через
    /// <c>WebApplicationFactory</c> — в Шаге 8.
    /// </para>
    /// </summary>
    public class AdminKnowledgeControllerTests
    {
        // ============================================================
        // Fake-зависимости
        // ============================================================

        /// <summary>
        /// Fake <see cref="IAdminKnowledgeService"/>: запоминает вызовы,
        /// возвращает настраиваемый результат / бросает настраиваемое исключение.
        /// </summary>
        private sealed class FakeAdminKnowledgeService : IAdminKnowledgeService
        {
            // ---- Indexes ----
            public List<RagIndexDto> NextIndexes { get; set; } = new List<RagIndexDto>();
            public Exception NextIndexesException { get; set; }

            // ---- Reindex ----
            public IngestionResultDto NextReindexResult { get; set; } = new IngestionResultDto
            {
                IndexName = "project_docs",
                DocumentChunksCreated = 100,
                TokensTotal = 5000,
                DurationMs = 2000,
                Skipped = false
            };
            public Exception NextReindexException { get; set; }

            // ---- Chunks ----
            public (IReadOnlyList<RagChunkDto> Items, int Page, int PageSize, int TotalCount, int TotalPages)
                NextChunksResult { get; set; }
                = (Array.Empty<RagChunkDto>(), 1, 20, 0, 0);
            public Exception NextChunksException { get; set; }

            // ---- DeleteChunk ----
            public bool NextDeleteChunkResult { get; set; } = true;
            public Exception NextDeleteChunkException { get; set; }

            // ---- GetSettings ----
            public RagSettingsDto NextSettings { get; set; } = new RagSettingsDto();
            public Exception NextSettingsException { get; set; }

            // ---- UpdateSettings ----
            public RagSettingsDto NextUpdatedSettings { get; set; } = new RagSettingsDto();
            public Exception NextUpdateSettingsException { get; set; }

            // ---- Счётчики вызовов ----
            public int GetIndexesCallCount { get; private set; }
            public int ReindexCallCount { get; private set; }
            public int UpdateSettingsCallCount { get; private set; }

            public Task<IReadOnlyList<RagIndexDto>> GetIndexesAsync(
                CancellationToken cancellationToken = default)
            {
                GetIndexesCallCount++;
                if (NextIndexesException != null) throw NextIndexesException;
                return Task.FromResult<IReadOnlyList<RagIndexDto>>(NextIndexes);
            }

            public Task<IngestionResultDto> ReindexProjectDocsAsync(
                CancellationToken cancellationToken = default)
            {
                ReindexCallCount++;
                if (NextReindexException != null) throw NextReindexException;
                return Task.FromResult(NextReindexResult);
            }

            public Task<(IReadOnlyList<RagChunkDto> Items, int Page, int PageSize, int TotalCount, int TotalPages)>
                GetChunksAsync(
                    string indexName, int page, int pageSize,
                    CancellationToken cancellationToken = default)
            {
                if (NextChunksException != null) throw NextChunksException;
                return Task.FromResult(NextChunksResult);
            }

            public Task<bool> DeleteChunkAsync(
                int chunkId, CancellationToken cancellationToken = default)
            {
                if (NextDeleteChunkException != null) throw NextDeleteChunkException;
                return Task.FromResult(NextDeleteChunkResult);
            }

            public Task<RagSettingsDto> GetSettingsAsync(
                CancellationToken cancellationToken = default)
            {
                if (NextSettingsException != null) throw NextSettingsException;
                return Task.FromResult(NextSettings);
            }

            public Task UpdateSettingsAsync(
                RagSettingsDto dto, CancellationToken cancellationToken = default)
            {
                UpdateSettingsCallCount++;
                if (NextUpdateSettingsException != null) throw NextUpdateSettingsException;
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Pass-through <see cref="IStringLocalizer{T}"/> — возвращает ключ как значение.
        /// </summary>
        private sealed class PassThroughLocalizer : IStringLocalizer<SharedResources>
        {
            public LocalizedString this[string name] => new LocalizedString(name, name);

            public LocalizedString this[string name, params object[] arguments]
                => new LocalizedString(name, string.Format(name, arguments));

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
                => Array.Empty<LocalizedString>();
        }

        // ============================================================
        // Хелперы
        // ============================================================

        private static (AdminKnowledgeController Controller, FakeAdminKnowledgeService Service)
            CreateController()
        {
            var service = new FakeAdminKnowledgeService();
            var controller = new AdminKnowledgeController(
                service,
                NullLogger<AdminKnowledgeController>.Instance,
                new PassThroughLocalizer());
            return (controller, service);
        }

        /// <summary>
        /// Достаёт поле success из OkObjectResult.
        /// </summary>
        private static bool GetSuccess(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            var token = JObject.Parse(json);
            return (bool?)token["success"] ?? false;
        }

        /// <summary>
        /// Достаёт поле message из OkObjectResult.
        /// </summary>
        private static string GetMessage(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            var token = JObject.Parse(json);
            return token["message"]?.ToString();
        }

        /// <summary>
        /// Достаёт поле data из OkObjectResult (как JToken).
        /// </summary>
        private static JToken GetData(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            return JObject.Parse(json)["data"];
        }

        /// <summary>
        /// Достаёт поле из JToken (case-insensitive).
        ///
        /// <para>
        /// Зачем: реальный ASP.NET Core MVC сериализует DTO в camelCase
        /// (в smoke-логе — <c>chunkCount</c>, <c>durationMs</c>), а
        /// <c>JsonConvert.SerializeObject</c> в тесте (дефолт Newtonsoft) —
        /// в PascalCase (<c>ChunkCount</c>, <c>DurationMs</c>).
        /// Используя <see cref="StringComparison.OrdinalIgnoreCase"/>, тест
        /// не привязан к настройкам сериализации.
        /// </para>
        ///
        /// <para>
        /// У <see cref="JToken"/> нет встроенного case-insensitive getter
        /// (только <c>Value&lt;T&gt;()</c> / <c>SelectToken()</c> с точным
        /// совпадением), поэтому перебираем <see cref="JObject.Properties"/>
        /// вручную.
        /// </para>
        /// </summary>
        private static JToken Field(JToken token, string name)
        {
            if (token is not JObject obj)
                return null;

            foreach (var prop in obj.Properties())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value;
            }

            return null;
        }

        /// <summary>
        /// Достаёт элемент массива по индексу (защита от null).
        /// </summary>
        private static JToken Item(JToken arrayToken, int index)
        {
            var arr = arrayToken as JArray;
            return arr != null && index >= 0 && index < arr.Count ? arr[index] : null;
        }

        // ============================================================
        // GET /api/admin/knowledge/indexes
        // ============================================================

        /// <summary>
        /// GET /indexes → 200 OK, success=true, 4 индекса.
        /// </summary>
        [Fact]
        public async Task GetIndexesAsync_ReturnsFourIndexes()
        {
            var (controller, service) = CreateController();
            service.NextIndexes = new List<RagIndexDto>
            {
                new RagIndexDto { Name = "project_docs", ChunkCount = 341, DocumentCount = 7 },
                new RagIndexDto { Name = "my_rag_docs",  ChunkCount = 11,  DocumentCount = 3 },
                new RagIndexDto { Name = "chat_history", ChunkCount = 0,   DocumentCount = 0 },
                new RagIndexDto { Name = "workspace",    ChunkCount = 0,   DocumentCount = 0 }
            };

            var result = await controller.GetIndexesAsync(CancellationToken.None);

            Assert.True(GetSuccess(result));
            Assert.Equal(1, service.GetIndexesCallCount);

            var data = GetData(result);
            Assert.NotNull(data);
            Assert.Equal(4, ((JArray)data).Count);

            var first = Item(data, 0);
            Assert.Equal("project_docs", Field(first, "name")?.ToString());
            Assert.Equal(341, Field(first, "chunkCount")?.Value<int>());
        }

        // ============================================================
        // POST /api/admin/knowledge/indexes/project-docs/reindex
        // ============================================================

        /// <summary>
        /// POST /reindex → 200 OK, success=true, data.documentChunksCreated.
        /// </summary>
        [Fact]
        public async Task ReindexProjectDocsAsync_ReturnsResult()
        {
            var (controller, service) = CreateController();
            service.NextReindexResult = new IngestionResultDto
            {
                IndexName = "project_docs",
                DocumentChunksCreated = 245,
                TokensTotal = 100_000,
                DurationMs = 8300,
                Skipped = false
            };

            var result = await controller.ReindexProjectDocsAsync(CancellationToken.None);

            Assert.True(GetSuccess(result));
            Assert.Equal(1, service.ReindexCallCount);

            var data = GetData(result);
            Assert.NotNull(data);
            Assert.Equal(245, Field(data, "documentChunksCreated")?.Value<int>());
            Assert.Equal(8300L, Field(data, "durationMs")?.Value<long>());
        }

        // ============================================================
        // PUT /api/admin/knowledge/settings
        // ============================================================

        /// <summary>
        /// PUT /settings с невалидным ChunkingStrategy → сервис бросает
        /// ArgumentException → контроллер возвращает success=false + сообщение.
        /// </summary>
        [Fact]
        public async Task UpdateSettingsAsync_InvalidStrategy_ReturnsFail()
        {
            var (controller, service) = CreateController();
            service.NextUpdateSettingsException = new ArgumentException(
                "ChunkingStrategy должен быть одним из: recursive, sentence, fixed");

            var dto = new RagSettingsDto
            {
                ChunkingStrategy = "bogus",
                ChunkSize = 500,
                ChunkOverlap = 64,
                MinChunkSize = 100,
                DefaultTopK = 5,
                MinScore = 0.3f,
                EmbeddingModel = "text-embedding-nomic-embed-text-v1.5"
            };

            var result = await controller.UpdateSettingsAsync(dto, CancellationToken.None);

            Assert.False(GetSuccess(result));
            Assert.Contains("ChunkingStrategy", GetMessage(result));
            Assert.Equal(1, service.UpdateSettingsCallCount);
        }
    }
}