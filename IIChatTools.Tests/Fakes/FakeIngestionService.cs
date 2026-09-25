using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake <see cref="IDocumentIngestionService"/> для тестов
    /// (v1.5.0, KI-083, Шаг 6A-тесты).
    ///
    /// <para>
    /// Не выполняет реальную индексацию (не парсит файлы, не считает эмбеддинги).
    /// Записывает все вызовы для ассертов; возвращает настраиваемый результат.
    /// </para>
    /// </summary>
    public sealed class FakeIngestionService : IDocumentIngestionService
    {
        /// <summary>Зафиксированные вызовы <see cref="IngestAsync"/>.</summary>
        public List<IngestionRequest> IngestCalls { get; } = new List<IngestionRequest>();

        /// <summary>Зафиксированные вызовы <see cref="DeleteDocumentAsync"/>.</summary>
        public List<(string Index, string Path, int? ChatId)> DeleteCalls { get; }
            = new List<(string, string, int?)>();

        /// <summary>Зафиксированные вызовы <see cref="ClearIndexAsync"/>.</summary>
        public List<(string Index, int? ChatId)> ClearCalls { get; }
            = new List<(string, int?)>();

        /// <summary>Сколько чанков «создать» в <see cref="IngestAsync"/> (по умолчанию 3).</summary>
        public int NextChunksCreated { get; set; } = 3;

        /// <summary>Если задано — <see cref="IngestAsync"/> бросит это исключение.</summary>
        public Exception NextIngestException { get; set; }

        /// <inheritdoc />
        public Task<IngestionResultDto> IngestAsync(
            IngestionRequest request, CancellationToken cancellationToken = default)
        {
            IngestCalls.Add(request);

            if (NextIngestException != null)
                throw NextIngestException;

            return Task.FromResult(new IngestionResultDto
            {
                IndexName = request.IndexName,
                DocumentChunksCreated = NextChunksCreated,
                TokensTotal = NextChunksCreated * 10,
                DurationMs = 5,
                DocumentHash = "test-hash",
                DocumentPath = request.FilePath ?? request.Url ?? "inline",
                Skipped = false
            });
        }

        /// <inheritdoc />
        public Task<int> DeleteDocumentAsync(
            string indexName, string documentPath, int? chatId = null,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls.Add((indexName, documentPath, chatId));
            return Task.FromResult(1);
        }

        /// <inheritdoc />
        public Task<int> ClearIndexAsync(
            string indexName, int? chatId = null,
            CancellationToken cancellationToken = default)
        {
            ClearCalls.Add((indexName, chatId));
            return Task.FromResult(1);
        }

        /// <summary>
        /// Заглушка для <see cref="IDocumentIngestionService.ClearIndexForUserAsync"/>
        /// (v1.5.0, KI-083, Шаг 7C.1). В тестах этот метод пока не вызывается
        /// напрямую — тесты <c>WorkspaceIndexService</c> будут в Шаге 7D.
        /// </summary>
        public Task<int> ClearIndexForUserAsync(
            string indexName,
            int userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}