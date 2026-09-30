using System;
using System.Collections.Generic;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Cache;
using IIChatTools.Services.Implementation.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Cache
{
    /// <summary>
    /// Тесты <see cref="ToolResultCache"/> (v1.8.2, Шаг 1.3).
    ///
    /// <para>
    /// Используют <b>реальный</b> <see cref="MemoryCache"/> — это позволяет
    /// проверить <c>CancellationChangeToken</c> (инвалидацию) и
    /// <c>SetAbsoluteExpiration</c>. Mock <see cref="IMemoryCache"/> дал бы
    /// хрупкий тест, который «проверяет сам себя».
    /// </para>
    /// </summary>
    public class ToolResultCacheTests
    {
        private const int UserId = 1;

        // ============================================================
        // Helpers
        // ============================================================

        /// <summary>
        /// Строит <see cref="ToolResultCacheOptions"/>.
        /// <paramref name="enabled"/> — позиционный первый параметр (не именованный):
        /// иначе вызовы со <c>params</c> дают CS8323 (смешение именованного
        /// и последующего неименованного аргумента).
        /// </summary>
        private static ToolResultCacheOptions BuildOptions(
            bool enabled,
            params (string name, bool entryEnabled, int ttl)[] tools)
        {
            var opts = new ToolResultCacheOptions
            {
                Enabled = enabled,
                SizeLimit = 1000,
                Tools = new Dictionary<string, ToolCacheEntryOptions>(
                    StringComparer.OrdinalIgnoreCase)
            };

            foreach (var (name, entryEnabled, ttl) in tools)
            {
                opts.Tools[name] = new ToolCacheEntryOptions
                {
                    Enabled = entryEnabled,
                    TtlSeconds = ttl
                };
            }

            return opts;
        }

        private static ToolResult Ok(string data = "ok")
            => ToolResult.Ok(new { data });

        private static ToolResultCache CreateCache(
            MemoryCache memCache,
            ToolResultCacheOptions options)
            => new ToolResultCache(
                memCache,
                Options.Create(options),
                NullLogger<ToolResultCache>.Instance);

        // ============================================================
        // IsCacheable
        // ============================================================

        [Fact]
        public void IsCacheable_WhitelistedTool_True()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            Assert.True(cache.IsCacheable("web_search"));
        }

        [Fact]
        public void IsCacheable_NonWhitelistedTool_False()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            Assert.False(cache.IsCacheable("ask_external_llm"));
        }

        [Fact]
        public void IsCacheable_GlobalDisabled_False()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(false, ("web_search", true, 60)));

            Assert.False(cache.IsCacheable("web_search"));
        }

        [Fact]
        public void IsCacheable_EntryDisabled_False()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", false, 60)));

            Assert.False(cache.IsCacheable("web_search"));
        }

        [Fact]
        public void IsCacheable_EmptyName_False()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            Assert.False(cache.IsCacheable(""));
            Assert.False(cache.IsCacheable(null));
        }

        // ============================================================
        // TryGet / Set
        // ============================================================

        [Fact]
        public void TryGet_BeforeSet_Null()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            var result = cache.TryGet("web_search", UserId, new JObject { ["q"] = "x" });

            Assert.Null(result);
        }

        [Fact]
        public void Set_Then_TryGet_ReturnsSameInstance()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            var args = new JObject { ["q"] = "x" };
            var original = Ok();

            cache.Set("web_search", UserId, args, original);
            var cached = cache.TryGet("web_search", UserId, args);

            Assert.Same(original, cached);
        }

        [Fact]
        public void TryGet_DifferentUsers_IsolatedMiss()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("search_chat_history", true, 60)));

            var args = new JObject { ["q"] = "my secrets" };

            cache.Set("search_chat_history", 1, args, Ok());

            // User 2 не должен видеть результат user 1 (per-user изоляция).
            var other = cache.TryGet("search_chat_history", 2, args);
            Assert.Null(other);

            // User 1 видит.
            var own = cache.TryGet("search_chat_history", 1, args);
            Assert.NotNull(own);
        }

        [Fact]
        public void TryGet_DifferentArguments_Null()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            cache.Set("web_search", UserId, new JObject { ["q"] = "a" }, Ok());

            var miss = cache.TryGet("web_search", UserId, new JObject { ["q"] = "b" });
            Assert.Null(miss);
        }

        [Fact]
        public void TryGet_DifferentKeyOrder_Hit()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            cache.Set("web_search", UserId,
                new JObject { ["a"] = 1, ["b"] = 2 }, Ok());

            // Порядок ключей не важен — канонизация.
            var hit = cache.TryGet("web_search", UserId,
                new JObject { ["b"] = 2, ["a"] = 1 });

            Assert.NotNull(hit);
        }

        [Fact]
        public void Set_FailedResult_NotCached()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            var args = new JObject { ["q"] = "x" };
            cache.Set("web_search", UserId, args, ToolResult.Fail("network error"));

            var result = cache.TryGet("web_search", UserId, args);
            Assert.Null(result);
        }

        [Fact]
        public void Set_NonCacheableTool_NoOp()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            var args = new JObject { ["q"] = "x" };
            cache.Set("ask_external_llm", UserId, args, Ok());

            var result = cache.TryGet("ask_external_llm", UserId, args);
            Assert.Null(result);
        }

        [Fact]
        public void TryGet_Set_UserIdZero_NoOp()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            var args = new JObject { ["q"] = "x" };
            cache.Set("web_search", 0, args, Ok());

            // UserId <= 0 → не кэшируется, не читается.
            Assert.Null(cache.TryGet("web_search", 0, args));
            Assert.Null(cache.TryGet("web_search", -1, args));
        }

        // ============================================================
        // InvalidateAll
        // ============================================================

        [Fact]
        public void InvalidateAll_RemovesEntries()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("search_knowledge_base", true, 60)));

            var args = new JObject { ["q"] = "x" };
            cache.Set("search_knowledge_base", UserId, args, Ok());
            Assert.NotNull(cache.TryGet("search_knowledge_base", UserId, args));

            cache.InvalidateAll("search_knowledge_base");

            Assert.Null(cache.TryGet("search_knowledge_base", UserId, args));
        }

        [Fact]
        public void InvalidateAll_DoesNotAffectOtherTools()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true,
                    ("web_search", true, 60),
                    ("wikipedia_search", true, 60)));

            var args = new JObject { ["q"] = "x" };
            cache.Set("web_search", UserId, args, Ok());
            cache.Set("wikipedia_search", UserId, args, Ok());

            cache.InvalidateAll("web_search");

            Assert.Null(cache.TryGet("web_search", UserId, args));
            Assert.NotNull(cache.TryGet("wikipedia_search", UserId, args));
        }

        [Fact]
        public void InvalidateAll_MultipleEntriesRemoved()
        {
            using var memCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
            using var cache = CreateCache(memCache,
                BuildOptions(true, ("web_search", true, 60)));

            // 3 разных запроса от 2 пользователей.
            cache.Set("web_search", 1, new JObject { ["q"] = "a" }, Ok());
            cache.Set("web_search", 1, new JObject { ["q"] = "b" }, Ok());
            cache.Set("web_search", 2, new JObject { ["q"] = "a" }, Ok());

            cache.InvalidateAll("web_search");

            Assert.Null(cache.TryGet("web_search", 1, new JObject { ["q"] = "a" }));
            Assert.Null(cache.TryGet("web_search", 1, new JObject { ["q"] = "b" }));
            Assert.Null(cache.TryGet("web_search", 2, new JObject { ["q"] = "a" }));
        }
    }
}