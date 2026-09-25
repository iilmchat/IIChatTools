using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис администрирования RAG Knowledge Base
    /// (v1.5.0, KI-083, Шаг 7A).
    ///
    /// <para>
    /// Все методы используются только из <c>AdminKnowledgeController</c>
    /// (доступ — роль Admin).
    /// </para>
    /// </summary>
    public interface IAdminKnowledgeService
    {
        /// <summary>
        /// Возвращает список всех 4 индексов с метриками
        /// (ChunkCount, DocumentCount, LastIndexedAt).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>4 индекса (всегда, даже пустые)</returns>
        Task<IReadOnlyList<RagIndexDto>> GetIndexesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Переиндексирует <c>project_docs</c> из путей
        /// <c>Rag:Ingestion:ProjectDocsPaths</c>.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Сводный результат (количество чанков, длительность, ошибки)</returns>
        Task<IngestionResultDto> ReindexProjectDocsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает постраничный список чанков индекса.
        /// </summary>
        /// <param name="indexName">Имя индекса</param>
        /// <param name="page">Номер страницы (1-based)</param>
        /// <param name="pageSize">Размер страницы (1–100)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Чанки + метаданные пагинации</returns>
        Task<(IReadOnlyList<RagChunkDto> Items, int Page, int PageSize, int TotalCount, int TotalPages)>
            GetChunksAsync(
                string indexName,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет один чанк по ID (из БД + VectorStore).
        /// </summary>
        /// <param name="chunkId">ID чанка</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>true, если чанк был удалён</returns>
        Task<bool> DeleteChunkAsync(int chunkId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает текущие настройки RAG (override'ы из AppSettings
        /// с fallback на appsettings.json).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Настройки RAG</returns>
        Task<RagSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Сохраняет override'ы настроек RAG в <c>AppSettings</c>.
        /// </summary>
        /// <param name="dto">Новые настройки</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <exception cref="System.ArgumentException">Если валидация не прошла</exception>
        Task UpdateSettingsAsync(RagSettingsDto dto, CancellationToken cancellationToken = default);
    }
}