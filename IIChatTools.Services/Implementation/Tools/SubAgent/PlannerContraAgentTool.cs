using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// v1.14.0 (KI-126, Фаза 2): Contra-агент для Debate.
    ///
    /// <para>
    /// Критикует предложение (план, архитектурное решение, подход):
    /// ищет пропущенные сценарии, неверные допущения, скрытые costs
    /// и риски безопасности. Возвращает структурированный JSON
    /// с issues и risks.
    /// </para>
    ///
    /// <para>
    /// Используется <b>внутри</b> top-level tool
    /// <c>planner_with_debate</c> (Шаг 2C). Прямого вызова из Chat нет
    /// (RULES § 4.44).
    /// </para>
    ///
    /// <para>
    /// <b>AllowedTools = []</b> и <b>RequiresApproval = false</b> —
    /// аналогично <see cref="PlannerProAgentTool"/>.
    /// </para>
    /// </summary>
    public class PlannerContraAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "planner_contra_agent";

        /// <inheritdoc />
        public override string Description =>
            "Contra-роль в Debate (скептик). Ищет дыры в предложении: " +
            "пропущенные edge cases, неверные допущения, скрытые costs, риски. " +
            "Используется внутри planner_with_debate, не для прямого вызова.";

        /// <inheritdoc />
        protected override string AgentName => "planner_contra_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента (ADR-002).</param>
        /// <param name="registry">Реестр суб-агентов.</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null.</exception>
        public PlannerContraAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<PlannerContraAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}