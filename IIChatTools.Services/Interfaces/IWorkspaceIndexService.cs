using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис управления per-user Workspace-индексом
    /// (v1.5.0, KI-083, Шаг 7C.1).
    ///
    /// <para>
    /// Workspace index — opt-in: пользователь явно включает его в
    /// <c>/profile</c>. Индексация запускается в фоне, статус доступен через
    /// <see cref="GetStatusAsync"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Реализация — Singleton</b> (нужен для фоновой работы, которая
    /// переживает HTTP-запрос). Scoped-зависимости достаются через
    /// <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"/>.
    /// </para>
    /// </summary>
    public interface IWorkspaceIndexService
    {
        /// <summary>
        /// Возвращает текущий статус Workspace-индекса для пользователя:
        /// флаг enabled, метрики из БД (<c>DocumentChunks</c>) и прогресс
        /// текущего фонового прогона (in-memory).
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Статус</returns>
        Task<WorkspaceIndexStatusDto> GetStatusAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Включает Workspace-индекс для пользователя и запускает фоновую
        /// индексацию. Если индекс уже включён — просто перезапускает
        /// (см. <see cref="ReindexAsync"/>).
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Обновлённый статус (сразу после запуска фоновой задачи)</returns>
        Task<WorkspaceIndexStatusDto> EnableAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Отключает Workspace-индекс и очищает все чанки пользователя
        /// (<c>workspace</c> + <c>UserId</c>).
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Обновлённый статус</returns>
        Task<WorkspaceIndexStatusDto> DisableAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Переиндексирует Workspace (очищает чанки + запускает фоновую
        /// индексацию). Если индекс выключен — бросает
        /// <see cref="System.InvalidOperationException"/>.
        /// Если индексация уже идёт — возвращает текущий статус без изменений.
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Обновлённый статус</returns>
        /// <exception cref="System.InvalidOperationException">
        /// Если Workspace index выключен
        /// </exception>
        Task<WorkspaceIndexStatusDto> ReindexAsync(
            int userId,
            CancellationToken cancellationToken = default);
    }
}