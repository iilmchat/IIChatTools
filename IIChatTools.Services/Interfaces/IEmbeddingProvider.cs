using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.LmStudio;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// D04 / KI-227: провайдер эмбеддингов (Port). Реализации:
    /// LmStudio (OpenAI-совместимый /v1/embeddings) и OpenAI-compatible
    /// внешний API. Выбор — конфигом <c>Rag:Embedding:Provider</c>.
    /// </summary>
    public interface IEmbeddingProvider
    {
        /// <summary>Имя провайдера (для логов и метрик).</summary>
        string Name { get; }

        /// <summary>
        /// Возвращает эмбеддинги для набора текстов в формате OpenAI.
        /// </summary>
        /// <param name="texts">Тексты</param>
        /// <param name="model">Модель (null — модель провайдера по умолчанию)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Ответ с векторами по индексу</returns>
        Task<EmbeddingResponse> GetEmbeddingsAsync(
            IReadOnlyList<string> texts,
            string model,
            CancellationToken cancellationToken);
    }
}
