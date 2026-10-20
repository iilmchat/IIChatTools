using IIChatTools.Services.DTO.Orchestrator;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Провайдер актуальных настроек Orchestrator-Worker паттерна
    /// (v2.2.2, KI-245, DESIGN_ORCHESTRATOR_WORKER § 7).
    /// <para>
    /// Хранит <b>baseline</b> из <c>appsettings.json</c> (секция
    /// <c>SubAgents:orchestrate_task</c>, читается один раз при инициализации)
    /// и <b>runtime-overrides</b>, применяемые из админки без перезапуска
    /// приложения.
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> Singleton. Все потребители (в первую очередь
    /// <c>OrchestrateTaskTool</c>) читают актуальные значения при каждом вызове
    /// и не кэшируют их.
    /// </para>
    /// <para>
    /// Регистрация в DI — <c>Startup.ConfigureServices</c>:
    /// <c>services.AddSingleton&lt;IOrchestratorOptionsProvider,
    /// OrchestratorOptionsProvider&gt;()</c>.
    /// </para>
    /// </summary>
    public interface IOrchestratorOptionsProvider
    {
        /// <summary>
        /// Возвращает актуальные настройки (runtime-override, если задан,
        /// иначе baseline). Возвращает <b>копию</b> — вызывающий не может
        /// изменить внутреннее состояние провайдера.
        /// </summary>
        /// <returns>Копия актуальных настроек</returns>
        OrchestratorSettingsDto Get();

        /// <summary>
        /// Применяет runtime-override настроек (вызывается из
        /// <c>AdminOrchestratorService</c> и <c>Program.LoadOrchestratorOverridesAsync</c>).
        /// </summary>
        /// <param name="settings">Новые настройки (заменяют override целиком)</param>
        /// <exception cref="System.ArgumentNullException">Если <paramref name="settings"/> = null</exception>
        /// <exception cref="System.ArgumentException">
        /// Если значения вне допустимых диапазонов или whitelist пуст.
        /// </exception>
        void Update(OrchestratorSettingsDto settings);

        /// <summary>
        /// Сбрасывает runtime-override — провайдер возвращается к baseline
        /// из <c>appsettings.json</c>.
        /// </summary>
        void Reset();

        /// <summary>
        /// Признак наличия runtime-override (настройки отличаются от baseline).
        /// </summary>
        bool IsOverridden { get; }
    }
}
