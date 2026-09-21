using System.Collections.Generic;
using System.Threading;
using IIChatTools.Services.DTO.Chat;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис стриминга ответов LLM (SSE).
    /// Координирует: сохранение сообщений в БД → вызов LM Studio → поток SSE-событий.
    /// </summary>
    public interface IChatStreamService
    {
        /// <summary>
        /// Стримит ответ LLM на сообщение пользователя.
        /// Возвращает поток SSE-событий: <c>start</c> → <c>delta</c>* → <c>done</c> | <c>error</c>.
        /// </summary>
        /// <param name="request">Запрос (ChatId + Message)</param>
        /// <param name="userId">Идентификатор пользователя (для проверки владения)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Асинхронный поток событий стрима</returns>
        IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatStreamRequest request,
            int userId,
            CancellationToken cancellationToken = default);
    }
}