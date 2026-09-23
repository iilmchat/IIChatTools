using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис управления чатами и сообщениями.
    /// CRUD-операции + сохранение истории диалогов с LLM.
    /// </summary>
    public interface IChatService
    {
        /// <summary>
        /// Возвращает список чатов пользователя (сортировка по UpdatedAt desc).
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список чатов (без сообщений)</returns>
        Task<IReadOnlyList<Chat>> GetUserChatsAsync(int userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает чат по идентификатору с проверкой принадлежности пользователю.
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Чат или null, если не найден / не принадлежит пользователю</returns>
        Task<Chat> GetChatAsync(int chatId, int userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает историю сообщений чата.
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="limit">Максимум сообщений (по умолчанию 50)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список сообщений (сортировка по CreatedAt asc)</returns>
        Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
            int chatId,
            int userId,
            int limit = 50,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Создаёт новый чат.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="model">Идентификатор модели LM Studio</param>
        /// <param name="title">Заголовок (по умолчанию «Новый чат»)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Созданный чат</returns>
        Task<Chat> CreateChatAsync(
            int userId,
            string model,
            string title = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Обновляет метаданные чата (заголовок, модель, системный промпт).
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="title">Новый заголовок (null = не менять)</param>
        /// <param name="model">Новая модель (null = не менять)</param>
        /// <param name="systemPrompt">Новый системный промпт (null = не менять)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Обновлённый чат</returns>
        Task<Chat> UpdateChatAsync(
            int chatId,
            int userId,
            string title = null,
            string model = null,
            string systemPrompt = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет чат со всеми сообщениями.
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>true, если чат был удалён</returns>
        Task<bool> DeleteChatAsync(int chatId, int userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Добавляет сообщение в чат.
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя (для проверки владения)</param>
        /// <param name="message">Сообщение</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Сохранённое сообщение с присвоенным Id</returns>
        Task<ChatMessage> AddMessageAsync(
            int chatId,
            int userId,
            ChatMessage message,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет все чаты пользователя старше <paramref name="retentionDays"/> дней.
        /// Используется фоновым сервисом retention.
        /// </summary>
        /// <param name="retentionDays">Срок хранения (в днях)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Количество удалённых чатов</returns>
        Task<int> DeleteOldChatsAsync(int retentionDays, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает количество сообщений для каждого чата пользователя.
        /// Один SQL-запрос с GROUP BY — используется для sidebar (KI-046).
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Словарь chatId → количество сообщений (чаты без сообщений отсутствуют)</returns>
        Task<IReadOnlyDictionary<int, int>> GetMessageCountsAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет последний обмен в чате: последний assistant-ответ и все tool-сообщения,
        /// которые к нему относятся (до предыдущего user-сообщения включительно).
        /// Используется для Regenerate — после удаления чат возвращается в состояние,
        /// в котором последним сообщением является user, и можно заново сгенерировать ответ.
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя (для проверки владения)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Количество удалённых сообщений (0, если нечего удалять)</returns>
        Task<int> DeleteLastAssistantExchangeAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default);
    }
}