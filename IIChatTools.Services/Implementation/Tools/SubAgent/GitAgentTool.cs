using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг Git-агента (v1.4.0 Фаза 3, KI-052).
    /// Внутри агента доступны 7 Git-инструментов (status, diff, log, add, commit, checkout, push).
    /// </summary>
    public class GitAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "git_agent";

        /// <inheritdoc />
        public override string Description =>
            "Git-агент. Выполняет операции с локальным Git-репозиторием: статус, diff, " +
            "логи, добавление, коммит, checkout, push. Требует подтверждения (approval).";

        /// <inheritdoc />
        protected override string AgentName => "git_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076)</param>
        /// <param name="logger">Логгер</param>
        public GitAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<GitAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}