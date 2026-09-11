using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис управления действиями, требующими подтверждения пользователя.
    /// </summary>
    public interface IApprovalService
    {
        /// <summary>
        /// Создаёт запрос на подтверждение действия.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="toolName">Имя инструмента</param>
        /// <param name="parametersJson">Параметры вызова (JSON)</param>
        /// <returns>Созданное действие</returns>
        Task<PendingAction> CreatePendingActionAsync(int userId, string toolName, string parametersJson);

        /// <summary>
        /// Возвращает действие по идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор действия</param>
        /// <returns>Действие или null</returns>
        Task<PendingAction> GetByIdAsync(int id);

        /// <summary>
        /// Возвращает список ожидающих действий пользователя.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>Список действий</returns>
        Task<IReadOnlyList<PendingAction>> GetPendingForUserAsync(int userId);

        /// <summary>
        /// Возвращает все ожидающие действия (для администратора).
        /// </summary>
        /// <returns>Список действий</returns>
        Task<IReadOnlyList<PendingAction>> GetAllPendingAsync();

        /// <summary>
        /// Подтверждает действие.
        /// </summary>
        /// <param name="actionId">Идентификатор действия</param>
        /// <param name="approverUserId">Идентификатор пользователя, подтвердившего действие</param>
        /// <returns>true, если действие успешно подтверждено</returns>
        Task<bool> ApproveAsync(int actionId, int approverUserId);

        /// <summary>
        /// Отклоняет действие.
        /// </summary>
        /// <param name="actionId">Идентификатор действия</param>
        /// <param name="approverUserId">Идентификатор пользователя, отклонившего действие</param>
        /// <param name="reason">Причина отклонения</param>
        /// <returns>true, если действие успешно отклонено</returns>
        Task<bool> RejectAsync(int actionId, int approverUserId, string reason);

        /// <summary>
        /// Проверяет, разрешён ли инструмент в белом списке (без подтверждения).
        /// </summary>
        /// <param name="toolName">Имя инструмента</param>
        /// <returns>true, если инструмент в белом списке</returns>
        Task<bool> IsWhitelistedAsync(string toolName);
    }
}