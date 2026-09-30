using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Cache;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.Cache;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests.Cache
{
    /// <summary>
    /// Интеграционные тесты связки <see cref="ToolRegistry"/> +
    /// <see cref="ToolResultCache"/> (v1.8.2, Шаг 1.3).
    ///
    /// <para>
    /// Проверяют, что кэш реально сокращает повторные вызовы инструментов,
    /// не ломает non-cacheable инструменты и не кэширует Fail.
    /// </para>
    /// </summary>
    public class ToolRegistryCacheTests
    {
        // ============================================================
        // Fake tool
        // ============================================================

        /// <summary>
        /// Fake tool с counter'ом вызовов и настраиваемым success/fail.
        /// </summary>
        private class CountingTool : ITool
        {
            public string Name { get; set; } = "cacheable_tool";
            public string Description => "Test tool";
            public bool RequiresApprovalByDefault => false;
            public IReadOnlyList<ToolParameterDescriptor> Parameters
                => new List<ToolParameterDescriptor>();

            public int ExecutionCount { get; private set; }
            public bool ReturnSuccess { get; set; } = true;

            public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
            {
                ExecutionCount++;
                return Task.FromResult(ReturnSuccess
                    ? ToolResult.Ok(new { count = ExecutionCount })
                    : ToolResult.Fail("boom"));
            }
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static ToolResultCacheOptions CacheOptions(
            params string[] whitelistedTools)
        {
            var opts = new ToolResultCacheOptions
            {
                Enabled = true,
                SizeLimit = 1000,
                Tools = new Dictionary<string, ToolCacheEntryOptions>(
                    StringComparer.OrdinalIgnoreCase)
            };

            foreach (var name in whitelistedTools)
            {
                opts.Tools[name] = new ToolCacheEntryOptions
                {
                    Enabled = true,
                    TtlSeconds = 60
                };
            }

            return opts;
        }

        private static ToolExecutionContext Context(int userId = 1)
            => new ToolExecutionContext { UserId = userId, WorkspaceRoot = "/tmp" };

        // ============================================================
        // Tests
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_NoCache_AlwaysExecutes()
        {
            // Без кэша (resultCache = null) — поведение v1.8.1.
            var tool = new CountingTool();
            var registry = new ToolRegistry(
                new[] { (ITool)tool },
                NullLogger<ToolRegistry>.Instance,
                resultCache: null);

            var args = new JObject { ["q"] = "x" };
            await registry.ExecuteAsync(tool.Name, Context(), args);
            await registry.ExecuteAsync(tool.Name, Context(), args);

            Assert.Equal(2, tool.ExecutionCount);
        }

        [Fact]
        public async Task ExecuteAsync_CacheableTool_SecondCallHit()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = new ToolResultCache(
                memCache,
                Options.Create(CacheOptions("cacheable_tool")),
                NullLogger<ToolResultCache>.Instance);

            var tool = new CountingTool { Name = "cacheable_tool" };
            var registry = new ToolRegistry(
                new[] { (ITool)tool },
                NullLogger<ToolRegistry>.Instance,
                resultCache: cache);

            var args = new JObject { ["q"] = "x" };

            var first = await registry.ExecuteAsync(tool.Name, Context(), args);
            var second = await registry.ExecuteAsync(tool.Name, Context(), args);

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.Same(first, second);       // тот же экземпляр из кэша
            Assert.Equal(1, tool.ExecutionCount);   // second — из кэша
        }

        [Fact]
        public async Task ExecuteAsync_NonCacheableTool_AlwaysExecutes()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = new ToolResultCache(
                memCache,
                Options.Create(CacheOptions("other_tool")),   // cacheable_tool — НЕ в whitelist
                NullLogger<ToolResultCache>.Instance);

            var tool = new CountingTool { Name = "cacheable_tool" };
            var registry = new ToolRegistry(
                new[] { (ITool)tool },
                NullLogger<ToolRegistry>.Instance,
                resultCache: cache);

            var args = new JObject { ["q"] = "x" };
            await registry.ExecuteAsync(tool.Name, Context(), args);
            await registry.ExecuteAsync(tool.Name, Context(), args);

            Assert.Equal(2, tool.ExecutionCount);
        }

        [Fact]
        public async Task ExecuteAsync_FailedResult_NotCached()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = new ToolResultCache(
                memCache,
                Options.Create(CacheOptions("cacheable_tool")),
                NullLogger<ToolResultCache>.Instance);

            var tool = new CountingTool { Name = "cacheable_tool", ReturnSuccess = false };
            var registry = new ToolRegistry(
                new[] { (ITool)tool },
                NullLogger<ToolRegistry>.Instance,
                resultCache: cache);

            var args = new JObject { ["q"] = "x" };

            var r1 = await registry.ExecuteAsync(tool.Name, Context(), args);
            var r2 = await registry.ExecuteAsync(tool.Name, Context(), args);

            Assert.False(r1.Success);
            Assert.False(r2.Success);
            Assert.Equal(2, tool.ExecutionCount);   // Fail не кэширован → второй вызов реальный
        }

        [Fact]
        public async Task ExecuteAsync_UserIdZero_NotCached()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = new ToolResultCache(
                memCache,
                Options.Create(CacheOptions("cacheable_tool")),
                NullLogger<ToolResultCache>.Instance);

            var tool = new CountingTool { Name = "cacheable_tool" };
            var registry = new ToolRegistry(
                new[] { (ITool)tool },
                NullLogger<ToolRegistry>.Instance,
                resultCache: cache);

            var args = new JObject { ["q"] = "x" };

            // UserId = 0 → cacheable = false (нет userId для ключа).
            await registry.ExecuteAsync(tool.Name, Context(userId: 0), args);
            await registry.ExecuteAsync(tool.Name, Context(userId: 0), args);

            Assert.Equal(2, tool.ExecutionCount);
        }

        [Fact]
        public async Task ExecuteAsync_DifferentUsers_IsolatedCache()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = new ToolResultCache(
                memCache,
                Options.Create(CacheOptions("cacheable_tool")),
                NullLogger<ToolResultCache>.Instance);

            var tool = new CountingTool { Name = "cacheable_tool" };
            var registry = new ToolRegistry(
                new[] { (ITool)tool },
                NullLogger<ToolRegistry>.Instance,
                resultCache: cache);

            var args = new JObject { ["q"] = "x" };

            await registry.ExecuteAsync(tool.Name, Context(userId: 1), args);
            await registry.ExecuteAsync(tool.Name, Context(userId: 2), args);
            await registry.ExecuteAsync(tool.Name, Context(userId: 1), args);   // hit для user 1

            // 2 реальных вызова (user 1 + user 2), третий — из кэша user 1.
            Assert.Equal(2, tool.ExecutionCount);
        }
    }
}