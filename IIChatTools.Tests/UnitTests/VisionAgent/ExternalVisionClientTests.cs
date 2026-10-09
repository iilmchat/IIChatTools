using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Тесты <see cref="ExternalVisionClient"/>
    /// (v1.13.9, KI-141 — multimodal через Yandex VL).
    /// </summary>
    public class ExternalVisionClientTests
    {
        private static VisionAgentOptions MakeOptions(params string[] chain)
        {
            var opts = new VisionAgentOptions();
            opts.VisionLlm.FallbackChain = new List<string>(chain);
            return opts;
        }

        private static ExternalProviderOptions MakeProvider(bool supportsVision)
            => new ExternalProviderOptions
            {
                DisplayName = "Yandex VL",
                SupportsVision = supportsVision,
                BaseUrl = "https://llm.api.cloud.yandex.net/v1",
                Model = "gpt://folder/qwen3.6-35b-a3b/latest"
            };

        private static ExternalVisionClient Create(
            VisionAgentOptions options,
            ExternalProviderOptions provider,
            Func<ExternalLlmRequest, ExternalLlmResponse> onComplete = null)
        {
            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.Get(It.IsAny<string>())).Returns(provider);

            var clientMock = new Mock<IExternalLlmClient>();
            clientMock
                .Setup(c => c.CompleteAsync(It.IsAny<int>(),
                    It.IsAny<ExternalLlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int _, ExternalLlmRequest req, CancellationToken __) =>
                {
                    return onComplete?.Invoke(req) ?? new ExternalLlmResponse
                    {
                        Provider = req.Provider,
                        Content = "{\"description\": \"Test\", \"ui_elements\": []}",
                        PromptTokens = 100,
                        CompletionTokens = 20,
                        CostUsd = 0.001m,
                        DurationMs = 500
                    };
                });

            return new ExternalVisionClient(
                Options.Create(options),
                clientMock.Object,
                registryMock.Object,
                NullLogger<ExternalVisionClient>.Instance);
        }

        [Fact]
        public void IsReady_NoExternalInChain_ReturnsFalse()
        {
            var client = Create(MakeOptions("lmstudio"), MakeProvider(true));

            Assert.False(client.IsReady);
        }

        [Fact]
        public void IsReady_WithExternalInChain_ReturnsTrue()
        {
            var client = Create(
                MakeOptions("lmstudio", "external:yandex-vl"),
                MakeProvider(true));

            Assert.True(client.IsReady);
        }

        [Fact]
        public async Task DescribeAsync_NoExternalInChain_Throws()
        {
            var client = Create(MakeOptions("lmstudio"), MakeProvider(true));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.DescribeAsync(new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public async Task DescribeAsync_ProviderWithoutVision_Throws()
        {
            var client = Create(
                MakeOptions("external:yandex-vl"),
                MakeProvider(supportsVision: false));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.DescribeAsync(new byte[] { 1, 2, 3 }));

            Assert.Contains("SupportsVision", ex.Message);
        }

        [Fact]
        public async Task DescribeAsync_HappyPath_ParsesResponse()
        {
            var client = Create(
                MakeOptions("external:yandex-vl"),
                MakeProvider(true));

            var result = await client.DescribeAsync(new byte[] { 1, 2, 3, 4 });

            Assert.NotNull(result);
            Assert.Equal("Test", result.Description);
        }

        [Fact]
        public async Task DescribeAsync_PassesImagesToExternalClient()
        {
            ExternalLlmRequest capturedRequest = null;

            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.Get(It.IsAny<string>()))
                .Returns(MakeProvider(true));

            var clientMock = new Mock<IExternalLlmClient>();
            clientMock
                .Setup(c => c.CompleteAsync(It.IsAny<int>(),
                    It.IsAny<ExternalLlmRequest>(), It.IsAny<CancellationToken>()))
                .Callback((int _, ExternalLlmRequest req, CancellationToken __) =>
                    capturedRequest = req)
                .ReturnsAsync(new ExternalLlmResponse
                {
                    Content = "{\"description\": \"X\", \"ui_elements\": []}",
                    PromptTokens = 10, CompletionTokens = 5, CostUsd = 0m
                });

            var client = new ExternalVisionClient(
                Options.Create(MakeOptions("external:yandex-vl")),
                clientMock.Object,
                registryMock.Object,
                NullLogger<ExternalVisionClient>.Instance);

            await client.DescribeAsync(new byte[] { 0xAA, 0xBB, 0xCC });

            Assert.NotNull(capturedRequest);
            Assert.Equal("yandex-vl", capturedRequest.Provider);
            Assert.NotNull(capturedRequest.Images);
            Assert.Single(capturedRequest.Images);
            Assert.Equal("image/png", capturedRequest.Images[0].MimeType);
            Assert.Equal("qrvM", capturedRequest.Images[0].Base64Data); // AA BB CC → qrvM
            Assert.False(string.IsNullOrWhiteSpace(capturedRequest.System));
        }
    }
}