using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Admin;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Административный сервис управления подключениями Database Agent
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6).
    /// <para>
    /// Управляет <b>runtime-override'ами</b>: значения сохраняются в
    /// <c>AppSettings</c> (ключи <c>SqlAgent.{name}.{field}</c>) и сразу
    /// применяются к <c>SqlAgentOptionsProvider</c> — без перезапуска.
    /// </para>
    /// <para>
    /// Persist-логика симметрична <c>AdminAgentsController</c> (v1.4.0, KI-052).
    /// </para>
    /// </summary>
    public interface IAdminSqlAgentService
    {
        /// <summary>
        /// Возвращает список всех подключений с актуальными настройками
        /// (override + baseline) и флагом <c>IsOverridden</c>.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список подключений</returns>
        Task<IReadOnlyList<SqlAgentConnectionItemDto>> GetAllConnectionsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Обновляет override-настройки подключения.
        /// <list type="bullet">
        ///   <item>Валидирует входные значения (<c>MaxRows</c>, <c>StatementTimeoutSeconds</c>).</item>
        ///   <item>Persist-ит изменения в <c>AppSettings</c>.</item>
        ///   <item>Применяет к <c>SqlAgentOptionsProvider</c> in-memory (без рестарта).</item>
        ///   <item>Пишет запись в audit log (действие администратора).</item>
        /// </list>
        /// </summary>
        /// <param name="name">Имя подключения</param>
        /// <param name="request">Поля для изменения (null = не менять)</param>
        /// <param name="userId">ID администратора (для аудита)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Обновлённое состояние подключения</returns>
        /// <exception cref="System.ArgumentException">
        /// Если подключение не найдено или значение вне диапазона.
        /// </exception>
        Task<SqlAgentConnectionItemDto> UpdateConnectionAsync(
            string name,
            UpdateSqlAgentConnectionRequest request,
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет подключение: открывает соединение и выполняет <c>SELECT 1</c>.
        /// <para>
        /// Работает даже для <c>Enabled = false</c> (проверка «до включения»).
        /// Все ошибки (SQL, сеть, аутентификация) возвращаются в
        /// <see cref="SqlAgentTestResultDto.Message"/>, исключения не бросаются.
        /// </para>
        /// </summary>
        /// <param name="name">Имя подключения</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Результат проверки</returns>
        Task<SqlAgentTestResultDto> TestConnectionAsync(
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Сбрасывает override-настройки подключения к baseline из
        /// <c>appsettings.json</c>: удаляет все ключи
        /// <c>SqlAgent.{name}.*</c> из <c>AppSettings</c> и применяет
        /// <c>SqlAgentOptionsProvider.Reset(name)</c>.
        /// </summary>
        /// <param name="name">Имя подключения</param>
        /// <param name="userId">ID администратора (для аудита)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Состояние после сброса</returns>
        Task<SqlAgentConnectionItemDto> ResetConnectionAsync(
            string name,
            int userId,
            CancellationToken cancellationToken = default);
    }
}