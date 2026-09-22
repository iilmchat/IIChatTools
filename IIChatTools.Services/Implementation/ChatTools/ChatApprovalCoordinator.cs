using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.ChatTools
{
    /// <summary>
    /// Singleton-координатор подтверждений tool call в чате.
    ///
    /// Связывает SSE-стрим (ждёт решение) и REST-endpoint (approve/reject)
    /// через <see cref="ConcurrentDictionary{TKey,TValue}"/> активных ожиданий.
    ///
    /// Жизненный цикл:
    /// 1. SSE-стрим вызывает <see cref="WaitForDecisionAsync"/> — регистрирует
    ///    TaskCompletionSource и ждёт.
    /// 2. REST-endpoint вызывает <see cref="ResolveAsync"/> — будит ожидающего.
    /// 3. При таймауте (5 минут) — <c>Expired</c>.
    /// 4. Cleanup в <c>finally</c> — удаление из словаря (защита от утечки, KI-043).
    /// </summary>
    public sealed class ChatApprovalCoordinator : IChatApprovalCoordinator
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ChatApprovalDecision>> _waiters
            = new ConcurrentDictionary<string, TaskCompletionSource<ChatApprovalDecision>>(StringComparer.Ordinal);

        private readonly ILogger<ChatApprovalCoordinator> _logger;

        /// <summary>
        /// Создаёт экземпляр координатора.
        /// </summary>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если logger равен null</exception>
        public ChatApprovalCoordinator(ILogger<ChatApprovalCoordinator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<ChatApprovalDecision> WaitForDecisionAsync(
            string callId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(callId))
            {
                throw new ArgumentException("Идентификатор вызова обязателен.", nameof(callId));
            }

            // TaskCreationOptions.RunContinuationsAsynchronously — чтобы continuation
            // не выполнялся синхронно в потоке ResolveAsync (защита от deadlock).
            var tcs = new TaskCompletionSource<ChatApprovalDecision>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_waiters.TryAdd(callId, tcs))
            {
                // Уже есть ожидающий с таким callId — возвращаем Expired
                _logger.LogWarning("Дубликат ожидания подтверждения с callId={CallId}", callId);
                return ChatApprovalDecision.Expired;
            }

            _logger.LogInformation(
                "Ожидание подтверждения: callId={CallId}, таймаут={Timeout}",
                callId, timeout);

            try
            {
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(timeout);

                    var timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);

                    var completed = await Task.WhenAny(tcs.Task, timeoutTask);

                    if (completed == tcs.Task)
                    {
                        // Пользователь принял решение (или ResolveAsync с Rejected)
                        var decision = await tcs.Task;
                        _logger.LogInformation(
                            "Решение по callId={CallId}: {Decision}",
                            callId, decision);
                        return decision;
                    }

                    // Таймаут или cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Ожидание callId={CallId} отменено", callId);
                        return ChatApprovalDecision.Expired;
                    }

                    _logger.LogWarning(
                        "Таймаут ожидания подтверждения callId={CallId} ({Timeout})",
                        callId, timeout);
                    return ChatApprovalDecision.Expired;
                }
            }
            finally
            {
                // Обязательная очистка — устраняет утечку памяти (урок KI-043).
                _waiters.TryRemove(callId, out _);
            }
        }

        /// <inheritdoc />
        public Task<bool> ResolveAsync(string callId, ChatApprovalDecision decision)
        {
            if (string.IsNullOrWhiteSpace(callId))
            {
                return Task.FromResult(false);
            }

            if (_waiters.TryRemove(callId, out var tcs))
            {
                // SetResult вернёт true, если ожидающий был разбужен
                var ok = tcs.TrySetResult(decision);
                _logger.LogInformation(
                    "ResolveAsync callId={CallId} → {Decision} (успех: {Ok})",
                    callId, decision, ok);
                return Task.FromResult(ok);
            }

            _logger.LogWarning(
                "ResolveAsync callId={CallId} → нет ожидающего (таймаут или неверный id)",
                callId);
            return Task.FromResult(false);
        }
    }
}