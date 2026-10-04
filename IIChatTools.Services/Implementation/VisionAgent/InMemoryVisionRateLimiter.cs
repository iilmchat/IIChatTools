using System;
using System.Collections.Concurrent;
using System.Threading;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// In-memory rate limiter для Vision Agent. Fixed-window:
    /// <c>MaxTasksPerUserPer5Min</c> задач на пользователя за 5 минут.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.4). См. DESIGN § 6.3.
    /// </para>
    /// <para>
    /// <b>Singleton, IDisposable.</b> State — <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// с per-user lock на обновление. Cleanup — <see cref="Timer"/> каждые 5 минут
    /// удаляет записи старше 10 минут (по образцу KI-043, KI-107).
    /// </para>
    /// <para>
    /// <b>Fixed-window:</b> окно начинается с первого разрешённого запроса
    /// пользователя и длится 5 минут. Когда окно истекает — счётчик сбрасывается,
    /// окно начинается заново.
    /// </para>
    /// </remarks>
    public sealed class InMemoryVisionRateLimiter : IVisionRateLimiter, IDisposable
    {
        /// <summary>Длительность окна — 5 минут.</summary>
        private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(5);

        /// <summary>Интервал cleanup'а (удаление устаревших записей).</summary>
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

        /// <summary>Порог «устарело» — 10 минут (2× окна).</summary>
        private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(10);

        private readonly int _maxTasksPerWindow;
        private readonly ILogger<InMemoryVisionRateLimiter> _logger;
        private readonly ConcurrentDictionary<int, UserWindow> _windows =
            new ConcurrentDictionary<int, UserWindow>();
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        /// <summary>
        /// Создаёт лимитер.
        /// </summary>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent</c>).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр null.</exception>
        public InMemoryVisionRateLimiter(
            IOptions<VisionAgentOptions> options,
            ILogger<InMemoryVisionRateLimiter> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var limits = options.Value.Limits ?? new VisionLimitsOptions();

            // Clamp [1, 100] (DESIGN § 6.3).
            var raw = limits.MaxTasksPerUserPer5Min;
            _maxTasksPerWindow = Math.Clamp(raw, 1, 100);

            if (_maxTasksPerWindow != raw)
            {
                _logger.LogWarning(
                    "VisionAgent: RateLimiter MaxTasksPerUserPer5Min clamped: {Raw} → {Clamped}",
                    raw, _maxTasksPerWindow);
            }

            _cleanupTimer = new Timer(
                CleanupStaleWindows,
                state: null,
                dueTime: CleanupInterval,
                period: CleanupInterval);
        }

        /// <inheritdoc />
        public VisionRateLimitResult TryAcquire(int userId)
        {
            if (userId <= 0)
            {
                return new VisionRateLimitResult
                {
                    Allowed = false,
                    RetryAfterSeconds = 0,
                    RemainingInWindow = 0
                };
            }

            var now = DateTime.UtcNow;
            var window = _windows.GetOrAdd(userId, _ => new UserWindow { WindowStartUtc = now });

            lock (window)
            {
                // Fixed-window: сброс при истечении окна.
                if (now - window.WindowStartUtc >= WindowDuration)
                {
                    window.WindowStartUtc = now;
                    window.Count = 0;
                }

                if (window.Count >= _maxTasksPerWindow)
                {
                    var windowEnds = window.WindowStartUtc + WindowDuration;
                    var retryAfter = (int)Math.Ceiling((windowEnds - now).TotalSeconds);
                    if (retryAfter < 1) retryAfter = 1;

                    _logger.LogWarning(
                        "VisionAgent: rate limit для user={UserId} ({Count}/{Max} за 5 мин), retry через {Sec}с",
                        userId, window.Count, _maxTasksPerWindow, retryAfter);

                    return new VisionRateLimitResult
                    {
                        Allowed = false,
                        RetryAfterSeconds = retryAfter,
                        RemainingInWindow = 0
                    };
                }

                window.Count++;
                var remaining = _maxTasksPerWindow - window.Count;

                _logger.LogDebug(
                    "VisionAgent: rate limit acquire user={UserId} ({Count}/{Max}), remaining={Remaining}",
                    userId, window.Count, _maxTasksPerWindow, remaining);

                return new VisionRateLimitResult
                {
                    Allowed = true,
                    RetryAfterSeconds = 0,
                    RemainingInWindow = remaining
                };
            }
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Удаляет окна без активности > 10 минут.
        /// </summary>
        private void CleanupStaleWindows(object _)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.UtcNow;
                var removed = 0;

                foreach (var kv in _windows)
                {
                    bool stale;
                    lock (kv.Value)
                    {
                        // Окно устарело, если закончилось более 5 минут назад.
                        stale = (now - (kv.Value.WindowStartUtc + WindowDuration)) > StaleThreshold - WindowDuration;
                    }

                    if (!stale) continue;

                    UserWindow removedValue;
                    if (_windows.TryRemove(kv.Key, out removedValue))
                        removed++;
                }

                if (removed > 0)
                {
                    _logger.LogDebug(
                        "VisionAgent: rate limiter cleanup removed {Count} stale windows",
                        removed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VisionAgent: rate limiter cleanup error");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cleanupTimer?.Dispose(); }
            catch { /* ignore */ }
        }

        // ============================================================
        // Внутреннее состояние
        // ============================================================

        /// <summary>
        /// Fixed-window состояние одного пользователя. Обновляется под lock.
        /// </summary>
        private sealed class UserWindow
        {
            /// <summary>Начало текущего окна (UTC).</summary>
            public DateTime WindowStartUtc { get; set; }

            /// <summary>Число задач, разрешённых в текущем окне.</summary>
            public int Count { get; set; }
        }
    }
}