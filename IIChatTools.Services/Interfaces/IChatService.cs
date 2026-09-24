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
        /// Редактирует содержимое user-сообщения и удаляет все сообщения после него
        /// (ассистент, tool, последующие user/assistant). Используется для
        /// ChatGPT-style edit + regenerate (Фаза 2.2.6).
        /// </summary>
        /// <param name="messageId">Идентификатор редактируемого сообщения</param>
        /// <param name="userId">Идентификатор пользователя (проверка владения через chat.UserId)</param>
        /// <param name="newContent">Новое содержимое (пробелы обрезаются)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Результат: <c>Success</c>, <c>Error</c>, <c>DeletedCount</c>, <c>MessageId</c></returns>
        Task<IIChatTools.Services.DTO.Chat.EditUserMessageResult> EditUserMessageAsync(
            int messageId,
            int userId,
            string newContent,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Ищет чаты пользователя по подстроке в названии чата или в содержимом
        /// любого из его сообщений (KI-068). Регистронезависимый поиск —
        /// через <c>LOWER()</c> на обеих сторонах (кросс-провайдерно).
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="search">
        /// Поисковый запрос. Пустой / <c>null</c> → возвращается весь список
        /// (эквивалентно <see cref="GetUserChatsAsync"/>). Обрезается до 200 символов.
        /// </param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Отфильтрованный список чатов (сортировка по UpdatedAt desc)</returns>
        Task<IReadOnlyList<Chat>> SearchUserChatsAsync(
            int userId,
            string search,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// KI-078B: расширенный поиск с превью совпадения — для ⌘K-модалки (Ctrl+K).
        /// Возвращает для каждого найденного чата сниппет (~150 символов) вокруг
        /// первого совпадения и позицию совпадения для подсветки на клиенте.
        ///
        /// Приоритет: сначала проверяется title, потом — content. Один чат —
        /// один результат (первое найденное совпадение).
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="search">
        /// Поисковый запрос. Пустой / <c>null</c> → пустой список.
        /// Обрезается до 200 символов.
        /// </param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список результатов (сортировка по UpdatedAt desc, как в <see cref="GetUserChatsAsync"/>)</returns>
        Task<IReadOnlyList<IIChatTools.Services.DTO.Chat.ChatSearchResultDto>>
            SearchUserChatsWithSnippetAsync(
                int userId,
                string search,
                CancellationToken cancellationToken = default);
    }
}