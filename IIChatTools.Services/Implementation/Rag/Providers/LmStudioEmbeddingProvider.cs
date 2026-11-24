using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.LmStudio;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag.Providers
{
    /// <summary>
    /// D04: провайдер эмбеддингов LM Studio — обёртка над ILmStudioClient.
    /// </summary>
    public sealed class LmStudioEmbeddingProvider : IEmbeddingProvider
    {
        private readonly ILmStudioClient _client;

        public LmStudioEmbeddingProvider(ILmStudioClient client)
        {
            _client = client;
        }

        /// <inheritdoc />
        public string Name => "lmstudio";

        /// <inheritdoc />
        public Task<EmbeddingResponse> GetEmbeddingsAsync(
            IReadOnlyList<string> texts,
            string model,
            CancellationToken cancellationToken)
            => _client.GetEmbeddingsAsync(texts, model, cancellationToken);
    }
}
