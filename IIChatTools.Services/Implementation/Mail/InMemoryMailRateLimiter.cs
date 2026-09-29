using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.Mail
{
    /// <summary>
    /// In-memory rate limiter для Mail Agent (v1.8.0, KI-107, DESIGN § 6.4).
    ///
    /// <para>
    /// Per-user лимиты:
    /// <list type="bullet">
    ///   <item><c>SendsPerHour</c> — 20;</item>
    ///   <item><c>SendsPerMinute</c> — 2;</item>
    ///   <item><c>ReadsPerMinute</c> — 30;</item>
    ///   <item><c>MaxHourlyBytesPerUser</c> — 50 МБ.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Lifecycle:</b> Singleton, <see cref="IDisposable"/> — Timer cleanup
    /// каждые 5 минут удаляет записи пользователей без активности > 2 часов
    /// (по образцу KI-043).
    /// </para>
    /// </summary>
    public sealed class InMemoryMailRateLimiter : IMailRateLimiter, IDisposable
    {
        private static readonly TimeSpan HourWindow = TimeSpan.FromHours(1);
        private static readonly TimeSpan MinuteWindow = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(2);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

        private readonly MailRateLimitOptions _options;
        private readonly ILogger<InMemoryMailRateLimiter> _logger;
        private readonly ConcurrentDictionary<int, UserState> _states =
            new ConcurrentDictionary<int, UserState>();
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        /// <summary>
        /// Создаёт limiter.
        /// </summary>
        /// <param name="options">Опции (лимиты) из appsettings:Mail:RateLimit</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public InMemoryMailRateLimiter(
            IOptions<MailRateLimitOptions> options,
            ILogger<InMemoryMailRateLimiter> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _cleanupTimer = new Timer(
                CleanupStaleStates,
                state: null,
                dueTime: CleanupInterval,
                period: CleanupInterval);
        }

        /// <inheritdoc />
        public RateLimitResult CheckSend(int userId)
        {
            if (userId <= 0)
                return new RateLimitResult { Allowed = false, Reason = "UserId не задан." };

            var now = DateTime.UtcNow;
            var state = _states.GetOrAdd(userId, _ => new UserState());

            lock (state)
            {
                TrimQueue(state.Sends, now - HourWindow);

                // Per-hour.
                if (state.Sends.Count >= _options.SendsPerHour)
                {
                    var oldest = state.Sends.Peek();
                    var retryAfter = (int)Math.Ceiling(
                        (oldest + HourWindow - now).TotalSeconds);

                    _logger.LogWarning(
                        "Mail: rate limit SendsPerHour exceeded (userId={UserId}, count={Count})",
                        userId, state.Sends.Count);

                    return new RateLimitResult
                    {
                        Allowed = false,
                        RetryAfterSeconds = Math.Max(1, retryAfter),
                        Reason = $"Превышен лимит: {_options.SendsPerHour} писем/час"
                    };
                }

                // Per-minute.
                var inLastMinute = CountInWindow(state.Sends, now - MinuteWindow);
                if (inLastMinute >= _options.SendsPerMinute)
                {
                    _logger.LogWarning(
                        "Mail: rate limit SendsPerMinute exceeded (userId={UserId}, count={Count})",
                        userId, inLastMinute);

                    return new RateLimitResult
                    {
                        Allowed = false,
                        RetryAfterSeconds = 60,
                        Reason = $"Превышен лимит: {_options.SendsPerMinute} писем/мин"
                    };
                }

                state.Sends.Enqueue(now);
                return new RateLimitResult { Allowed = true };
            }
        }

        /// <inheritdoc />
        public RateLimitResult CheckRead(int userId)
        {
            if (userId <= 0)
                return new RateLimitResult { Allowed = false, Reason = "UserId не задан." };

            var now = DateTime.UtcNow;
            var state = _states.GetOrAdd(userId, _ => new UserState());

            lock (state)
            {
                TrimQueue(state.Reads, now - HourWindow);

                var inLastMinute = CountInWindow(state.Reads, now - MinuteWindow);
                if (inLastMinute >= _options.ReadsPerMinute)
                {
                    _logger.LogWarning(
                        "Mail: rate limit ReadsPerMinute exceeded (userId={UserId}, count={Count})",
                        userId, inLastMinute);

                    return new RateLimitResult
                    {
                        Allowed = false,
                        RetryAfterSeconds = 60,
                        Reason = $"Превышен лимит: {_options.ReadsPerMinute} чтений/мин"
                    };
                }

                state.Reads.Enqueue(now);
                return new RateLimitResult { Allowed = true };
            }
        }

        /// <inheritdoc />
        public void RecordBytesSent(int userId, long bytes)
        {
            if (userId <= 0 || bytes <= 0) return;

            var now = DateTime.UtcNow;
            var state = _states.GetOrAdd(userId, _ => new UserState());

            lock (state)
            {
                // Скользящее окно на час — сбрасываем, если прошёл час.
                if ((now - state.BytesWindowStart) >= HourWindow)
                {
                    state.BytesWindowStart = now;
                    state.BytesThisHour = 0;
                }
                state.BytesThisHour += bytes;
            }
        }

        /// <summary>
        /// Фоновый cleanup: удаляет состояния пользователей без активности > 2 часов.
        /// </summary>
        private void CleanupStaleStates(object _)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.UtcNow;
                var removed = 0;

                foreach (var kv in _states)
                {
                    bool stale;
                    lock (kv.Value)
                    {
                        stale = (kv.Value.Sends.Count == 0 && kv.Value.Reads.Count == 0)
                            || (LastActivity(kv.Value) < now - StaleThreshold);
                    }

                    if (!stale) continue;

                    // Явная переменная вместо `out _` — CS1503 в .NET 10 / C# 13:
                    // компилятор не смог вывести тип discard'а из-за перегрузок
                    // ConcurrentDictionary.TryRemove (TKey, out TValue) vs
                    // (KeyValuePair<TKey, TValue>).
                    UserState removedValue;
                    if (_states.TryRemove(kv.Key, out removedValue))
                        removed++;
                }

                if (removed > 0)
                {
                    _logger.LogDebug(
                        "Mail: rate limiter cleanup removed {Count} stale states",
                        removed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mail: rate limiter cleanup error");
            }
        }

        /// <summary>
        /// Убирает из очереди всё, что старше cutoff.
        /// </summary>
        private static void TrimQueue(Queue<DateTime> q, DateTime cutoff)
        {
            while (q.Count > 0 && q.Peek() < cutoff)
                q.Dequeue();
        }

        /// <summary>
        /// Считает элементы в окне (после cutoff).
        /// </summary>
        private static int CountInWindow(Queue<DateTime> q, DateTime cutoff)
        {
            var count = 0;
            foreach (var t in q)
            {
                if (t >= cutoff) count++;
            }
            return count;
        }

        /// <summary>
        /// Возвращает последнюю активность (max из Sends/Reads).
        /// </summary>
        private static DateTime LastActivity(UserState state)
        {
            var lastSend = state.Sends.Count > 0
                ? state.Sends.Peek() : DateTime.MinValue;
            var lastRead = state.Reads.Count > 0
                ? state.Reads.Peek() : DateTime.MinValue;
            // Peek возвращает самый старый (FIFO), для LastActivity нужен самый новый.
            // Итерируем — но с ограничением: очереди обычно маленькие.
            var maxSend = DateTime.MinValue;
            foreach (var t in state.Sends) if (t > maxSend) maxSend = t;
            var maxRead = DateTime.MinValue;
            foreach (var t in state.Reads) if (t > maxRead) maxRead = t;
            return maxSend > maxRead ? maxSend : maxRead;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cleanupTimer?.Dispose(); }
            catch { /* ignore */ }
        }

        // ============ Внутреннее состояние ============

        private sealed class UserState
        {
            public Queue<DateTime> Sends { get; } = new Queue<DateTime>();
            public Queue<DateTime> Reads { get; } = new Queue<DateTime>();
            public long BytesThisHour { get; set; }
            public DateTime BytesWindowStart { get; set; } = DateTime.UtcNow;
        }
    }
}