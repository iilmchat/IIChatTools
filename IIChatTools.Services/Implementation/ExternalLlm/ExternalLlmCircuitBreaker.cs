using System;
using System.Collections.Concurrent;
using System.Threading;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.ExternalLlm
{
    /// <summary>
    /// Circuit breaker для внешних LLM (v1.8.1, KI-109, Фаза 2.2).
    ///
    /// <para>
    /// Per-provider: N подряд неудачных запросов → «открыт» на
    /// <c>BreakDurationSeconds</c> (DESIGN_EXTERNAL_LLM § 6.5).
    /// Пока открыт — <c>IExternalLlmClient.CompleteAsync</c> вызываться не должен.
    /// </para>
    ///
    /// <para>
    /// <b>Singleton, IDisposable.</b> State — <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// с <see cref="StringComparer.OrdinalIgnoreCase"/>. Cleanup устаревших записей —
    /// <see cref="Timer"/> каждые 5 минут (по образцу KI-043).
    /// </para>
    /// </summary>
    public sealed class ExternalLlmCircuitBreaker : IExternalLlmCircuitBreaker, IDisposable
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(2);

        private readonly int _failureThreshold;
        private readonly TimeSpan _breakDuration;
        private readonly ILogger<ExternalLlmCircuitBreaker> _logger;
        private readonly ConcurrentDictionary<string, CircuitState> _states =
            new ConcurrentDictionary<string, CircuitState>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        /// <summary>
        /// Создаёт breaker.
        /// </summary>
        /// <param name="options">Опции <c>ExternalLlm</c> (порог + длительность break)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public ExternalLlmCircuitBreaker(
            IOptions<ExternalLlmOptions> options,
            ILogger<ExternalLlmCircuitBreaker> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var cb = options.Value?.CircuitBreaker ?? new ExternalLlmCircuitBreakerOptions();

            // Clamp (DESIGN § 5.6).
            _failureThreshold = Math.Clamp(cb.FailureThreshold, 1, 10);
            var breakSeconds = Math.Clamp(cb.BreakDurationSeconds, 30, 3600);
            _breakDuration = TimeSpan.FromSeconds(breakSeconds);

            if (_failureThreshold != cb.FailureThreshold || breakSeconds != cb.BreakDurationSeconds)
            {
                _logger.LogWarning(
                    "External-LLM: CircuitBreaker настройки clamped: FailureThreshold={Threshold}, BreakDurationSeconds={Break}s",
                    _failureThreshold, breakSeconds);
            }

            _cleanupTimer = new Timer(
                CleanupStaleStates,
                state: null,
                dueTime: CleanupInterval,
                period: CleanupInterval);
        }

        /// <inheritdoc />
        public bool IsOpen(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return false;

            if (!_states.TryGetValue(providerName, out var state))
                return false;

            lock (state)
            {
                if (state.ConsecutiveFailures < _failureThreshold)
                    return false;

                if (state.OpenedAt == null)
                    return false;

                var elapsed = DateTime.UtcNow - state.OpenedAt.Value;
                if (elapsed >= _breakDuration)
                {
                    // Break истёк — позволяем попытку. state не сбрасываем:
                    // при следующем fail OpenedAt обновится, breaker снова откроется.
                    return false;
                }

                return true;
            }
        }

        /// <inheritdoc />
        public void RecordSuccess(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return;

            // Явная переменная вместо `out _` — CS1503 в .NET 10 / C# 13
            // (см. RULES § про ConcurrentDictionary, прецедент — InMemoryMailRateLimiter).
            CircuitState removed;
            if (_states.TryRemove(providerName, out removed))
            {
                _logger.LogDebug(
                    "External-LLM: circuit breaker reset для '{Provider}' (был успешный запрос).",
                    providerName);
            }
        }

        /// <inheritdoc />
        public void RecordFailure(string providerName, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return;

            var now = DateTime.UtcNow;
            var state = _states.GetOrAdd(providerName, _ => new CircuitState());

            lock (state)
            {
                state.ConsecutiveFailures++;
                state.LastFailureAt = now;
                state.LastErrorMessage = Truncate(errorMessage, 200);

                if (state.ConsecutiveFailures >= _failureThreshold)
                {
                    state.OpenedAt = now;

                    _logger.LogWarning(
                        "External-LLM: circuit breaker OPEN для '{Provider}' " +
                        "(failures={Failures}, break={Seconds}s): {Error}",
                        providerName, state.ConsecutiveFailures,
                        (int)_breakDuration.TotalSeconds, state.LastErrorMessage);
                }
            }
        }

        /// <inheritdoc />
        public ProviderHealthStatus GetStatus(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return null;

            var isOpen = IsOpen(providerName);

            if (!_states.TryGetValue(providerName, out var state))
            {
                return new ProviderHealthStatus
                {
                    Provider = providerName,
                    Available = true,
                    LastError = null,
                    LastCheckAt = null
                };
            }

            lock (state)
            {
                string lastError = null;
                if (!string.IsNullOrWhiteSpace(state.LastErrorMessage))
                {
                    lastError = isOpen
                        ? $"Circuit breaker open (retry in {RetryAfterMinutes(state, DateTime.UtcNow)}m): {state.LastErrorMessage}"
                        : state.LastErrorMessage;
                }

                return new ProviderHealthStatus
                {
                    Provider = providerName,
                    Available = !isOpen,
                    LastError = lastError,
                    LastCheckAt = state.LastFailureAt
                };
            }
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Сколько минут осталось до закрытия breaker'а.
        /// </summary>
        private int RetryAfterMinutes(CircuitState state, DateTime now)
        {
            if (state.OpenedAt == null)
                return 0;

            var remaining = _breakDuration - (now - state.OpenedAt.Value);
            if (remaining <= TimeSpan.Zero)
                return 0;

            return (int)Math.Ceiling(remaining.TotalMinutes);
        }

        /// <summary>
        /// Удаляет состояния без активности > <see cref="StaleThreshold"/>.
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
                        stale = (now - kv.Value.LastFailureAt) > StaleThreshold;
                    }

                    if (!stale) continue;

                    CircuitState removedValue;
                    if (_states.TryRemove(kv.Key, out removedValue))
                        removed++;
                }

                if (removed > 0)
                {
                    _logger.LogDebug(
                        "External-LLM: circuit breaker cleanup removed {Count} stale states",
                        removed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "External-LLM: circuit breaker cleanup error");
            }
        }

        /// <summary>
        /// Обрезает строку до указанной длины.
        /// </summary>
        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value.Substring(0, max) + "…";
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
        /// Состояние breaker'а для одного провайдера. Доступ — под lock.
        /// </summary>
        private sealed class CircuitState
        {
            /// <summary>Подряд неудачных запросов (сбрасывается на успехе).</summary>
            public int ConsecutiveFailures { get; set; }

            /// <summary>Когда breaker открылся последний раз.</summary>
            public DateTime? OpenedAt { get; set; }

            /// <summary>Время последнего fail (для cleanup).</summary>
            public DateTime LastFailureAt { get; set; } = DateTime.UtcNow;

            /// <summary>Короткое описание последней ошибки (без PII).</summary>
            public string LastErrorMessage { get; set; }
        }
    }
}