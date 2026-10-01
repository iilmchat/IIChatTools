using System;
using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Координатор Human-in-the-loop для Actor-Critic сессий
    /// (v1.11.0, KI-126, Шаг 1E).
    ///
    /// <para>
    /// Singleton. По образцу <see cref="IChatApprovalCoordinator"/>
    /// (RULES § 4.23 — <c>TaskCompletionSource</c> с
    /// <c>RunContinuationsAsynchronously</c>).
    /// </para>
    ///
    /// <para>
    /// Связывает фоновый цикл actor-critic (ждёт решение) и REST-endpoint
    /// <c>POST /api/chat/debate/{sessionId}/inject</c> (передаёт feedback).
    /// Используется только при <c>HumanApproval = "BetweenRounds"</c>.
    /// </para>
    /// </summary>
    public interface IAgentDebateCoordinator
    {
        /// <summary>
        /// Ожидает feedback пользователя для указанной сессии.
        /// </summary>
        /// <param name="sessionId">Идентификатор сессии.</param>
        /// <param name="timeout">Таймаут ожидания (например, 5 минут).</param>
        /// <param name="cancellationToken">Токен отмены (если клиент отключился).</param>
        /// <returns>
        /// Текст feedback, либо <c>null</c>, если таймаут истёк,
        /// клиент отключился, или уже есть другой ожидающий
        /// с тем же <paramref name="sessionId"/>.
        /// </returns>
        Task<string> WaitForFeedbackAsync(
            int sessionId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Передаёт feedback в ожидающий раунд.
        /// </summary>
        /// <param name="sessionId">Идентификатор сессии.</param>
        /// <param name="feedback">Текст feedback от пользователя.</param>
        /// <returns>
        /// <c>true</c>, если ожидающий найден и разбужен;
        /// <c>false</c>, если ожидающего нет (таймаут / неверный sessionId).
        /// </returns>
        Task<bool> ProvideFeedbackAsync(int sessionId, string feedback);
    }
}