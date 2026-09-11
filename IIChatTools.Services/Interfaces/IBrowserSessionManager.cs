using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Browser;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Менеджер долгоживущих сессий браузера.
    /// Регистрируется как singleton: держит словарь активных сессий PuppeteerSharp.
    /// </summary>
    public interface IBrowserSessionManager
    {
        /// <summary>
        /// Открывает новую браузерную сессию для пользователя.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="initialUrl">Опциональный начальный URL</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Информация о созданной сессии</returns>
        Task<BrowserSessionInfo> OpenAsync(int userId, string initialUrl, System.Threading.CancellationToken cancellationToken);

        /// <summary>
        /// Возвращает сессию по идентификатору, проверяя владельца.
        /// </summary>
        /// <param name="sessionId">Идентификатор сессии</param>
        /// <param name="userId">Идентификатор пользователя (для проверки прав)</param>
        /// <returns>Сессия или null</returns>
        Task<BrowserSessionInfo> GetInfoAsync(string sessionId, int userId);

        /// <summary>
        /// Закрывает сессию.
        /// </summary>
        /// <param name="sessionId">Идентификатор сессии</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>true, если сессия была закрыта</returns>
        Task<bool> CloseAsync(string sessionId, int userId);

        /// <summary>
        /// Возвращает список сессий пользователя.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>Список сессий</returns>
        Task<IReadOnlyList<BrowserSessionInfo>> ListForUserAsync(int userId);

        /// <summary>
        /// Закрывает все сессии пользователя (например, при выходе).
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>Асинхронная задача</returns>
        Task CloseAllForUserAsync(int userId);

        /// <summary>
        /// Выполняет команду управления сессией (goto, click, type и т.д.).
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="sessionId">Идентификатор сессии</param>
        /// <param name="command">Команда</param>
        /// <param name="arguments">Аргументы команды</param>
        /// <param name="timeoutMs">Таймаут операции</param>
        /// <returns>Результат выполнения</returns>
        Task<DTO.ToolResult> ExecuteCommandAsync(
            DTO.ToolExecutionContext context,
            string sessionId,
            string command,
            Newtonsoft.Json.Linq.JObject arguments,
            int timeoutMs);
    }
}