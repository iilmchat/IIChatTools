using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Implementation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты HTTP-слоя <see cref="LmStudioClient.GetEmbeddingsAsync"/>
    /// (v1.5.0, KI-083, Фаза 1).
    ///
    /// Используют mock <see cref="HttpMessageHandler"/> — проверяют
    /// формирование запроса и парсинг ответа, без реального LM Studio.
    /// </summary>
    public class LmStudioEmbeddingTests
    {
        /// <summary>
        /// Mock HttpMessageHandler: делегирует обработку переданной функции.
        /// Позволяет перехватить запрос и вернуть кастомный ответ.
        /// </summary>
        private sealed class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public MockHttpMessageHandler(
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }

        /// <summary>
        /// Создаёт LmStudioClient, замоканный на заданный ответ.
        /// </summary>
        /// <param name="handler">Функция-обработчик HTTP-запроса</param>
        /// <param name="model">Rag:Embedding:Model (если null — не задаётся)</param>
        /// <param name="timeoutSeconds">Rag:Embedding:TimeoutSeconds</param>
        private static LmStudioClient CreateClient(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
            string model = "text-embedding-nomic-embed-text-v1.5",
            int timeoutSeconds = 60)
        {
            var mockHandler = new MockHttpMessageHandler(handler);
            var httpClient = new HttpClient(mockHandler);

            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            var settings = new Dictionary<string, string>
            {
                ["LmStudio:BaseUrl"] = "http://localhost:8034",
                ["Rag:Embedding:TimeoutSeconds"] = timeoutSeconds.ToString()
            };
            if (model != null)
                settings["Rag:Embedding:Model"] = model;

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            return new LmStudioClient(
                mockFactory.Object,
                config,
                NullLogger<LmStudioClient>.Instance);
        }

        /// <summary>
        /// Строит валидный JSON-ответ LM Studio с N векторами.
        /// </summary>
        private static string BuildResponse(int count, int dim = 3)
        {
            var dataArray = new JArray();
            for (int i = 0; i < count; i++)
            {
                var vec = new JArray();
                for (int j = 0; j < dim; j++)
                    vec.Add((float)(i + j * 0.1));
                dataArray.Add(new JObject
                {
                    ["index"] = i,
                    ["embedding"] = vec
                });
            }
            var root = new JObject
            {
                ["data"] = dataArray,
                ["usage"] = new JObject
                {
                    ["prompt_tokens"] = count * 2,
                    ["total_tokens"] = count * 2
                }
            };
            return root.ToString();
        }

        /// <summary>
        /// Запрос формируется правильно: POST, URL, Content-Type, тело.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_SendsCorrectRequest()
        {
            HttpRequestMessage capturedRequest = null;
            string capturedBody = null;

            var client = CreateClient(async (req, ct) =>
            {
                capturedRequest = req;
                capturedBody = await req.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildResponse(1), Encoding.UTF8, "application/json")
                };
            });

            await client.GetEmbeddingsAsync(new[] { "test" });

            Assert.NotNull(capturedRequest);
            Assert.Equal(HttpMethod.Post, capturedRequest.Method);
            Assert.Equal("http://localhost:8034/v1/embeddings", capturedRequest.RequestUri.ToString());
            Assert.Equal("application/json", capturedRequest.Content.Headers.ContentType.MediaType);

            var body = JObject.Parse(capturedBody);
            Assert.Equal("text-embedding-nomic-embed-text-v1.5", body["model"]?.ToString());
            Assert.Equal(1, (body["input"] as JArray)?.Count ?? 0);
            Assert.Equal("test", body["input"]?[0]?.ToString());
        }

        /// <summary>
        /// Ответ парсится в EmbeddingResponse: data + usage.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_ParsesResponse()
        {
            var client = CreateClient((req, ct) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildResponse(2, dim: 5), Encoding.UTF8, "application/json")
                }));

            var response = await client.GetEmbeddingsAsync(new[] { "a", "b" });

            Assert.Equal(2, response.Data.Count);
            Assert.Equal(5, response.Data[0].Embedding.Length);
            Assert.NotNull(response.Usage);
            Assert.Equal(4, response.Usage.PromptTokens);
            Assert.Equal(4, response.Usage.TotalTokens);
        }

        /// <summary>
        /// Ошибка 500 → InvalidOperationException с телом в сообщении.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_Error_Throws()
        {
            var client = CreateClient((req, ct) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"model not found\"}", Encoding.UTF8, "application/json")
                }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetEmbeddingsAsync(new[] { "hello" }));

            Assert.Contains("500", ex.Message);
        }

        /// <summary>
        /// Таймаут → TimeoutException.
        ///
        /// ВАЖНО (RULES § 4.35): mock HttpMessageHandler ОБЯЗАН уважать
        /// CancellationToken — иначе HttpClient.Timeout не сработает.
        /// HttpClient.Timeout реализован через CancellationTokenSource.CancelAfter,
        /// и handler должен реагировать на отмену (Task.Delay(..., ct)).
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_Timeout_Throws()
        {
            var client = CreateClient(async (req, ct) =>
            {
                // Уважаем ct — при истечении Timeout (1 с) отмена прервёт Delay
                // через 1 секунду, а не через 3. Иначе HttpClient будет ждать
                // до фактического завершения handler'а и Timeout не сработает.
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildResponse(1), Encoding.UTF8, "application/json")
                };
            }, timeoutSeconds: 1);

            await Assert.ThrowsAsync<TimeoutException>(
                () => client.GetEmbeddingsAsync(new[] { "hello" }));
        }

        /// <summary>
        /// Если model не передан — используется Rag:Embedding:Model из конфига.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_UsesModelFromConfig()
        {
            string capturedBody = null;

            var client = CreateClient(async (req, ct) =>
            {
                capturedBody = await req.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildResponse(1), Encoding.UTF8, "application/json")
                };
            }, model: "custom-embedding-model");

            await client.GetEmbeddingsAsync(new[] { "hello" }, model: null);

            var body = JObject.Parse(capturedBody);
            Assert.Equal("custom-embedding-model", body["model"]?.ToString());
        }

        /// <summary>
        /// Если model передан — он имеет приоритет над конфигом.
        /// </summary>
        [Fact]
        public async Task GetEmbeddingsAsync_ModelOverride()
        {
            string capturedBody = null;

            var client = CreateClient(async (req, ct) =>
            {
                capturedBody = await req.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildResponse(1), Encoding.UTF8, "application/json")
                };
            }, model: "from-config");

            await client.GetEmbeddingsAsync(new[] { "hello" }, model: "override-model");

            var body = JObject.Parse(capturedBody);
            Assert.Equal("override-model", body["model"]?.ToString());
        }
    }
}