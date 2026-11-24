using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Rag.Providers
{
    /// <summary>
    /// D05 / KI-243: внешнее векторное хранилище Qdrant (адаптер IVectorStore).
    /// <para>
    /// Контракт IVectorStore синхронный, поэтому HTTP-вызовы выполняются
    /// через <c>GetAwaiter().GetResult()</c> с явным таймаутом 15 с
    /// (RULES § 4.28). Для высоконагруженного поиска предпочтительнее
    /// асинхронный контракт — кандидат на отдельный DESIGN.
    /// </para>
    /// Настройки: <c>VectorStore:Qdrant:BaseUrl</c>, <c>ApiKey</c>,
    /// <c>CollectionPrefix</c>. Если BaseUrl не задан — адаптер не регистрируется.
    /// </summary>
    public sealed class QdrantVectorStore : IVectorStore, IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _prefix;
        private readonly ILogger<QdrantVectorStore> _logger;

        public QdrantVectorStore(IConfiguration configuration, ILogger<QdrantVectorStore> logger)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var baseUrl = configuration["VectorStore:Qdrant:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("QdrantVectorStore: не задан VectorStore:Qdrant:BaseUrl");

            _client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
            var apiKey = configuration["VectorStore:Qdrant:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
                _client.DefaultRequestHeaders.Add("api-key", apiKey);
            _prefix = configuration["VectorStore:Qdrant:CollectionPrefix"] ?? "iichattools_";
        }

        /// <inheritdoc />
        public void Add(string indexName, int chunkId, float[] vector, ChunkMetadata metadata)
        {
            var body = new JObject
            {
                ["points"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = chunkId,
                        ["vector"] = JArray.FromObject(vector),
                        ["payload"] = JObject.FromObject(metadata ?? new ChunkMetadata())
                    }
                }
            };
            Post($"collections/{Collection(indexName)}/points?wait=true", body);
        }

        /// <inheritdoc />
        public IReadOnlyList<VectorSearchResult> Search(string indexName, float[] queryVector, int topK = 5)
        {
            var body = new JObject
            {
                ["vector"] = JArray.FromObject(queryVector),
                ["limit"] = topK,
                ["with_payload"] = true
            };
            var json = Post($"collections/{Collection(indexName)}/points/search", body);
            var results = new List<VectorSearchResult>();
            foreach (var item in json["result"] as JArray ?? new JArray())
            {
                results.Add(new VectorSearchResult
                {
                    ChunkId = item["id"]?.Value<int>() ?? 0,
                    Score = item["score"]?.Value<float>() ?? 0f,
                    Metadata = item["payload"]?.ToObject<ChunkMetadata>()
                });
            }
            return results;
        }

        /// <inheritdoc />
        public void Remove(string indexName, int chunkId)
            => Post($"collections/{Collection(indexName)}/points/delete?wait=true",
                new JObject { ["points"] = new JArray(chunkId) });

        /// <inheritdoc />
        public void Clear(string indexName)
            => Post($"collections/{Collection(indexName)}/points/delete?wait=true",
                new JObject { ["delete_all"] = true });

        /// <inheritdoc />
        public int Count(string indexName)
        {
            var json = Post($"collections/{Collection(indexName)}", null, method: HttpMethod.Get);
            return json["result"]?["points_count"]?.Value<int>() ?? 0;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetIndexNames()
        {
            var json = Post("collections", null, method: HttpMethod.Get);
            var names = new List<string>();
            foreach (var item in json["result"]?["collections"] as JArray ?? new JArray())
            {
                var name = item["name"]?.Value<string>();
                if (name != null && name.StartsWith(_prefix, StringComparison.Ordinal))
                    names.Add(name.Substring(_prefix.Length));
            }
            return names;
        }

        private string Collection(string indexName) => _prefix + indexName;

        private JObject Post(string path, JObject body, HttpMethod method = null)
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
            if (body != null)
                request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            using var response = _client.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (json["status"] != null && json["status"].Value<string>() == "error")
                throw new InvalidOperationException($"Qdrant error: {json["status"]}");
            return json;
        }

        /// <inheritdoc />
        public void Dispose() => _client?.Dispose();
    }
}
