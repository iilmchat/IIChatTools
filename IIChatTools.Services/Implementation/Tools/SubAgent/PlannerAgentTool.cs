using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг агента-планировщика (v1.4.0 Фаза 3, KI-052).
    /// Внутри агента доступны save_memory, get_system_info.
    /// Не требует подтверждения (read-only + память).
    /// </summary>
    public class PlannerAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "planner_agent";

        /// <inheritdoc />
        public override string Description =>
            "Агент-планировщик. Сохраняет важную информацию в долговременную память " +
            "пользователя и получает системную информацию. Используй для запоминания " +
            "фактов о пользователе и его предпочтениях. Не требует подтверждения.";

        /// <inheritdoc />
        protected override string AgentName => "planner_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076)</param>
        /// <param name="logger">Логгер</param>
        public PlannerAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<PlannerAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}