using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг GitHub-агента (v1.4.0 Фаза 3, KI-052).
    /// Внутри агента доступны 7 gh-инструментов (auth, issues, PRs, комментарии).
    /// </summary>
    public class GitHubAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "github_agent";

        /// <inheritdoc />
        public override string Description =>
            "GitHub-агент. Работает с GitHub через gh CLI: создание issues, PRs, " +
            "просмотр комментариев, diff. Требует подтверждения (approval).";

        /// <inheritdoc />
        protected override string AgentName => "github_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076)</param>
        /// <param name="logger">Логгер</param>
        public GitHubAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<GitHubAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}