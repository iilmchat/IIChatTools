using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.SqlAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Оркестратор Database Agent (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1).
    /// Предоставляет read-only доступ к данным приложения
    /// (в Фазе 1 — только подключение <c>internal</c>) через 4 операции:
    /// <list type="bullet">
    ///   <item><description><see cref="ListConnectionsAsync"/> — список подключений;</description></item>
    ///   <item><description><see cref="ListTablesAsync"/> — whitelist-таблицы подключения;</description></item>
    ///   <item><description><see cref="DescribeTableAsync"/> — колонки + типы + пример значения;</description></item>
    ///   <item><description><see cref="ExecuteQueryAsync"/> — выполнение read-only SQL.</description></item>
    /// </list>
    /// <para>
    /// Метод <see cref="ExecuteQueryAsync"/> прогоняет SQL через
    /// <see cref="ISqlQueryValidator"/> перед выполнением.
    /// </para>
    /// </summary>
    public interface ISqlAgentService
    {
        /// <summary>
        /// Возвращает список зарегистрированных подключений
        /// (без строк подключения — только метаданные).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список подключений (включая отключённые — с флагом <c>Enabled=false</c>)</returns>
        Task<IReadOnlyList<DatabaseConnectionInfoDto>> ListConnectionsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает whitelist-таблицы указанного подключения.
        /// </summary>
        /// <param name="connection">Имя подключения (например, <c>internal</c>)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список таблиц (с приблизительным количеством строк)</returns>
        Task<IReadOnlyList<SqlTableInfoDto>> ListTablesAsync(
            string connection,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает описание колонок указанной таблицы.
        /// </summary>
        /// <param name="connection">Имя подключения</param>
        /// <param name="table">Имя таблицы (должна быть в whitelist)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список колонок с типами и примером значения</returns>
        Task<IReadOnlyList<SqlColumnInfoDto>> DescribeTableAsync(
            string connection,
            string table,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Выполняет read-only SQL-запрос. SQL предварительно валидируется
        /// (<see cref="ISqlQueryValidator"/>) и дополняется auto-LIMIT, если
        /// в запросе нет явного лимита.
        /// </summary>
        /// <param name="request">Параметры запроса (подключение, SQL, опциональный лимит строк)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Результат: колонки, строки, флаг <c>truncated</c>, длительность</returns>
        Task<SqlQueryResultDto> ExecuteQueryAsync(
            SqlQueryRequest request,
            CancellationToken cancellationToken = default);
    }
}