using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
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
    /// (v1.11.0, KI-126, Шаг 1D).
    ///
    /// <para>
    /// <b>Не наследует <c>AgentToolBase</c></b> — по образцу
    /// <c>DatabaseAgentTool</c> (DESIGN_MULTI_AGENT_DEBATE § 5.1). Tool
    /// не «думает», а детерминированно координирует actor (<c>code_agent</c>)
    /// и critic (<c>code_reviewer_agent</c>) через <see cref="IToolRegistry"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Цикл:</b> до <c>MaxRounds</c> раундов:
    /// <list type="number">
    ///   <item>Actor (<c>code_agent</c>) пишет/переписывает код (учитывает feedback критика);</item>
    ///   <item>Critic (<c>code_reviewer_agent</c>) проверяет, возвращает JSON <c>{verdict, issues, summary}</c>;</item>
    ///   <item>Approved → финал; Rejected → следующий раунд; Uncertain → стоп (эскалация в Шаге 1F).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Отложено (следующие шаги):</b>
    /// <list type="bullet">
    ///   <item>Персистенция сессии в <c>AgentDebateSession</c> + <c>AgentDebateRound</c> — Шаг 1E
    ///   (требует ChatId в <c>ToolExecutionContext</c>);</item>
    ///   <item>SSE-события <c>debate_started</c> / <c>debate_round</c> / <c>debate_completed</c> — Шаг 1E;</item>
    ///   <item>Human-in-the-loop между раундами — Шаг 1E (TCS + endpoint);</item>
    ///   <item>Эскалация на <c>ask_external_llm</c> при <c>Uncertain</c> — Шаг 1F.</item>
    /// </list>
    /// В Шаге 1D работает полностью автономно: actor → critic → (loop до MaxRounds) → результат.
    /// </para>
    /// </summary>
    public class CodeAgentWithReviewTool : ITool
    {
        /// <summary>Имя actor-агента (top-level ITool).</summary>
        private const string CodeAgentToolName = "code_agent";

        /// <summary>Имя critic-агента (top-level ITool).</summary>
        private const string ReviewerToolName = "code_reviewer_agent";

        /// <summary>Дефолтное число раундов (DESIGN § 3.1).</summary>
        private const int DefaultMaxRounds = 3;

        /// <summary>Максимум символов в описании задачи.</summary>
        private const int MaxTaskLength = 8000;

        /// <summary>Максимум символов в контексте.</summary>
        private const int MaxContextLength = 20_000;

        /// <summary>Жёсткий верхний предел раундов (защита от бесконечного цикла).</summary>
        private const int HardMaxRounds = 5;

        private readonly IToolRegistry _toolRegistry;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CodeAgentWithReviewTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="toolRegistry">
        /// Реестр инструментов — для вызова <c>code_agent</c> и <c>code_reviewer_agent</c>.
        /// </param>
        /// <param name="configuration">Конфигурация (SubAgents:code_agent_with_review).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null.</exception>
        public CodeAgentWithReviewTool(
            IToolRegistry toolRegistry,
            IConfiguration configuration,
            ILogger<CodeAgentWithReviewTool> logger)
        {
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                Description =
                    "Дополнительный контекст: требования, ограничения, примеры " +
                    "(максимум 20 000 символов).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "maxRounds",
                Type = "integer",
                Description =
                    "Максимум раундов actor-critic (1–5). По умолчанию — из конфига " +
                    "(SubAgents:code_agent_with_review:MaxRounds, стандарт 3).",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context, JObject arguments)
        {
            if (context == null)
                return ToolResult.Fail("Контекст выполнения не задан.");

            // 1. Разбор и валидация аргументов.
            var task = arguments?.GetString("task");
            if (string.IsNullOrWhiteSpace(task))
                return ToolResult.Fail("Не указана задача.");

            if (task.Length > MaxTaskLength)
                return ToolResult.Fail(
                    $"Описание задачи превышает {MaxTaskLength} символов.");

            var extraContext = arguments?.GetString("context");
            if (extraContext != null && extraContext.Length > MaxContextLength)
                return ToolResult.Fail(
                    $"Контекст превышает {MaxContextLength} символов.");

            var maxRounds = arguments != null ? arguments.GetInt("maxRounds", 0) : 0;
            if (maxRounds <= 0)
            {
                maxRounds = GetIntConfig(
                    "SubAgents:code_agent_with_review:MaxRounds",
                    DefaultMaxRounds);
            }
            maxRounds = Math.Clamp(maxRounds, 1, HardMaxRounds);

            // 2. Цикл Actor-Critic.
            var sw = Stopwatch.StartNew();
            var rounds = new List<RoundResult>();

            string lastActorOutput = null;
            string lastVerdict = "Pending";
            string lastFeedback = null;

            for (var round = 1;
                 round <= maxRounds && !context.CancellationToken.IsCancellationRequested;
                 round++)
            {
                // 2a. Actor: code_agent.
                //     В первом раунде — оригинальная задача.
                //     В последующих — задача + feedback критика.
                var actorTask = round == 1
                    ? task
                    : BuildRevisionTask(task, lastFeedback);

                var actorArgs = new JObject { ["task"] = actorTask };
                if (!string.IsNullOrEmpty(extraContext))
                    actorArgs["context"] = extraContext;

                _logger.LogInformation(
                    "Actor-Critic round {Round}/{MaxRounds}: запуск code_agent (user={UserId})",
                    round, maxRounds, context.UserId);

                var actorResult = await _toolRegistry.ExecuteAsync(
                    CodeAgentToolName, context, actorArgs);

                if (!actorResult.Success)
                {
                    _logger.LogWarning(
                        "Actor-Critic round {Round}: code_agent fail — {Message}",
                        round, actorResult.Message);
                    return ToolResult.Fail(
                        $"Actor (code_agent) не справился в раунде {round}: " +
                        $"{actorResult.Message}");
                }

                var actorOutput = ExtractFinalAnswer(actorResult.Data);
                lastActorOutput = actorOutput;

                // 2b. Critic: code_reviewer_agent.
                var criticTask = BuildCriticTask(task, actorOutput);
                var criticArgs = new JObject { ["task"] = criticTask };

                _logger.LogInformation(
                    "Actor-Critic round {Round}/{MaxRounds}: запуск code_reviewer_agent",
                    round, maxRounds);

                var criticResult = await _toolRegistry.ExecuteAsync(
                    ReviewerToolName, context, criticArgs);

                if (!criticResult.Success)
                {
                    // Critic не справился — не блокируем, возвращаем текущий результат.
                    _logger.LogWarning(
                        "Actor-Critic round {Round}: code_reviewer_agent fail — {Message}",
                        round, criticResult.Message);

                    rounds.Add(new RoundResult
                    {
                        RoundNumber = round,
                        ActorOutput = actorOutput,
                        CriticVerdict = "Uncertain",
                        CriticFeedbackJson = null,
                        WasEscalated = false
                    });
                    lastVerdict = "Uncertain";
                    break;
                }

                // 2c. Парсинг вердикта критика (lenient — qwen3-4b может вернуть JSON
                //     в markdown-блоке, с текстом вокруг, или вообще без JSON).
                var criticAnswer = ExtractFinalAnswer(criticResult.Data);
                var (verdict, feedbackJson, summary) = ParseCriticVerdict(criticAnswer);

                _logger.LogInformation(
                    "Actor-Critic round {Round}/{MaxRounds}: verdict = {Verdict}",
                    round, maxRounds, verdict);

                rounds.Add(new RoundResult
                {
                    RoundNumber = round,
                    ActorOutput = actorOutput,
                    CriticVerdict = verdict,
                    CriticFeedbackJson = feedbackJson,
                    WasEscalated = false
                });

                lastVerdict = verdict;
                lastFeedback = feedbackJson ?? summary ?? criticAnswer;

                if (verdict == "Approved")
                {
                    _logger.LogInformation(
                        "Actor-Critic: Approved после {Round} раунд(ов).", round);
                    break;
                }

                if (verdict == "Uncertain")
                {
                    // Эскалация — Шаг 1F. В 1D останавливаемся.
                    _logger.LogInformation(
                        "Actor-Critic: verdict=Uncertain в раунде {Round}. " +
                        "Эскалация (1F) не реализована — возвращаем текущий результат.",
                        round);
                    break;
                }

                // verdict == "Rejected" → продолжаем цикл.
            }

            sw.Stop();

            if (context.CancellationToken.IsCancellationRequested)
            {
                return ToolResult.Fail("Операция отменена пользователем.");
            }

            // 3. Финальный вердикт.
            var finalVerdict = lastVerdict == "Approved"
                ? "Approved"
                : (rounds.Count >= maxRounds ? "MaxRoundsReached" : lastVerdict);

            var resultData = new
            {
                verdict = finalVerdict,
                totalRounds = rounds.Count,
                maxRounds = maxRounds,
                finalArtifact = lastActorOutput,
                rounds = rounds.Select(r => new
                {
                    roundNumber = r.RoundNumber,
                    actorOutput = r.ActorOutput,
                    criticVerdict = r.CriticVerdict,
                    criticFeedback = r.CriticFeedbackJson,
                    wasEscalated = r.WasEscalated
                }).ToList(),
                durationMs = sw.ElapsedMilliseconds
            };

            var message = finalVerdict switch
            {
                "Approved" => $"Код одобрен критиком после {rounds.Count} раунд(ов).",
                "MaxRoundsReached" =>
                    $"Достигнут лимит раундов ({maxRounds}). Финальный вердикт: {lastVerdict}.",
                "Uncertain" =>
                    "Критик не уверен — рекомендуется эскалация на внешнюю LLM (KI-126, Фаза 1F).",
                _ => $"Финальный вердикт: {finalVerdict}."
            };

            return ToolResult.Ok(resultData, message);
        }

        // ============ Helpers ============

        /// <summary>
        /// Строит задачу для actor'а в ревизионном раунде: оригинальная задача + feedback критика.
        /// </summary>
        /// <param name="originalTask">Оригинальная задача.</param>
        /// <param name="feedback">Feedback критика (JSON issues или текст).</param>
        /// <returns>Текст задачи для actor'а.</returns>
        private static string BuildRevisionTask(string originalTask, string feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback))
                return originalTask;

            return
                $"{originalTask}\n\n" +
                "⚠️ FEEDBACK ОТ КРИТИКА (учти ВСЕ замечания и перепиши код):\n" +
                feedback;
        }

        /// <summary>
        /// Строит задачу для критика: проверь код на соответствие задаче.
        /// </summary>
        /// <param name="originalTask">Оригинальная задача.</param>
        /// <param name="code">Код actor'а.</param>
        /// <returns>Текст задачи для критика.</returns>
        private static string BuildCriticTask(string originalTask, string code)
        {
            return
                "Проверь код, написанный для следующей задачи:\n\n" +
                $"ЗАДАЧА:\n{originalTask}\n\n" +
                $"КОД:\n{code}\n\n" +
                "Верни СТРОГО JSON: {verdict, issues, summary}.";
        }

        /// <summary>
        /// Извлекает <c>finalAnswer</c> из data (анонимный объект от AgentToolBase).
        /// </summary>
        /// <param name="data">Поле <c>ToolResult.Data</c>.</param>
        /// <returns>Текст финального ответа агента (или пустая строка).</returns>
        private static string ExtractFinalAnswer(object data)
        {
            if (data == null) return string.Empty;

            // JObject — уже JSON.
            if (data is JObject jObj)
                return jObj["finalAnswer"]?.ToString() ?? string.Empty;

            // Анонимный объект → JObject через Newtonsoft.
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
        /// Lenient-парсер вердикта критика. Понимает:
        /// <list type="bullet">
        ///   <item>чистый JSON <c>{verdict, issues, summary}</c>;</item>
        ///   <item>JSON в markdown-блоке <c>```json ... ```</c>;</item>
        ///   <item>текст с упоминанием «Approved» / «Rejected» / «Uncertain».</item>
        /// </list>
        /// </summary>
        /// <param name="answer">Сырой ответ критика.</param>
        /// <returns>Кортеж (verdict, feedbackJson, summary).</returns>
        private static (string Verdict, string FeedbackJson, string Summary) ParseCriticVerdict(
            string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return ("Uncertain", null, null);

            // 1. Попытка полного JSON-парсинга (с очисткой markdown-fences).
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
            catch
            {
                // Не JSON — идём в lenient-режим.
            }

            // 2. Lenient — поиск verdict в тексте.
            var verdictFromText = FindVerdictInText(answer);
            var summaryShort = answer.Length > 500 ? answer.Substring(0, 500) : answer;
            return (verdictFromText, null, summaryShort);
        }

        /// <summary>
        /// Убирает markdown-обёртку ```json ... ``` из строки.
        /// </summary>
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

        /// <summary>
        /// Ищет вердикт в свободном тексте (когда JSON-парсинг не удался).
        /// Порядок: Approved → Rejected → Uncertain. Fallback — Uncertain.
        /// </summary>
        private static string FindVerdictInText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Uncertain";

            // Приоритет — точные строки в кавычках (например, "verdict": "Approved").
            if (text.Contains("\"Approved\"", StringComparison.Ordinal) ||
                text.Contains("'Approved'", StringComparison.Ordinal))
                return "Approved";

            if (text.Contains("\"Rejected\"", StringComparison.Ordinal) ||
                text.Contains("'Rejected'", StringComparison.Ordinal))
                return "Rejected";

            if (text.Contains("\"Uncertain\"", StringComparison.Ordinal) ||
                text.Contains("'Uncertain'", StringComparison.Ordinal))
                return "Uncertain";

            // Мягкий поиск по словам (последнее — что встретилось первым).
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

        /// <summary>
        /// Нормализует verdict к одному из трёх канонических значений.
        /// </summary>
        private static string NormalizeVerdict(string raw)
        {
            var v = (raw ?? string.Empty).Trim();

            if (v.Equals("Approved", StringComparison.OrdinalIgnoreCase)) return "Approved";
            if (v.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
            if (v.Equals("Uncertain", StringComparison.OrdinalIgnoreCase)) return "Uncertain";

            return "Uncertain";
        }

        /// <summary>
        /// Читает int из конфига с fallback.
        /// </summary>
        private int GetIntConfig(string key, int defaultValue)
        {
            var raw = _configuration[key];
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        /// <summary>
        /// Состояние одного раунда (in-memory, до персистенции в БД — Шаг 1E).
        /// </summary>
        private sealed class RoundResult
        {
            public int RoundNumber { get; set; }
            public string ActorOutput { get; set; }
            public string CriticVerdict { get; set; }
            public string CriticFeedbackJson { get; set; }
            public bool WasEscalated { get; set; }
        }
    }
}