using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using IIChatTools.Services.Interfaces;
using IIChatTools.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionAgentService"/>
    /// (v1.12.0, KI-131, Ф6.9). Fake backend + fake LLM + real validator/rate-limiter.
    /// </summary>
    public class VisionAgentServiceTests
    {
        private const int UserId = 42;

        private static VisionAgentOptions DefaultOptions(
            int maxSteps = 10,
            int maxTaskSeconds = 300,
            int maxTasksPerUser = 100,   // высоко, чтобы не мешать большинству тестов
            int actionDelayMs = 0)
        {
            return new VisionAgentOptions
            {
                Enabled = true,
                Limits = new VisionLimitsOptions
                {
                    MaxSteps = maxSteps,
                    MaxTaskSeconds = maxTaskSeconds,
                    MaxTasksPerUserPer5Min = maxTasksPerUser,
                    ActionDelayMs = actionDelayMs
                },
                ActionValidation = new VisionActionValidationOptions
                {
                    BlockedKeys = new List<string> { "F12" },
                    BlockedHotkeys = new List<List<string>>(),
                    MaxTextLength = 2000,
                    MaxScrollDelta = 2000
                }
            };
        }

        private static VisionAgentService CreateService(
            FakeVisionBackend backend,
            FakeVisionLlmClient visionLlm,
            FakePlannerLlmClient plannerLlm,
            FakeVisionScreenshotStore store = null,
            IVisionRateLimiter rateLimiter = null,
            VisionAgentOptions options = null)
        {
            options ??= DefaultOptions();
            store ??= new FakeVisionScreenshotStore();
            rateLimiter ??= new InMemoryVisionRateLimiter(
                Options.Create(options),
                NullLogger<InMemoryVisionRateLimiter>.Instance);

            var validator = new VisionActionValidator(
                Options.Create(options),
                NullLogger<VisionActionValidator>.Instance);
            var overlay = new NoopVisionOverlayLauncher(
                NullLogger<NoopVisionOverlayLauncher>.Instance);

            return new VisionAgentService(
                backend, visionLlm, plannerLlm, validator, rateLimiter,
                store, overlay, Options.Create(options),
                NullLogger<VisionAgentService>.Instance);
        }

        private static VisionTaskRequest Request(string task = "test task", string url = null)
        {
            return new VisionTaskRequest { Task = task, Url = url };
        }

        // ============ 1. Null / empty request ============

        [Fact]
        public async Task RunTaskAsync_NullRequest_Throws()
        {
            var svc = CreateService(new FakeVisionBackend(), new FakeVisionLlmClient(), new FakePlannerLlmClient());
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => svc.RunTaskAsync(null, UserId, CancellationToken.None));
        }

        [Fact]
        public async Task RunTaskAsync_EmptyTask_Throws()
        {
            var svc = CreateService(new FakeVisionBackend(), new FakeVisionLlmClient(), new FakePlannerLlmClient());
            await Assert.ThrowsAsync<ArgumentException>(
                () => svc.RunTaskAsync(new VisionTaskRequest { Task = "" }, UserId, CancellationToken.None));
        }

        // ============ 2. Rate limit ============

        [Fact]
        public async Task RunTaskAsync_RateLimitExceeded_ReturnsErrorDto()
        {
            var options = DefaultOptions(maxTasksPerUser: 1);
            var backend = new FakeVisionBackend();
            var svc = CreateService(backend, new FakeVisionLlmClient(), new FakePlannerLlmClient(),
                options: options);

            // 1-я задача пройдёт. 2-я — уже нет.
            await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);
            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("лимит запусков", result.Error);
            Assert.Empty(result.Steps);   // loop не запускался
        }

        // ============ 3. Happy path: Planner вернул done ============

        [Fact]
        public async Task RunTaskAsync_DoneAction_ReturnsSuccess()
        {
            var backend = new FakeVisionBackend();
            var visionLlm = new FakeVisionLlmClient();
            var planner = new FakePlannerLlmClient
            {
                DefaultResponse = new VisionActionDto { Action = "done", Reason = "all done" }
            };
            var svc = CreateService(backend, visionLlm, planner);

            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("all done", result.Summary);
            Assert.Empty(result.Steps);      // done на первом шаге — действий не было
            Assert.Contains("Screenshot", backend.CallLog);
        }

        // ============ 4. Planner вернул fail ============

        [Fact]
        public async Task RunTaskAsync_FailAction_ReturnsFail()
        {
            var planner = new FakePlannerLlmClient
            {
                DefaultResponse = new VisionActionDto { Action = "fail", Reason = "не получилось" }
            };
            var svc = CreateService(new FakeVisionBackend(), new FakeVisionLlmClient(), planner);

            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("не получилось", result.Error);
        }

        // ============ 5. maxSteps исчерпан ============

        [Fact]
        public async Task RunTaskAsync_MaxStepsExhausted_ReturnsError()
        {
            // Planner всегда возвращает click, никогда done.
            var planner = new FakePlannerLlmClient
            {
                DefaultResponse = new VisionActionDto { Action = "click", X = 10, Y = 20 }
            };
            var backend = new FakeVisionBackend();

            var options = DefaultOptions(maxSteps: 3);
            var svc = CreateService(backend, new FakeVisionLlmClient(), planner, options: options);

            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("лимит шагов", result.Error);
            Assert.Equal(3, result.Steps.Count);
            Assert.Equal(3, backend.CallLog.FindAll(c => c.StartsWith("Click")).Count);
        }

        // ============ 6. Screenshot упал ============

        [Fact]
        public async Task RunTaskAsync_ScreenshotFails_ReturnsError()
        {
            var backend = new FakeVisionBackend
            {
                ScreenshotException = new InvalidOperationException("screen broken")
            };
            var svc = CreateService(backend, new FakeVisionLlmClient(), new FakePlannerLlmClient());

            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("screen broken", result.Error);
        }

        // ============ 7. Describe упал ============

        [Fact]
        public async Task RunTaskAsync_DescribeFails_ReturnsError()
        {
            var visionLlm = new FakeVisionLlmClient
            {
                Throws = new TimeoutException("VL timeout")
            };
            var svc = CreateService(new FakeVisionBackend(), visionLlm, new FakePlannerLlmClient());

            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("VL timeout", result.Error);
        }

        // ============ 8. Plan упал ============

        [Fact]
        public async Task RunTaskAsync_PlanFails_ReturnsError()
        {
            var planner = new FakePlannerLlmClient
            {
                Throws = new InvalidOperationException("planner broken")
            };
            var svc = CreateService(new FakeVisionBackend(), new FakeVisionLlmClient(), planner);

            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("planner broken", result.Error);
        }

        // ============ 9. Backend action упал → шаг с ошибкой, loop продолжается ============

        [Fact]
        public async Task RunTaskAsync_BackendActionFails_StepErrorButContinues()
        {
            var backend = new FakeVisionBackend
            {
                ActionException = new InvalidOperationException("click failed")
            };

            var planner = new FakePlannerLlmClient();
            planner.Responses.Enqueue(new VisionActionDto { Action = "click", X = 5, Y = 5 });
            planner.Responses.Enqueue(new VisionActionDto { Action = "done", Reason = "recovered" });

            var svc = CreateService(backend, new FakeVisionLlmClient(), planner);
            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.Contains("click failed", result.Steps[0].Error);
        }

        // ============ 10. Target not found → валидатор отклоняет, шаг записан ============

        [Fact]
        public async Task RunTaskAsync_TargetNotFound_StepErrorButContinues()
        {
            // Planner ссылается на несуществующий target, потом done.
            var planner = new FakePlannerLlmClient();
            planner.Responses.Enqueue(new VisionActionDto { Action = "click", Target = "nonexistent_btn" });
            planner.Responses.Enqueue(new VisionActionDto { Action = "done" });

            var visionLlm = new FakeVisionLlmClient
            {
                DefaultResponse = new ScreenDescriptionDto
                {
                    Description = "empty screen",
                    UiElements = new List<UiElementDto>()
                }
            };

            var svc = CreateService(new FakeVisionBackend(), visionLlm, planner);
            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.Contains("не найден в ui_elements", result.Steps[0].Error);
        }

        // ============ 11. Все 11 actions передаются в backend ============

        [Fact]
        public async Task RunTaskAsync_ExecutesVariousActions_ForwardsToBackend()
        {
            var backend = new FakeVisionBackend();
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto>
                {
                    new UiElementDto
                    {
                        Id = "btn",
                        Type = "button",
                        Center = new UiElementCenterDto { X = 100, Y = 200 }
                    }
                }
            };
            var visionLlm = new FakeVisionLlmClient { DefaultResponse = screen };

            var planner = new FakePlannerLlmClient();
            planner.Responses.Enqueue(new VisionActionDto { Action = "click", Target = "btn" });
            planner.Responses.Enqueue(new VisionActionDto { Action = "type", Text = "Привет" });
            planner.Responses.Enqueue(new VisionActionDto { Action = "press_key", Key = "Enter" });
            planner.Responses.Enqueue(new VisionActionDto { Action = "scroll", DeltaY = 300 });
            planner.Responses.Enqueue(new VisionActionDto { Action = "wait", DeltaY = 500 });
            planner.Responses.Enqueue(new VisionActionDto { Action = "done" });

            var svc = CreateService(backend, visionLlm, planner);
            var result = await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(5, result.Steps.Count);
            Assert.Contains("Click(100,200)", backend.CallLog);
            Assert.Contains("Type(Привет)", backend.CallLog);
            Assert.Contains("PressKey(Enter)", backend.CallLog);
            Assert.Contains("Scroll(300)", backend.CallLog);
            Assert.Contains("Wait(500)", backend.CallLog);
        }

        // ============ 12. URL → OpenAsync ============

        [Fact]
        public async Task RunTaskAsync_WithUrl_CallsOpenAsync()
        {
            var backend = new FakeVisionBackend();
            var planner = new FakePlannerLlmClient
            {
                DefaultResponse = new VisionActionDto { Action = "done" }
            };
            var svc = CreateService(backend, new FakeVisionLlmClient(), planner);

            await svc.RunTaskAsync(Request(url: "https://example.com"), UserId, CancellationToken.None);

            Assert.Equal("Open(https://example.com)", backend.CallLog[0]);
        }

        // ============ 13. Скриншоты сохраняются на каждом шаге ============

        [Fact]
        public async Task RunTaskAsync_SavesScreenshotEachStep()
        {
            var store = new FakeVisionScreenshotStore();
            var planner = new FakePlannerLlmClient();
            planner.Responses.Enqueue(new VisionActionDto { Action = "wait", DeltaY = 100 });
            planner.Responses.Enqueue(new VisionActionDto { Action = "done" });

            var svc = CreateService(new FakeVisionBackend(), new FakeVisionLlmClient(), planner, store);
            var result = await svc.RunTaskAsync(
                new VisionTaskRequest { Task = "task", TaskId = "vt_test" },
                UserId, CancellationToken.None);

            Assert.Equal(2, store.SavedPaths.Count);   // 2 итерации loop
            Assert.Contains("screenshots/vt_test/step-001.png", store.SavedPaths);
            Assert.Contains("screenshots/vt_test/step-002.png", store.SavedPaths);
            Assert.Equal("screenshots/vt_test/step-002.png", result.FinalScreenshotPath);
        }

        // ============ 14. Внешняя отмена → throw ============

        [Fact]
        public async Task RunTaskAsync_ExternalCancellation_Throws()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var svc = CreateService(new FakeVisionBackend(), new FakeVisionLlmClient(), new FakePlannerLlmClient());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => svc.RunTaskAsync(Request(), UserId, cts.Token));
        }

        // ============ 15. userId прокидывается в Planner ============

        [Fact]
        public async Task RunTaskAsync_UserIdForwardedToPlanner()
        {
            var planner = new FakePlannerLlmClient
            {
                DefaultResponse = new VisionActionDto { Action = "done" }
            };
            var svc = CreateService(new FakeVisionBackend(), new FakeVisionLlmClient(), planner);

            await svc.RunTaskAsync(Request(), UserId, CancellationToken.None);

            Assert.Contains(UserId, planner.UserIdsReceived);
        }
    }
}