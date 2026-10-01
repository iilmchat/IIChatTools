using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Debate
{
    /// <summary>
    /// Singleton-координатор Human-in-the-loop для Actor-Critic сессий
    /// (v1.11.0, KI-126, Шаг 1E).
    ///
    /// <para>
    /// По образцу <see cref="IIChatTools.Services.Implementation.ChatTools.ChatApprovalCoordinator"/>:
    /// <c>ConcurrentDictionary&lt;int, TaskCompletionSource&lt;string&gt;&gt;</c> +
    /// <c>RunContinuationsAsynchronously</c> (RULES § 4.23) + cleanup в
    /// <c>finally</c> (защита от утечки, KI-043).
    /// </para>
    /// </summary>
    public sealed class AgentDebateCoordinator : IAgentDebateCoordinator
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _waiters
            = new ConcurrentDictionary<int, TaskCompletionSource<string>>();

        private readonly ILogger<AgentDebateCoordinator> _logger;

        /// <summary>
        /// Создаёт экземпляр координатора.
        /// </summary>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="logger"/> равен <c>null</c>.</exception>
        public AgentDebateCoordinator(ILogger<AgentDebateCoordinator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<string> WaitForFeedbackAsync(
            int sessionId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (sessionId <= 0)
            {
                _logger.LogWarning(
                    "WaitForFeedbackAsync: невалидный sessionId={SessionId}", sessionId);
                return null;
            }

            // TaskCreationOptions.RunContinuationsAsynchronously — RULES § 4.23.
            var tcs = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_waiters.TryAdd(sessionId, tcs))
            {
                _logger.LogWarning(
                    "WaitForFeedbackAsync: дубликат ожидания для sessionId={SessionId}",
                    sessionId);
                return null;
            }

            _logger.LogInformation(
                "Ожидание feedback: sessionId={SessionId}, таймаут={Timeout}",
                sessionId, timeout);

            try
            {
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(timeout);

                    var timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
                    var completed = await Task.WhenAny(tcs.Task, timeoutTask);

                    if (completed == tcs.Task)
                    {
                        var feedback = await tcs.Task;
                        _logger.LogInformation(
                            "Feedback получен: sessionId={SessionId}, len={Len}",
                            sessionId, feedback?.Length ?? 0);
                        return feedback;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation(
                            "Ожидание feedback sessionId={SessionId} отменено", sessionId);
                        return null;
                    }

                    _logger.LogWarning(
                        "Таймаут ожидания feedback sessionId={SessionId} ({Timeout})",
                        sessionId, timeout);
                    return null;
                }
            }
            finally
            {
                // Обязательная очистка — защита от утечки (KI-043).
                _waiters.TryRemove(sessionId, out _);
            }
        }

        /// <inheritdoc />
        public Task<bool> ProvideFeedbackAsync(int sessionId, string feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback))
            {
                return Task.FromResult(false);
            }

            if (_waiters.TryRemove(sessionId, out var tcs))
            {
                var ok = tcs.TrySetResult(feedback);
                _logger.LogInformation(
                    "ProvideFeedback sessionId={SessionId} → успех={Ok}",
                    sessionId, ok);
                return Task.FromResult(ok);
            }

            _logger.LogWarning(
                "ProvideFeedback sessionId={SessionId} → нет ожидающего (таймаут или неверный id)",
                sessionId);
            return Task.FromResult(false);
        }
    }
}