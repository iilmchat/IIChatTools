using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Chat;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис вложений к чату (v1.5.0, KI-083, Шаг 6A).
    ///
    /// <para>
    /// Загружает файл в workspace пользователя, индексирует его
    /// в <c>my_rag_docs</c> через <see cref="IDocumentIngestionService"/>,
    /// возвращает DTO для UI.
    /// </para>
    ///
    /// <para>
    /// Реализация — <b>Scoped</b>: работает с <c>AppDbContext</c>
    /// и <see cref="IDocumentIngestionService"/>.
    /// </para>
    /// </summary>
    public interface IChatAttachmentService
    {
        /// <summary>
        /// Загружает файл в чат и индексирует его в <c>my_rag_docs</c>.
        ///
        /// <para>
        /// Проверки: размер файла, поддерживаемое расширение, лимиты
        /// <c>Rag:Attachments:{MaxFileSizeBytes, MaxFilesPerChat, MaxTotalSizePerChat}</c>.
        /// Дедупликация: если тот же файл (совпадение SHA256) уже приложен
        /// к этому же чату — возвращается существующая запись.
        /// </para>
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя-владельца</param>
        /// <param name="content">Поток файла (multipart)</param>
        /// <param name="fileName">Имя файла (для UI + расширение)</param>
        /// <param name="contentType">MIME-тип</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>DTO вложения</returns>
        /// <exception cref="System.ArgumentException">
        /// Если чат не найден / не принадлежит пользователю, формат не поддерживается,
        /// файл слишком большой, превышен лимит файлов / суммарного размера
        /// </exception>
        Task<ChatAttachmentDto> UploadAsync(
            int chatId,
            int userId,
            Stream content,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает список вложений чата (сортировка по CreatedAt asc).
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список DTO вложений</returns>
        Task<IReadOnlyList<ChatAttachmentDto>> GetForChatAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет вложение: файл с диска + чанки из <c>my_rag_docs</c> + запись из БД.
        /// </summary>
        /// <param name="attachmentId">Идентификатор вложения</param>
        /// <param name="userId">Идентификатор пользователя (проверка владения)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>true, если вложение было удалено</returns>
        Task<bool> DeleteAsync(
            int attachmentId,
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет все вложения чата (файлы + чанки + записи БД).
        /// Используется для кнопки «Очистить RAG».
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Количество удалённых вложений</returns>
        Task<int> ClearForChatAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default);
    }
}