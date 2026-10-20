using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Orchestrator;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Административный сервис управления настройками Orchestrator'а
    /// (v2.2.2, KI-245).
    /// <para>
    /// Управляет <b>runtime-override'ами</b>: значения сохраняются в
    /// <c>AppSettings</c> (ключи <c>Orchestrator.*</c>) и сразу применяются
    /// к <see cref="IOrchestratorOptionsProvider"/> — без перезапуска
    /// приложения. При старте override'ы восстанавливаются
    /// в <c>Program.LoadOrchestratorOverridesAsync</c>.
    /// </para>
    /// <para>
    /// Persist-логика симметрична <c>AdminSqlAgentService</c> (v1.7.0, KI-097).
    /// </para>
    /// </summary>
    public interface IAdminOrchestratorService
    {
        /// <summary>
        /// Возвращает актуальные настройки Orchestrator'а
        /// (override + baseline) и флаг <c>IsOverridden</c>.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Актуальные настройки</returns>
        Task<OrchestratorSettingsDto> GetSettingsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Обновляет настройки Orchestrator'а.
        /// <list type="bullet">
        ///   <item>Валидирует входные значения (диапазоны, whitelist, режим approval).</item>
        ///   <item>Persist-ит изменения в <c>AppSettings</c>.</item>
        ///   <item>Применяет к <see cref="IOrchestratorOptionsProvider"/> in-memory.</item>
        ///   <item>Пишет запись в audit log (действие администратора).</item>
        /// </list>
        /// </summary>
        /// <param name="settings">Новые настройки</param>
        /// <param name="userId">ID администратора (для аудита)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Сохранённые настройки</returns>
        /// <exception cref="System.ArgumentNullException">Если <paramref name="settings"/> = null</exception>
        /// <exception cref="System.ArgumentException">
        /// Если значения вне диапазонов, whitelist пуст или режим approval неизвестен.
        /// </exception>
        Task<OrchestratorSettingsDto> UpdateSettingsAsync(
            OrchestratorSettingsDto settings,
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Сбрасывает настройки к baseline из <c>appsettings.json</c>:
        /// удаляет все ключи <c>Orchestrator.*</c> из <c>AppSettings</c>
        /// и вызывает <see cref="IOrchestratorOptionsProvider.Reset"/>.
        /// </summary>
        /// <param name="userId">ID администратора (для аудита)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Настройки после сброса</returns>
        Task<OrchestratorSettingsDto> ResetAsync(
            int userId,
            CancellationToken cancellationToken = default);
    }
}
