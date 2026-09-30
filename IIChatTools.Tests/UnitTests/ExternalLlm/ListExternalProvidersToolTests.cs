using System;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.Tools.ExternalLlm;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm
{
    /// <summary>
    /// Тесты <see cref="ListExternalProvidersTool"/> (v1.8.1, KI-109, Фаза 3).
    /// </summary>
    public class ListExternalProvidersToolTests
    {
        private static ExternalProviderOptions Provider(string name, bool available)
            => new ExternalProviderOptions
            {
                DisplayName = name.ToUpperInvariant(),
                BaseUrl = "https://api.example.com/v1",
                Model = $"{name}-chat",
                CostPer1kInputUsd = 0.00014m,
                CostPer1kOutputUsd = 0.00028m
            };

        private static (ListExternalProvidersTool tool,
                        Mock<IExternalProviderRegistry> registryMock,
                        Mock<IExternalLlmCircuitBreaker> cbMock)
            Create()
        {
            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.DefaultProvider).Returns("deepseek");
            registryMock.Setup(r => r.GetNames()).Returns(new[] { "deepseek", "openai" });
            registryMock.Setup(r => r.Get("deepseek")).Returns(Provider("deepseek", true));
            registryMock.Setup(r => r.Get("openai")).Returns(Provider("openai", false));

            var cbMock = new Mock<IExternalLlmCircuitBreaker>();
            cbMock.Setup(c => c.GetStatus("deepseek")).Returns(new ProviderHealthStatus
            {
                Provider = "deepseek",
                Available = true
            });
            cbMock.Setup(c => c.GetStatus("openai")).Returns(new ProviderHealthStatus
            {
                Provider = "openai",
                Available = false,
                LastError = "Circuit breaker open (retry in 3m)"
            });

            var tool = new ListExternalProvidersTool(
                registryMock.Object,
                cbMock.Object,
                NullLogger<ListExternalProvidersTool>.Instance);

            return (tool, registryMock, cbMock);
        }

        private static ToolExecutionContext Context() => new ToolExecutionContext { UserId = 1 };

        [Fact]
        public async Task ExecuteAsync_ReturnsAllProviders()
        {
            var (tool, _, _) = Create();

            var result = await tool.ExecuteAsync(Context(), new JObject());

            Assert.True(result.Success);
            var json = JObject.FromObject(result.Data);
            Assert.Equal("deepseek", json["default"]?.ToString());

            var providers = json["providers"] as JArray;
            Assert.NotNull(providers);
            Assert.Equal(2, providers.Count);

            Assert.Equal("deepseek", providers[0]["name"]?.ToString());
            Assert.True(providers[0]["available"]?.Value<bool>());
            Assert.Equal(0.00014m, providers[0]["costPer1kInputUsd"]?.Value<decimal>());

            Assert.Equal("openai", providers[1]["name"]?.ToString());
            Assert.False(providers[1]["available"]?.Value<bool>());
            Assert.Contains("Circuit breaker", providers[1]["lastError"]?.ToString());
        }

        [Fact]
        public async Task ExecuteAsync_EmptyRegistry_ReturnsEmptyList()
        {
            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.GetNames()).Returns(Array.Empty<string>());
            registryMock.Setup(r => r.DefaultProvider).Returns("deepseek");

            var cbMock = new Mock<IExternalLlmCircuitBreaker>();

            var tool = new ListExternalProvidersTool(
                registryMock.Object,
                cbMock.Object,
                NullLogger<ListExternalProvidersTool>.Instance);

            var result = await tool.ExecuteAsync(Context(), new JObject());

            Assert.True(result.Success);
            var json = JObject.FromObject(result.Data);
            var providers = json["providers"] as JArray;
            Assert.NotNull(providers);
            Assert.Empty(providers);
        }

        [Fact]
        public async Task ExecuteAsync_NoParameters()
        {
            var (tool, _, _) = Create();
            Assert.Empty(tool.Parameters);
        }
    }
}