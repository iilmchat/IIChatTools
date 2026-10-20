using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Orchestrator
{
    /// <summary>
    /// Настройки Orchestrator-Worker паттерна
    /// (v2.2.2, KI-245, DESIGN_ORCHESTRATOR_WORKER § 7).
    /// <para>
    /// DTO используется в трёх местах:
    /// <list type="bullet">
    ///   <item><see cref="Implementation.Orchestrator.OrchestratorOptionsProvider"/> —
    ///   baseline + runtime overrides;</item>
    ///   <item><see cref="Interfaces.IAdminOrchestratorService"/> — чтение/запись
    ///   настроек из админки;</item>
    ///   <item><c>OrchestrateTaskTool</c> — актуальные лимиты и whitelist
    ///   воркеров на момент вызова.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Значения по умолчанию (когда секция <c>SubAgents:orchestrate_task</c>
    /// отсутствует в конфигурации): <c>MaxSubtasks = 5</c>,
    /// <c>MaxParallel = 3</c>, <c>TokenBudget = 50000</c>,
    /// <c>HumanApproval = Never</c>, whitelist из 6 базовых воркеров.
    /// </para>
    /// </summary>
    public class OrchestratorSettingsDto
    {
        /// <summary>
        /// Максимальное число подзадач, выполняемых Orchestrator'ом
        /// (диапазон 1–10, default 5).
        /// </summary>
        public int MaxSubtasks { get; set; }

        /// <summary>
        /// Максимальное число одновременно выполняемых подзадач
        /// (диапазон 1–10, default 3). Для <c>external_llm_agent</c>
        /// фактически снижается до 1.
        /// </summary>
        public int MaxParallel { get; set; }

        /// <summary>
        /// Бюджет токенов на одну оркестрацию
        /// (диапазон 1000–1 000 000, default 50000).
        /// </summary>
        public int TokenBudget { get; set; }

        /// <summary>
        /// Режим Human-in-the-loop: <c>Never</c> (default), <c>BetweenSubtasks</c>,
        /// <c>AtEnd</c>. Хранится в каноническом виде (PascalCase).
        /// </summary>
        public string HumanApproval { get; set; }

        /// <summary>
        /// Whitelist агентов-воркеров, которым разрешено выполнять подзадачи.
        /// Пустой список недопустим: провайдер подставляет baseline/default.
        /// </summary>
        public List<string> AllowedWorkerAgents { get; set; } = new List<string>();

        /// <summary>
        /// Признак того, что настройки изменены в runtime и отличаются от
        /// baseline из <c>appsettings.json</c> (для бейджа «изменено» в админке).
        /// При записи через <see cref="Interfaces.IAdminOrchestratorService"/>
        /// значение игнорируется — его вычисляет провайдер.
        /// </summary>
        public bool IsOverridden { get; set; }
    }
}
