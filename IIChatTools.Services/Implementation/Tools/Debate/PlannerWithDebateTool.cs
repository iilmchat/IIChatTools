using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.DTO.ExternalLlm;   // v1.14.0 (KI-126, Ф2, Шаг 2C.2): ExternalLlmRequest/Response
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Debate
{
    /// <summary>
    /// Top-level инструмент «Планирование с Debate» — оркестратор 3-ролевого
    /// паттерна Pro / Contra / Judge (v1.14.0, KI-126, Фаза 2, Шаг 2C.1b).
    ///
    /// <para>
    /// <b>Не наследует <c>AgentToolBase</c></b> — по образцу
    /// <c>CodeAgentWithReviewTool</c> (DESIGN_MULTI_AGENT_DEBATE § 9.2).
    /// Координирует Pro (<c>planner_pro_agent</c>), Contra
    /// (<c>planner_contra_agent</c>) и Judge (<c>planner_judge_agent</c>)
    /// через <see cref="IToolRegistry"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Цикл раунда:</b> Pro → Contra → Judge (последовательно, 3 LLM-вызова).
    /// </para>
    ///
    /// <para>
    /// <b>Persistence:</b> сессия и раунды сохраняются в
    /// <c>AgentDebateSession</c> / <c>AgentDebateRound</c> через
    /// <see cref="IAgentDebateSessionService"/>, если <c>context.ChatId</c> задан.
    /// Используются поля Debate: <c>ProOutput</c> / <c>ContraOutput</c> /
    /// <c>JudgeVerdict</c> / <c>JudgeFeedbackJson</c>.
    /// </para>
    ///
    /// <para>
    /// <b>SSE-события:</b> <c>debate_started</c> / <c>debate_round</c> /
    /// <c>debate_completed</c> с <c>PatternType = "Debate"</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Отложено в Шаг 2C.2:</b>
    /// <list type="bullet">
    ///   <item>эскалация на <c>ask_external_llm</c> при Judge <c>Uncertain</c>;</item>
    ///   <item>Human-in-the-loop между раундами после Judge <c>Rejected</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class PlannerWithDebateTool : ITool
    {
        private const string ProToolName = "planner_pro_agent";
        private const string ContraToolName = "planner_contra_agent";
        private const string JudgeToolName = "planner_judge_agent";

        private const int DefaultMaxRounds = 3;
        private const int MaxTaskLength = 8000;
        private const int MaxContextLength = 20_000;
        private const int HardMaxRounds = 5;

        /// <summary>
        /// Тип паттерна (в ConfigSnapshot + SSE-DTO).
        /// </summary>
        private const string PatternDebate = "Debate";

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): провайдер эскалации по умолчанию
        /// (при Judge verdict = Uncertain).
        /// </summary>
        private const string DefaultEscalationProvider = "deepseek";

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): лимит токенов для запроса к внешней
        /// LLM при эскалации. Достаточно для короткого мнения — не код.
        /// </summary>
        private const int EscalationMaxTokens = 1024;

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): таймаут ожидания feedback
        /// пользователя между раундами (Human-in-the-loop).
        /// </summary>
        private static readonly TimeSpan FeedbackTimeout = TimeSpan.FromMinutes(5);

        private readonly Func<IToolRegistry> _toolRegistryFactory;
        private readonly IAgentDebateSessionService _sessionService;

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): координатор Human-in-the-loop
        /// (Singleton, по образцу CodeAgentWithReviewTool).
        /// </summary>
        private readonly IAgentDebateCoordinator _coordinator;

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): клиент внешних LLM (Singleton) —
        /// для эскалации при Judge Uncertain.
        /// </summary>
        private readonly IExternalLlmClient _externalLlm;

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): реестр провайдеров (Singleton) —
        /// для проверки, что EscalationProvider зарегистрирован.
        /// </summary>
        private readonly IExternalProviderRegistry _externalProviderRegistry;

        private readonly IConfiguration _configuration;
        private readonly ILogger<PlannerWithDebateTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="toolRegistryFactory">Ленивая фабрика реестра (RULES § 4.51).</param>
        /// <param name="sessionService">Сервис управления сессиями.</param>
        /// <param name="coordinator">Координатор Human-in-the-loop (Singleton).</param>
        /// <param name="externalLlm">Клиент внешних LLM (для эскалации).</param>
        /// <param name="externalProviderRegistry">Реестр провайдеров External-LLM.</param>
        /// <param name="configuration">Конфигурация.</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null.</exception>
        public PlannerWithDebateTool(
            Func<IToolRegistry> toolRegistryFactory,
            IAgentDebateSessionService sessionService,
            IAgentDebateCoordinator coordinator,
            IExternalLlmClient externalLlm,
            IExternalProviderRegistry externalProviderRegistry,
            IConfiguration configuration,
            ILogger<PlannerWithDebateTool> logger)
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
        public string Name => "planner_with_debate";

        /// <inheritdoc />
        public string Description =>
            "Планирование с 3-ролевым Debate (Pro / Contra / Judge). " +
            "Используется для СЛОЖНЫХ архитектурных / стратегических решений: " +
            "выбор подхода, планирование фичи, сравнение технологий, оценка рисков. " +
            "Pro защищает предложение, Contra критикует, Judge выносит вердикт " +
            "(Approved / Rejected / Uncertain). До 3 раундов. " +
            "НЕ используй для простых задач (сохрани факт в память, задай вопрос) — " +
            "там достаточно planner_agent. Требует подтверждения (approval).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "task",
                Type = "string",
                Description =
                    "Предложение / план / архитектурное решение для оценки (максимум 8000 символов). " +
                    "Формулировка: «Использовать REST для API», «Мигрировать на Qdrant», " +
                    "«Хранить RAG-чанки в Sqlite».",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "context",
                Type = "string",
                Description =
                    "Дополнительный контекст: ограничения, требования, известные факты " +
                    "(максимум 20 000 символов).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "maxRounds",
                Type = "integer",
                Description =
                    "Максимум раундов Debate (1–5). По умолчанию — из конфига " +
                    "(SubAgents:planner_with_debate:MaxRounds, стандарт 3).",
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
                return ToolResult.Fail("Не указано предложение / задача для Debate.");

            if (task.Length > MaxTaskLength)
                return ToolResult.Fail($"Описание предложения превышает {MaxTaskLength} символов.");

            var extraContext = arguments?.GetString("context");
            if (extraContext != null && extraContext.Length > MaxContextLength)
                return ToolResult.Fail($"Контекст превышает {MaxContextLength} символов.");

            var maxRounds = arguments != null ? arguments.GetInt("maxRounds", 0) : 0;
            if (maxRounds <= 0)
            {
                maxRounds = GetIntConfig(
                    "SubAgents:planner_with_debate:MaxRounds", DefaultMaxRounds);
            }
            maxRounds = Math.Clamp(maxRounds, 1, HardMaxRounds);

            // 2. Persistence: создаём сессию.
            int? sessionId = null;
            AgentDebateConfigSnapshot configSnapshot = null;

            if (context.ChatId.HasValue && context.ChatId.Value > 0)
            {
                try
                {
                    configSnapshot = new AgentDebateConfigSnapshot
                    {
                        PatternType = PatternDebate,
                        MaxRounds = maxRounds,
                        TokenBudget = GetIntConfig(
                            "SubAgents:planner_with_debate:TokenBudget", 50_000),
                        ProModel = GetStringConfig(
                            "SubAgents:planner_pro_agent:Model", null),
                        ContraModel = GetStringConfig(
                            "SubAgents:planner_contra_agent:Model", null),
                        JudgeModel = GetStringConfig(
                            "SubAgents:planner_judge_agent:Model", null),
                        AllowEscalation = GetBoolConfig(
                            "SubAgents:planner_with_debate:AllowEscalation", true),
                        EscalationProvider = GetStringConfig(
                            "SubAgents:planner_with_debate:EscalationProvider", "deepseek"),
                        HumanApproval = GetStringConfig(
                            "SubAgents:planner_with_debate:HumanApproval", "BetweenRounds")
                    };

                    sessionId = await _sessionService.StartAsync(
                        context.ChatId.Value,
                        context.UserId,
                        task,
                        configSnapshot,
                        context.CancellationToken);

                    await _sessionService.MarkInProgressAsync(
                        sessionId.Value, context.CancellationToken);

                    TryEmit(context, ChatStreamEvent.DebateStarted(new ChatDebateStartedDto
                    {
                        SessionId = sessionId.Value,
                        Task = task,
                        PatternType = PatternDebate,
                        ProAgent = ProToolName,
                        ContraAgent = ContraToolName,
                        JudgeAgent = JudgeToolName,
                        MaxRounds = maxRounds,
                        HumanApproval = configSnapshot.HumanApproval
                    }));

                    _logger.LogInformation(
                        "Debate: sessionId={SessionId}, chatId={ChatId}, maxRounds={MaxRounds}",
                        sessionId, context.ChatId, maxRounds);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        "Не удалось создать debate-сессию: {Message}", ex.Message);
                    return ToolResult.Fail(ex.Message);
                }
            }
            else
            {
                _logger.LogDebug(
                    "PlannerWithDebate: без ChatId (sessionId не создаётся, " +
                    "SSE-события не пишутся).");
            }

            // 3. Цикл Pro → Contra → Judge.
            var sw = Stopwatch.StartNew();
            var rounds = new List<AgentDebateRoundDto>();

            string lastProOutput = null;
            string lastVerdict = "Pending";
            string lastJudgeFeedback = null;

            for (var round = 1;
                 round <= maxRounds && !context.CancellationToken.IsCancellationRequested;
                 round++)
            {
                var toolRegistry = _toolRegistryFactory();

                // 3a. Pro.
                var proTask = BuildProTask(task, round, lastJudgeFeedback);
                var proArgs = new JObject { ["task"] = proTask };
                if (!string.IsNullOrEmpty(extraContext))
                    proArgs["context"] = extraContext;

                _logger.LogInformation(
                    "Debate round {Round}/{MaxRounds}: Pro (user={UserId})",
                    round, maxRounds, context.UserId);

                var proResult = await toolRegistry.ExecuteAsync(
                    ProToolName, context, proArgs);

                if (!proResult.Success)
                {
                    _logger.LogWarning(
                        "Debate round {Round}: Pro fail — {Message}",
                        round, proResult.Message);

                    if (sessionId.HasValue)
                    {
                        await SafeCompleteAsync(
                            sessionId.Value, "Failed", null, context.CancellationToken);
                    }

                    return ToolResult.Fail(
                        $"Pro-агент не справился в раунде {round}: {proResult.Message}");
                }

                var proOutput = ExtractFinalAnswer(proResult.Data);
                lastProOutput = proOutput;

                // 3b. Contra.
                var contraTask = BuildContraTask(task, proOutput);
                var contraArgs = new JObject { ["task"] = contraTask };

                _logger.LogInformation(
                    "Debate round {Round}/{MaxRounds}: Contra", round, maxRounds);

                var contraResult = await toolRegistry.ExecuteAsync(
                    ContraToolName, context, contraArgs);

                var contraOutput = contraResult.Success
                    ? ExtractFinalAnswer(contraResult.Data)
                    : $"{{\"error\": \"Contra-агент не справился: {contraResult.Message}\"}}";

                // 3c. Judge.
                var judgeTask = BuildJudgeTask(task, proOutput, contraOutput);
                var judgeArgs = new JObject { ["task"] = judgeTask };

                _logger.LogInformation(
                    "Debate round {Round}/{MaxRounds}: Judge", round, maxRounds);

                var judgeResult = await toolRegistry.ExecuteAsync(
                    JudgeToolName, context, judgeArgs);

                string verdict;
                string judgeFeedback;
                string judgeSummary;

                if (!judgeResult.Success)
                {
                    _logger.LogWarning(
                        "Debate round {Round}: Judge fail — {Message}",
                        round, judgeResult.Message);

                    verdict = "Uncertain";
                    judgeFeedback = null;
                    judgeSummary = judgeResult.Message;
                }
                else
                {
                    var judgeAnswer = ExtractFinalAnswer(judgeResult.Data);
                    (verdict, judgeFeedback, judgeSummary) = ParseJudgeVerdict(judgeAnswer);
                }

                _logger.LogInformation(
                    "Debate round {Round}/{MaxRounds}: verdict = {Verdict}",
                    round, maxRounds, verdict);

                // 3d. Эскалация на внешнюю LLM при Uncertain (v1.14.0, KI-126, Шаг 2C.2).
                var wasEscalated = false;
                string escalationProvider = null;
                var escalationCost = 0m;
                var escalationTokensIn = 0;
                var escalationTokensOut = 0;

                if (verdict == "Uncertain"
                    && configSnapshot != null
                    && configSnapshot.AllowEscalation)
                {
                    var judgeReason = judgeFeedback ?? judgeSummary
                        ?? "Арбитр не уверен в корректности предложения.";

                    var escResult = await TryEscalateAsync(
                        context,
                        sessionId,
                        task,
                        proOutput,
                        contraOutput,
                        judgeReason,
                        context.CancellationToken);

                    if (escResult.Escalated)
                    {
                        verdict = escResult.Verdict;
                        judgeFeedback = escResult.Feedback;
                        judgeSummary = escResult.Summary;
                        wasEscalated = true;
                        escalationProvider = escResult.Provider;
                        escalationCost = escResult.Cost;
                        escalationTokensIn = escResult.TokensIn;
                        escalationTokensOut = escResult.TokensOut;

                        _logger.LogInformation(
                            "Debate: после эскалации verdict = {Verdict}", verdict);
                    }
                }

                // 3e. Persist round (после эскалации — финальный verdict).
                var roundDto = new AgentDebateRoundDto
                {
                    RoundNumber = round,
                    ProOutput = proOutput,
                    ContraOutput = contraOutput,
                    JudgeVerdict = verdict,
                    JudgeFeedbackJson = judgeFeedback ?? judgeSummary,
                    WasEscalated = wasEscalated,
                    EscalationProvider = escalationProvider,
                    TokensIn = escalationTokensIn,
                    TokensOut = escalationTokensOut,
                    CostUsd = escalationCost,
                    DurationMs = (int)sw.ElapsedMilliseconds
                };

                rounds.Add(roundDto);
                lastVerdict = verdict;
                lastJudgeFeedback = judgeFeedback ?? judgeSummary;

                if (sessionId.HasValue)
                {
                    await SafeAddRoundAsync(
                        sessionId.Value, roundDto, context.CancellationToken);
                }

                // SSE: debate_round.
                TryEmit(context, ChatStreamEvent.DebateRound(new ChatDebateRoundDto
                {
                    SessionId = sessionId ?? 0,
                    RoundNumber = round,
                    PatternType = PatternDebate,
                    ProOutput = proOutput,
                    ContraOutput = contraOutput,
                    JudgeVerdict = verdict,
                    JudgeFeedback = judgeFeedback ?? judgeSummary,
                    WasEscalated = wasEscalated
                }));

                // 3f. Условия выхода.
                if (verdict == "Approved")
                {
                    _logger.LogInformation(
                        "Debate: Approved после {Round} раунд(ов).", round);
                    break;
                }

                if (verdict == "Uncertain")
                {
                    _logger.LogInformation(
                        "Debate: Uncertain в раунде {Round} — " +
                        "эскалация не помогла, завершаем.", round);
                    break;
                }

                // 3g. Human-in-the-loop (между раундами) — только если ещё есть
                //     раунды (v1.14.0, KI-126, Шаг 2C.2). По образцу
                //     CodeAgentWithReviewTool (KI-126, Шаг 1E-part2).
                if (verdict == "Rejected"
                    && round < maxRounds
                    && sessionId.HasValue
                    && IsHumanApprovalEnabled(configSnapshot))
                {
                    _logger.LogInformation(
                        "Debate: ожидание feedback пользователя (sessionId={SessionId})",
                        sessionId);

                    var userFeedback = await _coordinator.WaitForFeedbackAsync(
                        sessionId.Value,
                        FeedbackTimeout,
                        context.CancellationToken);

                    if (!string.IsNullOrWhiteSpace(userFeedback))
                    {
                        // Пользовательский feedback имеет приоритет над feedback Judge.
                        lastJudgeFeedback = userFeedback;
                        _logger.LogInformation(
                            "Debate: feedback принят (len={Len}), продолжаем",
                            userFeedback.Length);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Debate: feedback не получен (таймаут/cancel), " +
                            "продолжаем с feedback Judge");
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

                return ToolResult.Fail(
                    "Операция отменена пользователем.",
                    data: sessionId.HasValue
                        ? new { sessionId = sessionId.Value, cancelled = true }
                        : null);
            }

            // 4. Финальный вердикт.
            var finalVerdict = lastVerdict == "Approved"
                ? "Approved"
                : (rounds.Count >= maxRounds ? "MaxRoundsReached" : lastVerdict);

            var resultData = new
            {
                verdict = finalVerdict,
                totalRounds = rounds.Count,
                maxRounds = maxRounds,
                finalArtifact = lastProOutput,
                sessionId = sessionId,
                durationMs = sw.ElapsedMilliseconds
            };

            if (sessionId.HasValue)
            {
                await SafeCompleteAsync(
                    sessionId.Value,
                    finalVerdict,
                    JsonConvert.SerializeObject(new { finalArtifact = lastProOutput }),
                    context.CancellationToken);

                TryEmit(context, ChatStreamEvent.DebateCompleted(new ChatDebateCompletedDto
                {
                    SessionId = sessionId.Value,
                    PatternType = PatternDebate,
                    Verdict = finalVerdict,
                    TotalRounds = rounds.Count,
                    FinalArtifact = lastProOutput,
                    TotalCostUsd = 0m
                }));
            }

            var message = finalVerdict switch
            {
                "Approved" => $"Предложение одобрено арбитром после {rounds.Count} раунд(ов).",
                "MaxRoundsReached" => $"Достигнут лимит раундов ({maxRounds}). " +
                                      $"Финальный вердикт: {lastVerdict}.",
                "Uncertain" => "Арбитр не уверен — рекомендуется эскалация на внешнюю LLM " +
                               "(KI-126, Шаг 2C.2).",
                _ => $"Финальный вердикт: {finalVerdict}."
            };

            return ToolResult.Ok(resultData, message);
        }

        // ============ Helpers ============

        /// <summary>
        /// Пишет SSE-событие в <see cref="ToolExecutionContext.EventWriter"/>.
        /// Игнорирует ошибки записи (канал может быть закрыт).
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

        private async Task SafeCompleteAsync(
            int sessionId, string verdict, string artifactJson, CancellationToken ct)
        {
            try
            {
                await _sessionService.CompleteAsync(sessionId, verdict, artifactJson, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось завершить сессию {SessionId}", sessionId);
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

        /// <summary>
        /// Task для Pro-агента: защита предложения (+ feedback Judge предыдущего
        /// раунда, если есть). Явный запрет на использование инструментов.
        /// </summary>
        private static string BuildProTask(string originalTask, int round, string judgeFeedback)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Предложение для защиты:");
            sb.AppendLine(originalTask);
            sb.AppendLine();
            sb.AppendLine("⚠️ ЗАПРЕЩЕНО использовать инструменты (web_search, " +
                          "wikipedia_search, list_directory и т.п.). Отвечай " +
                          "ТОЛЬКО JSON-объектом по инструкции из system-prompt.");

            if (round > 1 && !string.IsNullOrWhiteSpace(judgeFeedback))
            {
                sb.AppendLine();
                sb.AppendLine("⚠️ FEEDBACK ОТ АРБИТРА ПРЕДЫДУЩЕГО РАУНДА " +
                              "(учти его в своей защите):");
                sb.AppendLine(judgeFeedback);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Task для Contra-агента: критика предложения с учётом аргументов Pro.
        /// </summary>
        private static string BuildContraTask(string originalTask, string proOutput)
        {
            return
                "Предложение:\n" + originalTask + "\n\n" +
                "Аргументы Pro (защитник):\n" + proOutput + "\n\n" +
                "⚠️ ЗАПРЕЩЕНО использовать инструменты. Найди дыры в предложении " +
                "и аргументах Pro. Отвечай ТОЛЬКО JSON-объектом по инструкции " +
                "из system-prompt.";
        }

        /// <summary>
        /// Task для Judge-агента: вердикт по аргументам Pro и Contra.
        /// </summary>
        private static string BuildJudgeTask(
            string originalTask, string proOutput, string contraOutput)
        {
            return
                "Предложение:\n" + originalTask + "\n\n" +
                "Аргументы Pro (защитник):\n" + proOutput + "\n\n" +
                "Претензии Contra (скептик):\n" + contraOutput + "\n\n" +
                "⚠️ ЗАПРЕЩЕНО использовать инструменты. Вынеси вердикт. " +
                "Отвечай ТОЛЬКО JSON-объектом по инструкции из system-prompt.";
        }

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): второй раунд Judge с ответом
        /// внешней LLM как доп. контекстом.
        /// </summary>
        private static string BuildJudgeTaskWithExternalContext(
            string originalTask,
            string proOutput,
            string contraOutput,
            string externalProvider,
            string externalAnswer)
        {
            return
                "Предложение:\n" + originalTask + "\n\n" +
                "Аргументы Pro (защитник):\n" + proOutput + "\n\n" +
                "Претензии Contra (скептик):\n" + contraOutput + "\n\n" +
                $"ВНЕШНЯЯ МОДЕЛЬ ({externalProvider}) ДАЛА СЛЕДУЮЩИЙ ОТВЕТ " +
                "на твой вопрос:\n" +
                externalAnswer + "\n\n" +
                "Учти это мнение. ⚠️ ЗАПРЕЩЕНО использовать инструменты. " +
                "Вынеси финальный вердикт. Отвечай ТОЛЬКО JSON-объектом " +
                "по инструкции из system-prompt.";
        }

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): включён ли Human-in-the-loop
        /// между раундами.
        /// </summary>
        private static bool IsHumanApprovalEnabled(AgentDebateConfigSnapshot snapshot)
        {
            if (snapshot == null) return true;
            return string.Equals(
                snapshot.HumanApproval, "BetweenRounds", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// v1.14.0 (KI-126, Ф2, Шаг 2C.2): эскалация на внешнюю LLM при
        /// Judge <c>Uncertain</c> + второй раунд Judge с ответом внешней
        /// модели. По образцу <c>CodeAgentWithReviewTool.TryEscalateAsync</c>
        /// (KI-126, Шаг 1F).
        ///
        /// <para>
        /// Возвращает <c>Escalated = false</c>, если:
        /// <list type="bullet">
        ///   <item>провайдер не зарегистрирован в <c>ExternalProviderRegistry</c>;</item>
        ///   <item>HTTP-запрос к внешней LLM упал (circuit breaker / budget / error);</item>
        ///   <item>второй раунд Judge'а упал.</item>
        /// </list>
        /// В этих случаях вызывающая сторона оставляет <c>Uncertain</c>.
        /// </para>
        /// </summary>
        private async Task<(bool Escalated, string Verdict, string Feedback,
            string Summary, string Provider, decimal Cost, int TokensIn, int TokensOut)>
            TryEscalateAsync(
                ToolExecutionContext context,
                int? sessionId,
                string originalTask,
                string proOutput,
                string contraOutput,
                string judgeReason,
                CancellationToken cancellationToken)
        {
            var providerName = GetStringConfig(
                "SubAgents:planner_with_debate:EscalationProvider",
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
                "Ты — эксперт-консультант. Другой арбитр (Judge) оценивал " +
                "архитектурное предложение и не смог дать однозначный вердикт. " +
                "Помоги разобраться.\n\n" +
                "ВОПРОС АРБИТРА:\n" + judgeReason + "\n\n" +
                "ПРЕДЛОЖЕНИЕ:\n" + originalTask + "\n\n" +
                "АРГУМЕНТЫ PRO (защитник):\n" + proOutput + "\n\n" +
                "ПРЕТЕНЗИИ CONTRA (скептик):\n" + contraOutput + "\n\n" +
                "Ответь КРАТКО (1 абзац): предложение заслуживает принятия? " +
                "Есть ли КРИТИЧЕСКИЕ проблемы (безопасность, масштабируемость, " +
                "реальные риски)? Без кода, только мнение.";

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
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Escalation: вызов '{Provider}' упал — оставляем Uncertain.",
                    providerName);
                failureReason = ex.Message;
            }

            // 2. SSE: debate_escalated.
            var escalatedDto = new ChatDebateEscalatedDto
            {
                SessionId = sessionId ?? 0,
                PatternType = PatternDebate,
                Reason = response != null
                    ? judgeReason
                    : $"Escalation failed: {failureReason}",
                ExternalProvider = providerName,
                CostUsd = response?.CostUsd ?? 0m
            };
            TryEmit(context, ChatStreamEvent.DebateEscalated(escalatedDto));

            if (response == null)
            {
                return (false, null, null, null, null, 0m, 0, 0);
            }

            // 3. Второй раунд Judge'а с ответом внешней LLM.
            var secondJudgeTask = BuildJudgeTaskWithExternalContext(
                originalTask, proOutput, contraOutput,
                providerName, response.Content ?? "(пусто)");

            var registry = _toolRegistryFactory();
            var secondJudgeResult = await registry.ExecuteAsync(
                JudgeToolName, context,
                new JObject { ["task"] = secondJudgeTask });

            if (!secondJudgeResult.Success)
            {
                _logger.LogWarning(
                    "Escalation: второй раунд Judge упал ({Message}) — " +
                    "оставляем Uncertain.",
                    secondJudgeResult.Message);
                return (false, null, null, null, null, 0m, 0, 0);
            }

            var secondAnswer = ExtractFinalAnswer(secondJudgeResult.Data);
            var (secondVerdict, secondFeedback, secondSummary) =
                ParseJudgeVerdict(secondAnswer);

            _logger.LogInformation(
                "Escalation: второй раунд Judge → verdict = {Verdict}",
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

        /// <summary>
        /// Извлекает <c>finalAnswer</c> из <c>ToolResult.Data</c> (формат
        /// <see cref="DTO.SubAgent.SubAgentTaskResult"/>).
        /// </summary>
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

        /// <summary>
        /// Устойчивый парсер ответа Judge'а: JSON <c>{verdict, reasoning,
        /// issues, summary}</c> + fallback на текст.
        /// </summary>
        private static (string Verdict, string FeedbackJson, string Summary)
            ParseJudgeVerdict(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return ("Uncertain", null, null);

            try
            {
                var clean = StripMarkdownFences(answer);
                var obj = JObject.Parse(clean);

                var verdict = obj["verdict"]?.ToString();
                var reasoning = obj["reasoning"]?.ToString();
                var issues = obj["issues"];
                var summary = obj["summary"]?.ToString();

                if (!string.IsNullOrWhiteSpace(verdict))
                {
                    var normalized = NormalizeVerdict(verdict);

                    // Feedback: reasoning + issues (одним JSON).
                    var feedbackObj = new JObject();
                    if (!string.IsNullOrWhiteSpace(reasoning))
                        feedbackObj["reasoning"] = reasoning;
                    if (issues != null && issues.Type != JTokenType.Null)
                        feedbackObj["issues"] = issues;

                    var feedbackJson = feedbackObj.Count > 0
                        ? feedbackObj.ToString(Formatting.None)
                        : null;

                    return (normalized, feedbackJson, summary);
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