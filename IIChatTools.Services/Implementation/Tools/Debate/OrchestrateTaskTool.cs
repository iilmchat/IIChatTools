using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Debate
{
    /// <summary>
    /// v2.0.0 (KI-126, Фаза 3, Шаг 3D): top-level <see cref="ITool"/> для
    /// Orchestrator-Worker паттерна.
    ///
    /// <para>
    /// Не наследник <see cref="SubAgent.AgentToolBase"/> — собственный цикл
    /// (по образцу <see cref="PlannerWithDebateTool"/> и
    /// <see cref="CodeAgentWithReviewTool"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Алгоритм:</b>
    /// <list type="number">
    ///   <item>Вызов <c>orchestrator_planner_agent</c> → JSON с подзадачами;</item>
    ///   <item>Lenient-парсер JSON (markdown-обёртки, до/после);</item>
    ///   <item>Обрезка до <c>MaxSubtasks</c> (default 5);</item>
    ///   <item>Whitelist check (RULES § 4.44);</item>
    ///   <item>Последовательное выполнение подзадач через <see cref="IToolRegistry"/>;</item>
    ///   <item>Сборка финального артефакта (конкатенация результатов).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Отложено в Шаги 3E / 3F:</b> параллельное выполнение, External
    /// pre-checks, persistence в БД, SSE-события, HITL.
    /// </para>
    ///
    /// <para>
    /// <b>DI-цикл:</b> использует <see cref="Func{T}"/> от <see cref="IToolRegistry"/>
    /// (RULES § 4.51, ADR-002).
    /// </para>
    /// </summary>
    public class OrchestrateTaskTool : ITool
    {
        // ============================================================
        // Константы
        // ============================================================

        private const string OrchestratorPlannerAgent = "orchestrator_planner_agent";

        private const int DefaultMaxSubtasks = 5;
        private const int HardLimitMaxSubtasks = 10;
        private const int MaxTaskLength = 8000;
        private const int MaxSubtaskLength = 2000;

        // ============================================================
        // Зависимости
        // ============================================================

        private readonly Func<IToolRegistry> _toolRegistryFactory;
        private readonly IAgentDebateSessionService _sessionService;
        private readonly IAgentBlackboard _blackboard;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OrchestrateTaskTool> _logger;

        /// <inheritdoc />
        public string Name => "orchestrate_task";

        /// <inheritdoc />
        public string Description =>
            "Top-level оркестратор для сложных multi-step задач с декомпозицией. " +
            "ВЫБИРАЙ ЭТОТ ИНСТРУМЕНТ когда задача требует: " +
            "(а) исследования в нескольких источниках («найди X в источниках A, B, C»); " +
            "(б) сравнения подходов по критериям («сравни A/B/C по X, Y»); " +
            "(в) сборки отчёта из N независимых частей; " +
            "(г) cross-tool workflow (web → code → file). " +
            "Оркестратор сам разобьёт задачу на 2-5 подзадач и распределит " +
            "между агентами (web_agent, code_agent, file_system_agent и др.). " +
            "НЕ ИСПОЛЬЗУЙ для простых одношаговых задач — там достаточно " +
            "прямого вызова агента. Требует подтверждения (approval).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "task",
                Type = "string",
                Description = "Сложная multi-step задача (максимум 8000 символов). " +
                              "Опиши КОНЕЧНУЮ ЦЕЛЬ, а не шаги — оркестратор сам " +
                              "декомпозирует.",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "parallel",
                Type = "boolean",
                Description = "Опциональный override флага параллелизма. " +
                              "Если не задан — решает декомпозитор.",
                Required = false
            }
        };

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="toolRegistryFactory">Фабрика реестра инструментов (ADR-002).</param>
        /// <param name="sessionService">
        /// v2.0.0 (KI-126, Ф3, Шаг 3F): сервис управления сессиями
        /// (Persistence в <c>AgentDebateSession</c> / <c>AgentDebateRound</c>).
        /// </param>
        /// <param name="blackboard">
        /// v2.0.0 (KI-126, Ф3, Шаг 3F): общий shared state Orchestrator-Worker
        /// (in-memory, TTL 30 мин).
        /// </param>
        /// <param name="configuration">Конфигурация (для чтения SubAgents:orchestrate_task:*).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null.</exception>
        public OrchestrateTaskTool(
            Func<IToolRegistry> toolRegistryFactory,
            IAgentDebateSessionService sessionService,
            IAgentBlackboard blackboard,
            IConfiguration configuration,
            ILogger<OrchestrateTaskTool> logger)
        {
            _toolRegistryFactory = toolRegistryFactory
                ?? throw new ArgumentNullException(nameof(toolRegistryFactory));
            _sessionService = sessionService
                ?? throw new ArgumentNullException(nameof(sessionService));
            _blackboard = blackboard
                ?? throw new ArgumentNullException(nameof(blackboard));
            _configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger
                ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            JObject arguments)
        {
            // ---------- 1. Валидация ----------
            var task = arguments["task"]?.ToString();
            if (string.IsNullOrWhiteSpace(task))
                return ToolResult.Fail("Не указана задача (поле `task`).");
            if (task.Length > MaxTaskLength)
                return ToolResult.Fail($"Задача превышает {MaxTaskLength} символов.");

            // ---------- 2. Конфиг ----------
            var config = ReadConfig();
            var parallelOverride = arguments["parallel"]?.Value<bool?>();

            _logger.LogInformation(
                "OrchestrateTaskTool: старт (userId={UserId}, chatId={ChatId}, " +
                "maxSubtasks={MaxSubtasks}, parallelOverride={Parallel}, task={Task})",
                context.UserId, context.ChatId, config.MaxSubtasks,
                parallelOverride?.ToString() ?? "<auto>",
                Truncate(task, 120));

            var stopwatch = Stopwatch.StartNew();

            // ---------- 2.5. Persistence: создать сессию + Blackboard ----------
            int? sessionId = null;
            if (context.ChatId.HasValue && context.ChatId.Value > 0)
            {
                try
                {
                    var snapshot = new AgentDebateConfigSnapshot
                    {
                        PatternType = "Orchestrator",
                        MaxSubtasks = config.MaxSubtasks,
                        Parallel = false,  // уточним после декомпозиции
                        TokenBudget = config.TokenBudget,
                        HumanApproval = config.HumanApproval,
                        AllowedWorkerAgents = config.AllowedWorkerAgents.ToList()
                    };

                    sessionId = await _sessionService.StartAsync(
                        context.ChatId.Value,
                        context.UserId,
                        task,
                        snapshot,
                        context.CancellationToken);

                    await _sessionService.MarkInProgressAsync(
                        sessionId.Value, context.CancellationToken);

                    _blackboard.GetOrCreate(sessionId.Value);

                    _logger.LogInformation(
                        "OrchestrateTaskTool: sessionId={SessionId}, chatId={ChatId}",
                        sessionId, context.ChatId);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        "OrchestrateTaskTool: не удалось создать сессию: {Message}",
                        ex.Message);
                    return ToolResult.Fail(ex.Message);
                }
            }
            else
            {
                _logger.LogDebug(
                    "OrchestrateTaskTool: без ChatId (sessionId не создаётся, " +
                    "SSE-события и persistence недоступны).");
            }

            // ---------- 3. Декомпозиция ----------
            var planResult = await CallPlannerAsync(context, task);
            if (!planResult.Success)
            {
                return ToolResult.Fail(
                    $"Декомпозиция не удалась: {planResult.Message}");
            }

            var plan = TryParsePlan(planResult.RawAnswer);
            if (plan == null)
            {
                _logger.LogWarning(
                    "OrchestrateTaskTool: не удалось распарсить JSON декомпозитора. " +
                    "Raw: {Raw}", Truncate(planResult.RawAnswer ?? "", 500));
                return ToolResult.Fail(
                    "Декомпозитор вернул невалидный JSON. " +
                    "Попробуйте переформулировать задачу более конкретно.");
            }

            if (plan.Subtasks.Count == 0)
            {
                if (sessionId.HasValue)
                {
                    await SafeCompleteAsync(
                        sessionId.Value, "Cancelled", null, context.CancellationToken);
                    _blackboard.Clear(sessionId.Value);
                }
                return ToolResult.Fail(
                    "Декомпозитор вернул пустой список подзадач. " +
                    "Задача выглядит одношаговой — используйте прямого агента.");
            }

            // ---------- SSE: orchestrator_started ----------
            if (sessionId.HasValue)
            {
                TryEmit(context, ChatStreamEvent.OrchestratorStarted(
                    new ChatOrchestratorStartedDto
                    {
                        SessionId = sessionId.Value,
                        Task = task,
                        Parallel = plan.Parallel,
                        MaxSubtasks = config.MaxSubtasks,
                        AllowedWorkerAgents = config.AllowedWorkerAgents.ToList(),
                        HumanApproval = config.HumanApproval
                    }));
            }

            // ---------- 4. Обрезка до MaxSubtasks ----------
            var subtasks = plan.Subtasks.Take(config.MaxSubtasks).ToList();
            var truncated = plan.Subtasks.Count > config.MaxSubtasks;

            // ---------- 5. Whitelist check ----------
            var whitelist = new HashSet<string>(
                config.AllowedWorkerAgents, StringComparer.OrdinalIgnoreCase);

            foreach (var st in subtasks)
            {
                if (string.IsNullOrWhiteSpace(st.Agent))
                {
                    st.Status = "rejected";
                    st.Error = "Agent не указан декомпозитором.";
                    continue;
                }

                if (string.Equals(st.Agent, Name, StringComparison.OrdinalIgnoreCase))
                {
                    st.Status = "rejected";
                    st.Error = "Рекурсивный вызов orchestrate_task запрещён (Max depth = 2).";
                    continue;
                }

                if (!whitelist.Contains(st.Agent))
                {
                    st.Status = "rejected";
                    st.Error = $"Агент «{st.Agent}» не в whitelist воркеров.";
                }
            }

            // ---------- 6. Выполнение ----------
            // v2.0.0 (KI-126, Фаза 3, Шаг 3E): параллелизм + External-detection.
            //
            // Логика:
            //   1. Если parallel=false (default или override) — последовательно.
            //   2. Если parallel=true, но есть external_llm_agent — maxParallel = 1
            //      (последовательно, чтобы не «добивать» circuit breaker).
            //   3. Иначе — Task.WhenAll + SemaphoreSlim(maxParallel).
            //
            // Pre-checks circuit breaker / budget — отложены (см. KI-247):
            //   - orchestrate_task не знает, какого провайдера выберет
            //     external_llm_agent (у него fallback chain);
            //   - сам ExternalLlmClient уже проверяет breaker + budget на
            //     каждом вызове (v1.8.1, KI-109).
            var hasExternalWorker = subtasks.Any(s =>
                string.Equals(s.Agent, "external_llm_agent", StringComparison.OrdinalIgnoreCase));

            var effectiveParallel = plan.Parallel
                && parallelOverride != false
                && !hasExternalWorker;

            var maxParallel = hasExternalWorker ? 1 : config.MaxParallel;

            if (hasExternalWorker)
            {
                _logger.LogInformation(
                    "OrchestrateTaskTool: обнаружен external_llm_agent в подзадачах — " +
                    "снижаем maxParallel до 1 (последовательное выполнение External).");
            }

            var activeSubtasks = subtasks.Where(s => s.Status != "rejected").ToList();

            if (effectiveParallel && activeSubtasks.Count > 1)
            {
                _logger.LogInformation(
                    "OrchestrateTaskTool: параллельное выполнение " +
                    "(maxParallel={Max}, подзадач={Count})",
                    maxParallel, activeSubtasks.Count);

                using var semaphore = new SemaphoreSlim(maxParallel);

                var tasks = activeSubtasks.Select(async (st, idx) =>
                {
                    await semaphore.WaitAsync(context.CancellationToken);
                    try
                    {
                        await ExecuteSubtaskAsync(
                            context, st, sessionId, subtasks.Count, idx + 1);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
            else
            {
                if (plan.Parallel && hasExternalWorker)
                {
                    _logger.LogInformation(
                        "OrchestrateTaskTool: parallel=true запрошено, но из-за " +
                        "external_llm_agent выполняем последовательно.");
                }

                var seqIndex = 0;
                foreach (var st in activeSubtasks)
                {
                    await ExecuteSubtaskAsync(
                        context, st, sessionId, subtasks.Count, ++seqIndex);
                }
            }

            stopwatch.Stop();

            // ---------- 7. Финальный артефакт ----------
            var completed = subtasks.Count(s => s.Status == "completed");
            var failed = subtasks.Count(s => s.Status == "failed" || s.Status == "rejected");

            var finalArtifact = BuildFinalArtifact(task, subtasks);

            var data = new
            {
                // v2.0.0 (KI-126, Ф3, Шаг 3H): для F5-восстановления
                // orchestrator-блоков в UI.
                sessionId,
                finalArtifact,
                subtasks = subtasks.Select(s => new
                {
                    id = s.Id,
                    agent = s.Agent,
                    task = s.Task,
                    status = s.Status,
                    result = s.Result,
                    error = s.Error,
                    durationMs = s.DurationMs
                }).ToList(),
                totalSubtasks = subtasks.Count,
                completedSubtasks = completed,
                failedSubtasks = failed,
                parallel = effectiveParallel,
                parallelRequested = plan.Parallel,
                hasExternalWorker = hasExternalWorker,
                maxParallel = maxParallel,
                truncated,
                totalDurationMs = stopwatch.ElapsedMilliseconds,
                reasoning = plan.Reasoning
            };

            _logger.LogInformation(
                "OrchestrateTaskTool: завершено за {Ms} мс " +
                "(подзадач: {Total}, completed: {Ok}, failed: {Fail}, truncated: {Trunc})",
                stopwatch.ElapsedMilliseconds, subtasks.Count, completed, failed, truncated);

            // ---------- Persistence: CompleteAsync + SSE orchestrator_completed ----------
            if (sessionId.HasValue)
            {
                var verdict = failed == 0
                    ? "Approved"
                    : (completed > 0 ? "PartiallyApproved" : "Failed");

                await SafeCompleteAsync(
                    sessionId.Value,
                    verdict,
                    Newtonsoft.Json.JsonConvert.SerializeObject(new { finalArtifact }),
                    context.CancellationToken);

                TryEmit(context, ChatStreamEvent.OrchestratorCompleted(
                    new ChatOrchestratorCompletedDto
                    {
                        SessionId = sessionId.Value,
                        TotalSubtasks = subtasks.Count,
                        CompletedSubtasks = completed,
                        FailedSubtasks = failed,
                        FinalArtifact = finalArtifact,
                        TotalDurationMs = stopwatch.ElapsedMilliseconds
                    }));

                // Cleanup Blackboard.
                _blackboard.Clear(sessionId.Value);
            }

            var message = failed > 0
                ? $"Оркестрация завершена частично: {completed} из {subtasks.Count} подзадач успешно."
                : null;

            return ToolResult.Ok(data, message);
        }

        // ============================================================
        // Вызов декомпозитора
        // ============================================================

        /// <summary>
        /// Вызывает <c>orchestrator_planner_agent</c> через <see cref="IToolRegistry"/>.
        /// </summary>
        /// <param name="context">Контекст (для UserId, ChatId, CancellationToken).</param>
        /// <param name="task">Задача пользователя.</param>
        /// <returns>Результат вызова (success + rawAnswer = finalAnswer).</returns>
        private async Task<PlannerCallResult> CallPlannerAsync(
            ToolExecutionContext context,
            string task)
        {
            try
            {
                var registry = _toolRegistryFactory();
                var args = new JObject
                {
                    ["task"] = task,
                    ["context"] = "Разбей на 2-5 подзадач. Формат ответа — строго JSON " +
                                  "из твоего system prompt."
                };

                var result = await registry.ExecuteAsync(
                    OrchestratorPlannerAgent, context, args);

                if (!result.Success)
                {
                    return new PlannerCallResult
                    {
                        Success = false,
                        Message = result.Message ?? "Планировщик вернул Fail."
                    };
                }

                // Data — объект { agent, sessionId, finalAnswer, completed, ... }
                var dataJson = result.Data is JObject jObj
                    ? jObj
                    : (result.Data != null ? JObject.FromObject(result.Data) : new JObject());

                var finalAnswer = dataJson["finalAnswer"]?.ToString();

                if (string.IsNullOrWhiteSpace(finalAnswer))
                {
                    return new PlannerCallResult
                    {
                        Success = false,
                        Message = "Планировщик вернул пустой finalAnswer."
                    };
                }

                return new PlannerCallResult
                {
                    Success = true,
                    RawAnswer = finalAnswer
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrchestrateTaskTool: ошибка вызова планировщика");
                return new PlannerCallResult
                {
                    Success = false,
                    Message = $"Ошибка вызова планировщика: {ex.Message}"
                };
            }
        }

        // ============================================================
        // Парсинг JSON-ответа планировщика
        // ============================================================

        /// <summary>
        /// Lenient-парсер ответа планировщика:
        /// снимает markdown-обёртки (<c>```json ... ```</c>),
        /// игнорирует текст до/после JSON, читает поля case-insensitive.
        /// </summary>
        /// <param name="text">Сырой ответ <c>finalAnswer</c>.</param>
        /// <returns>Распарсенный план или <c>null</c>.</returns>
        private static OrchestratorPlan TryParsePlan(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var json = ExtractJsonObject(text);
            if (json == null)
                return null;

            try
            {
                var obj = JObject.Parse(json);

                var plan = new OrchestratorPlan
                {
                    Parallel = obj["parallel"]?.Value<bool>() ?? false,
                    Reasoning = obj["reasoning"]?.ToString() ?? string.Empty
                };

                var subtasksToken = obj["subtasks"] as JArray;
                if (subtasksToken == null)
                    return plan;

                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var stToken in subtasksToken)
                {
                    if (stToken is not JObject stObj)
                        continue;

                    var id = stObj["id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        id = (seenIds.Count + 1).ToString();
                    }

                    // Дедупликация id.
                    var baseId = id;
                    var suffix = 1;
                    while (!seenIds.Add(id))
                    {
                        id = $"{baseId}-{suffix++}";
                    }

                    var agent = stObj["agent"]?.ToString() ?? string.Empty;
                    var taskText = stObj["task"]?.ToString() ?? string.Empty;

                    if (taskText.Length > MaxSubtaskLength)
                        taskText = taskText.Substring(0, MaxSubtaskLength) + "...";

                    plan.Subtasks.Add(new SubtaskState
                    {
                        Id = id,
                        Agent = agent,
                        Task = taskText,
                        Status = "pending"
                    });
                }

                return plan;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Извлекает JSON-объект из текста: снимает markdown-обёртки
        /// и находит первый сбалансированный <c>{ ... }</c>.
        /// </summary>
        /// <param name="text">Сырой текст.</param>
        /// <returns>JSON-строка или <c>null</c>.</returns>
        private static string ExtractJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // 1. Снять markdown-обёртки ```json ... ``` или ``` ... ```.
            var mdMatch = Regex.Match(text,
                @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
                RegexOptions.IgnoreCase);
            if (mdMatch.Success)
                return mdMatch.Groups[1].Value;

            // 2. Найти первый сбалансированный { ... }.
            var start = text.IndexOf('{');
            if (start < 0) return null;

            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(start, i - start + 1);
                }
            }

            return null;
        }

        // ============================================================
        // Выполнение одной подзадачи
        // ============================================================

        /// <summary>
        /// Выполняет одну подзадачу через <see cref="IToolRegistry.ExecuteAsync"/>.
        /// Обновляет <see cref="SubtaskState"/> in-place.
        ///
        /// <para>
        /// v2.0.0 (KI-126, Ф3, Шаг 3F): после выполнения:
        /// <list type="bullet">
        ///   <item>пишет результат в <see cref="IAgentBlackboard"/> (ключи <c>subtask.{id}.*</c>);</item>
        ///   <item>сохраняет round в <see cref="IAgentDebateSessionService"/> (Subtask*-поля);</item>
        ///   <item>эмитит SSE-событие <c>orchestrator_round</c>.</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="context">Контекст (UserId, ChatId, ...).</param>
        /// <param name="subtask">Состояние подзадачи (мутируется).</param>
        /// <param name="sessionId">
        /// ID сессии <c>AgentDebateSession</c>. <c>null</c> — persistence отключён
        /// (нет ChatId). Blackboard и SSE также пропускаются.
        /// </param>
        /// <param name="totalSubtasks">Общее число подзадач (для диагностики, сейчас не используется).</param>
        /// <param name="index">Порядковый номер подзадачи (1-based) — для <c>RoundNumber</c>.</param>
        private async Task ExecuteSubtaskAsync(
            ToolExecutionContext context,
            SubtaskState subtask,
            int? sessionId,
            int totalSubtasks,
            int index)
        {
            subtask.Status = "running";
            var sw = Stopwatch.StartNew();

            try
            {
                var registry = _toolRegistryFactory();
                var args = new JObject
                {
                    ["task"] = subtask.Task
                };

                var result = await registry.ExecuteAsync(
                    subtask.Agent, context, args);

                sw.Stop();
                subtask.DurationMs = sw.ElapsedMilliseconds;

                if (result.Success)
                {
                    // Извлекаем finalAnswer, если это агент; иначе — data как есть.
                    subtask.Result = ExtractWorkerResult(result);
                    subtask.Status = "completed";

                    _logger.LogInformation(
                        "OrchestrateTaskTool: подзадача {Id} ({Agent}) завершена " +
                        "за {Ms} мс",
                        subtask.Id, subtask.Agent, subtask.DurationMs);
                }
                else
                {
                    subtask.Status = "failed";
                    subtask.Error = result.Message ?? "Tool вернул Fail без сообщения.";

                    _logger.LogWarning(
                        "OrchestrateTaskTool: подзадача {Id} ({Agent}) провалена: {Error}",
                        subtask.Id, subtask.Agent, subtask.Error);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                subtask.DurationMs = sw.ElapsedMilliseconds;
                subtask.Status = "failed";
                subtask.Error = ex.Message;

                _logger.LogError(ex,
                    "OrchestrateTaskTool: подзадача {Id} ({Agent}) упала с исключением",
                    subtask.Id, subtask.Agent);
            }

            // ---------- Blackboard ----------
            if (sessionId.HasValue)
            {
                try
                {
                    _blackboard.Set(sessionId.Value, $"subtask.{subtask.Id}.status", subtask.Status);
                    _blackboard.Set(sessionId.Value, $"subtask.{subtask.Id}.agent", subtask.Agent);
                    _blackboard.Set(sessionId.Value, $"subtask.{subtask.Id}.task", subtask.Task);
                    _blackboard.Set(sessionId.Value, $"subtask.{subtask.Id}.durationMs", subtask.DurationMs);

                    if (subtask.Result != null)
                        _blackboard.Set(sessionId.Value, $"subtask.{subtask.Id}.result", subtask.Result);
                    if (subtask.Error != null)
                        _blackboard.Set(sessionId.Value, $"subtask.{subtask.Id}.error", subtask.Error);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "OrchestrateTaskTool: ошибка записи в Blackboard (sessionId={SessionId})",
                        sessionId);
                }
            }

            // ---------- Persistence: AddRoundAsync ----------
            if (sessionId.HasValue)
            {
                try
                {
                    var roundDto = new AgentDebateRoundDto
                    {
                        RoundNumber = index,
                        SubtaskId = subtask.Id,
                        SubtaskAgent = subtask.Agent,
                        SubtaskTask = subtask.Task,
                        SubtaskStatus = subtask.Status,
                        SubtaskResult = subtask.Result,
                        SubtaskError = subtask.Error,
                        DurationMs = (int)subtask.DurationMs
                    };

                    await _sessionService.AddRoundAsync(
                        sessionId.Value, roundDto, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "OrchestrateTaskTool: ошибка AddRoundAsync (sessionId={SessionId})",
                        sessionId);
                }
            }

            // ---------- SSE: orchestrator_round ----------
            if (sessionId.HasValue)
            {
                TryEmit(context, ChatStreamEvent.OrchestratorRound(new ChatOrchestratorRoundDto
                {
                    SessionId = sessionId.Value,
                    SubtaskId = subtask.Id,
                    Agent = subtask.Agent,
                    Task = subtask.Task,
                    Status = subtask.Status,
                    Result = subtask.Result,
                    Error = subtask.Error,
                    DurationMs = (int)subtask.DurationMs,
                    TokensIn = 0,   // Worker'ы не возвращают токены (KI-241)
                    TokensOut = 0
                }));
            }
        }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3, Шаг 3F): пишет SSE-событие в
        /// <see cref="ToolExecutionContext.EventWriter"/>. Ошибки игнорирует
        /// (канал может быть закрыт при отмене). По образцу
        /// <c>PlannerWithDebateTool.TryEmit</c>.
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
        /// v2.0.0 (KI-126, Фаза 3, Шаг 3F): безопасный вызов
        /// <see cref="IAgentDebateSessionService.CompleteAsync"/> — не падает
        /// при ошибке.
        /// </summary>
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
                    "OrchestrateTaskTool: ошибка CompleteAsync (sessionId={SessionId})",
                    sessionId);
            }
        }

        /// <summary>
        /// Извлекает «полезный» текст из <see cref="ToolResult"/>:
        /// <c>finalAnswer</c> для агентов, иначе — <c>Data</c> как JSON-строка.
        /// </summary>
        private static string ExtractWorkerResult(ToolResult result)
        {
            if (result.Data == null)
                return result.Message ?? string.Empty;

            try
            {
                var jObj = result.Data is JObject jo
                    ? jo
                    : JObject.FromObject(result.Data);

                var finalAnswer = jObj["finalAnswer"]?.ToString();
                if (!string.IsNullOrWhiteSpace(finalAnswer))
                    return finalAnswer;

                // Fallback — сырой JSON (truncate до 4 KB).
                var raw = jObj.ToString(Newtonsoft.Json.Formatting.None);
                return raw.Length > 4096
                    ? raw.Substring(0, 4096) + "..."
                    : raw;
            }
            catch
            {
                return result.Data.ToString();
            }
        }

        // ============================================================
        // Сборка финального артефакта
        // ============================================================

        /// <summary>
        /// Собирает финальный артефакт — конкатенацию результатов подзадач.
        /// Агрегатор (Chat LLM) дополнительно сведёт это в единый ответ.
        /// </summary>
        /// <param name="originalTask">Исходная задача.</param>
        /// <param name="subtasks">Список подзадач (с результатами).</param>
        /// <returns>Markdown-строка с результатами.</returns>
        private static string BuildFinalArtifact(
            string originalTask,
            IReadOnlyList<SubtaskState> subtasks)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Результаты оркестрации");
            sb.AppendLine();
            sb.AppendLine($"**Задача:** {originalTask}");
            sb.AppendLine();
            sb.AppendLine($"**Подзадач:** {subtasks.Count} " +
                          $"(completed: {subtasks.Count(s => s.Status == "completed")}, " +
                          $"failed: {subtasks.Count(s => s.Status == "failed" || s.Status == "rejected")})");
            sb.AppendLine();

            foreach (var st in subtasks)
            {
                var statusIcon = st.Status switch
                {
                    "completed" => "✅",
                    "failed" => "❌",
                    "rejected" => "⛔",
                    _ => "⏳"
                };

                sb.AppendLine($"## {statusIcon} Подзадача {st.Id} — `{st.Agent}`");
                sb.AppendLine();
                sb.AppendLine($"**Задача:** {st.Task}");
                sb.AppendLine();

                if (st.Status == "completed")
                {
                    sb.AppendLine("**Результат:**");
                    sb.AppendLine();
                    sb.AppendLine(st.Result ?? "(пусто)");
                }
                else if (st.Status == "failed" || st.Status == "rejected")
                {
                    sb.AppendLine($"**Ошибка:** {st.Error}");
                }
                else
                {
                    sb.AppendLine($"**Статус:** {st.Status}");
                }

                sb.AppendLine();
                sb.AppendLine($"*Длительность: {st.DurationMs} мс*");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        // ============================================================
        // Конфигурация
        // ============================================================

        /// <summary>
        /// Читает секцию <c>SubAgents:orchestrate_task:*</c> из конфигурации.
        /// </summary>
        private OrchestrateTaskConfig ReadConfig()
        {
            var section = _configuration.GetSection("SubAgents:orchestrate_task");

            var maxSubtasks = section.GetValue<int>("MaxSubtasks", DefaultMaxSubtasks);
            maxSubtasks = Math.Clamp(maxSubtasks, 1, HardLimitMaxSubtasks);

            var allowed = section.GetSection("AllowedWorkerAgents")
                .Get<string[]>() ?? Array.Empty<string>();

            // Default whitelist — если в конфиге пусто.
            if (allowed.Length == 0)
            {
                allowed = new[]
                {
                    "code_agent",
                    "web_agent",
                    "file_system_agent",
                    "database_agent",
                    "browser_agent",
                    "external_llm_agent"
                };
            }

            return new OrchestrateTaskConfig
            {
                MaxSubtasks = maxSubtasks,
                MaxParallel = section.GetValue<int>("MaxParallel", 3),
                TokenBudget = section.GetValue<int>("TokenBudget", 50000),
                HumanApproval = section.GetValue<string>("HumanApproval") ?? "Never",
                AllowedWorkerAgents = allowed
            };
        }

        // ============================================================
        // Внутренние типы
        // ============================================================

        /// <summary>Распарсенный план от декомпозитора.</summary>
        private sealed class OrchestratorPlan
        {
            public bool Parallel { get; set; }
            public string Reasoning { get; set; } = string.Empty;
            public List<SubtaskState> Subtasks { get; } = new List<SubtaskState>();
        }

        /// <summary>Состояние одной подзадачи (мутируется в процессе выполнения).</summary>
        private sealed class SubtaskState
        {
            public string Id { get; set; }
            public string Agent { get; set; }
            public string Task { get; set; }
            public string Status { get; set; } = "pending";
            public string Result { get; set; }
            public string Error { get; set; }
            public long DurationMs { get; set; }
        }

        /// <summary>Результат вызова декомпозитора (обёртка).</summary>
        private sealed class PlannerCallResult
        {
            public bool Success { get; set; }
            public string RawAnswer { get; set; }
            public string Message { get; set; }
        }

        /// <summary>Настройки Orchestrator'а (из IConfiguration).</summary>
        private sealed class OrchestrateTaskConfig
        {
            public int MaxSubtasks { get; set; }
            public int MaxParallel { get; set; }
            public int TokenBudget { get; set; }
            public string HumanApproval { get; set; }
            public string[] AllowedWorkerAgents { get; set; }
        }

        // ============================================================
        // Хелперы
        // ============================================================

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }
    }
}