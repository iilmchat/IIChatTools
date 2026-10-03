using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Debate
{
    /// <summary>
    /// Top-level инструмент «Кодинг с ревью» — оркестратор Actor-Critic
    /// (v1.11.0, KI-126, Шаг 1E-part2 — полная реализация).
    ///
    /// <para>
    /// <b>Не наследует <c>AgentToolBase</c></b> — по образцу
    /// <c>DatabaseAgentTool</c> (DESIGN_MULTI_AGENT_DEBATE § 5.1).
    /// Координирует actor (<c>code_agent</c>) и critic
    /// (<c>code_reviewer_agent</c>) через <see cref="IToolRegistry"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Цикл:</b> до <c>MaxRounds</c> раундов:
    /// <list type="number">
    ///   <item>Actor пишет/переписывает код (учитывает feedback критика);</item>
    ///   <item>Critic проверяет, возвращает JSON <c>{verdict, issues, summary}</c>;</item>
    ///   <item>Approved → финал; Rejected → следующий раунд (если <c>HumanApproval</c>); Uncertain → стоп (эскалация в Шаге 1F).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Persistence (Шаг 1E-part2):</b> сессия и раунды сохраняются
    /// в <c>AgentDebateSession</c> / <c>AgentDebateRound</c> через
    /// <see cref="IAgentDebateSessionService"/>, если <c>context.ChatId</c> задан.
    /// </para>
    ///
    /// <para>
    /// <b>SSE-события (Шаг 1E-part2):</b> <c>debate_started</c> / <c>debate_round</c>
    /// / <c>debate_completed</c> пишутся в <c>context.EventWriter</c>
    /// (ChannelWriter) — <c>ChatStreamService</c> стримит их клиенту параллельно.
    /// </para>
    ///
    /// <para>
    /// <b>Human-in-the-loop (Шаг 1E-part2):</b> при <c>HumanApproval = "BetweenRounds"</c>
    /// после Rejected-раунда tool ждёт feedback через <see cref="IAgentDebateCoordinator"/>
    /// (макс 5 мин). Feedback передаётся в следующий раунд actor'а.
    /// </para>
    ///
    /// <para>
    /// <b>Отложено в 1F:</b> эскалация на <c>ask_external_llm</c> при <c>Uncertain</c>.
    /// </para>
    /// </summary>
    public class CodeAgentWithReviewTool : ITool
    {
        private const string CodeAgentToolName = "code_agent";
        private const string ReviewerToolName = "code_reviewer_agent";
        private const int DefaultMaxRounds = 3;
        private const int MaxTaskLength = 8000;
        private const int MaxContextLength = 20_000;
        private const int HardMaxRounds = 5;
        private static readonly TimeSpan FeedbackTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Ленивая фабрика реестра инструментов — разрывает DI-цикл
        /// (RULES § 4.51, ADR-002).
        /// </summary>
        /// <summary>
        /// Провайдер эскалации по умолчанию (v1.11.0, KI-126, Шаг 1F).
        /// </summary>
        private const string DefaultEscalationProvider = "deepseek";

        /// <summary>
        /// Максимум токенов для запроса к внешней LLM при эскалации.
        /// Достаточно для короткого ответа (не пишем код — только мнение).
        /// </summary>
        private const int EscalationMaxTokens = 1024;

        private readonly Func<IToolRegistry> _toolRegistryFactory;

        /// <summary>
        /// Сервис управления сессиями Actor-Critic (Scoped).
        /// </summary>
        private readonly IAgentDebateSessionService _sessionService;

        /// <summary>
        /// Координатор Human-in-the-loop (Singleton).
        /// </summary>
        private readonly IAgentDebateCoordinator _coordinator;

        /// <summary>
        /// Клиент внешних LLM (Singleton) — для эскалации при Uncertain
        /// (v1.11.0, KI-126, Шаг 1F).
        /// </summary>
        private readonly IExternalLlmClient _externalLlm;

        /// <summary>
        /// Реестр провайдеров (Singleton) — для проверки, что
        /// <c>EscalationProvider</c> зарегистрирован (v1.11.0, Шаг 1F).
        /// </summary>
        private readonly IExternalProviderRegistry _externalProviderRegistry;

        private readonly IConfiguration _configuration;
        private readonly ILogger<CodeAgentWithReviewTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="toolRegistryFactory">Ленивая фабрика реестра (RULES § 4.51).</param>
        /// <param name="sessionService">Сервис управления сессиями.</param>
        /// <param name="coordinator">Координатор Human-in-the-loop.</param>
        /// <param name="externalLlm">Клиент внешних LLM (для эскалации).</param>
        /// <param name="externalProviderRegistry">Реестр провайдеров External-LLM.</param>
        /// <param name="configuration">Конфигурация.</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null.</exception>
        public CodeAgentWithReviewTool(
            Func<IToolRegistry> toolRegistryFactory,
            IAgentDebateSessionService sessionService,
            IAgentDebateCoordinator coordinator,
            IExternalLlmClient externalLlm,
            IExternalProviderRegistry externalProviderRegistry,
            IConfiguration configuration,
            ILogger<CodeAgentWithReviewTool> logger)
        {
            _toolRegistryFactory = toolRegistryFactory
                ?? throw new ArgumentNullException(nameof(toolRegistryFactory));
            _sessionService = sessionService
                ?? throw new ArgumentNullException(nameof(sessionService));
            _coordinator = coordinator
                ?? throw new ArgumentNullException(nameof(coordinator));
            _externalLlm = externalLlm
                ?? throw new ArgumentNullException(nameof(externalLlm));
            _externalProviderRegistry = externalProviderRegistry
                ?? throw new ArgumentNullException(nameof(externalProviderRegistry));
            _configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger
                ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "code_agent_with_review";

        /// <inheritdoc />
        public string Description =>
            "Кодинг с итеративным ревью (Actor-Critic). Для СЛОЖНЫХ задач кодинга: " +
            "алгоритмы, парсеры, обработка данных, security-критичный код. " +
            "Actor (code_agent) пишет код → Critic (code_reviewer_agent) проверяет → " +
            "если есть замечания, actor исправляет. До 3 раундов или до Approved. " +
            "НЕ используй для простых операций (rename, add import, тривиальные функции) — " +
            "там достаточно code_agent. Требует подтверждения (approval).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "task",
                Type = "string",
                Description = "Подробное описание задачи кодинга (максимум 8000 символов).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "context",
                Type = "string",
                Description = "Дополнительный контекст: требования, ограничения, примеры (максимум 20 000 символов).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "maxRounds",
                Type = "integer",
                Description = "Максимум раундов actor-critic (1–5). По умолчанию — из конфига (SubAgents:code_agent_with_review:MaxRounds, стандарт 3).",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context, JObject arguments)
        {
            if (context == null)
                return ToolResult.Fail("Контекст выполнения не задан.");

            // 1. Валидация.
            var task = arguments?.GetString("task");
            if (string.IsNullOrWhiteSpace(task))
                return ToolResult.Fail("Не указана задача.");

            if (task.Length > MaxTaskLength)
                return ToolResult.Fail($"Описание задачи превышает {MaxTaskLength} символов.");

            var extraContext = arguments?.GetString("context");
            if (extraContext != null && extraContext.Length > MaxContextLength)
                return ToolResult.Fail($"Контекст превышает {MaxContextLength} символов.");

            var maxRounds = arguments != null ? arguments.GetInt("maxRounds", 0) : 0;
            if (maxRounds <= 0)
            {
                maxRounds = GetIntConfig(
                    "SubAgents:code_agent_with_review:MaxRounds", DefaultMaxRounds);
            }
            maxRounds = Math.Clamp(maxRounds, 1, HardMaxRounds);

            // 2. Persistence: создаём сессию (если есть ChatId).
            int? sessionId = null;
            AgentDebateConfigSnapshot configSnapshot = null;

            if (context.ChatId.HasValue && context.ChatId.Value > 0)
            {
                try
                {
                    configSnapshot = new AgentDebateConfigSnapshot
                    {
                        MaxRounds = maxRounds,
                        TokenBudget = GetIntConfig(
                            "SubAgents:code_agent_with_review:TokenBudget", 50_000),
                        ActorModel = GetStringConfig(
                            "SubAgents:code_agent:Model", null),
                        CriticModel = GetStringConfig(
                            "SubAgents:code_reviewer_agent:Model", null),
                        AllowEscalation = GetBoolConfig(
                            "SubAgents:code_agent_with_review:AllowEscalation", true),
                        EscalationProvider = GetStringConfig(
                            "SubAgents:code_agent_with_review:EscalationProvider", "deepseek"),
                        HumanApproval = GetStringConfig(
                            "SubAgents:code_agent_with_review:HumanApproval", "BetweenRounds")
                    };

                    sessionId = await _sessionService.StartAsync(
                        context.ChatId.Value,
                        context.UserId,
                        task,
                        configSnapshot,
                        context.CancellationToken);

                    await _sessionService.MarkInProgressAsync(
                        sessionId.Value, context.CancellationToken);

                    // SSE: debate_started
                    // v1.11.0 (KI-126, Шаг 1G.3): передаём HumanApproval —
                    // UI решает, показывать ли feedback-блок после Rejected.
                    TryEmit(context, ChatStreamEvent.DebateStarted(new ChatDebateStartedDto
                    {
                        SessionId = sessionId.Value,
                        Task = task,
                        ActorAgent = CodeAgentToolName,
                        CriticAgent = ReviewerToolName,
                        MaxRounds = maxRounds,
                        HumanApproval = configSnapshot?.HumanApproval
                    }));

                    _logger.LogInformation(
                        "Actor-Critic: sessionId={SessionId}, chatId={ChatId}, maxRounds={MaxRounds}",
                        sessionId, context.ChatId, maxRounds);
                }
                catch (InvalidOperationException ex)
                {
                    // Concurrency-guard (max 3) или чат не найден.
                    _logger.LogWarning(
                        "Не удалось создать debate-сессию: {Message}", ex.Message);
                    return ToolResult.Fail(ex.Message);
                }
            }
            else
            {
                _logger.LogDebug(
                    "CodeAgentWithReview: без ChatId (sessionId не создаётся, " +
                    "SSE-события не пишутся).");
            }

            // 3. Цикл Actor-Critic.
            var sw = Stopwatch.StartNew();
            var rounds = new List<AgentDebateRoundDto>();

            string lastActorOutput = null;
            string lastVerdict = "Pending";
            string lastFeedback = null;

            for (var round = 1;
                 round <= maxRounds && !context.CancellationToken.IsCancellationRequested;
                 round++)
            {
                // 3a. Actor.
                var actorTask = round == 1
                    ? task
                    : BuildRevisionTask(task, lastFeedback);

                var actorArgs = new JObject { ["task"] = actorTask };
                if (!string.IsNullOrEmpty(extraContext))
                    actorArgs["context"] = extraContext;

                _logger.LogInformation(
                    "Actor-Critic round {Round}/{MaxRounds}: actor (user={UserId})",
                    round, maxRounds, context.UserId);

                var toolRegistry = _toolRegistryFactory();
                var actorResult = await toolRegistry.ExecuteAsync(
                    CodeAgentToolName, context, actorArgs);

                if (!actorResult.Success)
                {
                    _logger.LogWarning(
                        "Actor-Critic round {Round}: actor fail — {Message}",
                        round, actorResult.Message);

                    if (sessionId.HasValue)
                    {
                        await SafeCompleteAsync(
                            sessionId.Value, "Failed", null, context.CancellationToken);
                    }

                    return ToolResult.Fail(
                        $"Actor (code_agent) не справился в раунде {round}: {actorResult.Message}");
                }

                var actorOutput = ExtractFinalAnswer(actorResult.Data);
                lastActorOutput = actorOutput;

                // 3b. Critic.
                var criticTask = BuildCriticTask(task, actorOutput);
                var criticArgs = new JObject { ["task"] = criticTask };

                _logger.LogInformation(
                    "Actor-Critic round {Round}/{MaxRounds}: critic", round, maxRounds);

                var criticResult = await toolRegistry.ExecuteAsync(
                    ReviewerToolName, context, criticArgs);

                string verdict;
                string feedbackJson;
                string summary;

                if (!criticResult.Success)
                {
                    _logger.LogWarning(
                        "Actor-Critic round {Round}: critic fail — {Message}",
                        round, criticResult.Message);

                    verdict = "Uncertain";
                    feedbackJson = null;
                    summary = criticResult.Message;
                }
                else
                {
                    var criticAnswer = ExtractFinalAnswer(criticResult.Data);
                    (verdict, feedbackJson, summary) = ParseCriticVerdict(criticAnswer);
                }

                _logger.LogInformation(
                    "Actor-Critic round {Round}/{MaxRounds}: verdict = {Verdict}",
                    round, maxRounds, verdict);

                // 3c. Эскалация на внешнюю LLM при Uncertain (v1.11.0, KI-126, Шаг 1F).
                var wasEscalated = false;
                string escalationProvider = null;
                var escalationCost = 0m;
                var escalationTokensIn = 0;
                var escalationTokensOut = 0;

                if (verdict == "Uncertain"
                    && configSnapshot != null
                    && configSnapshot.AllowEscalation)
                {
                    var critReason = feedbackJson ?? summary
                        ?? "Критик не уверен в корректности кода.";

                    var escResult = await TryEscalateAsync(
                        context,
                        sessionId,
                        task,
                        actorOutput,
                        critReason,
                        context.CancellationToken);

                    if (escResult.Escalated)
                    {
                        verdict = escResult.Verdict;
                        feedbackJson = escResult.Feedback;
                        summary = escResult.Summary;
                        wasEscalated = true;
                        escalationProvider = escResult.Provider;
                        escalationCost = escResult.Cost;
                        escalationTokensIn = escResult.TokensIn;
                        escalationTokensOut = escResult.TokensOut;

                        _logger.LogInformation(
                            "Actor-Critic: после эскалации verdict = {Verdict}", verdict);
                    }
                }

                // 3d. Persist round (после эскалации — финальный verdict).
                var roundDto = new AgentDebateRoundDto
                {
                    RoundNumber = round,
                    ActorOutput = actorOutput,
                    CriticVerdict = verdict,
                    CriticFeedbackJson = feedbackJson,
                    ActorModel = configSnapshot?.ActorModel,
                    CriticModel = configSnapshot?.CriticModel,
                    WasEscalated = wasEscalated,
                    EscalationProvider = escalationProvider,
                    TokensIn = escalationTokensIn,
                    TokensOut = escalationTokensOut,
                    CostUsd = escalationCost,
                    DurationMs = (int)sw.ElapsedMilliseconds
                };

                rounds.Add(roundDto);
                lastVerdict = verdict;
                lastFeedback = feedbackJson ?? summary;

                if (sessionId.HasValue)
                {
                    await SafeAddRoundAsync(
                        sessionId.Value, roundDto, context.CancellationToken);
                }

                // SSE: debate_round
                TryEmit(context, ChatStreamEvent.DebateRound(new ChatDebateRoundDto
                {
                    SessionId = sessionId ?? 0,
                    RoundNumber = round,
                    ActorOutput = actorOutput,
                    CriticVerdict = verdict,
                    CriticFeedback = feedbackJson ?? summary,
                    WasEscalated = wasEscalated
                }));

                // 3e. Условия выхода.
                if (verdict == "Approved")
                {
                    _logger.LogInformation(
                        "Actor-Critic: Approved после {Round} раунд(ов).", round);
                    break;
                }

                if (verdict == "Uncertain")
                {
                    _logger.LogInformation(
                        "Actor-Critic: Uncertain в раунде {Round} — " +
                        "эскалация не помогла, завершаем.", round);
                    break;
                }

                // 3e. Human-in-the-loop (между раундами) — только если ещё есть раунды.
                if (verdict == "Rejected"
                    && round < maxRounds
                    && sessionId.HasValue
                    && IsHumanApprovalEnabled(configSnapshot))
                {
                    _logger.LogInformation(
                        "Actor-Critic: ожидание feedback пользователя (sessionId={SessionId})",
                        sessionId);

                    var userFeedback = await _coordinator.WaitForFeedbackAsync(
                        sessionId.Value,
                        FeedbackTimeout,
                        context.CancellationToken);

                    if (!string.IsNullOrWhiteSpace(userFeedback))
                    {
                        // Пользовательский feedback имеет приоритет над критиком.
                        lastFeedback = userFeedback;
                        _logger.LogInformation(
                            "Actor-Critic: feedback принят (len={Len}), продолжаем",
                            userFeedback.Length);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Actor-Critic: feedback не получен (таймаут/cancel), продолжаем с feedback критика");
                    }
                }

                // verdict == "Rejected" → следующий раунд.
            }

            sw.Stop();

            if (context.CancellationToken.IsCancellationRequested)
            {
                if (sessionId.HasValue)
                {
                    await SafeCompleteAsync(
                        sessionId.Value, "Cancelled", null, CancellationToken.None);
                }
                return ToolResult.Fail("Операция отменена пользователем.");
            }

            // 4. Финальный вердикт.
            var finalVerdict = lastVerdict == "Approved"
                ? "Approved"
                : (rounds.Count >= maxRounds ? "MaxRoundsReached" : lastVerdict);

            // v1.11.0 (KI-132): не возвращаем rounds[] — каждый actorOutput
            // содержит полный код (~1500-2500 токенов), и при 2-3 раундах
            // tool_result раздувается до ~5000+ токенов. На следующей итерации
            // Chat LLM этот JSON попадает в контекст → превышение 8192/16384.
            // История раундов доступна через:
            //   - SSE-событие debate_round (live);
            //   - GET /api/chats/{id} → DebateSessions[].Rounds (F5).
            var resultData = new
            {
                verdict = finalVerdict,
                totalRounds = rounds.Count,
                maxRounds = maxRounds,
                finalArtifact = lastActorOutput,
                sessionId = sessionId,
                durationMs = sw.ElapsedMilliseconds
            };

            // Persist completion.
            if (sessionId.HasValue)
            {
                await SafeCompleteAsync(
                    sessionId.Value,
                    finalVerdict,
                    JsonConvert.SerializeObject(new { finalArtifact = lastActorOutput }),
                    context.CancellationToken);

                // SSE: debate_completed
                TryEmit(context, ChatStreamEvent.DebateCompleted(new ChatDebateCompletedDto
                {
                    SessionId = sessionId.Value,
                    Verdict = finalVerdict,
                    TotalRounds = rounds.Count,
                    FinalArtifact = lastActorOutput,
                    TotalCostUsd = 0m
                }));
            }

            var message = finalVerdict switch
            {
                "Approved" => $"Код одобрен критиком после {rounds.Count} раунд(ов).",
                "MaxRoundsReached" => $"Достигнут лимит раундов ({maxRounds}). Финальный вердикт: {lastVerdict}.",
                "Uncertain" => "Критик не уверен — рекомендуется эскалация на внешнюю LLM (KI-126, Фаза 1F).",
                _ => $"Финальный вердикт: {finalVerdict}."
            };

            return ToolResult.Ok(resultData, message);
        }

        // ============ Helpers ============

        /// <summary>
        /// Пишет SSE-событие в <see cref="ToolExecutionContext.EventWriter"/>,
        /// если он установлен. Игнорирует ошибки записи.
        /// </summary>
        private static void TryEmit(ToolExecutionContext context, ChatStreamEvent evt)
        {
            if (context?.EventWriter == null || evt == null) return;
            try
            {
                context.EventWriter.TryWrite(evt);
            }
            catch
            {
                // Канал закрыт — не критично.
            }
        }

        /// <summary>
        /// Проверяет, включён ли Human-in-the-loop между раундами.
        /// </summary>
        private static bool IsHumanApprovalEnabled(AgentDebateConfigSnapshot snapshot)
        {
            if (snapshot == null) return true;
            return string.Equals(
                snapshot.HumanApproval, "BetweenRounds", StringComparison.OrdinalIgnoreCase);
        }

        private async Task SafeCompleteAsync(
            int sessionId, string verdict, string artifactJson, CancellationToken ct)
        {
            try
            {
                await _sessionService.CompleteAsync(sessionId, verdict, artifactJson, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось завершить сессию {SessionId}", sessionId);
            }
        }

        private async Task SafeAddRoundAsync(
            int sessionId, AgentDebateRoundDto round, CancellationToken ct)
        {
            try
            {
                await _sessionService.AddRoundAsync(sessionId, round, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось добавить раунд {Round} в сессию {SessionId}",
                    round.RoundNumber, sessionId);
            }
        }

        private static string BuildRevisionTask(string originalTask, string feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback))
                return originalTask;

            return
                $"{originalTask}\n\n" +
                "⚠️ FEEDBACK ОТ КРИТИКА (учти ВСЕ замечания и перепиши код):\n" +
                feedback;
        }

        private static string BuildCriticTask(string originalTask, string code)
        {
            return
                "Проверь код, написанный для следующей задачи:\n\n" +
                $"ЗАДАЧА:\n{originalTask}\n\n" +
                $"КОД:\n{code}\n\n" +
                "Верни СТРОГО JSON: {verdict, issues, summary}.";
        }

        /// <summary>
        /// Второй раунд критика с ответом внешней LLM как доп. контекстом
        /// (v1.11.0, KI-126, Шаг 1F).
        /// </summary>
        private static string BuildCriticTaskWithExternalContext(
            string originalTask, string code, string externalProvider, string externalAnswer)
        {
            return
                "Проверь код, написанный для следующей задачи (повторное ревью " +
                "после консультации с внешней моделью):\n\n" +
                $"ЗАДАЧА:\n{originalTask}\n\n" +
                $"КОД:\n{code}\n\n" +
                $"ВНЕШНЯЯ МОДЕЛЬ ({externalProvider}) ДАЛА СЛЕДУЮЩИЙ ОТВЕТ " +
                "НА ТВОЙ ВОПРОС:\n" +
                $"{externalAnswer}\n\n" +
                "Учти это мнение. Верни СТРОГО JSON: {verdict, issues, summary}.";
        }

        /// <summary>
        /// Эскалация на внешнюю LLM при Uncertain + повторное ревью
        /// (v1.11.0, KI-126, Шаг 1F, DESIGN § 3.3).
        ///
        /// <para>
        /// Возвращает <c>Escalated = false</c> (без изменений verdict), если:
        /// провайдер не зарегистрирован, HTTP-запрос упал, второй раунд
        /// критика упал. <c>Escalated = true</c> + финальный verdict —
        /// при успехе.
        /// </para>
        /// </summary>
        private async Task<(bool Escalated, string Verdict, string Feedback,
            string Summary, string Provider, decimal Cost, int TokensIn, int TokensOut)>
            TryEscalateAsync(
                ToolExecutionContext context,
                int? sessionId,
                string originalTask,
                string actorOutput,
                string criticReason,
                CancellationToken cancellationToken)
        {
            var providerName = GetStringConfig(
                "SubAgents:code_agent_with_review:EscalationProvider",
                DefaultEscalationProvider);

            if (_externalProviderRegistry.Get(providerName) == null)
            {
                _logger.LogWarning(
                    "Escalation: провайдер '{Provider}' не зарегистрирован " +
                    "(ExternalLlm:Enabled=false или опечатка) — пропускаем эскалацию.",
                    providerName);
                return (false, null, null, null, null, 0m, 0, 0);
            }

            // 1. Формируем prompt для внешней LLM.
            var escalationPrompt =
                "Ты — эксперт-консультант. Другой критик проверял код и " +
                "не смог дать однозначный вердикт. Помоги разобраться.\n\n" +
                "ВОПРОС КРИТИКА:\n" +
                $"{criticReason}\n\n" +
                "ИСХОДНАЯ ЗАДАЧА:\n" +
                $"{originalTask}\n\n" +
                "ПРОВЕРЯЕМЫЙ КОД:\n" +
                $"{actorOutput}\n\n" +
                "Ответь КРАТКО (1 абзац, без кода): прав ли критик? Есть ли " +
                "РЕАЛЬНЫЕ проблемы (edge cases, безопасность, обработка ошибок)?";

            ExternalLlmResponse response = null;
            string failureReason = null;

            try
            {
                var request = new ExternalLlmRequest
                {
                    Provider = providerName,
                    Prompt = escalationPrompt,
                    IncludeContext = false,
                    MaxTokens = EscalationMaxTokens,
                    Temperature = 0.3
                };

                response = await _externalLlm.CompleteAsync(
                    context.UserId, request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;   // Пробрасываем — вызывающий код обработает.
            }
            catch (Exception ex)
            {
                // Budget exceeded / circuit breaker / HTTP error.
                _logger.LogWarning(ex,
                    "Escalation: вызов '{Provider}' упал — оставляем Uncertain.",
                    providerName);
                failureReason = ex.Message;
            }

            // 2. SSE: debate_escalated (success или failure).
            var escalatedDto = new ChatDebateEscalatedDto
            {
                SessionId = sessionId ?? 0,
                Reason = response != null
                    ? criticReason
                    : $"Escalation failed: {failureReason}",
                ExternalProvider = providerName,
                CostUsd = response?.CostUsd ?? 0m
            };
            TryEmit(context, ChatStreamEvent.DebateEscalated(escalatedDto));

            if (response == null)
            {
                return (false, null, null, null, null, 0m, 0, 0);
            }

            // 3. Второй раунд критика с ответом внешней LLM.
            var secondCriticTask = BuildCriticTaskWithExternalContext(
                originalTask, actorOutput, providerName, response.Content ?? "(пусто)");

            var registry = _toolRegistryFactory();
            var secondCriticResult = await registry.ExecuteAsync(
                ReviewerToolName, context,
                new JObject { ["task"] = secondCriticTask });

            if (!secondCriticResult.Success)
            {
                _logger.LogWarning(
                    "Escalation: второй раунд критика упал ({Message}) — " +
                    "оставляем Uncertain.",
                    secondCriticResult.Message);
                return (false, null, null, null, null, 0m, 0, 0);
            }

            var secondAnswer = ExtractFinalAnswer(secondCriticResult.Data);
            var (secondVerdict, secondFeedback, secondSummary) =
                ParseCriticVerdict(secondAnswer);

            _logger.LogInformation(
                "Escalation: второй раунд критика → verdict = {Verdict}",
                secondVerdict);

            return (true,
                    secondVerdict,
                    secondFeedback,
                    secondSummary,
                    providerName,
                    response.CostUsd,
                    response.PromptTokens,
                    response.CompletionTokens);
        }

        private static string ExtractFinalAnswer(object data)
        {
            if (data == null) return string.Empty;

            if (data is JObject jObj)
                return jObj["finalAnswer"]?.ToString() ?? string.Empty;

            try
            {
                var json = JObject.FromObject(data);
                return json["finalAnswer"]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static (string Verdict, string FeedbackJson, string Summary) ParseCriticVerdict(
            string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return ("Uncertain", null, null);

            try
            {
                var clean = StripMarkdownFences(answer);
                var obj = JObject.Parse(clean);

                var verdict = obj["verdict"]?.ToString();
                var issues = obj["issues"];
                var summary = obj["summary"]?.ToString();

                if (!string.IsNullOrWhiteSpace(verdict))
                {
                    var normalized = NormalizeVerdict(verdict);
                    var issuesJson = issues != null && issues.Type != JTokenType.Null
                        ? issues.ToString(Formatting.None)
                        : null;
                    return (normalized, issuesJson, summary);
                }
            }
            catch { }

            var verdictFromText = FindVerdictInText(answer);
            var summaryShort = answer.Length > 500 ? answer.Substring(0, 500) : answer;
            return (verdictFromText, null, summaryShort);
        }

        private static string StripMarkdownFences(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;

            s = s.Trim();
            if (!s.StartsWith("```")) return s;

            var firstNl = s.IndexOf('\n');
            if (firstNl < 0) return s;

            s = s.Substring(firstNl + 1);

            var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                s = s.Substring(0, lastFence);

            return s.Trim();
        }

        private static string FindVerdictInText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Uncertain";

            if (text.Contains("\"Approved\"", StringComparison.Ordinal) ||
                text.Contains("'Approved'", StringComparison.Ordinal))
                return "Approved";

            if (text.Contains("\"Rejected\"", StringComparison.Ordinal) ||
                text.Contains("'Rejected'", StringComparison.Ordinal))
                return "Rejected";

            if (text.Contains("\"Uncertain\"", StringComparison.Ordinal) ||
                text.Contains("'Uncertain'", StringComparison.Ordinal))
                return "Uncertain";

            var approvedIdx = text.IndexOf("Approved", StringComparison.OrdinalIgnoreCase);
            var rejectedIdx = text.IndexOf("Rejected", StringComparison.OrdinalIgnoreCase);
            var uncertainIdx = text.IndexOf("Uncertain", StringComparison.OrdinalIgnoreCase);

            var minIdx = int.MaxValue;
            var result = "Uncertain";

            if (approvedIdx >= 0 && approvedIdx < minIdx) { minIdx = approvedIdx; result = "Approved"; }
            if (rejectedIdx >= 0 && rejectedIdx < minIdx) { minIdx = rejectedIdx; result = "Rejected"; }
            if (uncertainIdx >= 0 && uncertainIdx < minIdx) { minIdx = uncertainIdx; result = "Uncertain"; }

            return result;
        }

        private static string NormalizeVerdict(string raw)
        {
            var v = (raw ?? string.Empty).Trim();

            if (v.Equals("Approved", StringComparison.OrdinalIgnoreCase)) return "Approved";
            if (v.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
            if (v.Equals("Uncertain", StringComparison.OrdinalIgnoreCase)) return "Uncertain";

            return "Uncertain";
        }

        private int GetIntConfig(string key, int defaultValue)
        {
            var raw = _configuration[key];
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        private bool GetBoolConfig(string key, bool defaultValue)
        {
            var raw = _configuration[key];
            return bool.TryParse(raw, out var v) ? v : defaultValue;
        }

        private string GetStringConfig(string key, string defaultValue)
        {
            var raw = _configuration[key];
            return string.IsNullOrWhiteSpace(raw) ? defaultValue : raw;
        }
    }
}
