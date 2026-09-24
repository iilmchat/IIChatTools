using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис per-user настроек (v1.4.x, KI-067).
    ///
    /// Хранит настройки пользователя в таблице <c>UserSettings</c>
    /// (ключ-значение), с типизированными get-методами.
    /// Используется для override глобальных настроек:
    /// - <c>Chat.RetentionDays</c> — свой срок хранения чатов;
    /// - <c>Chat.DoNotDelete</c> — «не удалять чаты вообще».
    /// </summary>
    public interface IUserSettingsService
    {
        /// <summary>
        /// Возвращает все настройки пользователя.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список настроек</returns>
        Task<IReadOnlyList<UserSetting>> GetAllForUserAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает все настройки всех пользователей, у которых ключ начинается
        /// с указанного префикса (v1.4.x, KI-067).
        ///
        /// Используется фоновым <c>ChatRetentionService</c> для получения
        /// per-user overrides: <c>Chat.RetentionDays</c> и <c>Chat.DoNotDelete</c>.
        /// </summary>
        /// <param name="keyPrefix">Префикс ключа (например, <c>Chat.</c>)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список настроек (UserId + Key + Value + Type)</returns>
        Task<IReadOnlyList<UserSetting>> GetAllWithKeyPrefixAsync(
            string keyPrefix,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает строковое значение настройки или <c>null</c>, если не задано.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="key">Ключ настройки</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Значение или <c>null</c></returns>
        Task<string> GetStringAsync(
            int userId,
            string key,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает int-значение настройки или <paramref name="defaultValue"/>,
        /// если настройка не задана или не парсится.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="key">Ключ настройки</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Значение или <paramref name="defaultValue"/></returns>
        Task<int> GetIntAsync(
            int userId,
            string key,
            int defaultValue = 0,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает bool-значение настройки или <paramref name="defaultValue"/>,
        /// если настройка не задана или не парсится.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="key">Ключ настройки</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Значение или <paramref name="defaultValue"/></returns>
        Task<bool> GetBoolAsync(
            int userId,
            string key,
            bool defaultValue = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Устанавливает (upsert) значение настройки пользователя.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="key">Ключ настройки</param>
        /// <param name="value">Значение (строка)</param>
        /// <param name="type">Тип: <c>string</c> / <c>int</c> / <c>bool</c> / <c>json</c></param>
        /// <param name="cancellationToken">Токен отмены</param>
        Task SetAsync(
            int userId,
            string key,
            string value,
            string type = "string",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет настройку пользователя (no-op, если не существует).
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="key">Ключ настройки</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>true, если настройка была удалена</returns>
        Task<bool> DeleteAsync(
            int userId,
            string key,
            CancellationToken cancellationToken = default);
    }
}