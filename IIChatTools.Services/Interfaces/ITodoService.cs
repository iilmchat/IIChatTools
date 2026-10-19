using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис плана задач (Todo) для чата (v2.1.0, Фаза 5).
    /// </summary>
    public interface ITodoService
    {
        /// <summary>
        /// Создаёт пункт плана. Проверяет, что чат принадлежит пользователю.
        /// </summary>
        /// <param name="chatId">Идентификатор чата.</param>
        /// <param name="userId">Пользователь (владелец).</param>
        /// <param name="title">Заголовок (1..200).</param>
        /// <param name="description">Описание (≤ 2000, может быть null).</param>
        /// <returns>Созданный пункт.</returns>
        Task<ChatTodo> CreateAsync(int chatId, int userId, string title, string description);

        /// <summary>
        /// Возвращает пункты чата в порядке <c>SortOrder</c>.
        /// </summary>
        Task<IReadOnlyList<ChatTodo>> GetForChatAsync(int chatId, int userId);

        /// <summary>
        /// Меняет статус пункта (Pending / InProgress / Completed).
        /// </summary>
        Task UpdateStatusAsync(int todoId, int userId, string status);

        /// <summary>
        /// Удаляет пункт плана.
        /// </summary>
        Task DeleteAsync(int todoId, int userId);
    }
}
