using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг почтового агента
    /// (v1.8.0, KI-107, Фаза 5).
    ///
    /// <para>
    /// Внутри агента доступны 7 mail-tools: <c>send_email</c>, <c>list_emails</c>,
    /// <c>read_email</c>, <c>search_emails</c>, <c>delete_email</c>, <c>move_email</c>,
    /// <c>mark_as_read</c> (см. <c>SubAgents:mail_agent:AllowedTools</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Approval:</b> <see cref="AgentToolBase.RequiresApprovalByDefault"/>
    /// резолвится из дескриптора (<c>SubAgents:mail_agent:RequiresApproval</c>),
    /// не хардкодится.
    /// </para>
    ///
    /// <para>
    /// <b>Условная регистрация:</b> tool регистрируется в DI **всегда**, но
    /// <c>SubAgents:mail_agent:Enabled = false</c> скрывает его из Chat
    /// (не попадает в <c>GetEnabled()</c>). Дополнительно, реальные mail-tools
    /// внутри агента доступны только при <c>Mail:Enabled = true</c> — иначе
    /// LLM внутри агента получит Fail от каждого mail-tool (tool'ов нет в DI).
    /// </para>
    /// </summary>
    public class MailAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "mail_agent";

        /// <inheritdoc />
        public override string Description =>
            "Почтовый агент. Работает с электронной почтой через IMAP/SMTP: " +
            "чтение, поиск, отправка, перемещение, удаление, пометка писем. " +
            "Требует подтверждения (approval). " +
            "Используй для задач: «прочитай последнее письмо», " +
            "«найди письма от X за неделю», «отправь письмо на Y», " +
            "«перемести письмо в архив», «помечай как прочитанное». " +
            "НИКОГДА не отправляй письма без явной просьбы пользователя.";

        /// <inheritdoc />
        protected override string AgentName => "mail_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076)</param>
        /// <param name="logger">Логгер</param>
        public MailAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<MailAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}