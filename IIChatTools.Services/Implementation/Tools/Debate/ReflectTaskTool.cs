using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;      // <--- Добавлено
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Implementation.Debate;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Debate
{
    /// <summary>
    /// v2.2.0 (KI-244): Паттерн Reflection (черновик + самокритика + переписывание).
    /// </summary>
    public class ReflectTaskTool : ITool
    {
        private readonly Func<ISubAgentService> _subAgentServiceFactory;
        private readonly ISubAgentRegistry _subAgentRegistry;
        private readonly IAgentDebateSessionService _sessionService;
        private readonly ILogger<ReflectTaskTool> _logger;

        public ReflectTaskTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry subAgentRegistry,
            IAgentDebateSessionService sessionService,
            ILogger<ReflectTaskTool> logger)
        {
            _subAgentServiceFactory = subAgentServiceFactory;
            _subAgentRegistry = subAgentRegistry;
            _sessionService = sessionService;
            _logger = logger;
        }

        public string Name => "reflect_task";
        public string Description =>
            "Паттерн Reflection: черновик → самокритика → переписывание до Approved / MaxRounds. " +
            "Идеален для текстов, планов, ТЗ. Для кода используй code_agent_with_review.";
        public bool RequiresApprovalByDefault => true;

        public IReadOnlyList<ToolParameterDescriptor> Parameters => new List<ToolParameterDescriptor>
        {
            new() { Name = "task", Type = "string", Description = "Задача для черновика (≤8000 символов)", Required = true },
            new() { Name = "maxRounds", Type = "integer", Description = "Макс. раундов (1..5, по умолчанию 2)", Required = false }
        };

        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            // 1. Валидация
            var task = arguments["task"]?.ToString();
            if (string.IsNullOrWhiteSpace(task) || task.Length > 8000)
                return ToolResult.Fail("Параметр 'task' обязателен (не более 8000 символов).");

            var maxRounds = arguments["maxRounds"]?.Value<int>()
                            ?? arguments["max_rounds"]?.Value<int>()
                            ?? 2;
            maxRounds = Math.Clamp(maxRounds, 1, 5);

            if (context.CancellationToken.IsCancellationRequested)
                return ToolResult.Fail("Операция отменена.");

            // 2. Дескриптор агента
            var descriptor = _subAgentRegistry.Get("reflector_agent");
            if (descriptor == null || descriptor.Disabled)
                return ToolResult.Fail("Агент 'reflector_agent' не зарегистрирован или отключён.");

            var draftPrompt = descriptor.SystemPrompt;
            var critiquePrompt = descriptor.SelfCritiquePrompt;
            if (string.IsNullOrWhiteSpace(draftPrompt) || string.IsNullOrWhiteSpace(critiquePrompt))
                return ToolResult.Fail("У агента 'reflector_agent' не заданы SystemPrompt / SelfCritiquePrompt.");

            // 3. Создаём сессию
            int? sessionId = null;
            try
            {
                var config = new AgentDebateConfigSnapshot
                {
                    PatternType = "Reflection",
                    MaxRounds = maxRounds,
                    TokenBudget = 50000,
                    AllowEscalation = false,
                    HumanApproval = "Never",
                    ActorModel = descriptor.Model,
                    CriticModel = descriptor.Model
                };

                sessionId = await _sessionService.StartAsync(
                    context.ChatId ?? 0, context.UserId, task, config, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReflectTask: ошибка создания сессии");
                return ToolResult.Fail($"Не удалось создать сессию: {ex.Message}");
            }

            if (!sessionId.HasValue || sessionId.Value <= 0)
                return ToolResult.Fail("Не удалось создать сессию Reflection.");

            // 4. SSE: reflect_started
            await EmitEventAsync(context, ChatStreamEvent.ReflectStarted(new ChatReflectStartedDto
            {
                SessionId = sessionId.Value,
                Task = task,
                MaxRounds = maxRounds,
                DraftModel = descriptor.Model,
                SelfCritiqueModel = descriptor.Model
            }));

            try
            {
                // 5. Черновик
                var draft = await CallAgentAsync(context, task, draftPrompt, "draft");
                if (draft == null)
                {
                    await SafeCancelAsync(sessionId.Value, context.UserId);
                    return ToolResult.Fail("Не удалось сгенерировать черновик.");
                }

                // 6. Цикл критика → (при Rejected) переписывание
                string verdict = "Uncertain";
                JArray issues = new JArray();
                string summary = null;
                int completedRounds = 0;
                var draftHash = ComputeHash(draft);

                for (int round = 1; round <= maxRounds; round++)
                {
                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        await SafeCancelAsync(sessionId.Value, context.UserId);
                        return ToolResult.Fail("Операция отменена.");
                    }

                    // 6.1. Критика
                    var critiqueTask =
                        $"Оцени свой черновик критически, как если бы его писал другой человек.\n\n" +
                        $"Черновик:\n{draft}";

                    var critiqueRaw = await CallAgentAsync(
                        context, critiqueTask, critiquePrompt, $"critique-{round}");

                    if (critiqueRaw == null)
                    {
                        verdict = "Uncertain";
                        break;
                    }

                    ReflectionCritiqueParser.TryParse(
                        critiqueRaw, out verdict, out issues, out summary);

                    // 6.2. Persist раунда (используем AgentDebateRoundDto)
                    try
                    {
                        var roundDto = new AgentDebateRoundDto
                        {
                            RoundNumber = round,
                            ActorOutput = draft,
                            CriticVerdict = verdict,
                            CriticFeedbackJson = JsonConvert.SerializeObject(new { issues, summary })
                        };

                        await _sessionService.AddRoundAsync(
                            sessionId.Value, roundDto, context.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "ReflectTask: не удалось сохранить раунд {Round}", round);
                    }

                    // 6.3. SSE: reflect_round
                    await EmitEventAsync(context, ChatStreamEvent.ReflectRound(new ChatReflectRoundDto
                    {
                        SessionId = sessionId.Value,
                        RoundNumber = round,
                        Draft = draft,
                        Verdict = verdict,
                        Issues = issues,
                        CritiqueSummary = summary
                    }));

                    completedRounds = round;

                    // 6.4. Выход
                    if (verdict == "Approved" || verdict == "Uncertain")
                        break;

                    if (verdict == "Rejected")
                    {
                        // Rejected без issues → трактуем как Uncertain
                        if (issues == null || issues.Count == 0)
                        {
                            verdict = "Uncertain";
                            break;
                        }

                        if (round == maxRounds)
                            break;

                        // 6.5. Переписывание
                        var issuesText = issues.ToString(Formatting.None);
                        var rewriteTask =
                            $"Перепиши черновик с учётом критики.\n\n" +
                            $"Черновик:\n{draft}\n\n" +
                            $"Замечания:\n{issuesText}";

                        var newDraft = await CallAgentAsync(
                            context, rewriteTask, draftPrompt, $"rewrite-{round}");

                        if (newDraft == null)
                        {
                            verdict = "Uncertain";
                            break;
                        }

                        var newHash = ComputeHash(newDraft);
                        if (newHash == draftHash)
                        {
                            _logger.LogInformation(
                                "ReflectTask: черновик не изменился (duplicate-guard), раунд {Round}.", round);
                            verdict = "Uncertain";
                            break;
                        }

                        draft = newDraft;
                        draftHash = newHash;
                    }
                }

                // 6.6. v2.2.0 (KI-244): если все раунды отклонены и лимит
                // исчерпан — итоговый вердикт MaxRoundsReached (DESIGN § 3.1;
                // иначе теряется разница между «критика против» и «лимит»).
                if (verdict == "Rejected" && completedRounds >= maxRounds)
                {
                    verdict = "MaxRoundsReached";
                }

                // 7. Финализация сессии
                try
                {
                    // v2.2.0 (KI-244): FinalArtifactJson — единый формат с
                    // остальными паттернами: { "finalArtifact": "..." }
                    // (его читает UI-функция parseFinalArtifact при F5).
                    var artifactJson = JsonConvert.SerializeObject(new { finalArtifact = draft });
                    await _sessionService.CompleteAsync(
                        sessionId.Value, verdict, artifactJson, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ReflectTask: ошибка CompleteAsync");
                }

                // 8. SSE: reflect_completed
                await EmitEventAsync(context, ChatStreamEvent.ReflectCompleted(new ChatReflectCompletedDto
                {
                    SessionId = sessionId.Value,
                    Verdict = verdict,
                    TotalRounds = completedRounds,
                    FinalArtifact = draft
                }));

                var data = new
                {
                    sessionId = sessionId.Value,
                    verdict,
                    totalRounds = completedRounds,
                    finalArtifact = draft
                };

                return ToolResult.Ok(data,
                    $"Reflection завершён: {verdict} ({completedRounds} раундов).");
            }
            catch (OperationCanceledException)
            {
                await SafeCancelAsync(sessionId.Value, context.UserId);
                return ToolResult.Fail("Операция отменена.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReflectTask: ошибка цикла");
                await SafeCancelAsync(sessionId.Value, context.UserId);
                return ToolResult.Fail($"Ошибка Reflection: {ex.Message}");
            }
        }

        private async Task<string> CallAgentAsync(
            ToolExecutionContext context,
            string task,
            string promptOverride,
            string label)
        {
            try
            {
                var service = _subAgentServiceFactory();
                var request = new SubAgentTaskRequest
                {
                    Task = task,
                    MaxSteps = 3,
                    SystemPromptOverride = promptOverride
                };

                var result = await service.ExecuteTaskAsync(context, request);

                if (context.CancellationToken.IsCancellationRequested)
                    return null;

                if (result == null || !result.Completed)
                {
                    _logger.LogWarning(
                        "ReflectTask: вызов {Label} не завершён (Completed=false).", label);
                    return null;
                }

                return result.FinalAnswer;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReflectTask: ошибка в вызове {Label}", label);
                return null;
            }
        }

        private static async Task EmitEventAsync(ToolExecutionContext context, ChatStreamEvent evt)
        {
            if (context.EventWriter == null) return;
            await context.EventWriter.WriteAsync(evt);
        }

        private async Task SafeCancelAsync(int sessionId, int userId)
        {
            try
            {
                await _sessionService.CancelAsync(sessionId, userId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ReflectTask: не удалось отменить сессию {SessionId}", sessionId);
            }
        }

        private static string ComputeHash(string text)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }
    }
}