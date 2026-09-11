using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Интерфейс генерации JWT-токенов.
    /// </summary>
    public interface IJwtService
    {
        /// <summary>
        /// Формирует JWT-токен для указанного пользователя.
        /// </summary>
        /// <param name="user">Пользователь</param>
        /// <param name="roles">Список ролей пользователя</param>
        /// <returns>Строка JWT-токена</returns>
        Task<string> GenerateTokenAsync(ApplicationUser user, IList<string> roles);
    }
}