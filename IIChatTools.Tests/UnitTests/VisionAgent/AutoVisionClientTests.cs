using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    /// Unit-тесты для <see cref="AutoVisionClient"/>
    /// (v1.12.0, KI-131, Ф5.4). Fake-фабрики вместо IHttpClientFactory.
    /// </summary>
    public class AutoVisionClientTests
    {
        private static readonly byte[] FakePng = new byte[] { 1, 2, 3 };

        private static AutoVisionClient CreateClient(
            IVisionLlmClient lmStudio,
            IVisionLlmClient external,
            List<string> fallbackChain = null)
        {
            var options = new VisionAgentOptions
            {
                VisionLlm = new VisionLlmOptions
                {
                    Provider = "auto",
                    Model = "test-model",
                    FallbackChain = fallbackChain ?? new List<string>
                    {
                        "lmstudio", "external:groq", "external:openai"
                    }
                }
            };

            return new AutoVisionClient(
                Options.Create(options),
                () => lmStudio,
                () => external,
                NullLogger<AutoVisionClient>.Instance);
        }

        private static IVisionLlmClient MockVision(bool isReady, ScreenDescriptionDto result)
        {
            var mock = new Mock<IVisionLlmClient>();
            mock.SetupGet(c => c.IsReady).Returns(isReady);
            mock.Setup(c => c.DescribeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            return mock.Object;
        }

        // ============ 1. Первый провайдер отвечает — успех ============

        [Fact]
        public async Task DescribeAsync_FirstReady_ReturnsResult()
        {
            var lmStudio = MockVision(true, new ScreenDescriptionDto
            {
                Description = "LM Studio OK",
                UiElements = new List<UiElementDto>()
            });
            var external = MockVision(true, new ScreenDescriptionDto { Description = "unused" });

            var client = CreateClient(lmStudio, external);
            var result = await client.DescribeAsync(FakePng, CancellationToken.None);

            Assert.Equal("LM Studio OK", result.Description);
        }

        // ============ 2. Первый не-ready → переключение на второй ============

        [Fact]
        public async Task DescribeAsync_FirstNotReady_UsesSecond()
        {
            var lmStudio = MockVision(isReady: false, result: null);
            var external = MockVision(true, new ScreenDescriptionDto
            {
                Description = "External OK",
                UiElements = new List<UiElementDto>()
            });

            var client = CreateClient(lmStudio, external);
            var result = await client.DescribeAsync(FakePng, CancellationToken.None);

            Assert.Equal("External OK", result.Description);
        }

        // ============ 3. Первый вернул пусто → fallback ============

        [Fact]
        public async Task DescribeAsync_FirstEmpty_SecondFills()
        {
            var lmStudio = MockVision(true, new ScreenDescriptionDto
            {
                Description = "",
                UiElements = new List<UiElementDto>()
            });
            var external = MockVision(true, new ScreenDescriptionDto
            {
                Description = "External provided data",
                UiElements = new List<UiElementDto>()
            });

            var client = CreateClient(lmStudio, external);
            var result = await client.DescribeAsync(FakePng, CancellationToken.None);

            Assert.Equal("External provided data", result.Description);
        }

        // ============ 4. Первый упал → fallback ============

        [Fact]
        public async Task DescribeAsync_FirstThrows_FallsBack()
        {
            var lmStudioMock = new Mock<IVisionLlmClient>();
            lmStudioMock.SetupGet(c => c.IsReady).Returns(true);
            lmStudioMock
                .Setup(c => c.DescribeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("LM Studio timeout"));

            var external = MockVision(true, new ScreenDescriptionDto
            {
                Description = "External after timeout",
                UiElements = new List<UiElementDto>()
            });

            var client = CreateClient(lmStudioMock.Object, external);
            var result = await client.DescribeAsync(FakePng, CancellationToken.None);

            Assert.Equal("External after timeout", result.Description);
        }

        // ============ 5. External NotSupported → возвращает то, что дал LM ============

        [Fact]
        public async Task DescribeAsync_ExternalNotSupported_FallsBackToLmStudio()
        {
            var lmStudio = MockVision(true, new ScreenDescriptionDto
            {
                Description = "LM Studio only",
                UiElements = new List<UiElementDto>()
            });

            var externalMock = new Mock<IVisionLlmClient>();
            externalMock.SetupGet(c => c.IsReady).Returns(true);
            externalMock
                .Setup(c => c.DescribeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotSupportedException("External VL не поддерживается"));

            // Цепочка: external первый, lmstudio второй.
            var client = CreateClient(lmStudio, externalMock.Object,
                new List<string> { "external:groq", "lmstudio" });

            var result = await client.DescribeAsync(FakePng, CancellationToken.None);

            Assert.Equal("LM Studio only", result.Description);
        }

        // ============ 6. Все не-ready → InvalidOperationException ============

        [Fact]
        public async Task DescribeAsync_AllNotReady_Throws()
        {
            var lmStudio = MockVision(isReady: false, result: null);
            var external = MockVision(isReady: false, result: null);

            var client = CreateClient(lmStudio, external);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.DescribeAsync(FakePng, CancellationToken.None));

            Assert.Contains("ни один", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ============ 7. Все упали → InvalidOperationException ============

        [Fact]
        public async Task DescribeAsync_AllThrow_Throws()
        {
            var lmStudioMock = new Mock<IVisionLlmClient>();
            lmStudioMock.SetupGet(c => c.IsReady).Returns(true);
            lmStudioMock
                .Setup(c => c.DescribeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("lm fail"));

            var externalMock = new Mock<IVisionLlmClient>();
            externalMock.SetupGet(c => c.IsReady).Returns(true);
            externalMock
                .Setup(c => c.DescribeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("ext fail"));

            var client = CreateClient(lmStudioMock.Object, externalMock.Object);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.DescribeAsync(FakePng, CancellationToken.None));

            Assert.Contains("все", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ext fail", ex.Message);
        }

        // ============ 8. Пустая FallbackChain → throw ============

        [Fact]
        public async Task DescribeAsync_EmptyChain_Throws()
        {
            var client = CreateClient(
                MockVision(false, null),
                MockVision(false, null),
                fallbackChain: new List<string>());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.DescribeAsync(FakePng, CancellationToken.None));

            Assert.Contains("FallbackChain пуст", ex.Message);
        }

        // ============ 9. IsReady = true, если хоть один готов ============

        [Fact]
        public void IsReady_AnyReady_ReturnsTrue()
        {
            var client = CreateClient(
                MockVision(false, null),
                MockVision(true, new ScreenDescriptionDto()));
            Assert.True(client.IsReady);
        }

        [Fact]
        public void IsReady_AllNotReady_ReturnsFalse()
        {
            var client = CreateClient(
                MockVision(false, null),
                MockVision(false, null));
            Assert.False(client.IsReady);
        }
    }
}