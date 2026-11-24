using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.LmStudio;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Rag;
using IIChatTools.Services.Implementation.Rag.Providers;
using IIChatTools.Services.Interfaces;
using IIChatTools.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// D17: контрактные тесты провайдеров (Port/Adapter из D04/D05).
    /// </summary>
    public class ProviderContractTests
    {
        [Fact]
        public async Task EmbeddingProvider_OpenAiCompatibleShape_ReturnsVectors()
        {
            var mock = new Moq.Mock<ILmStudioClient>();
            mock
                .Setup(x => x.GetEmbeddingsAsync(
                    Moq.It.IsAny<IReadOnlyList<string>>(),
                    Moq.It.IsAny<string>(),
                    Moq.It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EmbeddingResponse
                {
                    Data = new List<EmbeddingData>
                    {
                        new EmbeddingData { Index = 0, Embedding = new[] { 1f, 0f, 0f } }
                    }
                });
            IEmbeddingProvider provider = new LmStudioEmbeddingProvider(mock.Object);

            var result = await provider.GetEmbeddingsAsync(new[] { "hello" }, null, CancellationToken.None);

            Assert.Equal("lmstudio", provider.Name);
            Assert.Single(result.Data);
            Assert.Equal(3, result.Data[0].Embedding.Length);
        }

        [Fact]
        public void VectorStore_InMemory_ImplementsFullContract()
        {
            IVectorStore store = new InMemoryVectorStore();
            var vector = new[] { 1f, 0f, 0f };

            store.Add("project_docs", 1, vector, new ChunkMetadata());
            Assert.Equal(1, store.Count("project_docs"));
            Assert.Contains("project_docs", store.GetIndexNames());

            var results = store.Search("project_docs", vector, 5);
            Assert.NotEmpty(results);

            store.Remove("project_docs", 1);
            Assert.Equal(0, store.Count("project_docs"));

            store.Clear("project_docs");
            Assert.Equal(0, store.Count("project_docs"));  // индекс остаётся, но пуст
        }
    }
}
