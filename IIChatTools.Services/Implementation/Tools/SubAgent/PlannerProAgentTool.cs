using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// v1.14.0 (KI-126, Фаза 2): Pro-агент для Debate.
    ///
    /// <para>
    /// Защищает предложение (план, архитектурное решение, подход):
    /// приводит аргументы ЗА, показывает, какие edge cases покрыты,
    /// даёт обоснованное заключение.
    /// </para>
    ///
    /// <para>
    /// Используется <b>внутри</b> top-level tool
    /// <c>planner_with_debate</c> (Шаг 2C) — Chat-LLM вызывает его
    /// косвенно, через оркестратор. Прямого вызова из Chat нет —
    /// его имя отсутствует в <c>allowedNames</c>
    /// (<see cref="Implementation.ChatStreamService"/>, RULES § 4.44).
    /// </para>
    ///
    /// <para>
    /// <b>AllowedTools = []:</b> роль — чистый LLM-вызов без инструментов
    /// (анализ текста, не выполнение действий).
    /// </para>
    ///
    /// <para>
    /// <b>RequiresApproval = false:</b> read-only анализ — approval
    /// запрашивается оркестратором на уровне <c>planner_with_debate</c>.
    /// </para>
    /// </summary>
    public class PlannerProAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "planner_pro_agent";

        /// <inheritdoc />
        public override string Description =>
            "Pro-роль в Debate (защитник). Приводит аргументы ЗА предложение, " +
            "обосновывает его преимущества и показывает покрытые edge cases. " +
            "Используется внутри planner_with_debate, не для прямого вызова.";

        /// <inheritdoc />
        protected override string AgentName => "planner_pro_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента (ADR-002).</param>
        /// <param name="registry">Реестр суб-агентов.</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null.</exception>
        public PlannerProAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<PlannerProAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}