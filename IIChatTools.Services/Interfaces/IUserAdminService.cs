using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Admin;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис администрирования пользователей (CRUD).
    /// </summary>
    public interface IUserAdminService
    {
        /// <summary>
        /// Возвращает список пользователей с ролями.
        /// </summary>
        /// <returns>Список пользователей</returns>
        Task<IReadOnlyList<UserListDto>> GetAllAsync();

        /// <summary>
        /// Возвращает данные пользователя по идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>DTO или null</returns>
        Task<UserEditDto> GetByIdAsync(int id);

        /// <summary>
        /// Создаёт пользователя.
        /// </summary>
        /// <param name="dto">Данные</param>
        /// <returns>Созданный пользователь</returns>
        /// <exception cref="System.InvalidOperationException">При ошибке Identity</exception>
        Task<UserListDto> CreateAsync(UserEditDto dto);

        /// <summary>
        /// Обновляет пользователя.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <param name="dto">Новые данные</param>
        /// <returns>Обновлённый пользователь или null</returns>
        Task<UserListDto> UpdateAsync(int id, UserEditDto dto);

        /// <summary>
        /// Удаляет пользователя.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>true, если удалён</returns>
        Task<bool> DeleteAsync(int id);

        /// <summary>
        /// Сбрасывает пароль пользователя.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <param name="newPassword">Новый пароль</param>
        /// <returns>true, если пароль изменён</returns>
        Task<bool> ResetPasswordAsync(int id, string newPassword);

        /// <summary>
        /// Возвращает список доступных ролей.
        /// </summary>
        /// <returns>Список имён ролей</returns>
        Task<IReadOnlyList<string>> GetRolesAsync();
    }
}