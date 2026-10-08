using System;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Tools.SubAgent
{
    /// <summary>
    /// Инструмент-обёртка вокруг браузерного агента
    /// (v1.13.6, KI-128).
    ///
    /// <para>
    /// Внутри агента доступны 4 browser-tool: <c>browser_session_open</c>,
    /// <c>browser_session_control</c>, <c>browser_session_close</c>,
    /// <c>browser_open_page</c> (см. <c>SubAgents:browser_agent:AllowedTools</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Назначение:</b> интерактивные задачи с реальным Chromium
    /// (PuppeteerSharp) — открыть сайт, кликнуть, заполнить форму,
    /// сделать скриншот в workspace. Для сайтов с динамическим DOM
    /// (RZD, Госуслуги, маркетплейсы), где нужны реальные клики и
    /// точные селекторы (в отличие от <c>web_agent</c> — только чтение HTML;
    /// и от <c>vision_agent</c> — координаты через VL, ±20-30 px).
    /// </para>
    ///
    /// <para>
    /// <b>Approval:</b> <see cref="AgentToolBase.RequiresApprovalByDefault"/>
    /// резолвится из дескриптора (<c>SubAgents:browser_agent:RequiresApproval</c>),
    /// не хардкодится. В appsettings выставлено <c>true</c> — браузерная
    /// автоматизация может совершать mutating-действия (клик, ввод, скриншот).
    /// </para>
    ///
    /// <para>
    /// <b>Разница с <c>vision_agent</c>:</b>
    /// <list type="bullet">
    ///   <item><description><c>browser_agent</c> — DOM-доступ (PuppeteerSharp), 0 px ошибки, быстрее;</description></item>
    ///   <item><description><c>vision_agent</c> — скриншот + VL-модель, для desktop-приложений и canvas/WebGL/shadow-DOM.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public class BrowserAgentTool : AgentToolBase
    {
        /// <inheritdoc />
        public override string Name => "browser_agent";

        /// <inheritdoc />
        public override string Description =>
            "Браузерный агент. Управляет реальным Chromium (PuppeteerSharp): " +
            "открывает сайты, кликает, вводит текст, заполняет формы, делает " +
            "скриншоты в workspace пользователя. Требует подтверждения (approval). " +
            "Используй для задач: «открой сайт X, найди Y, сделай скриншот в Z.png», " +
            "«купи билет на rzd.ru», «проверь баланс на сайте банка», " +
            "«заполни форму на госуслугах». " +
            "НЕ используй web_agent (он только читает HTML через HTTP) " +
            "и vision_agent (он для desktop/canvas) — для обычных сайтов " +
            "с DOM-структурой используй browser_agent.";

        /// <inheritdoc />
        protected override string AgentName => "browser_agent";

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="subAgentServiceFactory">Фабрика сервиса суб-агента</param>
        /// <param name="registry">Реестр суб-агентов</param>
        /// <param name="auditService">Сервис аудита (v1.4.x, KI-076)</param>
        /// <param name="logger">Логгер</param>
        public BrowserAgentTool(
            Func<ISubAgentService> subAgentServiceFactory,
            ISubAgentRegistry registry,
            IAuditService auditService,
            ILogger<BrowserAgentTool> logger)
            : base(subAgentServiceFactory, registry, auditService, logger)
        {
        }
    }
}