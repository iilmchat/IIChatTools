using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

        /// <summary>
        /// Сервис аудита — для записи статистики запусков агента (v1.4.x, KI-076).
        /// </summary>
        protected readonly IAuditService AuditService;

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
        /// <param name="auditService">Сервис аудита (запись статистики запусков, KI-076)</param>
        /// <param name="logger">Логгер наследника</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        protected AgentToolBase(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger logger)
        {
            SubAgentServiceFactory = subAgentServiceFactory
                ?? throw new ArgumentNullException(nameof(subAgentServiceFactory));
            Registry = registry
                ?? throw new ArgumentNullException(nameof(registry));
            AuditService = auditService
                ?? throw new ArgumentNullException(nameof(auditService));
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

            var sw = Stopwatch.StartNew();
            try
            {
                // Ленивое создание сервиса суб-агента — разрывает DI-цикл.
                var subAgent = SubAgentServiceFactory();
                var result = await subAgent.ExecuteTaskAsync(context, request);

                sw.Stop();

                // v1.4.x (KI-076): запись статистики запуска в AuditLogs.
                // Источник для /api/admin/agents/stats. Не критично — ошибка
                // записи не должна ломать основной поток.
                await LogAgentRunAsync(context, descriptor, result, sw.ElapsedMilliseconds, status: "Success");

                // v1.13.x (KI-114, KI-167): если агент не завершил задачу
                // (result.Completed == false) — возвращаем Fail, а не Ok.
                //
                // <para>
                // <b>Причина:</b> раньше AgentToolBase всегда возвращал Ok —
                // Chat LLM видела «success: true» даже когда SubAgent вернул
                // completed=false (исчерпан MaxSteps, LLM не нашла путь решения).
                // Это усиливало «эффект галлюцинации успеха» (KI-113).
                // См. DESIGN v1.4 § 3.3 — результат агента теперь строго
                // соответствует его завершённости.
                // </para>
                //
                // <para>
                // Внешняя семантика: для Chat «success: false» = агент
                // не справился. LLM перепланирует или честно сообщит пользователю.
                // </para>
                var resultData = new
                {
                    agent = AgentName,
                    sessionId = result.SessionId,
                    finalAnswer = result.FinalAnswer,
                    completed = result.Completed,
                    steps = result.Steps,
                    durationMs = result.DurationMs,
                    usedTools = result.UsedTools
                };

                // v1.13.x (KI-114-fix, 2026-10-05): смягчение критерия Fail.
                //
                // Раньше: Completed == false → всегда Fail. Это давало ложные
                // негативы: SubAgent создал файл, но упёрся в MaxSteps до
                // финального ответа — LLM в Chat видела «success: false»,
                // хотя задача фактически выполнена (smoke #3, 2026-10-05).
                //
                // Теперь: Fail только если агент вообще НЕ вызывал инструменты
                // (UsedTools.Count == 0) — значит, он реально не справился
                // (не нашёл путь, ошибка LM Studio, таймаут до первого tool call).
                // Если хотя бы один инструмент вызван — Ok с полными данными,
                // а LLM в Chat сама решит по finalAnswer, отвечать ли «успех».
                //
                // <para>
                // v1.6.1 (KI-086-post): sources от inner-инструментов
                // (wikipedia_search, search_knowledge_base, ...) пробрасываются
                // наружу через ToolResult.Sources.
                // </para>
                var usedAnyTool = result.UsedTools != null && result.UsedTools.Count > 0;

                if (result.Completed || usedAnyTool)
                {
                    return ToolResult.Ok(
                        resultData,
                        message: null,
                        sources: result.Sources);
                }

                // Агент не завершил задачу И не вызвал ни одного инструмента —
                // реальный провал.
                var failMessage = string.IsNullOrWhiteSpace(result.FinalAnswer)
                    ? $"Агент '{AgentName}' не завершил задачу (шагов: {result.Steps}, инструментов: 0)."
                    : $"Агент '{AgentName}' не завершил задачу: {result.FinalAnswer}";

                return ToolResult.Fail(failMessage, resultData);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                Logger.LogInformation("Агент {Agent} отменён клиентом", AgentName);

                await LogAgentRunAsync(context, descriptor, result: null, sw.ElapsedMilliseconds, status: "Cancelled");

                return ToolResult.Fail("Задача отменена");
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.LogError(ex, "Ошибка выполнения агента {Agent}", AgentName);

                await LogAgentRunAsync(context, descriptor, result: null, sw.ElapsedMilliseconds, status: "Error",
                    errorMessage: ex.Message);

                return ToolResult.Fail($"Ошибка агента: {ex.Message}");
            }
        }

        /// <summary>
        /// Записывает запуск агента в AuditLogs (v1.4.x, KI-076).
        /// ToolName = <c>agent.{AgentName}</c> — источник для <c>/api/admin/agents/stats</c>.
        /// </summary>
        /// <param name="context">Контекст выполнения (для UserId)</param>
        /// <param name="descriptor">Дескриптор агента (для ParametersJson)</param>
        /// <param name="result">Результат (null при ошибке/отмене)</param>
        /// <param name="durationMs">Длительность (мс)</param>
        /// <param name="status">Статус: Success / Error / Cancelled</param>
        /// <param name="errorMessage">Сообщение об ошибке (для Error)</param>
        private async Task LogAgentRunAsync(
            ToolExecutionContext context,
            SubAgentDescriptor descriptor,
            SubAgentTaskResult result,
            long durationMs,
            string status,
            string errorMessage = null)
        {
            try
            {
                var parametersJson = JsonConvert.SerializeObject(new
                {
                    model = descriptor?.Model,
                    maxSteps = descriptor?.MaxSteps,
                    // Задача НЕ логируется целиком (без PII) — только флаг наличия.
                    hasTask = true
                });

                var resultJson = result == null
                    ? JsonConvert.SerializeObject(new { error = errorMessage })
                    : JsonConvert.SerializeObject(new
                    {
                        completed = result.Completed,
                        steps = result.Steps,
                        usedToolsCount = result.UsedTools?.Count ?? 0
                    });

                await AuditService.LogActionAsync(new AuditLog
                {
                    UserId = context?.UserId ?? 0,
                    ToolName = $"agent.{AgentName}",
                    ParametersJson = parametersJson,
                    ResultJson = resultJson,
                    DurationMs = durationMs,
                    Status = status,
                    ClientIp = context?.ClientIp
                });
            }
            catch (Exception ex)
            {
                // Аудит не должен ломать основной поток.
                Logger.LogWarning(ex, "Не удалось записать audit статистики агента {Agent}", AgentName);
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