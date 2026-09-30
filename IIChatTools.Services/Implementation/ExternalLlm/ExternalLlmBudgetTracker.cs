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
    /// Дневной бюджет / лимит токенов для External-LLM Agent
    /// (v1.8.1, KI-109, Фаза 2.3, DESIGN_EXTERNAL_LLM § 6.4).
    ///
    /// <para>
    /// <b>Per-user.</b> Защищает от «$1000 за ночь»:
    /// <list type="bullet">
    ///   <item><c>DailyBudgetUsd</c> — 5 USD по умолчанию;</item>
    ///   <item><c>DailyTokensLimit</c> — 500k токенов по умолчанию.</item>
    /// </list>
    /// Lazy-reset при смене календарного дня UTC (без фонового таймера).
    /// </para>
    ///
    /// <para>
    /// <b>Singleton, IDisposable.</b> State — <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// Cleanup — <see cref="Timer"/> каждые 30 минут удаляет записи старше 1 дня.
    /// </para>
    /// </summary>
    public sealed class ExternalLlmBudgetTracker : IExternalLlmBudgetTracker, IDisposable
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(1);

        private readonly decimal _dailyBudgetUsd;
        private readonly long _dailyTokensLimit;
        private readonly ILogger<ExternalLlmBudgetTracker> _logger;
        private readonly ConcurrentDictionary<int, DailyUsage> _usage =
            new ConcurrentDictionary<int, DailyUsage>();
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        /// <summary>
        /// Создаёт трекер.
        /// </summary>
        /// <param name="options">Опции <c>ExternalLlm</c> (дневные лимиты)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public ExternalLlmBudgetTracker(
            IOptions<ExternalLlmOptions> options,
            ILogger<ExternalLlmBudgetTracker> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var opts = options.Value ?? new ExternalLlmOptions();

            // Clamp (DESIGN § 5.6).
            var rawBudget = opts.DailyBudgetUsd;
            if (rawBudget < 0m) rawBudget = 0m;
            if (rawBudget > 1000m) rawBudget = 1000m;
            _dailyBudgetUsd = rawBudget;

            var rawTokens = opts.DailyTokensLimit;
            if (rawTokens < 0) rawTokens = 0;
            if (rawTokens > 10_000_000) rawTokens = 10_000_000;
            _dailyTokensLimit = rawTokens;

            if (_dailyBudgetUsd != opts.DailyBudgetUsd || _dailyTokensLimit != opts.DailyTokensLimit)
            {
                _logger.LogWarning(
                    "External-LLM: BudgetTracker настройки clamped: DailyBudgetUsd={Budget}, DailyTokensLimit={Tokens}",
                    _dailyBudgetUsd, _dailyTokensLimit);
            }

            _cleanupTimer = new Timer(
                CleanupStaleStates,
                state: null,
                dueTime: CleanupInterval,
                period: CleanupInterval);
        }

        /// <inheritdoc />
        public bool CanSpend(int userId)
        {
            if (userId <= 0)
                return false;

            if (!_usage.TryGetValue(userId, out var state))
                return true;

            lock (state)
            {
                ResetIfNewDay(state, DateTime.UtcNow);

                if (_dailyBudgetUsd > 0m && state.CostUsd >= _dailyBudgetUsd)
                    return false;

                if (_dailyTokensLimit > 0 && state.Tokens >= _dailyTokensLimit)
                    return false;

                return true;
            }
        }

        /// <inheritdoc />
        public void RecordUsage(
            int userId, int promptTokens, int completionTokens, decimal costUsd)
        {
            if (userId <= 0) return;

            var state = _usage.GetOrAdd(userId, _ => new DailyUsage());

            lock (state)
            {
                ResetIfNewDay(state, DateTime.UtcNow);

                // Защита от случайного переполнения / отрицательных значений.
                var addTokens = Math.Max(0, promptTokens) + Math.Max(0, completionTokens);
                var addCost = costUsd < 0m ? 0m : costUsd;

                state.Tokens += addTokens;
                state.CostUsd += addCost;
            }
        }

        /// <inheritdoc />
        public decimal GetTodayCostUsd(int userId)
        {
            if (userId <= 0)
                return 0m;

            if (!_usage.TryGetValue(userId, out var state))
                return 0m;

            lock (state)
            {
                ResetIfNewDay(state, DateTime.UtcNow);
                return state.CostUsd;
            }
        }

        /// <inheritdoc />
        public long GetTodayTokens(int userId)
        {
            if (userId <= 0)
                return 0L;

            if (!_usage.TryGetValue(userId, out var state))
                return 0L;

            lock (state)
            {
                ResetIfNewDay(state, DateTime.UtcNow);
                return state.Tokens;
            }
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Сбрасывает состояние, если календарный день UTC изменился.
        /// </summary>
        private static void ResetIfNewDay(DailyUsage state, DateTime utcNow)
        {
            var today = utcNow.Date;
            if (state.DateUtc != today)
            {
                state.DateUtc = today;
                state.CostUsd = 0m;
                state.Tokens = 0L;
            }
        }

        /// <summary>
        /// Удаляет состояния без активности > 1 дня.
        /// </summary>
        private void CleanupStaleStates(object _)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.UtcNow;
                var removed = 0;

                foreach (var kv in _usage)
                {
                    bool stale;
                    lock (kv.Value)
                    {
                        stale = (now.Date - kv.Value.DateUtc).TotalDays > 1.0;
                    }

                    if (!stale) continue;

                    DailyUsage removedValue;
                    if (_usage.TryRemove(kv.Key, out removedValue))
                        removed++;
                }

                if (removed > 0)
                {
                    _logger.LogDebug(
                        "External-LLM: budget tracker cleanup removed {Count} stale entries",
                        removed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "External-LLM: budget tracker cleanup error");
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
        /// Расход одного пользователя за текущий день UTC. Доступ — под lock.
        /// </summary>
        private sealed class DailyUsage
        {
            /// <summary>День UTC, к которому относятся счётчики.</summary>
            public DateTime DateUtc { get; set; } = DateTime.UtcNow.Date;

            /// <summary>Потрачено USD за день.</summary>
            public decimal CostUsd { get; set; }

            /// <summary>Израсходовано токенов (prompt + completion) за день.</summary>
            public long Tokens { get; set; }
        }
    }
}