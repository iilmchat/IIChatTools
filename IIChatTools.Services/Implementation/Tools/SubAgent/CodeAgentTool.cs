using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг агента выполнения кода (v1.4.0 Фаза 3, KI-052).
    /// Внутри агента доступны run_javascript, run_python, execute_command.
    /// </summary>
    public class CodeAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "code_agent";

        /// <inheritdoc />
        public override string Description =>
            "Агент выполнения кода. Пишет и запускает JavaScript, Python и shell-команды " +
            "для решения задач пользователя. Используй для вычислений, обработки данных, " +
            "автоматизации. Требует подтверждения (approval).";

        /// <inheritdoc />
        protected override string AgentName => "code_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="logger">Логгер</param>
        public CodeAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            ILogger<CodeAgentTool> logger)
            : base(subAgentServiceFactory, registry, logger)
        {
        }
    }
}