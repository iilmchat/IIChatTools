using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Имя actor-агента для Ф1 (Actor-Critic).
        /// Хардкод, т.к. в Ф1 паттерн только один (v1.11.0, KI-129).
        /// </summary>
        private const string ActorAgentName = "code_agent";

        /// <summary>
        /// Имя critic-агента для Ф1 (Actor-Critic).
        /// Хардкод, т.к. в Ф1 паттерн только один (v1.11.0, KI-129).
        /// </summary>
        private const string CriticAgentName = "code_reviewer_agent";

        // ============================================================
        // v1.14.0 (KI-126, Фаза 2): роли Debate.
        // ============================================================

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Pro-агента (защитник) для Debate.
        /// </summary>
        private const string ProAgentName = "planner_pro_agent";

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Contra-агента (скептик) для Debate.
        /// </summary>
        private const string ContraAgentName = "planner_contra_agent";

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Judge-агента (арбитр) для Debate.
        /// </summary>
        private const string JudgeAgentName = "planner_judge_agent";

        private readonly AppDbContext _db;
        private readonly IChatService _chatService;
        private readonly IAgentDebateCoordinator _coordinator;
        private readonly ILogger<AgentDebateSessionService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="db">Контекст БД</param>
        /// <param name="chatService">Сервис чатов (для проверки владения)</param>
        /// <param name="coordinator">
        /// Координатор Human-in-the-loop (Singleton).
        /// </param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public AgentDebateSessionService(
            AppDbContext db,
            IChatService chatService,
            IAgentDebateCoordinator coordinator,
            ILogger<AgentDebateSessionService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
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

            // v2.0.0 (KI-126, Фаза 3): PatternType берём из snapshot
            // с fallback на ActorCritic (backward-compat).
            if (string.IsNullOrWhiteSpace(snapshot.PatternType))
            {
                snapshot.PatternType = PatternActorCritic;
            }

            var session = new AgentDebateSession
            {
                ChatId = chatId,
                InitiatedByUserId = userId,
                Task = task.Trim(),
                PatternType = snapshot.PatternType,
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

            return BuildStatusDto(session, rounds);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<AgentDebateStatusDto>> GetSessionsByChatAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            // 1. Проверка владения чатом (по образцу StartAsync).
            var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
            if (chat == null)
            {
                // Обобщённый ответ — не палим существование чужих чатов.
                return Array.Empty<AgentDebateStatusDto>();
            }

            // 2. Eager-load раундов через Include — избегаем N+1.
            //    Сортировка: StartedAt asc (порядок появления в чате),
            //    затем Id asc для детерминизма при равных StartedAt.
            var sessions = await _db.AgentDebateSessions
                .AsNoTracking()
                .Where(s => s.ChatId == chatId
                            && s.InitiatedByUserId == userId)
                .Include(s => s.Rounds)
                .OrderBy(s => s.StartedAt)
                .ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);

            if (sessions.Count == 0)
            {
                return Array.Empty<AgentDebateStatusDto>();
            }

            // 3. Проекция. Раунды уже загружены — сортируем в памяти
            //    (Include не гарантирует порядок коллекции).
            return sessions
                .Select(s => BuildStatusDto(
                    s,
                    s.Rounds.OrderBy(r => r.RoundNumber).ToList()))
                .ToList();
        }

        /// <summary>
        /// Общая проекция <see cref="AgentDebateSession"/> + раунды → DTO
        /// (v1.11.0, KI-129). Используется в <see cref="GetStatusAsync"/>
        /// и <see cref="GetSessionsByChatAsync"/>.
        ///
        /// <para>
        /// Парсит <c>ConfigSnapshotJson</c> для <c>MaxRounds</c> и
        /// <c>HumanApproval</c>. При битом / отсутствующем snapshot —
        /// безопасные дефолты (MaxRounds=0, HumanApproval=null).
        /// </para>
        /// </summary>
        private static AgentDebateStatusDto BuildStatusDto(
            AgentDebateSession session,
            IReadOnlyList<AgentDebateRound> rounds)
        {
            // Парсим snapshot (для MaxRounds / HumanApproval).
            int maxRounds = 0;
            string humanApproval = null;

            if (!string.IsNullOrWhiteSpace(session.ConfigSnapshotJson))
            {
                try
                {
                    var snapshot = JsonConvert.DeserializeObject<AgentDebateConfigSnapshot>(
                        session.ConfigSnapshotJson);
                    if (snapshot != null)
                    {
                        maxRounds = snapshot.MaxRounds;
                        humanApproval = snapshot.HumanApproval;
                    }
                }
                catch
                {
                    // Битый JSON — безопасные дефолты.
                }
            }

            // v1.14.0 (KI-126, Фаза 2) + v2.0.0 (KI-126, Фаза 3):
            // ветвление по PatternType на 3 паттерна.
            // ActorCritic (Ф1) — Actor + Critic.
            // Debate (Ф2) — Pro + Contra + Judge.
            // Orchestrator (Ф3) — декомпозитор + subtasks.
            var patternType = session.PatternType ?? PatternActorCritic;
            var isDebate = string.Equals(
                patternType, "Debate", StringComparison.OrdinalIgnoreCase);
            var isOrchestrator = string.Equals(
                patternType, "Orchestrator", StringComparison.OrdinalIgnoreCase);
            var isReflection = string.Equals(
                patternType, "Reflection", StringComparison.OrdinalIgnoreCase);
            var isActorCritic = !isDebate && !isOrchestrator && !isReflection;

            // v2.0.0 (KI-126, Фаза 3): статистика подзадач — для UI-заголовка.
            int totalSubtasks = 0;
            int completedSubtasks = 0;
            int failedSubtasks = 0;

            if (isOrchestrator)
            {
                totalSubtasks = rounds.Count;
                foreach (var r in rounds)
                {
                    if (string.Equals(r.SubtaskStatus, "completed",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        completedSubtasks++;
                    }
                    else if (string.Equals(r.SubtaskStatus, "failed",
                                 StringComparison.OrdinalIgnoreCase)
                          || string.Equals(r.SubtaskStatus, "rejected",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        failedSubtasks++;
                    }
                }
            }

            return new AgentDebateStatusDto
            {
                SessionId = session.Id,
                ChatId = session.ChatId,
                Task = session.Task,
                Status = session.Status,
                FinalVerdict = session.FinalVerdict,
                FinalArtifactJson = session.FinalArtifactJson,
                TotalRounds = rounds.Count,
                TotalCostUsd = session.TotalCostUsd,
                StartedAt = session.StartedAt,
                CompletedAt = session.CompletedAt,
                MaxRounds = maxRounds,
                HumanApproval = humanApproval,
                PatternType = patternType,
                // ActorCritic-роли (v1.11.0).
                ActorAgent = isActorCritic ? ActorAgentName : null,
                CriticAgent = isActorCritic ? CriticAgentName : null,
                // Debate-роли (v1.14.0).
                ProAgent = isDebate ? ProAgentName : null,
                ContraAgent = isDebate ? ContraAgentName : null,
                JudgeAgent = isDebate ? JudgeAgentName : null,
                // Orchestrator (v2.0.0, KI-126, Фаза 3):
                OrchestratorAgent = isOrchestrator ? "orchestrator_planner_agent" : null,
                TotalSubtasks = isOrchestrator ? totalSubtasks : 0,
                CompletedSubtasks = isOrchestrator ? completedSubtasks : 0,
                FailedSubtasks = isOrchestrator ? failedSubtasks : 0,
                // v2.2.0 (KI-244): Reflection
                ReflectionAgent = isReflection ? "reflector_agent" : null,                   
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
        public async Task<bool> InjectFeedbackAsync(
            int sessionId,
            int userId,
            string feedback,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(feedback))
            {
                return false;
            }

            // 1. Проверка владения + статуса (Pending / InProgress).
            var session = await _db.AgentDebateSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId
                                          && s.InitiatedByUserId == userId,
                    cancellationToken);

            if (session == null)
            {
                return false;
            }

            if (session.Status != StatusPending && session.Status != StatusInProgress)
            {
                _logger.LogWarning(
                    "InjectFeedback: сессия {SessionId} в статусе {Status} — feedback не принят",
                    sessionId, session.Status);
                return false;
            }

            // 2. Прокидываем feedback в ожидающий TCS.
            var ok = await _coordinator.ProvideFeedbackAsync(sessionId, feedback.Trim());

            _logger.LogInformation(
                "InjectFeedback: sessionId={SessionId}, userId={UserId}, len={Len}, ok={Ok}",
                sessionId, userId, feedback.Length, ok);

            return ok;
        }

        /// <inheritdoc />
        public async Task<bool> MarkInProgressAsync(
            int sessionId,
            CancellationToken cancellationToken = default)
        {
            var session = await _db.AgentDebateSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (session == null) return false;
            if (session.Status != StatusPending) return false;

            session.Status = StatusInProgress;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Debate session {SessionId}: Pending → InProgress", sessionId);

            return true;
        }

        /// <inheritdoc />
        public async Task<bool> AddRoundAsync(
            int sessionId,
            DTO.Debate.AgentDebateRoundDto round,
            CancellationToken cancellationToken = default)
        {
            if (round == null) return false;

            var sessionExists = await _db.AgentDebateSessions
                .AnyAsync(s => s.Id == sessionId, cancellationToken);

            if (!sessionExists) return false;

            var entity = new AgentDebateRound
            {
                SessionId = sessionId,
                RoundNumber = round.RoundNumber,
                // ActorCritic:
                ActorOutput = round.ActorOutput,
                CriticVerdict = round.CriticVerdict,
                CriticFeedbackJson = round.CriticFeedbackJson,
                // Debate (v1.14.0, KI-126, Фаза 2):
                ProOutput = round.ProOutput,
                ContraOutput = round.ContraOutput,
                JudgeVerdict = round.JudgeVerdict,
                JudgeFeedbackJson = round.JudgeFeedbackJson,
                // Orchestrator (v2.0.0, KI-126, Фаза 3):
                SubtaskId = round.SubtaskId,
                SubtaskAgent = round.SubtaskAgent,
                SubtaskTask = round.SubtaskTask,
                SubtaskStatus = round.SubtaskStatus,
                SubtaskResult = round.SubtaskResult,
                SubtaskError = round.SubtaskError,
                // Общие:
                ActorModel = round.ActorModel,
                CriticModel = round.CriticModel,
                WasEscalated = round.WasEscalated,
                EscalationProvider = round.EscalationProvider,
                TokensIn = round.TokensIn,
                TokensOut = round.TokensOut,
                CostUsd = round.CostUsd,
                DurationMs = round.DurationMs,
                CreatedAt = DateTime.UtcNow
            };

            _db.AgentDebateRounds.Add(entity);

            // Обновляем totals сессии (increment).
            var session = await _db.AgentDebateSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (session != null)
            {
                session.TotalCostUsd += round.CostUsd;
                session.TotalTokensIn += round.TokensIn;
                session.TotalTokensOut += round.TokensOut;
                session.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);

            // v2.0.0 (KI-126, Фаза 3): для Orchestrator вердикт = SubtaskStatus
            // (CriticVerdict / JudgeVerdict = null). Fallback для лога.
            var logVerdict = round.CriticVerdict
                ?? round.JudgeVerdict
                ?? round.SubtaskStatus
                ?? "n/a";

            _logger.LogDebug(
                "Debate session {SessionId}: раунд {Round} добавлен (verdict={Verdict})",
                sessionId, round.RoundNumber, logVerdict);

            return true;
        }

        /// <inheritdoc />
        public async Task<bool> CompleteAsync(
            int sessionId,
            string finalVerdict,
            string finalArtifactJson,
            CancellationToken cancellationToken = default)
        {
            var session = await _db.AgentDebateSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (session == null) return false;

            // Завершать можно только из Pending / InProgress.
            if (session.Status == StatusCancelled
                || session.Status == "Completed"
                || session.Status == "Failed")
            {
                return false;
            }

            session.Status = "Completed";
            session.FinalVerdict = finalVerdict;
            session.FinalArtifactJson = finalArtifactJson;
            session.CompletedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Debate session {SessionId}: завершена, verdict={Verdict}, rounds={Rounds}",
                sessionId, finalVerdict, session.TotalTokensIn);

            return true;
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
                // ActorCritic:
                ActorOutput = entity.ActorOutput,
                CriticVerdict = entity.CriticVerdict,
                CriticFeedbackJson = entity.CriticFeedbackJson,
                // Debate (v1.14.0, KI-126, Фаза 2):
                ProOutput = entity.ProOutput,
                ContraOutput = entity.ContraOutput,
                JudgeVerdict = entity.JudgeVerdict,
                JudgeFeedbackJson = entity.JudgeFeedbackJson,
                // Orchestrator (v2.0.0, KI-126, Фаза 3):
                SubtaskId = entity.SubtaskId,
                SubtaskAgent = entity.SubtaskAgent,
                SubtaskTask = entity.SubtaskTask,
                SubtaskStatus = entity.SubtaskStatus,
                SubtaskResult = entity.SubtaskResult,
                SubtaskError = entity.SubtaskError,
                // Общие:
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
