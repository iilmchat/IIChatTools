using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IIChatTools.Services.Implementation.Debate
{
    /// <summary>
    /// Реализация сервиса Actor-Critic / Debate
    /// (v1.11.0, KI-126, Шаг 1B).
    ///
    /// <para>
    /// <b>Scoped</b> — работает с <see cref="AppDbContext"/> напрямую
    /// (создание / отмена сессии, чтение статуса). Не хранит состояние
    /// между запросами.
    /// </para>
    ///
    /// <para>
    /// State machine: <c>Pending</c> → <c>InProgress</c> →
    /// (<c>Completed</c> | <c>Failed</c> | <c>Cancelled</c>).
    /// В Шаге 1B реальный цикл раундов отсутствует (заглушка) — переход
    /// <c>Pending</c> → <c>InProgress</c> → <c>Completed</c> произойдёт
    /// в Шаге 1D, когда появятся <c>code_reviewer_agent</c>
    /// и <c>code_agent_with_review</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Concurrency-guard:</b> не более
    /// <see cref="MaxConcurrentSessions"/> (3) активных сессий
    /// на пользователя (по образцу <c>Security:MaxBrowserSessionsPerUser</c>).
    /// </para>
    /// </summary>
    public sealed class AgentDebateSessionService : IAgentDebateSessionService
    {
        /// <summary>
        /// Максимум одновременных активных сессий на пользователя
        /// (согласовано с пользователем: 3 — по образцу
        /// <c>Security:MaxBrowserSessionsPerUser</c>).
        /// </summary>
        public const int MaxConcurrentSessions = 3;

        /// <summary>Дефолтное значение <c>MaxRounds</c> (DESIGN § 3.1).</summary>
        public const int DefaultMaxRounds = 3;

        /// <summary>Дефолтный token budget (DESIGN § 3.1).</summary>
        public const int DefaultTokenBudget = 50_000;

        /// <summary>Статус: сессия создана, но цикл ещё не запущен.</summary>
        private const string StatusPending = "Pending";

        /// <summary>Статус: активный цикл actor-critic.</summary>
        private const string StatusInProgress = "InProgress";

        /// <summary>Статус: сессия отменена пользователем.</summary>
        private const string StatusCancelled = "Cancelled";

        /// <summary>Тип паттерна — Actor-Critic (единственный в Фазе 1).</summary>
        private const string PatternActorCritic = "ActorCritic";

        private readonly AppDbContext _db;
        private readonly IChatService _chatService;
        private readonly ILogger<AgentDebateSessionService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="db">Контекст БД</param>
        /// <param name="chatService">Сервис чатов (для проверки владения)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public AgentDebateSessionService(
            AppDbContext db,
            IChatService chatService,
            ILogger<AgentDebateSessionService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<int> StartAsync(
            int chatId,
            int userId,
            string task,
            AgentDebateConfigSnapshot config,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(task))
            {
                throw new ArgumentException("Задача обязательна.", nameof(task));
            }

            // 1. Проверка владения чатом.
            var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
            if (chat == null)
            {
                throw new InvalidOperationException(
                    $"Чат {chatId} не найден или не принадлежит пользователю {userId}.");
            }

            // 2. Concurrency-guard: не более MaxConcurrentSessions активных на юзера.
            var activeCount = await _db.AgentDebateSessions
                .CountAsync(s => s.InitiatedByUserId == userId
                                 && (s.Status == StatusPending || s.Status == StatusInProgress),
                    cancellationToken);

            if (activeCount >= MaxConcurrentSessions)
            {
                throw new InvalidOperationException(
                    $"Достигнут лимит одновременных review-сессий ({MaxConcurrentSessions}). " +
                    "Дождитесь завершения активных или отмените одну из них.");
            }

            // 3. Снимок конфига (нормализуем дефолты).
            var snapshot = config ?? new AgentDebateConfigSnapshot();
            if (snapshot.MaxRounds <= 0) snapshot.MaxRounds = DefaultMaxRounds;
            if (snapshot.TokenBudget <= 0) snapshot.TokenBudget = DefaultTokenBudget;

            var session = new AgentDebateSession
            {
                ChatId = chatId,
                InitiatedByUserId = userId,
                Task = task.Trim(),
                PatternType = PatternActorCritic,
                Status = StatusPending,
                FinalVerdict = null,
                FinalArtifactJson = null,
                ConfigSnapshotJson = JsonConvert.SerializeObject(snapshot),
                TotalCostUsd = 0m,
                TotalTokensIn = 0,
                TotalTokensOut = 0,
                StartedAt = DateTime.UtcNow,
                CompletedAt = null
            };

            _db.AgentDebateSessions.Add(session);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Debate session создана: id={SessionId}, chatId={ChatId}, userId={UserId}, " +
                "pattern={Pattern}, maxRounds={MaxRounds}, tokenBudget={Budget}",
                session.Id, chatId, userId, session.PatternType,
                snapshot.MaxRounds, snapshot.TokenBudget);

            return session.Id;
        }

        /// <inheritdoc />
        public async Task<AgentDebateStatusDto> GetStatusAsync(
            int sessionId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            var session = await _db.AgentDebateSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId
                                          && s.InitiatedByUserId == userId,
                    cancellationToken);

            if (session == null)
            {
                return null;
            }

            var rounds = await _db.AgentDebateRounds
                .AsNoTracking()
                .Where(r => r.SessionId == sessionId)
                .OrderBy(r => r.RoundNumber)
                .ToListAsync(cancellationToken);

            return new AgentDebateStatusDto
            {
                SessionId = session.Id,
                ChatId = session.ChatId,
                Status = session.Status,
                FinalVerdict = session.FinalVerdict,
                TotalRounds = rounds.Count,
                TotalCostUsd = session.TotalCostUsd,
                StartedAt = session.StartedAt,
                CompletedAt = session.CompletedAt,
                Rounds = rounds.Select(ToRoundDto).ToList()
            };
        }

        /// <inheritdoc />
        public async Task<bool> CancelAsync(
            int sessionId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            var session = await _db.AgentDebateSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId
                                          && s.InitiatedByUserId == userId,
                    cancellationToken);

            if (session == null)
            {
                return false;
            }

            // Отменить можно только Pending / InProgress.
            if (session.Status != StatusPending && session.Status != StatusInProgress)
            {
                _logger.LogWarning(
                    "Cancel: сессия {SessionId} в статусе {Status} — отмена невозможна",
                    sessionId, session.Status);
                return false;
            }

            session.Status = StatusCancelled;
            session.CompletedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Debate session отменена: id={SessionId}, userId={UserId}",
                sessionId, userId);

            return true;
        }

        /// <inheritdoc />
        public Task<bool> InjectFeedbackAsync(
            int sessionId,
            int userId,
            string feedback,
            CancellationToken cancellationToken = default)
        {
            // Заглушка Шага 1B.
            //
            // Реальная реализация — в Шаге 1E: понадобится Singleton-coordinator
            // с ConcurrentDictionary<string, TaskCompletionSource<string>>
            // (по образцу ChatApprovalCoordinator — RULES § 4.23) плюс SSE-события
            // debate_round / debate_started для доставки feedback в фоновый цикл.
            //
            // Возвращаем false — сигнал «фича не активна».
            _logger.LogDebug(
                "InjectFeedbackAsync (заглушка 1B): sessionId={SessionId}, userId={UserId}, " +
                "feedbackLen={FeedbackLen}",
                sessionId, userId, feedback?.Length ?? 0);

            return Task.FromResult(false);
        }

        /// <summary>
        /// Проекция entity → DTO раунда.
        /// </summary>
        /// <param name="entity">Сущность <see cref="AgentDebateRound"/>.</param>
        /// <returns>DTO для API / UI.</returns>
        private static AgentDebateRoundDto ToRoundDto(AgentDebateRound entity)
        {
            return new AgentDebateRoundDto
            {
                RoundNumber = entity.RoundNumber,
                ActorOutput = entity.ActorOutput,
                CriticVerdict = entity.CriticVerdict,
                CriticFeedbackJson = entity.CriticFeedbackJson,
                ActorModel = entity.ActorModel,
                CriticModel = entity.CriticModel,
                WasEscalated = entity.WasEscalated,
                EscalationProvider = entity.EscalationProvider,
                TokensIn = entity.TokensIn,
                TokensOut = entity.TokensOut,
                CostUsd = entity.CostUsd,
                DurationMs = entity.DurationMs
            };
        }
    }
}