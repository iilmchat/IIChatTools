using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг агента внешних LLM
    /// (v1.8.1, KI-109, Фаза 4).
    ///
    /// <para>
    /// Внутри агента доступны 3 external-llm-tools:
    /// <c>ask_external_llm</c>, <c>list_external_providers</c>,
    /// <c>check_internet_connection</c>
    /// (см. <c>SubAgents:external_llm_agent:AllowedTools</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Approval:</b> <see cref="AgentToolBase.RequiresApprovalByDefault"/>
    /// резолвится из дескриптора
    /// (<c>SubAgents:external_llm_agent:RequiresApproval</c> = <c>false</c>).
    /// Защита от «$1000 за ночь» — через <c>DailyBudgetUsd</c> (per-user, DESIGN § 6.4).
    /// </para>
    ///
    /// <para>
    /// <b>Условная регистрация:</b> tool регистрируется в DI **всегда** (как 7
    /// других агентов), но <c>SubAgents:external_llm_agent:Enabled</c> управляет
    /// видимостью в Chat. Реальные external-llm-tools внутри агента доступны
    /// только при <c>ExternalLlm:Enabled = true</c> — иначе LLM внутри агента
    /// получит Fail от каждого tool (их нет в DI).
    /// </para>
    /// </summary>
    public class ExternalLlmAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "external_llm_agent";

        /// <inheritdoc />
        public override string Description =>
            "Агент внешних LLM. Обращается к внешним моделям (DeepSeek, OpenAI, Groq, " +
            "Together AI, Ollama), когда локальная модель не справляется: свежие данные " +
            "после knowledge cutoff, специализированные знания, сравнение ответов. " +
            "Используй для задач: «спроси DeepSeek про X», «сравни ответы OpenAI и DeepSeek», " +
            "«что нового в .NET 10» (внешние данные). " +
            "Не требует подтверждения — защита через дневной бюджет ($5/день). " +
            "По умолчанию отправляет во внешнюю модель ТОЛЬКО prompt (без истории чата).";

        /// <inheritdoc />
        protected override string AgentName => "external_llm_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076)</param>
        /// <param name="logger">Логгер</param>
        public ExternalLlmAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<ExternalLlmAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}