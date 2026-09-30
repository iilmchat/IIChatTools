using System.Threading;
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
    /// Тесты <see cref="CheckInternetConnectionTool"/> (v1.8.1, KI-109, Фаза 3).
    /// </summary>
    public class CheckInternetConnectionToolTests
    {
        private static ExternalProviderOptions Provider() => new ExternalProviderOptions
        {
            DisplayName = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            Model = "deepseek-chat"
        };

        private static (CheckInternetConnectionTool tool, Mock<IExternalLlmClient> clientMock)
            Create(bool available)
        {
            var clientMock = new Mock<IExternalLlmClient>();
            clientMock
                .Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(available);

            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.DefaultProvider).Returns("deepseek");
            registryMock.Setup(r => r.Get("deepseek")).Returns(Provider());
            registryMock.Setup(r => r.Get(It.IsNotIn("deepseek")))
                .Returns((ExternalProviderOptions)null);

            var tool = new CheckInternetConnectionTool(
                clientMock.Object,
                registryMock.Object,
                NullLogger<CheckInternetConnectionTool>.Instance);

            return (tool, clientMock);
        }

        private static ToolExecutionContext Context() => new ToolExecutionContext { UserId = 1 };

        [Fact]
        public async Task ExecuteAsync_AvailableTrue()
        {
            var (tool, _) = Create(available: true);

            var result = await tool.ExecuteAsync(Context(), new JObject());

            Assert.True(result.Success);
            var json = JObject.FromObject(result.Data);
            Assert.Equal("deepseek", json["provider"]?.ToString());
            Assert.True(json["available"]?.Value<bool>());
        }

        [Fact]
        public async Task ExecuteAsync_Unavailable_FalseButSuccess()
        {
            var (tool, _) = Create(available: false);

            var result = await tool.ExecuteAsync(Context(), new JObject());

            // Внешний провайдер недоступен — это не ошибка tool'а (успех с false).
            Assert.True(result.Success);
            var json = JObject.FromObject(result.Data);
            Assert.False(json["available"]?.Value<bool>());
            Assert.Contains("недоступен", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownProvider_Fail()
        {
            var (tool, _) = Create(available: true);

            var result = await tool.ExecuteAsync(Context(), new JObject
            {
                ["provider"] = "unknown"
            });

            Assert.False(result.Success);
            Assert.Contains("unknown", result.Message);
        }
    }
}