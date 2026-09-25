using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис индексации документов для RAG
    /// (v1.5.0, KI-083, Шаг 4C).
    ///
    /// <para>
    /// Оркестрирует полный цикл: <c>parse → chunk → embed → store</c>.
    /// <list type="bullet">
    ///   <item><b>parse</b> — <see cref="IRagDocumentParserRegistry"/> (по расширению).</item>
    ///   <item><b>chunk</b> — <see cref="IChunkingStrategyResolver"/> (default = recursive).</item>
    ///   <item><b>embed</b> — <see cref="IEmbeddingService"/> (LM Studio <c>/v1/embeddings</c>).</item>
    ///   <item><b>store</b> — БД (<c>DocumentChunk</c> entity) + <see cref="IVectorStore"/> (InMemory MVP).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Реализация — <b>Scoped</b>: работает с <c>AppDbContext</c> и
    /// <c>HttpContext</c>-независимыми синглтонами.
    /// </para>
    /// </summary>
    public interface IDocumentIngestionService
    {
        /// <summary>
        /// Индексирует документ (файл / текст / URL).
        ///
        /// <para>
        /// Возвращает результат с количеством чанков, токенов, длительностью
        /// и hash. Если содержимое уже проиндексировано (совпадение hash)
        /// и <c>ForceReindex = false</c> — возвращает <c>Skipped = true</c>.
        /// </para>
        /// </summary>
        /// <param name="request">Запрос индексации</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Результат индексации</returns>
        /// <exception cref="System.ArgumentNullException">Если <paramref name="request"/> = null</exception>
        /// <exception cref="System.ArgumentException">
        /// Если не заполнено обязательное поле (<c>IndexName</c> или соответствующее
        /// <c>SourceType</c> поле), либо формат не поддерживается парсером.
        /// </exception>
        Task<IngestionResultDto> IngestAsync(
            IngestionRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет все чанки документа из БД и векторного хранилища.
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <param name="documentPath">Путь документа (или URL)</param>
        /// <param name="chatId">ID чата (для <c>my_rag_docs</c>), <c>null</c> для остальных</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Количество удалённых чанков</returns>
        Task<int> DeleteDocumentAsync(
            string indexName,
            string documentPath,
            int? chatId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Полностью очищает индекс (все чанки + все векторы).
        ///
        /// <para>
        /// Используется для кнопки «Очистить RAG» в UI (Фаза 6) и
        /// для Admin UI (Фаза 7).
        /// </para>
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <param name="chatId">Ограничить очистку конкретным чатом (<c>my_rag_docs</c>), <c>null</c> — весь индекс</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Количество удалённых чанков</returns>
        Task<int> ClearIndexAsync(
            string indexName,
            int? chatId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Полностью очищает индекс для конкретного пользователя
        /// (v1.5.0, KI-083, Шаг 7C.1).
        ///
        /// <para>
        /// Используется для очистки per-user индексов (<c>workspace</c>,
        /// <c>chat_history</c>) при отключении/переиндексации.
        /// </para>
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <param name="userId">ID пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Количество удалённых чанков</returns>
        Task<int> ClearIndexForUserAsync(
            string indexName,
            int userId,
            CancellationToken cancellationToken = default);
    }
}