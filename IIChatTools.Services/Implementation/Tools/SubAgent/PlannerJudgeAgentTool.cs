using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// v1.14.0 (KI-126, Фаза 2): Judge-агент для Debate.
    ///
    /// <para>
    /// Выносит вердикт по аргументам Pro и претензиям Contra:
    /// <list type="bullet">
    ///   <item><c>Approved</c> — Contra не нашёл Critical / Major issues;</item>
    ///   <item><c>Rejected</c> — есть хотя бы один Critical / Major issue;</item>
    ///   <item><c>Uncertain</c> — не хватает данных → эскалация на внешнюю LLM.</item>
    /// </list>
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
    public class PlannerJudgeAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "planner_judge_agent";

        /// <inheritdoc />
        public override string Description =>
            "Judge-роль в Debate (арбитр). Синтезирует аргументы Pro и Contra, " +
            "выносит вердикт Approved / Rejected / Uncertain. " +
            "Используется внутри planner_with_debate, не для прямого вызова.";

        /// <inheritdoc />
        protected override string AgentName => "planner_judge_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента (ADR-002).</param>
        /// <param name="registry">Реестр суб-агентов.</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null.</exception>
        public PlannerJudgeAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<PlannerJudgeAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}