using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Chat;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Координатор подтверждений tool call в чате.
    /// Singleton: связывает SSE-стрим (ждущий) и REST-endpoint (approve/reject).
    ///
    /// Жизненный цикл:
    /// 1. SSE-стрим вызывает <see cref="WaitForDecisionAsync"/> — блокируется до решения.
    /// 2. REST-endpoint <c>POST /api/chat/approve/{callId}</c> вызывает
    ///    <see cref="ResolveAsync"/> — будит ожидающего.
    /// 3. Если таймаут (5 минут) — возвращается <c>Expired</c>.
    /// </summary>
    public interface IChatApprovalCoordinator
    {
        /// <summary>
        /// Ожидает решение пользователя по конкретному вызову.
        /// </summary>
        /// <param name="callId">Идентификатор вызова инструмента (от LM Studio)</param>
        /// <param name="timeout">Таймаут ожидания (5 минут)</param>
        /// <param name="cancellationToken">Токен отмены (если клиент отключился)</param>
        /// <returns>Решение пользователя или <c>Expired</c> при таймауте</returns>
        Task<ChatApprovalDecision> WaitForDecisionAsync(
            string callId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Фиксирует решение пользователя.
        /// Если ожидающий не найден (таймаут уже истёк или неправильный callId) — no-op.
        /// </summary>
        /// <param name="callId">Идентификатор вызова</param>
        /// <param name="decision">Решение (<c>Approved</c> / <c>Rejected</c>)</param>
        /// <returns>true, если ожидающий был найден и разбужен</returns>
        Task<bool> ResolveAsync(string callId, ChatApprovalDecision decision);
    }
}