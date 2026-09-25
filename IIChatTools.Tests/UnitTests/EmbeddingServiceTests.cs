using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.LmStudio;
using IIChatTools.Services.Implementation.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты сервиса эмбеддингов (v1.5.0, KI-083, Фаза 1).
    /// Используют fake <see cref="ILmStudioClient"/> — без реального HTTP.
    /// </summary>
    public class EmbeddingServiceTests
    {
        /// <summary>
        /// Fake ILmStudioClient: задаёт ответ <see cref="EmbeddingResponse"/>
        /// или исключение для <see cref="GetEmbeddingsAsync"/>. Остальные методы
        /// не используются в тестах — бросают NotImplementedException.
        /// </summary>
        private sealed class FakeLmStudioClient : ILmStudioClient
        {
            /// <summary>Что вернуть (если <see cref="ExceptionToThrow"/> = null).</summary>
            public Func<IReadOnlyList<string>, EmbeddingResponse> ResponseFactory { get; set; }

            /// <summary>Исключение, которое надо бросить вместо ответа.</summary>
            public Exception ExceptionToThrow { get; set; }

            /// <summary>Сколько раз вызывался GetEmbeddingsAsync.</summary>
            public int CallCount { get; private set; }

            /// <summary>Зафиксированные батчи (для проверки разбивки).</summary>
            public List<string[]> Batches { get; } = new List<string[]>();

            public Task<EmbeddingResponse> GetEmbeddingsAsync(
                IReadOnlyList<string> inputs, string model, CancellationToken cancellationToken)
            {
                CallCount++;
                Batches.Add(inputs.ToArray());

                if (ExceptionToThrow != null)
                    throw ExceptionToThrow;

                var response = ResponseFactory?.Invoke(inputs)
                    ?? BuildDefaultResponse(inputs);
                return Task.FromResult(response);
            }

            /// <summary>
            /// Строит дефолтный ответ: N векторов размерности 3 (для простоты проверок).
            /// </summary>
            private static EmbeddingResponse BuildDefaultResponse(IReadOnlyList<string> inputs)
            {
                var data = new List<EmbeddingData>();
                for (int i = 0; i < inputs.Count; i++)
                {
                    data.Add(new EmbeddingData
                    {
                        Index = i,
                        Embedding = new[] { (float)i, (float)i + 0.5f, (float)i + 1.0f }
                    });
                }
                return new EmbeddingResponse
                {
                    Data = data,
                    Usage = new EmbeddingUsage
                    {
                        PromptTokens = inputs.Count * 2,
                        TotalTokens = inputs.Count * 2
                    }
                };
            }

            // Остальные методы интерфейса не используются — заглушки.
            public Task<ChatCompletionResponse> CompleteAsync(
                JArray messages, JArray tools, CancellationToken cancellationToken, string model = null)
                => throw new NotImplementedException();

            public async IAsyncEnumerable<ChatCompletionChunk> ChatStreamAsync(
                JArray messages, JArray tools,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.CompletedTask;
                yield break;
            }

            public Task<IReadOnlyList<string>> GetModelIdsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<string>>(new List<string>());
        }

        /// <summary>
        /// Создаёт EmbeddingService с fake-клиентом и in-memory конфигом.
        /// </summary>
        /// <param name="fake">Fake ILmStudioClient</param>
        /// <param name="dimensions">Rag:Embedding:Dimensions</param>
        /// <param name="batchSize">Rag:Embedding:BatchSize</param>
        private static EmbeddingService CreateService(
            FakeLmStudioClient fake,
            int dimensions = 3,
            int batchSize = 64)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rag:Embedding:Dimensions"] = dimensions.ToString(),
                    ["Rag:Embedding:BatchSize"] = batchSize.ToString(),
                    ["Rag:Embedding:Model"] = "test-embedding-model"
                })
                .Build();

            return new EmbeddingService(fake, config, NullLogger<EmbeddingService>.Instance);
        }

        /// <summary>
        /// Пустой текст → ArgumentException.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingAsync_EmptyText_Throws()
        {
            var fake = new FakeLmStudioClient();
            var service = CreateService(fake);

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.GetEmbeddingAsync(""));

            Assert.Equal(0, fake.CallCount);
        }

        /// <summary>
        /// Один текст → возвращается вектор размерности Dimensions.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingAsync_ReturnsVector_WithDimensions()
        {
            var fake = new FakeLmStudioClient();
            var service = CreateService(fake, dimensions: 3);

            var vector = await service.GetEmbeddingAsync("hello");

            Assert.Equal(3, vector.Length);
            Assert.Equal(1, fake.CallCount);
        }

        /// <summary>
        /// Батч (3 текста) → возвращается 3 вектора в порядке исходных текстов.
        /// Fake генерирует векторы с фиксированным «index» — проверяем порядок.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_Batch_ReturnsInOrder()
        {
            var fake = new FakeLmStudioClient();
            var service = CreateService(fake, dimensions: 3);

            var texts = new[] { "a", "b", "c" };
            var vectors = await service.GetEmbeddingsAsync(texts);

            Assert.Equal(3, vectors.Count);
            // Fake отдаёт вектор [i, i+0.5, i+1] — проверяем, что порядок сохранён.
            Assert.Equal(0f, vectors[0][0]);
            Assert.Equal(1f, vectors[1][0]);
            Assert.Equal(2f, vectors[2][0]);
        }

        /// <summary>
        /// Большой список (5 текстов) с BatchSize=2 → 3 вызова к fake:
        /// 2 + 2 + 1. Результат — 5 векторов в порядке.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_LargeBatch_SplitsIntoChunks()
        {
            var fake = new FakeLmStudioClient();
            var service = CreateService(fake, dimensions: 3, batchSize: 2);

            var texts = new[] { "t0", "t1", "t2", "t3", "t4" };
            var vectors = await service.GetEmbeddingsAsync(texts);

            Assert.Equal(3, fake.CallCount);
            Assert.Equal(2, fake.Batches[0].Length);
            Assert.Equal(2, fake.Batches[1].Length);
            Assert.Single(fake.Batches[2]);   // xUnit2013: 1 элемент → Assert.Single

            Assert.Equal(5, vectors.Count);
        }

        /// <summary>
        /// Пустой список → ArgumentException, LM Studio не вызывается.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_EmptyList_Throws()
        {
            var fake = new FakeLmStudioClient();
            var service = CreateService(fake);

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.GetEmbeddingsAsync(new List<string>()));

            Assert.Equal(0, fake.CallCount);
        }

        /// <summary>
        /// Ошибка от ILmStudioClient пробрасывается наружу.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_LmStudioError_Throws()
        {
            var fake = new FakeLmStudioClient
            {
                ExceptionToThrow = new InvalidOperationException("LM Studio 500")
            };
            var service = CreateService(fake);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetEmbeddingsAsync(new[] { "hello" }));

            Assert.Contains("500", ex.Message);
        }
    }
}