using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент: делегирование задачи суб-агенту.
    /// </summary>
    public class ConsultSecondaryAgentTool : ITool
    {
        private readonly ISubAgentService _subAgentService;
        private readonly ILogger<ConsultSecondaryAgentTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentService">Сервис суб-агента</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ConsultSecondaryAgentTool(
            ISubAgentService subAgentService,
            ILogger<ConsultSecondaryAgentTool> logger)
        {
            _subAgentService = subAgentService ?? throw new ArgumentNullException(nameof(subAgentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "consult_secondary_agent";

        /// <inheritdoc />
        public string Description =>
            "Делегирует задачу вторичному агенту. Агент автономно выполняет многошаговую работу (генерация кода, рефакторинг, исследование), " +
            "используя все доступные инструменты, и возвращает финальный результат. " +
            "Поддерживает авто-отладку через агента-рецензента.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "task",
                Type = "string",
                Description = "Подробное описание задачи для суб-агента (максимум 8000 символов).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "context",
                Type = "string",
                Description = "Дополнительный контекст: содержимое файлов, требования, ограничения (максимум 20000 символов).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "maxSteps",
                Type = "integer",
                Description = "Максимум шагов цикла (1–30, по умолчанию 10).",
                Required = false,
                Default = 10
            },
            new ToolParameterDescriptor
            {
                Name = "autoDebug",
                Type = "bool",
                Description = "Включить агента-рецензента после завершения задачи.",
                Required = false,
                Default = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var task = arguments.GetString("task");
            if (string.IsNullOrWhiteSpace(task))
                return ToolResult.Fail("Не указана задача");
            if (task.Length > 8000)
                return ToolResult.Fail("Описание задачи превышает 8000 символов");

            var extraContext = arguments.GetString("context");
            if (extraContext != null && extraContext.Length > 20000)
                return ToolResult.Fail("Контекст превышает 20 000 символов");

            var maxSteps = arguments.GetInt("maxSteps", 10);
            if (maxSteps <= 0 || maxSteps > 30) maxSteps = 10;

            var autoDebug = arguments.GetBool("autoDebug");

            try
            {
                var request = new SubAgentTaskRequest
                {
                    Task = task,
                    Context = extraContext,
                    MaxSteps = maxSteps,
                    AutoDebug = autoDebug
                };

                var result = await _subAgentService.ExecuteTaskAsync(context, request);

                return ToolResult.Ok(new
                {
                    sessionId = result.SessionId,
                    finalAnswer = result.FinalAnswer,
                    completed = result.Completed,
                    steps = result.Steps,
                    durationMs = result.DurationMs,
                    usedTools = result.UsedTools,
                    debugReview = result.DebugReview
                });
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Задача отменена");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения суб-агента");
                return ToolResult.Fail($"Ошибка суб-агента: {ex.Message}");
            }
        }
    }
}