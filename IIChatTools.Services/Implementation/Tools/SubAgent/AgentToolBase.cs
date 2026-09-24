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
    /// Базовый класс для инструментов-обёрток вокруг специализированных
    /// суб-агентов (v1.4.0 Фаза 3, KI-052).
    ///
    /// Наследники переопределяют только <see cref="Name"/>, <see cref="Description"/>
    /// и <see cref="AgentName"/> — всё остальное (параметры, вызов SubAgentService,
    /// резолв дескриптора из реестра) реализовано здесь.
    ///
    /// <para>
    /// <b>DI-цикл:</b> использует <see cref="Func{T}"/> от <see cref="ISubAgentService"/>,
    /// чтобы разорвать зависимость <c>ToolRegistry → ITool → ISubAgentService → IToolRegistry</c>
    /// (аналогично <c>ConsultSecondaryAgentTool</c>, см. RULES).
    /// </para>
    /// </summary>
    public abstract class AgentToolBase : ITool
    {
        /// <summary>
        /// Фабрика сервиса суб-агента (ленивая, разрывает DI-цикл).
        /// </summary>
        protected readonly Func<ISubAgentService> SubAgentServiceFactory;

        /// <summary>
        /// Реестр специализированных суб-агентов (Singleton).
        /// </summary>
        protected readonly ISubAgentRegistry Registry;

        /// <summary>
        /// Логгер (по типу конкретного наследника).
        /// </summary>
        protected readonly ILogger Logger;

        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract string Description { get; }

        /// <summary>
        /// Техническое имя агента в <see cref="ISubAgentRegistry"/>
        /// (<c>file_system_agent</c>, <c>code_agent</c>, ...).
        /// </summary>
        protected abstract string AgentName { get; }

        /// <summary>
        /// Требует ли вызов агента подтверждения — резолвится из дескриптора
        /// (<c>SubAgents:X.RequiresApproval</c>). По умолчанию <c>true</c>, если
        /// агент не найден в реестре.
        /// </summary>
        public virtual bool RequiresApprovalByDefault
        {
            get
            {
                var d = Registry?.Get(AgentName);
                return d?.RequiresApprovalByDefault ?? true;
            }
        }

        /// <summary>
        /// Параметры вызова агента. Единый набор для всех агентов:
        /// <c>task</c> (обязательный), <c>context</c> (опциональный),
        /// <c>maxSteps</c> (опциональный).
        /// </summary>
        public virtual IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "task",
                Type = "string",
                Description = "Подробное описание задачи для агента (максимум 8000 символов).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "context",
                Type = "string",
                Description = "Дополнительный контекст: содержимое файлов, требования, ограничения (максимум 20 000 символов).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "maxSteps",
                Type = "integer",
                Description = "Максимум шагов агента (1–30). По умолчанию — из настроек агента.",
                Required = false
            }
        };

        /// <summary>
        /// Создаёт инструмент-обёртку.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента (разрывает DI-цикл)</param>
        /// <param name="registry">Реестр специализированных суб-агентов</param>
        /// <param name="logger">Логгер наследника</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        protected AgentToolBase(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            ILogger logger)
        {
            SubAgentServiceFactory = subAgentServiceFactory
                ?? throw new ArgumentNullException(nameof(subAgentServiceFactory));
            Registry = registry
                ?? throw new ArgumentNullException(nameof(registry));
            Logger = logger
                ?? throw new ArgumentNullException(nameof(logger));
        }

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

            var maxSteps = arguments.GetInt("maxSteps", 0);

            var descriptor = Registry.Get(AgentName);
            if (descriptor == null)
            {
                Logger.LogWarning("Агент {Agent} не найден в SubAgentRegistry", AgentName);
                return ToolResult.Fail($"Агент '{AgentName}' не зарегистрирован");
            }

            if (descriptor.Disabled)
            {
                Logger.LogInformation("Попытка вызвать отключённый агент {Agent}", AgentName);
                return ToolResult.Fail($"Агент '{AgentName}' отключён администратором");
            }

            var effectiveMaxSteps = maxSteps > 0
                ? Math.Min(maxSteps, 30)
                : descriptor.MaxSteps;

            Logger.LogInformation(
                "Запуск агента {Agent} (модель: {Model}, шагов: {MaxSteps}, задача: {Task})",
                AgentName,
                string.IsNullOrWhiteSpace(descriptor.Model) ? "<default>" : descriptor.Model,
                effectiveMaxSteps,
                Truncate(task, 120));

            var request = new SubAgentTaskRequest
            {
                Task = task,
                Context = extraContext,
                MaxSteps = effectiveMaxSteps,
                AllowedTools = descriptor.AllowedTools,
                SystemPromptOverride = descriptor.SystemPrompt,
                ModelOverride = descriptor.Model
            };

            try
            {
                // Ленивое создание сервиса суб-агента — разрывает DI-цикл.
                var subAgent = SubAgentServiceFactory();
                var result = await subAgent.ExecuteTaskAsync(context, request);

                return ToolResult.Ok(new
                {
                    agent = AgentName,
                    sessionId = result.SessionId,
                    finalAnswer = result.FinalAnswer,
                    completed = result.Completed,
                    steps = result.Steps,
                    durationMs = result.DurationMs,
                    usedTools = result.UsedTools
                });
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("Агент {Agent} отменён клиентом", AgentName);
                return ToolResult.Fail("Задача отменена");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Ошибка выполнения агента {Agent}", AgentName);
                return ToolResult.Fail($"Ошибка агента: {ex.Message}");
            }
        }

        /// <summary>
        /// Обрезает строку до указанной длины (для логов).
        /// </summary>
        /// <param name="value">Строка</param>
        /// <param name="max">Максимум символов</param>
        /// <returns>Обрезанная строка</returns>
        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }
    }
}