using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис генерации короткого названия чата из первого сообщения
    /// пользователя (аналогично ChatGPT). Не изменяет чат, если генерация
    /// не удалась — возвращает <c>null</c>, и прежний title сохраняется.
    /// </summary>
    public interface IChatTitleService
    {
        /// <summary>
        /// Генерирует название для чата и сохраняет его в БД.
        /// Fallback: если LLM недоступен или ответ пустой — возвращает <c>null</c>
        /// (чат не модифицируется).
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя (проверка владения)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Сгенерированное название или <c>null</c> при ошибке</returns>
        Task<string> GenerateAndSetTitleAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default);
    }
}