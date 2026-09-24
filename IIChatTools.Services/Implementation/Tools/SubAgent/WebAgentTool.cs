using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг веб-агента (v1.4.0 Фаза 3, KI-052).
    /// Внутри агента доступны web_search, wikipedia_search, fetch_web_content.
    /// Не требует подтверждения (read-only).
    /// </summary>
    public class WebAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "web_agent";

        /// <inheritdoc />
        public override string Description =>
            "Веб-агент. Ищет информацию в интернете (DuckDuckGo) и Wikipedia, " +
            "загружает содержимое веб-страниц. Используй для поиска актуальной информации. " +
            "Не требует подтверждения (read-only).";

        /// <inheritdoc />
        protected override string AgentName => "web_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076)</param>
        /// <param name="logger">Логгер</param>
        public WebAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<WebAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}