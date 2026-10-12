using System;
using System.Collections.Concurrent;
using System.Threading;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Debate
{
    /// <summary>
    /// v2.0.0 (KI-126, Фаза 3): in-memory реализация <see cref="IAgentBlackboard"/>.
    ///
    /// <para>
    /// <b>Lifetime:</b> Singleton.
    /// </para>
    ///
    /// <para>
    /// <b>Потокобезопасность:</b>
    /// <list type="bullet">
    ///   <item><c>ConcurrentDictionary&lt;int, BlackboardEntry&gt;</c> — thread-safe доступ к словарю;</item>
    ///   <item><c>lock (_sync)</c> — синхронизация мутаций <c>JObject</c> (JObject не потокобезопасен);</item>
    ///   <item>нагрузка низкая (5 подзадач × 2-3 операции) — contention минимальный.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Cleanup:</b> Timer каждые 5 минут удаляет сессии, не использованные
    /// более 30 минут (TTL). По образцу <c>AgentDebateCoordinator</c> (KI-043).
    /// </para>
    ///
    /// <para>
    /// <b>Persistence:</b> НЕТ. Blackboard живёт только в памяти. Persistence —
    /// на уровне <c>OrchestrateTaskTool</c> (Шаг 3F) через
    /// <c>AgentDebateRound.ActorOutput</c>.
    /// </para>
    /// </summary>
    public sealed class InMemoryAgentBlackboard : IAgentBlackboard, IDisposable
    {
        private const int DefaultTtlMinutes = 30;
        private const int CleanupIntervalMinutes = 5;

        private readonly ConcurrentDictionary<int, BlackboardEntry> _boards
            = new ConcurrentDictionary<int, BlackboardEntry>();

        private readonly object _sync = new object();
        private readonly ILogger<InMemoryAgentBlackboard> _logger;
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _ttl;
        private bool _disposed;

        /// <summary>
        /// Создаёт Blackboard с TTL 30 мин и Timer cleanup каждые 5 мин.
        /// </summary>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если logger равен null.</exception>
        public InMemoryAgentBlackboard(ILogger<InMemoryAgentBlackboard> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ttl = TimeSpan.FromMinutes(DefaultTtlMinutes);

            _cleanupTimer = new Timer(
                callback: CleanupCallback,
                state: null,
                dueTime: TimeSpan.FromMinutes(CleanupIntervalMinutes),
                period: TimeSpan.FromMinutes(CleanupIntervalMinutes));
        }

        /// <inheritdoc />
        public JObject GetOrCreate(int sessionId)
        {
            if (sessionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(sessionId),
                    "SessionId должен быть положительным.");

            var entry = _boards.GetOrAdd(sessionId, _ => new BlackboardEntry());
            entry.Touch();
            return entry.Data;
        }

        /// <inheritdoc />
        public void Set(int sessionId, string key, JToken value)
        {
            if (sessionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(sessionId),
                    "SessionId должен быть положительным.");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key обязателен.", nameof(key));

            var entry = _boards.GetOrAdd(sessionId, _ => new BlackboardEntry());

            lock (_sync)
            {
                entry.Data[key] = value;
                entry.Touch();
            }
        }

        /// <inheritdoc />
        public JToken Get(int sessionId, string key)
        {
            if (sessionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(sessionId),
                    "SessionId должен быть положительным.");
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (!_boards.TryGetValue(sessionId, out var entry))
                return null;

            lock (_sync)
            {
                entry.Touch();
                return entry.Data[key];
            }
        }

        /// <inheritdoc />
        public JObject Snapshot(int sessionId)
        {
            if (sessionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(sessionId),
                    "SessionId должен быть положительным.");

            if (!_boards.TryGetValue(sessionId, out var entry))
                return new JObject();

            lock (_sync)
            {
                entry.Touch();
                // DeepClone — вызывающий может мутировать копию, не затрагивая оригинал.
                return (JObject)entry.Data.DeepClone();
            }
        }

        /// <inheritdoc />
        public void Clear(int sessionId)
        {
            if (sessionId <= 0)
                return;

            if (_boards.TryRemove(sessionId, out _))
            {
                _logger.LogDebug(
                    "InMemoryAgentBlackboard: Blackboard сессии {SessionId} очищен",
                    sessionId);
            }
        }

        /// <summary>
        /// Timer-callback: удаляет сессии, не использованные более TTL.
        /// Ошибки логируются, не пробрасываются (иначе Timer убьёт приложение).
        /// </summary>
        private void CleanupCallback(object state)
        {
            if (_disposed) return;

            try
            {
                var cutoff = DateTime.UtcNow - _ttl;
                var removed = 0;

                foreach (var kvp in _boards)
                {
                    if (kvp.Value.LastUsedUtc < cutoff)
                    {
                        if (_boards.TryRemove(kvp.Key, out _))
                        {
                            removed++;
                        }
                    }
                }

                if (removed > 0)
                {
                    _logger.LogDebug(
                        "InMemoryAgentBlackboard: cleanup удалил {Count} сессий " +
                        "(TTL {Ttl} мин, осталось {Remaining})",
                        removed, _ttl.TotalMinutes, _boards.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InMemoryAgentBlackboard: cleanup упал");
            }
        }

        /// <summary>
        /// Останавливает Timer и очищает словарь (вызывается хостом при shutdown).
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer?.Dispose();
            _boards.Clear();
        }

        /// <summary>
        /// Внутренняя запись: JObject + LastUsedUtc для TTL-cleanup.
        /// </summary>
        private sealed class BlackboardEntry
        {
            public JObject Data { get; } = new JObject();
            public DateTime LastUsedUtc { get; private set; } = DateTime.UtcNow;

            public void Touch() => LastUsedUtc = DateTime.UtcNow;
        }
    }
}