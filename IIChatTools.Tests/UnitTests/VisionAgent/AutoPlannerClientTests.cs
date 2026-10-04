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
    /// Unit-тесты для <see cref="AutoPlannerClient"/>
    /// (v1.12.0, KI-131, Ф5.4).
    /// </summary>
    public class AutoPlannerClientTests
    {
        private static AutoPlannerClient CreateClient(
            IPlannerLlmClient lmStudio,
            IPlannerLlmClient external,
            List<string> fallbackChain = null)
        {
            var options = new VisionAgentOptions
            {
                PlannerLlm = new PlannerLlmOptions
                {
                    Provider = "auto",
                    Model = "test-model",
                    FallbackChain = fallbackChain ?? new List<string>
                    {
                        "lmstudio", "external:deepseek"
                    }
                }
            };

            return new AutoPlannerClient(
                Options.Create(options),
                () => lmStudio,
                () => external,
                NullLogger<AutoPlannerClient>.Instance);
        }

        private static IPlannerLlmClient MockPlanner(bool isReady, VisionActionDto result)
        {
            var mock = new Mock<IPlannerLlmClient>();
            mock.SetupGet(c => c.IsReady).Returns(isReady);
            mock.Setup(c => c.PlanNextAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<VisionStepDto>>(),
                    It.IsAny<ScreenDescriptionDto>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            return mock.Object;
        }

        private static VisionActionDto Done() => new VisionActionDto
        {
            Action = "done", Reason = "all good"
        };

        private static VisionActionDto Fail(string reason) => new VisionActionDto
        {
            Action = "fail", Reason = reason
        };

        // ============ 1. Первый возвращает action — успех ============

        [Fact]
        public async Task PlanNextAsync_FirstReturns_ReturnsAction()
        {
            var lmStudio = MockPlanner(true, new VisionActionDto
            {
                Action = "click", Target = "btn1"
            });
            var external = MockPlanner(true, Done());

            var client = CreateClient(lmStudio, external);
            var action = await client.PlanNextAsync(
                "task", null, new ScreenDescriptionDto(), null, 1, CancellationToken.None);

            Assert.Equal("click", action.Action);
            Assert.Equal("btn1", action.Target);
        }

        // ============ 2. Первый не-ready → второй ============

        [Fact]
        public async Task PlanNextAsync_FirstNotReady_UsesSecond()
        {
            var lmStudio = MockPlanner(isReady: false, result: null);
            var external = MockPlanner(true, new VisionActionDto
            {
                Action = "type", Text = "Москва"
            });

            var client = CreateClient(lmStudio, external);
            var action = await client.PlanNextAsync(
                "task", null, new ScreenDescriptionDto(), null, 1, CancellationToken.None);

            Assert.Equal("type", action.Action);
        }

        // ============ 3. Первый вернул fail → второй ============

        [Fact]
        public async Task PlanNextAsync_FirstFail_UsesSecond()
        {
            var lmStudio = MockPlanner(true, Fail("не смог"));
            var external = MockPlanner(true, new VisionActionDto
            {
                Action = "click", Target = "saved"
            });

            var client = CreateClient(lmStudio, external);
            var action = await client.PlanNextAsync(
                "task", null, new ScreenDescriptionDto(), null, 1, CancellationToken.None);

            Assert.Equal("click", action.Action);
            Assert.Equal("saved", action.Target);
        }

        // ============ 4. Все fail → возвращаем последний fail ============

        [Fact]
        public async Task PlanNextAsync_AllFail_ReturnsLastFail()
        {
            var lmStudio = MockPlanner(true, Fail("LM не смогла"));
            var external = MockPlanner(true, Fail("External не смогла"));

            var client = CreateClient(lmStudio, external);
            var action = await client.PlanNextAsync(
                "task", null, new ScreenDescriptionDto(), null, 1, CancellationToken.None);

            Assert.Equal("fail", action.Action);
            Assert.Contains("External", action.Reason);
        }

        // ============ 5. Первый упал → второй ============

        [Fact]
        public async Task PlanNextAsync_FirstThrows_FallsBack()
        {
            var lmStudioMock = new Mock<IPlannerLlmClient>();
            lmStudioMock.SetupGet(c => c.IsReady).Returns(true);
            lmStudioMock
                .Setup(c => c.PlanNextAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<VisionStepDto>>(),
                    It.IsAny<ScreenDescriptionDto>(), It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("LM timeout"));

            var external = MockPlanner(true, new VisionActionDto
            {
                Action = "click", Target = "after_timeout"
            });

            var client = CreateClient(lmStudioMock.Object, external);
            var action = await client.PlanNextAsync(
                "task", null, new ScreenDescriptionDto(), null, 1, CancellationToken.None);

            Assert.Equal("click", action.Action);
        }

        // ============ 6. Все не-ready → fail с reason ============

        [Fact]
        public async Task PlanNextAsync_AllNotReady_ReturnsFail()
        {
            var lmStudio = MockPlanner(false, null);
            var external = MockPlanner(false, null);

            var client = CreateClient(lmStudio, external);
            var action = await client.PlanNextAsync(
                "task", null, new ScreenDescriptionDto(), null, 1, CancellationToken.None);

            Assert.Equal("fail", action.Action);
            Assert.Contains("Ни один", action.Reason);
        }

        // ============ 7. Пустая цепочка → fail ============

        [Fact]
        public async Task PlanNextAsync_EmptyChain_ReturnsFail()
        {
            var client = CreateClient(
                MockPlanner(false, null),
                MockPlanner(false, null),
                fallbackChain: new List<string>());

            var action = await client.PlanNextAsync(
                "task", null, new ScreenDescriptionDto(), null, 1, CancellationToken.None);

            Assert.Equal("fail", action.Action);
            Assert.Contains("FallbackChain пуст", action.Reason);
        }

        // ============ 8. userId прокидывается ============

        [Fact]
        public async Task PlanNextAsync_UserIdForwardedToWinner()
        {
            int capturedUserId = -1;
            var lmStudioMock = new Mock<IPlannerLlmClient>();
            lmStudioMock.SetupGet(c => c.IsReady).Returns(true);
            lmStudioMock
                .Setup(c => c.PlanNextAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<VisionStepDto>>(),
                    It.IsAny<ScreenDescriptionDto>(), It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<string, IReadOnlyList<VisionStepDto>, ScreenDescriptionDto,
                    IReadOnlyList<string>, int, CancellationToken>(
                    (_, __, ___, ____, uid, _____) => capturedUserId = uid)
                .ReturnsAsync(new VisionActionDto { Action = "done" });

            var client = CreateClient(lmStudioMock.Object, MockPlanner(false, null));
            await client.PlanNextAsync(
                "task", null, new ScreenDescriptionDto(), null, 42, CancellationToken.None);

            Assert.Equal(42, capturedUserId);
        }

        // ============ 9. IsReady ============

        [Fact]
        public void IsReady_AnyReady_ReturnsTrue()
        {
            var client = CreateClient(
                MockPlanner(false, null),
                MockPlanner(true, Done()));
            Assert.True(client.IsReady);
        }

        [Fact]
        public void IsReady_AllNotReady_ReturnsFalse()
        {
            var client = CreateClient(
                MockPlanner(false, null),
                MockPlanner(false, null));
            Assert.False(client.IsReady);
        }
    }
}