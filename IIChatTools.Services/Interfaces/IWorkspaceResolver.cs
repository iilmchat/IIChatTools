using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис получения пути к рабочему пространству пользователя.
    /// </summary>
    public interface IWorkspaceResolver
    {
        /// <summary>
        /// Возвращает абсолютный путь к рабочему пространству пользователя,
        /// создавая директорию при необходимости.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>Абсолютный путь</returns>
        Task<string> GetWorkspacePathAsync(int userId);
    }
}