using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.Tools.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using IIChatTools.Services.Interfaces;
using IIChatTools.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionAgentTool"/>
    /// (v1.12.0, KI-131, Ф7.1-Ф7.2).
    /// </summary>
    public class VisionAgentToolTests
    {
        private const int UserId = 42;

        // ============ Билдеры ============

        private static VisionAgentOptions DefaultOptions()
        {
            return new VisionAgentOptions
            {
                Enabled = true,
                Limits = new VisionLimitsOptions
                {
                    MaxSteps = 30,
                    MaxTaskSeconds = 300,
                    MaxTasksPerUserPer5Min = 5,
                    ActionDelayMs = 0
                },
                ActionValidation = new VisionActionValidationOptions
                {
                    BlockedKeys = new List<string> { "F12", "Ctrl+Shift+I" },
                    BlockedHotkeys = new List<List<string>>
                    {
                        new List<string> { "Ctrl", "Alt", "Delete" },
                        new List<string> { "Alt", "Tab" }
                    },
                    MaxTextLength = 2000,
                    MaxScrollDelta = 2000
                }
            };
        }

        private static VisionAgentTool CreateTool(
            FakeVisionBackend backend = null,
            FakeVisionLlmClient visionLlm = null,
            FakeVisionScreenshotStore store = null,
            FakeVisionAgentService agentService = null,
            VisionAgentOptions options = null)
        {
            backend ??= new FakeVisionBackend();
            visionLlm ??= new FakeVisionLlmClient();
            store ??= new FakeVisionScreenshotStore();
            agentService ??= new FakeVisionAgentService();
            options ??= DefaultOptions();

            var validator = new VisionActionValidator(
                Options.Create(options),
                NullLogger<VisionActionValidator>.Instance);

            return new VisionAgentTool(
                agentService,
                backend,
                visionLlm,
                validator,
                store,
                Options.Create(options),
                NullLogger<VisionAgentTool>.Instance);
        }

        private static ToolExecutionContext Ctx()
        {
            return new ToolExecutionContext
            {
                UserId = UserId,
                WorkspaceRoot = @"C:\test-workspace",
                CancellationToken = CancellationToken.None
            };
        }

        // ============ 1. Структурные ============

        [Fact]
        public void Name_IsVisionAgent()
        {
            Assert.Equal("vision_agent", CreateTool().Name);
        }

        [Fact]
        public void Parameters_ActionIsRequired()
        {
            var tool = CreateTool();
            var actionParam = Assert.Single(
                tool.Parameters,
                p => p.Name == "action");
            Assert.True(actionParam.Required);
        }

        [Fact]
        public void Parameters_All12ActionsDescribed()
        {
            var desc = CreateTool().Description;
            // Проверяем, что все 12 действий упомянуты в описании.
            Assert.Contains("run_task", desc);
            Assert.Contains("describe", desc);
            Assert.Contains("screenshot", desc);
            Assert.Contains("click", desc);
            Assert.Contains("double_click", desc);
            Assert.Contains("right_click", desc);
            Assert.Contains("move_mouse", desc);
            Assert.Contains("type", desc);
            Assert.Contains("press_key", desc);
            Assert.Contains("hotkey", desc);
            Assert.Contains("scroll", desc);
            Assert.Contains("wait", desc);
        }

        // ============ 2. RequiresApprovalForCall (7.2) ============

        [Theory]
        [InlineData("describe", false)]
        [InlineData("screenshot", false)]
        [InlineData("move_mouse", false)]
        [InlineData("scroll", false)]
        [InlineData("wait", false)]
        [InlineData("run_task", true)]
        [InlineData("click", true)]
        [InlineData("double_click", true)]
        [InlineData("right_click", true)]
        [InlineData("type", true)]
        [InlineData("press_key", true)]
        [InlineData("hotkey", true)]
        public void RequiresApprovalForCall_PerAction(string action, bool expected)
        {
            var tool = CreateTool();
            var args = new JObject { ["action"] = action };
            Assert.Equal(expected, tool.RequiresApprovalForCall(args));
        }

        [Theory]
        [InlineData("DESCRIBE")]
        [InlineData("Describe")]
        [InlineData("describe")]
        [InlineData("  describe  ")]
        public void RequiresApprovalForCall_CaseInsensitiveReadOnly(string action)
        {
            var tool = CreateTool();
            var args = new JObject { ["action"] = action };
            Assert.False(tool.RequiresApprovalForCall(args));
        }

        [Fact]
        public void RequiresApprovalForCall_NullArgs_ReturnsTrue()
        {
            Assert.True(CreateTool().RequiresApprovalForCall(null));
        }

        [Fact]
        public void RequiresApprovalForCall_EmptyAction_ReturnsTrue()
        {
            var tool = CreateTool();
            Assert.True(tool.RequiresApprovalForCall(new JObject()));
            Assert.True(tool.RequiresApprovalForCall(
                new JObject { ["action"] = "" }));
            Assert.True(tool.RequiresApprovalForCall(
                new JObject { ["action"] = "   " }));
        }

        [Fact]
        public void RequiresApprovalByDefault_IsTrue()
        {
            Assert.True(CreateTool().RequiresApprovalByDefault);
        }

        // ============ 3. ExecuteAsync — валидация входа ============

        [Fact]
        public async Task ExecuteAsync_NullContext_ReturnsFail()
        {
            var result = await CreateTool().ExecuteAsync(null, new JObject());
            Assert.False(result.Success);
            Assert.Contains("Контекст", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_EmptyAction_ReturnsFail()
        {
            var result = await CreateTool().ExecuteAsync(Ctx(), new JObject());
            Assert.False(result.Success);
            Assert.Contains("action", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownAction_ReturnsFail()
        {
            var args = new JObject { ["action"] = "foobar" };
            var result = await CreateTool().ExecuteAsync(Ctx(), args);
            Assert.False(result.Success);
            Assert.Contains("foobar", result.Message);
        }

        // ============ 4. describe ============

        [Fact]
        public async Task ExecuteAsync_Describe_ReturnsScreenDescription()
        {
            var backend = new FakeVisionBackend();
            var visionLlm = new FakeVisionLlmClient
            {
                DefaultResponse = new ScreenDescriptionDto
                {
                    Description = "Страница поиска",
                    UiElements = new List<UiElementDto>
                    {
                        new UiElementDto
                        {
                            Id = "search_btn",
                            Type = "button",
                            Center = new UiElementCenterDto { X = 100, Y = 200 }
                        }
                    }
                }
            };

            var args = new JObject { ["action"] = "describe" };
            var result = await CreateTool(backend, visionLlm).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("Screenshot", backend.CallLog);
            Assert.Contains("1", result.Message);   // 1 элемент UI
        }

        [Fact]
        public async Task ExecuteAsync_Describe_WithUrl_CallsOpen()
        {
            var backend = new FakeVisionBackend();
            var args = new JObject
            {
                ["action"] = "describe",
                ["url"] = "https://example.com"
            };

            var result = await CreateTool(backend).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("Open(https://example.com)", backend.CallLog);
        }

        // ============ 5. screenshot ============

        [Fact]
        public async Task ExecuteAsync_Screenshot_ReturnsPathAndBase64()
        {
            var backend = new FakeVisionBackend();
            var store = new FakeVisionScreenshotStore();

            var args = new JObject { ["action"] = "screenshot" };
            var result = await CreateTool(backend, store: store).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Single(store.SavedPaths);
            // Data — анонимный объект с path/base64/sizeBytes.
            var data = JObject.FromObject(result.Data);
            Assert.NotNull(data["path"]);
            Assert.NotNull(data["base64"]);
            Assert.True(data["sizeBytes"]?.Value<long>() > 0);
        }

        // ============ 6. click (coordinate action) ============

        [Fact]
        public async Task ExecuteAsync_Click_WithCoords_CallsBackend()
        {
            var backend = new FakeVisionBackend();
            var args = new JObject
            {
                ["action"] = "click",
                ["x"] = 100,
                ["y"] = 200
            };

            var result = await CreateTool(backend).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("Click(100,200)", backend.CallLog);
        }

        [Fact]
        public async Task ExecuteAsync_Click_WithTarget_ResolvesFromScreen()
        {
            var backend = new FakeVisionBackend();
            var visionLlm = new FakeVisionLlmClient
            {
                DefaultResponse = new ScreenDescriptionDto
                {
                    UiElements = new List<UiElementDto>
                    {
                        new UiElementDto
                        {
                            Id = "submit_btn",
                            Type = "button",
                            Center = new UiElementCenterDto { X = 300, Y = 400 }
                        }
                    }
                }
            };

            var args = new JObject
            {
                ["action"] = "click",
                ["target"] = "submit_btn"
            };

            var result = await CreateTool(backend, visionLlm).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("Screenshot", backend.CallLog);   // для резолва target
            Assert.Contains("Click(300,400)", backend.CallLog);
        }

        [Fact]
        public async Task ExecuteAsync_Click_TargetNotFound_ReturnsFail()
        {
            var backend = new FakeVisionBackend();
            var visionLlm = new FakeVisionLlmClient
            {
                DefaultResponse = new ScreenDescriptionDto
                {
                    UiElements = new List<UiElementDto>()
                }
            };

            var args = new JObject
            {
                ["action"] = "click",
                ["target"] = "nonexistent"
            };

            var result = await CreateTool(backend, visionLlm).ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("nonexistent", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_Click_NoTargetNoCoords_ReturnsFail()
        {
            var args = new JObject { ["action"] = "click" };
            var result = await CreateTool().ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("target", result.Message);
        }

        // ============ 7. input-actions ============

        [Fact]
        public async Task ExecuteAsync_Type_ForwardsText()
        {
            var backend = new FakeVisionBackend();
            var args = new JObject
            {
                ["action"] = "type",
                ["text"] = "Привет"
            };

            var result = await CreateTool(backend).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("Type(Привет)", backend.CallLog);
        }

        [Fact]
        public async Task ExecuteAsync_Type_NoText_ReturnsFail()
        {
            var args = new JObject { ["action"] = "type" };
            var result = await CreateTool().ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("text", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_PressKey_BlockedKey_ReturnsFail()
        {
            var args = new JObject
            {
                ["action"] = "press_key",
                ["key"] = "F12"
            };

            var result = await CreateTool().ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("F12", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_PressKey_AllowedKey_Forwards()
        {
            var backend = new FakeVisionBackend();
            var args = new JObject
            {
                ["action"] = "press_key",
                ["key"] = "Enter"
            };

            var result = await CreateTool(backend).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("PressKey(Enter)", backend.CallLog);
        }

        [Fact]
        public async Task ExecuteAsync_Hotkey_ForwardsKeys()
        {
            var backend = new FakeVisionBackend();
            var args = new JObject
            {
                ["action"] = "hotkey",
                ["keys"] = new JArray { "Ctrl", "C" }
            };

            var result = await CreateTool(backend).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("Hotkey(Ctrl+C)", backend.CallLog);
        }

        [Fact]
        public async Task ExecuteAsync_Scroll_ForwardsDeltaY()
        {
            var backend = new FakeVisionBackend();
            var args = new JObject
            {
                ["action"] = "scroll",
                ["deltaY"] = 300
            };

            var result = await CreateTool(backend).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("Scroll(300)", backend.CallLog);
        }

        [Fact]
        public async Task ExecuteAsync_Wait_DefaultDuration()
        {
            var backend = new FakeVisionBackend();
            var args = new JObject { ["action"] = "wait" };

            var result = await CreateTool(backend).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Contains("Wait(1000)", backend.CallLog);   // DefaultWaitMs
        }

        // ============ 8. run_task ============

        [Fact]
        public async Task ExecuteAsync_RunTask_NoTask_ReturnsFail()
        {
            var args = new JObject { ["action"] = "run_task" };
            var result = await CreateTool().ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("task", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_RunTask_Success_ReturnsOk()
        {
            var agentService = new FakeVisionAgentService
            {
                NextResult = new VisionTaskResultDto
                {
                    Task = "test",
                    Backend = "local-harness",
                    Success = true,
                    Summary = "Найдено",
                    Steps = new List<VisionStepDto>(),
                    TotalDurationMs = 1234
                }
            };

            var args = new JObject
            {
                ["action"] = "run_task",
                ["task"] = "Найди статью про Москву",
                ["url"] = "https://wikipedia.org"
            };

            var result = await CreateTool(agentService: agentService).ExecuteAsync(Ctx(), args);

            Assert.True(result.Success);
            Assert.Equal("Найди статью про Москву", agentService.LastRequest?.Task);
            Assert.Equal("https://wikipedia.org", agentService.LastRequest?.Url);
            Assert.Equal(UserId, agentService.LastUserId);
        }

        [Fact]
        public async Task ExecuteAsync_RunTask_Failure_ReturnsFailWithData()
        {
            var agentService = new FakeVisionAgentService
            {
                NextResult = new VisionTaskResultDto
                {
                    Task = "test",
                    Backend = "local-harness",
                    Success = false,
                    Error = "Домен не в whitelist",
                    Steps = new List<VisionStepDto>()
                }
            };

            var args = new JObject
            {
                ["action"] = "run_task",
                ["task"] = "test"
            };

            var result = await CreateTool(agentService: agentService).ExecuteAsync(Ctx(), args);

            Assert.False(result.Success);
            Assert.Contains("whitelist", result.Message);
            Assert.NotNull(result.Data);   // Data=result — детали сохранены
        }

        // ============ 9. Fakes ============

        /// <summary>
        /// Fake IVisionAgentService — для тестов Tool'а.
        /// </summary>
        private sealed class FakeVisionAgentService : IVisionAgentService
        {
            public VisionTaskResultDto NextResult { get; set; } = new VisionTaskResultDto
            {
                Success = true,
                Steps = new List<VisionStepDto>()
            };

            public Exception Throws { get; set; }
            public VisionTaskRequest LastRequest { get; private set; }
            public int LastUserId { get; private set; }

            public Task<VisionTaskResultDto> RunTaskAsync(
                VisionTaskRequest request,
                int userId,
                CancellationToken cancellationToken = default)
            {
                if (Throws != null) throw Throws;
                LastRequest = request;
                LastUserId = userId;
                return Task.FromResult(NextResult);
            }
        }
    }
}