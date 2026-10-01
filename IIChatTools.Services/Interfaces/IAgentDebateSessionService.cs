using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Debate;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис управления сессиями Actor-Critic / Debate
    /// (v1.11.0, KI-126, Шаг 1B).
    ///
    /// <para>
    /// Жизненный цикл сессии:
    /// <list type="number">
    ///   <item><see cref="StartAsync"/> — создаёт сессию (<c>Pending</c>),
    ///   проверяет concurrency (max 3 активных на пользователя).</item>
    ///   <item>Фоновый цикл раундов (<c>InProgress</c> → <c>Completed</c>) —
    ///   реализация в Шаге 1D (после <c>code_reviewer_agent</c>
    ///   и <c>code_agent_with_review</c>).</item>
    ///   <item><see cref="GetStatusAsync"/> — статус + раунды (для UI-polling).</item>
    ///   <item><see cref="CancelAsync"/> — отмена (<c>Cancelled</c>).</item>
    ///   <item><see cref="InjectFeedbackAsync"/> — Human-in-the-loop между
    ///   раундами (реализация в Шаге 1E — TCS + SSE).</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IAgentDebateSessionService
    {
        /// <summary>
        /// Создаёт новую сессию Actor-Critic (<c>Status = Pending</c>).
        /// </summary>
        /// <param name="chatId">Идентификатор чата (проверка владения)</param>
        /// <param name="userId">Идентификатор пользователя-инициатора</param>
        /// <param name="task">Исходная задача для review (текст от Chat LLM / пользователя)</param>
        /// <param name="config">
        /// Снимок конфига. <c>null</c> → используются дефолты из DESIGN § 3.1:
        /// <c>MaxRounds = 3</c>, <c>TokenBudget = 50000</c>, <c>AllowEscalation = true</c>.
        /// </param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>ID созданной сессии.</returns>
        /// <exception cref="System.ArgumentException">
        /// Если <paramref name="task"/> пустой.
        /// </exception>
        /// <exception cref="System.InvalidOperationException">
        /// Если чат не найден / не принадлежит пользователю,
        /// либо превышен лимит одновременных сессий (3).
        /// </exception>
        Task<int> StartAsync(
            int chatId,
            int userId,
            string task,
            AgentDebateConfigSnapshot config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает статус сессии + список завершённых раундов.
        /// </summary>
        /// <param name="sessionId">Идентификатор сессии</param>
        /// <param name="userId">Идентификатор пользователя (проверка владения)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>
        /// DTO со статусом и раундами, либо <c>null</c>, если сессия
        /// не найдена или не принадлежит пользователю (обобщённый ответ —
        /// не палим существование чужих сессий).
        /// </returns>
        Task<AgentDebateStatusDto> GetStatusAsync(
            int sessionId,
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Отменяет сессию (<c>Status = Cancelled</c>).
        /// Отмена возможна только из статусов <c>Pending</c> / <c>InProgress</c>.
        /// </summary>
        /// <param name="sessionId">Идентификатор сессии</param>
        /// <param name="userId">Идентификатор пользователя-владельца</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>
        /// <c>true</c> — сессия отменена.
        /// <c>false</c> — сессия не найдена / не принадлежит пользователю /
        /// уже завершена (Completed / Failed / Cancelled).
        /// </returns>
        Task<bool> CancelAsync(
            int sessionId,
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Внедряет feedback пользователя между раундами
        /// (Human-in-the-loop, режим <c>BetweenRounds</c>).
        ///
        /// <para>
        /// <b>Заглушка в Шаге 1B</b> (возвращает <c>false</c>). Реальная
        /// реализация — в Шаге 1E, когда появится
        /// <c>AgentDebateCoordinator</c> (Singleton с TCS, по образцу
        /// <see cref="IChatApprovalCoordinator"/>) и SSE-события
        /// <c>debate_round</c> / <c>debate_started</c>.
        /// </para>
        /// </summary>
        /// <param name="sessionId">Идентификатор сессии</param>
        /// <param name="userId">Идентификатор пользователя-владельца</param>
        /// <param name="feedback">Текст feedback от пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>true — feedback принят и передан в ожидающий раунд.</returns>
        Task<bool> InjectFeedbackAsync(
            int sessionId,
            int userId,
            string feedback,
            CancellationToken cancellationToken = default);
    }
}