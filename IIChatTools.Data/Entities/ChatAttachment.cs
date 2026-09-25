namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Вложение к чату: файл, загруженный пользователем и проиндексированный
    /// в <c>my_rag_docs</c> (v1.5.0, KI-083, Шаг 6A).
    ///
    /// <para>
    /// Физический файл хранится в workspace пользователя:
    /// <c>{UserWorkspace}/chat-attachments/{chatId}/{guid}.ext</c>.
    /// Чанки (текст + эмбеддинги) — в <see cref="DocumentChunk"/>
    /// (IndexName = <c>my_rag_docs</c>) + <c>IVectorStore</c>.
    /// </para>
    ///
    /// <para>
    /// FK на <see cref="Chat"/> — <c>DeleteBehavior.Cascade</c>:
    /// при удалении чата запись в БД удаляется автоматически.
    /// Физические файлы + чанки чистятся через
    /// <c>IChatAttachmentService.ClearForChatAsync</c>.
    /// </para>
    /// </summary>
    public class ChatAttachment : BaseEntity
    {
        /// <summary>
        /// Идентификатор чата (FK на <see cref="Chat"/>).
        /// </summary>
        public int ChatId { get; set; }

        /// <summary>
        /// Навигационное свойство чата.
        /// </summary>
        public virtual Chat Chat { get; set; }

        /// <summary>
        /// Идентификатор пользователя-владельца (защита от чужих вложений).
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Имя файла, как его видит пользователь (для UI-отображения в чипе).
        /// Не используется при работе с ФС — только для display.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// MIME-тип (<c>application/pdf</c>, <c>text/plain</c>, ...).
        /// Берётся из multipart-запроса, не проверяется.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Размер файла в байтах.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Относительный путь внутри user workspace:
        /// <c>chat-attachments/{chatId}/{guid}.ext</c>.
        /// Используется как часть пути для <c>DocumentChunk.DocumentPath</c>.
        /// </summary>
        public string StoragePath { get; set; }

        /// <summary>
        /// SHA256-хэш содержимого (hex, lower case). Для дедупликации:
        /// повторная загрузка того же файла в тот же чат возвращает существующую запись.
        /// </summary>
        public string ContentHash { get; set; }

        /// <summary>
        /// Количество проиндексированных чанков.
        /// <c>0</c> — если ingestion не удался (файл сохранён, но не проиндексирован).
        /// </summary>
        public int ChunksCount { get; set; }
    }
}