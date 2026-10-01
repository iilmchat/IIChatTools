using System;
using System.Collections.Generic;
using System.Net.Http;                          // IHttpClientFactory + HttpClient (CS0246 / CS1503)
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace IIChatTools.Tests.IntegrationTests.ExternalLlm
{
    /// <summary>
    /// Integration-тесты <see cref="ExternalLlmClient"/> с реальными провайдерами
    /// (v1.8.1, KI-109, Фаза 5).
    ///
    /// <para>
    /// <b>Все тесты (кроме одного) — Skip по умолчанию.</b> Для запуска:
    /// <list type="number">
    ///   <item>Задать env-переменную с ключом:
    ///     <c>$env:EXTERNALLLM__DEEPSEEK__APIKEY = "sk-..."</c>
    ///     (двойное подчёркивание = вложенный ключ конфигурации .NET);</item>
    ///   <item>Убрать атрибут <c>Skip</c> у нужного теста;</item>
    ///   <item><c>dotnet test --filter "FullyQualifiedName~ExternalLlmIntegrationTests"</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Тесты тратят реальные деньги</b> (DeepSeek / OpenAI). Один запрос
    /// ≈ $0.0002. <b>Не запускать в CI</b> — только вручную, когда нужно.
    /// </para>
    /// </summary>
    public class ExternalLlmIntegrationTests
    {
        private const string TestPrompt = "Ответь одним словом: сколько будет 2+2?";

        private readonly ITestOutputHelper _output;

        /// <summary>
        /// Создаёт тест-класс.
        /// </summary>
        /// <param name="output">xUnit output (для диагностики)</param>
        public ExternalLlmIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ============================================================
        // Без Skip — проверяет fail-fast на неизвестном провайдере.
        // Работает без сети / ключей.
        // ============================================================

        [Fact]
        public async Task CompleteAsync_UnknownProvider_FailsBeforeHttp()
        {
            // Arrange: registry без провайдеров.
            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.DefaultProvider).Returns("nonexistent");
            registryMock.Setup(r => r.Get(It.IsAny<string>()))
                .Returns((ExternalProviderOptions)null);
            registryMock.Setup(r => r.GetNames()).Returns(Array.Empty<string>());

            var cbMock = new Mock<IExternalLlmCircuitBreaker>();
            cbMock.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);

            var budgetMock = new Mock<IExternalLlmBudgetTracker>();
            budgetMock.Setup(b => b.CanSpend(It.IsAny<int>())).Returns(true);

            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Throws(new InvalidOperationException("HTTP не должен вызываться"));

            var config = new ConfigurationBuilder().Build();

            var client = new ExternalLlmClient(
                registryMock.Object,
                cbMock.Object,
                budgetMock.Object,
                httpFactory.Object,
                config,
                NullLogger<ExternalLlmClient>.Instance);

            // Act + Assert: fail до HTTP.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(
                    userId: 1,
                    new ExternalLlmRequest { Provider = "nonexistent", Prompt = "test" },
                    CancellationToken.None));

            Assert.Contains("не зарегистрирован", ex.Message);
        }

        // ============================================================
        // Skip — требуют реальных ключей + интернета.
        // ============================================================

        /// <summary>
        /// Реальный запрос к DeepSeek. Требует env <c>EXTERNALLLM__DEEPSEEK__APIKEY</c>.
        /// </summary>
        [Fact(Skip = "Требует EXTERNALLLM__DEEPSEEK__APIKEY env + убрать Skip вручную")]
        public async Task DeepSeek_RealRequest_ReturnsResponse()
        {
            var apiKey = Environment.GetEnvironmentVariable("EXTERNALLLM__DEEPSEEK__APIKEY");
            Assert.False(string.IsNullOrWhiteSpace(apiKey),
                "Не задан env EXTERNALLLM__DEEPSEEK__APIKEY.");

            var client = CreateRealClient(
                provider: "deepseek",
                baseUrl: "https://api.deepseek.com/v1",
                model: "deepseek-chat",
                apiKey: apiKey);

            var response = await client.CompleteAsync(
                userId: 1,
                new ExternalLlmRequest { Prompt = TestPrompt, MaxTokens = 20 },
                CancellationToken.None);

            _output.WriteLine($"Provider: {response.Provider}");
            _output.WriteLine($"Content:  {response.Content}");
            _output.WriteLine($"Tokens:   {response.PromptTokens}+{response.CompletionTokens}");
            _output.WriteLine($"Cost:     ${response.CostUsd}");

            Assert.Equal("deepseek", response.Provider);
            Assert.False(string.IsNullOrWhiteSpace(response.Content));
            Assert.True(response.PromptTokens > 0);
            Assert.True(response.CompletionTokens > 0);
            Assert.True(response.DurationMs > 0);
        }

        /// <summary>
        /// Реальный запрос к OpenAI. Требует env <c>EXTERNALLLM__OPENAI__APIKEY</c>
        /// и VPN из РФ.
        /// </summary>
        [Fact(Skip = "Требует EXTERNALLLM__OPENAI__APIKEY env + VPN из РФ")]
        public async Task OpenAI_RealRequest_ReturnsResponse()
        {
            var apiKey = Environment.GetEnvironmentVariable("EXTERNALLLM__OPENAI__APIKEY");
            Assert.False(string.IsNullOrWhiteSpace(apiKey),
                "Не задан env EXTERNALLLM__OPENAI__APIKEY.");

            var client = CreateRealClient(
                provider: "openai",
                baseUrl: "https://api.openai.com/v1",
                model: "gpt-4o-mini",
                apiKey: apiKey);

            var response = await client.CompleteAsync(
                userId: 1,
                new ExternalLlmRequest { Prompt = TestPrompt, MaxTokens = 20 },
                CancellationToken.None);

            _output.WriteLine($"Provider: {response.Provider}");
            _output.WriteLine($"Content:  {response.Content}");

            Assert.Equal("openai", response.Provider);
            Assert.False(string.IsNullOrWhiteSpace(response.Content));
        }

        /// <summary>
        /// Реальный запрос к Ollama на localhost:11434. Не требует ключа.
        /// </summary>
        [Fact(Skip = "Требует Ollama на localhost:11434 с моделью qwen3:4b")]
        public async Task Ollama_RealRequest_ReturnsResponse()
        {
            var client = CreateRealClient(
                provider: "ollama",
                baseUrl: "http://localhost:11434/v1",
                model: "qwen3:4b",
                apiKey: null);   // без ключа

            var response = await client.CompleteAsync(
                userId: 1,
                new ExternalLlmRequest { Prompt = TestPrompt, MaxTokens = 20 },
                CancellationToken.None);

            _output.WriteLine($"Provider: {response.Provider}");
            _output.WriteLine($"Content:  {response.Content}");

            Assert.Equal("ollama", response.Provider);
            Assert.False(string.IsNullOrWhiteSpace(response.Content));
        }

        /// <summary>
        /// Реальный запрос к Anthropic Claude (v1.9.0, KI-110a, KI-124).
        ///
        /// <para>
        /// Требует env <c>EXTERNALLLM__ANTHROPIC__APIKEY</c> + <b>VPN из РФ</b>
        /// (Anthropic блокирует регистрацию и API-запросы по IP РФ,
        /// см. DESIGN_ANTHROPIC_GEMINI § 5.3).
        /// </para>
        ///
        /// <para>
        /// Проверяет путь <c>CompleteAnthropicAsync</c> (Фаза 3 KI-110a):
        /// <c>POST /v1/messages</c>, <c>x-api-key</c>,
        /// <c>anthropic-version: 2023-06-01</c>, парсинг <c>content[]</c>
        /// через <c>AnthropicResponseParser</c>.
        /// </para>
        ///
        /// <para>
        /// <b>Тратит реальные деньги</b> — один запрос ≈ $0.0004 (Haiku 4.5,
        /// 20 output токенов).
        /// </para>
        /// </summary>
        [Fact(Skip = "Требует EXTERNALLLM__ANTHROPIC__APIKEY env + VPN из РФ")]
        public async Task Anthropic_RealRequest_ReturnsResponse()
        {
            var apiKey = Environment.GetEnvironmentVariable("EXTERNALLLM__ANTHROPIC__APIKEY");
            Assert.False(string.IsNullOrWhiteSpace(apiKey),
                "Не задан env EXTERNALLLM__ANTHROPIC__APIKEY.");

            var client = CreateRealClient(
                provider: "anthropic",
                baseUrl: "https://api.anthropic.com/v1",
                model: "claude-haiku-4-5",
                apiKey: apiKey,
                format: ProviderFormat.Anthropic);

            var response = await client.CompleteAsync(
                userId: 1,
                new ExternalLlmRequest
                {
                    Provider = "anthropic",
                    Prompt = TestPrompt,
                    MaxTokens = 20
                },
                CancellationToken.None);

            _output.WriteLine($"Provider: {response.Provider}");
            _output.WriteLine($"Content:  {response.Content}");
            _output.WriteLine($"Tokens:   {response.PromptTokens}+{response.CompletionTokens}");
            _output.WriteLine($"Cost:     ${response.CostUsd}");

            Assert.Equal("anthropic", response.Provider);
            Assert.False(string.IsNullOrWhiteSpace(response.Content));
            Assert.True(response.PromptTokens > 0);
            Assert.True(response.CompletionTokens > 0);
            Assert.True(response.DurationMs > 0);
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Создаёт реальный <see cref="ExternalLlmClient"/> для указанного провайдера.
        /// </summary>
        /// <param name="provider">Имя провайдера</param>
        /// <param name="baseUrl">Base URL</param>
        /// <param name="model">Модель</param>
        /// <param name="apiKey">API-ключ (null — без ключа, для Ollama)</param>
        /// <param name="format">
        /// Формат API (v1.9.0, KI-110a, KI-124). По умолчанию
        /// <see cref="ProviderFormat.OpenAI"/> — обратно совместимо с DeepSeek /
        /// OpenAI / Ollama-тестами.
        /// </param>
        private static ExternalLlmClient CreateRealClient(
            string provider,
            string baseUrl,
            string model,
            string apiKey,
            ProviderFormat format = ProviderFormat.OpenAI)
        {
            var providerOptions = new ExternalProviderOptions
            {
                DisplayName = provider,
                Format = format,
                BaseUrl = baseUrl,
                Model = model,
                ApiKeySecretName = $"ExternalLlm:Providers:{provider}:ApiKey",
                CostPer1kInputUsd = 0.00014m,
                CostPer1kOutputUsd = 0.00028m,
                MaxTokens = 100,
                TimeoutSeconds = 30
            };

            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.DefaultProvider).Returns(provider);
            registryMock.Setup(r => r.Get(It.IsAny<string>())).Returns(providerOptions);
            registryMock.Setup(r => r.GetNames()).Returns(new[] { provider });

            var cbMock = new Mock<IExternalLlmCircuitBreaker>();
            cbMock.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            cbMock.Setup(c => c.GetStatus(It.IsAny<string>())).Returns(new ProviderHealthStatus
            {
                Provider = provider,
                Available = true
            });

            var budgetMock = new Mock<IExternalLlmBudgetTracker>();
            budgetMock.Setup(b => b.CanSpend(It.IsAny<int>())).Returns(true);

            // Реальный HttpClient (без мока) — IHttpClientFactory возвращает его.
            // Тестовый хост — реальный интернет.
            var httpClient = new System.Net.Http.HttpClient();
            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            // Конфиг: ключ в in-memory-коллекции.
            var dict = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(apiKey))
                dict[providerOptions.ApiKeySecretName] = apiKey;
            var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

            return new ExternalLlmClient(
                registryMock.Object,
                cbMock.Object,
                budgetMock.Object,
                httpFactory.Object,
                config,
                NullLogger<ExternalLlmClient>.Instance);
        }
    }
}