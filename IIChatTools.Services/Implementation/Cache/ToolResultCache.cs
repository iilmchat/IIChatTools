using System;
using System.Collections.Concurrent;
using System.Threading;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Cache;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Cache
{
    /// <summary>
    /// Реализация кэша результатов инструментов на базе
    /// <see cref="IMemoryCache"/> (v1.8.2, DESIGN § 2.1).
    ///
    /// <para>
    /// <b>Singleton, IDisposable.</b> Обёртка над <see cref="IMemoryCache"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Ключ:</b> <c>tool:{name}:u{userId}:{sha256(canonical_json(args))}</c>.
    /// <c>userId</c> — защита от утечки между пользователями (per-user
    /// инструменты: <c>search_chat_history</c>, <c>search_workspace</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Инвалидация:</b> <see cref="IMemoryCache"/> не поддерживает
    /// prefix-eviction, поэтому для каждого инструмента держим
    /// <see cref="CancellationTokenSource"/> и добавляем к записям
    /// <see cref="CancellationChangeToken"/>. <see cref="InvalidateAll"/>
    /// отменяет CTS — все записи инструмента удаляются автоматически.
    /// </para>
    /// </summary>
    public sealed class ToolResultCache : IToolResultCache, IDisposable
    {
        private readonly IMemoryCache _cache;
        private readonly ToolResultCacheOptions _options;
        private readonly ILogger<ToolResultCache> _logger;

        /// <summary>
        /// Per-tool CTS для префиксной инвалидации. Ключ — имя инструмента
        /// (case-insensitive, как в whitelist).
        /// </summary>
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _invalidationTokens =
            new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);

        private bool _disposed;

        /// <summary>
        /// Создаёт кэш.
        /// </summary>
        /// <param name="cache">Обёрнутый IMemoryCache (Singleton)</param>
        /// <param name="options">Настройки whitelist / TTL / SizeLimit</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public ToolResultCache(
            IMemoryCache cache,
            IOptions<ToolResultCacheOptions> options,
            ILogger<ToolResultCache> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options?.Value ?? new ToolResultCacheOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsCacheable(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            if (!_options.Enabled)
                return false;

            if (_options.Tools == null || _options.Tools.Count == 0)
                return false;

            if (!_options.Tools.TryGetValue(toolName, out var entry))
                return false;

            return entry != null && entry.Enabled;
        }

        /// <inheritdoc />
        public ToolResult TryGet(string toolName, int userId, JObject arguments)
        {
            if (!IsCacheable(toolName) || userId <= 0)
                return null;

            var key = CanonicalJsonHelper.BuildCacheKey(toolName, userId, arguments);

            if (_cache.TryGetValue(key, out var value) && value is ToolResult result)
            {
                return result;
            }

            return null;
        }

        /// <inheritdoc />
        public void Set(string toolName, int userId, JObject arguments, ToolResult result)
        {
            if (!IsCacheable(toolName) || userId <= 0)
                return;

            if (result == null || !result.Success)
                return;

            // TTL — clamp [1, 86400] (DESIGN § 2.1).
            var rawTtl = _options.Tools[toolName].TtlSeconds;
            var ttlSeconds = Math.Clamp(rawTtl, 1, 86_400);

            var key = CanonicalJsonHelper.BuildCacheKey(toolName, userId, arguments);

            // Per-tool CTS — если ещё нет, создаём. Это гарантирует, что все
            // записи инструмента шарят один токен инвалидации.
            var cts = _invalidationTokens.GetOrAdd(
                toolName, _ => new CancellationTokenSource());

            var entryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(ttlSeconds))
                .SetSize(1)
                .AddExpirationToken(new CancellationChangeToken(cts.Token));

            _cache.Set(key, result, entryOptions);

            _logger.LogDebug(
                "Tool cache SET: {Tool} (user={UserId}, ttl={Ttl}s)",
                toolName, userId, ttlSeconds);
        }

        /// <inheritdoc />
        public void InvalidateAll(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return;

            // Явная переменная вместо `out _` — CS1503 в C# 13
            // (прецедент — ExternalLlmCircuitBreaker.RecordSuccess).
            CancellationTokenSource cts;
            if (!_invalidationTokens.TryRemove(toolName, out cts))
                return;

            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* уже dispose — ignore */ }

            try { cts.Dispose(); }
            catch { /* ignore */ }

            _logger.LogInformation(
                "Tool cache INVALIDATED: {Tool} (все записи удалены).", toolName);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kv in _invalidationTokens)
            {
                try { kv.Value.Cancel(); }
                catch { /* ignore */ }

                try { kv.Value.Dispose(); }
                catch { /* ignore */ }
            }

            _invalidationTokens.Clear();
        }
    }
}