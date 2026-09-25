namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Чанк документа для RAG (v1.5.0, KI-083, Шаг 2A).
    ///
    /// <para>
    /// Хранит текст и метаданные одного фрагмента документа.
    /// Embedding (вектор) хранится отдельно в <c>IVectorStore</c>
    /// (InMemory MVP → Qdrant в v1.5.x) — не в этой таблице.
    /// Связка: <see cref="IVectorStore"/> хранит пары (ChunkId → вектор),
    /// а сама таблица <c>DocumentChunks</c> — текст + метаданные.
    /// </para>
    ///
    /// <para>
    /// Индексы (см. DESIGN v1.5 § 4.3.2):
    /// <list type="bullet">
    ///   <item><c>(IndexName, DocumentHash)</c> — проверка «уже индексировался» (skip re-index).</item>
    ///   <item><c>(IndexName, ChatId, UserId)</c> — фильтрация чанков по индексу/чату/пользователю при поиске.</item>
    ///   <item><c>(DocumentPath)</c> — удаление всех чанков документа при переиндексации.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class DocumentChunk : BaseEntity
    {
        /// <summary>
        /// Имя индекса (namespace). Одно из:
        /// <list type="bullet">
        ///   <item><c>project_docs</c> — глобальные документы проекта (README, RULES, ...)</item>
        ///   <item><c>my_rag_docs</c> — документы, приложенные к конкретному чату</item>
        ///   <item><c>chat_history</c> — история сообщений пользователя</item>
        ///   <item><c>workspace</c> — файлы workspace пользователя (opt-in)</item>
        /// </list>
        /// </summary>
        public string IndexName { get; set; }

        /// <summary>
        /// Идентификатор чата, к которому относится чанк.
        /// <c>null</c> — для глобальных индексов (<c>project_docs</c>) и
        /// per-user (<c>chat_history</c>, <c>workspace</c>).
        /// </summary>
        public int? ChatId { get; set; }

        /// <summary>
        /// Идентификатор пользователя-владельца чанка.
        /// <c>0</c> — для глобальных индексов (<c>project_docs</c>).
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Относительный путь документа (или URL для веб-источников).
        /// Используется для группировки чанков одного документа
        /// и удаления при переиндексации.
        /// </summary>
        public string DocumentPath { get; set; }

        /// <summary>
        /// SHA256-хэш содержимого документа.
        /// Позволяет пропустить переиндексацию, если содержимое не изменилось.
        /// </summary>
        public string DocumentHash { get; set; }

        /// <summary>
        /// Порядковый номер чанка в документе (0-based).
        /// </summary>
        public int ChunkIndex { get; set; }

        /// <summary>
        /// Текст чанка (nvarchar(max)).
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Количество токенов в чанке (tiktoken, ±5-10% для Qwen/Gemma).
        /// </summary>
        public int Tokens { get; set; }

        /// <summary>
        /// Дополнительные метаданные (JSON): <c>{ page, section, offset }</c>
        /// или произвольные поля от парсера документа.
        /// </summary>
        public string MetadataJson { get; set; }

        /// <summary>
        /// Навигационное свойство чата (для чанков индекса <c>my_rag_docs</c>).
        /// <c>null</c> для глобальных и per-user индексов.
        ///
        /// <para>
        /// FK настроен с <c>DeleteBehavior.Cascade</c>: удаление чата
        /// автоматически удаляет все его чанки (см. <c>AppDbContext.OnModelCreating</c>).
        /// </para>
        /// </summary>
        public virtual Chat Chat { get; set; }

        // Примечание: FK на ApplicationUser НЕТ — поле UserId = 0
        // используется как маркер «глобальный чанк» (project_docs).
        // См. DESIGN v1.5 § 5.2.
    }
}