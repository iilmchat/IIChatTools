using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис поиска по векторным индексам для RAG
    /// (v1.5.0, KI-083, Шаг 5A).
    ///
    /// <para>
    /// Оркестрирует: <c>embed query → search vector store → filter by metadata → filter by MinScore → enrich from DB</c>.
    /// </para>
    ///
    /// <para>
    /// Реализация — <b>Scoped</b>: читает <c>DocumentChunk</c> из БД
    /// (для обогащения результата полным текстом + <c>MetadataJson</c>).
    /// </para>
    /// </summary>
    public interface IRetrievalService
    {
        /// <summary>
        /// Ищет top-K релевантных чанков по запросу.
        /// </summary>
        /// <param name="query">Текст запроса (не пустой)</param>
        /// <param name="indexName">Имя индекса (<c>project_docs</c>, <c>my_rag_docs</c>, ...)</param>
        /// <param name="topK">Сколько результатов вернуть (по умолчанию 5; из конфига <c>Rag:Retrieval:DefaultTopK</c>)</param>
        /// <param name="chatId">ID чата — фильтр по метаданным (для <c>my_rag_docs</c>), <c>null</c> — без фильтра</param>
        /// <param name="userId">ID пользователя — фильтр по метаданным (для <c>workspace</c> / <c>chat_history</c>), <c>null</c> — без фильтра</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список найденных чанков, отсортированный по score (убывание)</returns>
        /// <exception cref="System.ArgumentException">Если <paramref name="query"/> или <paramref name="indexName"/> пустые</exception>
        Task<IReadOnlyList<RetrievedChunkDto>> SearchAsync(
            string query,
            string indexName,
            int topK = 5,
            int? chatId = null,
            int? userId = null,
            CancellationToken cancellationToken = default);
    }
}