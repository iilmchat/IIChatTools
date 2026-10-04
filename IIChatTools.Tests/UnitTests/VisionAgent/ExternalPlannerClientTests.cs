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
    /// Unit-тесты для <see cref="ExternalPlannerClient"/>
    /// (v1.12.0, KI-131, Ф5.3).
    /// </summary>
    public class ExternalPlannerClientTests
    {
        private static ExternalPlannerClient CreateClient(
            IExternalLlmClient externalClient,
            string externalProviderFromChain = "external:deepseek",
            string defaultProvider = "openai")
        {
            var options = new VisionAgentOptions
            {
                PlannerLlm = new PlannerLlmOptions
                {
                    Provider = "external",
                    Model = "qwen3-coder-30b-a3b-instruct",
                    MaxHistorySteps = 20,
                    MaxTokens = 2048,
                    Temperature = 0.1f,
                    FallbackChain = new List<string> { "lmstudio", externalProviderFromChain }
                }
            };

            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.DefaultProvider).Returns(defaultProvider);
            registryMock.Setup(r => r.Get(It.IsAny<string>()))
                .Returns((string name) => new ExternalProviderOptions
                {
                    BaseUrl = "https://api.example.com/v1",
                    Model = "test-model"
                });

            return new ExternalPlannerClient(
                Options.Create(options),
                externalClient,
                registryMock.Object,
                NullLogger<ExternalPlannerClient>.Instance);
        }

        private static ScreenDescriptionDto EmptyScreen() => new ScreenDescriptionDto();

        // ============ 1. Успешный вызов ============

        [Fact]
        public async Task PlanNextAsync_ValidResponse_ReturnsAction()
        {
            var externalMock = new Mock<IExternalLlmClient>();
            externalMock
                .Setup(c => c.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExternalLlmResponse
                {
                    Provider = "deepseek",
                    Content = "{\"action\":\"click\",\"target\":\"btn\"}"
                });

            var client = CreateClient(externalMock.Object);
            var action = await client.PlanNextAsync(
                "test task", new List<VisionStepDto>(), EmptyScreen(),
                new List<string>(), 42, CancellationToken.None);

            Assert.Equal("click", action.Action);
            Assert.Equal("btn", action.Target);
        }

        // ============ 2. Провайдер резолвится из FallbackChain ============

        [Fact]
        public async Task PlanNextAsync_ProviderFromFallbackChain()
        {
            ExternalLlmRequest captured = null;
            var externalMock = new Mock<IExternalLlmClient>();
            externalMock
                .Setup(c => c.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(), It.IsAny<CancellationToken>()))
                .Callback<int, ExternalLlmRequest, CancellationToken>((_, req, __) => captured = req)
                .ReturnsAsync(new ExternalLlmResponse { Content = "{\"action\":\"done\"}" });

            var client = CreateClient(externalMock.Object, "external:groq");
            await client.PlanNextAsync("task", null, EmptyScreen(), null, 1, CancellationToken.None);

            Assert.NotNull(captured);
            Assert.Equal("groq", captured.Provider);
            Assert.Equal(VisionSystemPrompt.PlannerPlanNext, captured.System);
            Assert.False(captured.IncludeContext);
        }

        // ============ 3. userId передаётся дальше ============

        [Fact]
        public async Task PlanNextAsync_UserIdForwarded()
        {
            int capturedUserId = -1;
            var externalMock = new Mock<IExternalLlmClient>();
            externalMock
                .Setup(c => c.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(), It.IsAny<CancellationToken>()))
                .Callback<int, ExternalLlmRequest, CancellationToken>((u, _, __) => capturedUserId = u)
                .ReturnsAsync(new ExternalLlmResponse { Content = "{\"action\":\"done\"}" });

            var client = CreateClient(externalMock.Object);
            await client.PlanNextAsync("task", null, EmptyScreen(), null, 42, CancellationToken.None);

            Assert.Equal(42, capturedUserId);
        }

        // ============ 4. Ошибка ExternalLlmClient → fail ============

        [Fact]
        public async Task PlanNextAsync_ExternalThrows_ReturnsFail()
        {
            var externalMock = new Mock<IExternalLlmClient>();
            externalMock
                .Setup(c => c.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("budget exceeded"));

            var client = CreateClient(externalMock.Object);
            var action = await client.PlanNextAsync(
                "task", null, EmptyScreen(), null, 1, CancellationToken.None);

            Assert.Equal("fail", action.Action);
            Assert.Contains("budget exceeded", action.Reason);
        }

        // ============ 5. Пустой ответ → fail ============

        [Fact]
        public async Task PlanNextAsync_EmptyContent_ReturnsFail()
        {
            var externalMock = new Mock<IExternalLlmClient>();
            externalMock
                .Setup(c => c.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExternalLlmResponse { Content = string.Empty });

            var client = CreateClient(externalMock.Object);
            var action = await client.PlanNextAsync(
                "task", null, EmptyScreen(), null, 1, CancellationToken.None);

            Assert.Equal("fail", action.Action);
            Assert.Contains("пустой", action.Reason);
        }

        // ============ 6. history обрезается ============

        [Fact]
        public async Task PlanNextAsync_HistoryTrimmed_ToMaxHistorySteps()
        {
            ExternalLlmRequest captured = null;
            var externalMock = new Mock<IExternalLlmClient>();
            externalMock
                .Setup(c => c.CompleteAsync(
                    It.IsAny<int>(), It.IsAny<ExternalLlmRequest>(), It.IsAny<CancellationToken>()))
                .Callback<int, ExternalLlmRequest, CancellationToken>((_, req, __) => captured = req)
                .ReturnsAsync(new ExternalLlmResponse { Content = "{\"action\":\"done\"}" });

            // 30 шагов, MaxHistorySteps=20.
            var history = new List<VisionStepDto>();
            for (int i = 1; i <= 30; i++)
            {
                history.Add(new VisionStepDto { StepIndex = i, Action = "click" });
            }

            var client = CreateClient(externalMock.Object);
            await client.PlanNextAsync("task", history, EmptyScreen(), null, 1, CancellationToken.None);

            Assert.NotNull(captured);
            // Проверим, что в payload нет шага #1 (он обрезан).
            Assert.DoesNotContain("\"stepIndex\": 1,", captured.Prompt);
            Assert.Contains("\"stepIndex\": 30", captured.Prompt);
        }
    }
}