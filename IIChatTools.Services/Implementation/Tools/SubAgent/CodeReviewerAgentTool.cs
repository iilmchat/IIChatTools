using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг агента-критика (Critic)
    /// (v1.11.0, KI-126, Шаг 1C).
    ///
    /// <para>
    /// Используется внутри Actor-Critic цикла (<c>code_agent_with_review</c>,
    /// Шаг 1D). Проверяет код actor'а, возвращает JSON:
    /// <c>{ verdict, issues, summary }</c> (DESIGN § 2.3).
    /// </para>
    ///
    /// <para>
    /// <b>AllowedTools: пусто</b> — критик не вызывает инструменты, только
    /// анализирует полученный код. <b>RequiresApproval: false</b> — read-only
    /// анализ (внешний approval на весь review-цикл — на уровне
    /// <c>code_agent_with_review</c>, Шаг 1D).
    /// </para>
    ///
    /// <para>
    /// <b>Видимость в Chat:</b> tool регистрируется в DI, но попадает в Chat
    /// только если <c>SubAgents:code_reviewer_agent:Enabled = true</c>
    /// (через <c>SubAgentRegistry.GetEnabled()</c>). В Actor-Critic критик
    /// вызывается <b>внутри</b> другого tool'а (не из Chat) — прямые вызовы
    /// из Chat LLM не предполагаются, но технически возможны
    /// (LLM увидит его как один из top-level инструментов).
    /// </para>
    /// </summary>
    public class CodeReviewerAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "code_reviewer_agent";

        /// <inheritdoc />
        public override string Description =>
            "Код-ревьюер (Critic). Проверяет код на edge cases, безопасность, " +
            "обработку ошибок. Возвращает JSON {verdict, issues, summary}. " +
            "Используется внутри Actor-Critic (code_agent_with_review). " +
            "НЕ требует approval (read-only анализ). " +
            "Не вызывай напрямую из Chat — используй code_agent_with_review для " +
            "сложных задач кодинга.";

        /// <inheritdoc />
        protected override string AgentName => "code_reviewer_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076)</param>
        /// <param name="logger">Логгер</param>
        public CodeReviewerAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<CodeReviewerAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}