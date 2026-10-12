using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// v2.0.0 (KI-126, Фаза 3): агент-декомпозитор для Orchestrator-Worker.
    ///
    /// <para>
    /// Разбивает сложную multi-step задачу на 2-5 подзадач и распределяет
    /// их между существующими агентами (<c>code_agent</c>, <c>web_agent</c>,
    /// <c>file_system_agent</c>, ...). Возвращает строго JSON:
    /// <c>{ parallel, reasoning, subtasks: [{id, agent, task}, ...] }</c>.
    /// </para>
    ///
    /// <para>
    /// Используется <b>внутри</b> top-level tool <c>orchestrate_task</c>
    /// (Шаг 3D) — Chat-LLM вызывает его косвенно, через оркестратор.
    /// Прямого вызова из Chat нет (RULES § 4.44):
    /// <see cref="DTO.SubAgent.SubAgentDescriptor.Internal"/> = <c>true</c>
    /// исключает его из <c>allowedNames</c>.
    /// </para>
    ///
    /// <para>
    /// <b>AllowedTools = ["__no_tools__"]:</b> роль — чистый LLM-вызов
    /// без инструментов (только декомпозиция, без выполнения действий).
    /// </para>
    ///
    /// <para>
    /// <b>RequiresApproval = false:</b> декомпозиция — read-only операция;
    /// approval запрашивается оркестратором на уровне <c>orchestrate_task</c>.
    /// </para>
    /// </summary>
    public class OrchestratorPlannerAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "orchestrator_planner_agent";

        /// <inheritdoc />
        public override string Description =>
            "Планировщик-декомпозитор для Orchestrator-Worker. Разбивает сложную " +
            "задачу на 2-5 подзадач и распределяет между агентами-воркерами. " +
            "Используется внутри orchestrate_task, не для прямого вызова.";

        /// <inheritdoc />
        protected override string AgentName => "orchestrator_planner_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента (ADR-002).</param>
        /// <param name="registry">Реестр суб-агентов.</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null.</exception>
        public OrchestratorPlannerAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<OrchestratorPlannerAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}