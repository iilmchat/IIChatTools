using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Фабрика <see cref="DbConnection"/> по имени подключения
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1).
    /// <para>
    /// Реализация кэширует connection strings (полученные из User Secrets /
    /// env-переменных через <c>ConnectionStringKey</c>) и создаёт
    /// провайдер-специфичные <see cref="DbConnection"/>:
    /// <c>SqliteConnection</c>, <c>SqlConnection</c>
    /// (в Фазе 2 — <c>NpgsqlConnection</c>, <c>MySqlConnection</c>).
    /// </para>
    /// </summary>
    public interface ISqlConnectionProvider
    {
        /// <summary>
        /// Создаёт и ОТКРЫВАЕТ соединение с БД по имени подключения.
        /// Вызывающий код отвечает за закрытие (через <c>using</c>).
        /// </summary>
        /// <param name="connectionName">Имя подключения (например, <c>internal</c>)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Открытое <see cref="DbConnection"/></returns>
        /// <exception cref="System.ArgumentException">
        /// Если подключение не зарегистрировано или отключено.
        /// </exception>
        /// <exception cref="System.InvalidOperationException">
        /// Если строка подключения не разрешается
        /// (<c>ConnectionStringKey</c> не найден в конфигурации).
        /// </exception>
        Task<DbConnection> CreateConnectionAsync(
            string connectionName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет, зарегистрировано ли подключение с указанным именем.
        /// Возвращает <c>false</c>, если подключение не найдено или отключено.
        /// </summary>
        /// <param name="connectionName">Имя подключения</param>
        /// <returns>true, если подключение существует и включено</returns>
        bool Exists(string connectionName);

        /// <summary>
        /// Возвращает имя провайдера БД для указанного подключения
        /// (<c>Sqlite</c> / <c>SqlServer</c> / ...).
        /// </summary>
        /// <param name="connectionName">Имя подключения</param>
        /// <returns>Имя провайдера (строка из конфигурации)</returns>
        /// <exception cref="System.ArgumentException">
        /// Если подключение не зарегистрировано.
        /// </exception>
        string GetProvider(string connectionName);
    }
}