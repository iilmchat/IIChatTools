using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.LmStudio;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Rag.Providers
{
    /// <summary>
    /// D04: внешний OpenAI-совместимый провайдер эмбеддингов.
    /// Читает Rag:Embedding:ExternalBaseUrl / ExternalApiKey / ExternalModel
    /// через индексер (RULES § 4.56). Таймаут 15 с (RULES § 4.28).
    /// </summary>
    public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
    {
        private readonly HttpClient _client;
        private readonly string _model;

        public OpenAiEmbeddingProvider(IConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var baseUrl = configuration["Rag:Embedding:ExternalBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException(
                    "OpenAiEmbeddingProvider: не задан Rag:Embedding:ExternalBaseUrl");

            _client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(15)
            };

            var apiKey = configuration["Rag:Embedding:ExternalApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
                _client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);

            _model = configuration["Rag:Embedding:ExternalModel"];
        }

        /// <inheritdoc />
        public string Name => "openai-compatible";

        /// <inheritdoc />
        public async Task<EmbeddingResponse> GetEmbeddingsAsync(
            IReadOnlyList<string> texts,
            string model,
            CancellationToken cancellationToken)
        {
            var payload = new JObject
            {
                ["model"] = string.IsNullOrWhiteSpace(model)
                    ? (string.IsNullOrWhiteSpace(_model) ? "text-embedding-3-small" : _model)
                    : model,
                ["input"] = JArray.FromObject(texts)
            };

            using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            using var response = await _client.PostAsync("v1/embeddings", content, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = JObject.Parse(await response.Content
                .ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var result = new EmbeddingResponse();
            foreach (var item in json["data"] as JArray ?? new JArray())
            {
                result.Data.Add(new EmbeddingData
                {
                    Index = item["index"]?.Value<int>() ?? 0,
                    Embedding = item["embedding"]?.ToObject<float[]>() ?? Array.Empty<float>()
                });
            }
            return result;
        }
    }
}
