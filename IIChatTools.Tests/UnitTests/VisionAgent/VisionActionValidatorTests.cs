using System.Collections.Generic;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionActionValidator"/>
    /// (v1.12.0, KI-131, Ф6.1).
    /// </summary>
    public class VisionActionValidatorTests
    {
        private static VisionActionValidator CreateValidator(
            List<string> blockedKeys = null,
            List<List<string>> blockedHotkeys = null,
            int maxTextLength = 2000,
            int maxScrollDelta = 2000)
        {
            var options = new VisionAgentOptions
            {
                ActionValidation = new VisionActionValidationOptions
                {
                    BlockedKeys = blockedKeys ?? new List<string>
                    {
                        "F12", "F5",
                        "Ctrl+Shift+I", "Ctrl+Shift+J", "Ctrl+U",
                        "Alt+F4", "Ctrl+W"
                    },
                    BlockedHotkeys = blockedHotkeys ?? new List<List<string>>
                    {
                        new List<string> { "Ctrl", "Alt", "Delete" },
                        new List<string> { "Alt", "Tab" },
                        new List<string> { "Meta", "L" },
                        new List<string> { "Meta", "D" }
                    },
                    MaxTextLength = maxTextLength,
                    MaxScrollDelta = maxScrollDelta
                }
            };

            return new VisionActionValidator(
                Options.Create(options),
                NullLogger<VisionActionValidator>.Instance);
        }

        private static ScreenDescriptionDto ScreenWithElements(params string[] ids)
        {
            var screen = new ScreenDescriptionDto();
            foreach (var id in ids)
            {
                screen.UiElements.Add(new UiElementDto { Id = id, Type = "button" });
            }
            return screen;
        }

        // ============ 1. Null / пустой action ============

        [Fact]
        public void Validate_NullAction_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(null, new ScreenDescriptionDto());
            Assert.False(r.Success);
            Assert.Contains("null", r.Error);
        }

        [Fact]
        public void Validate_EmptyAction_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(new VisionActionDto { Action = "" }, new ScreenDescriptionDto());
            Assert.False(r.Success);
        }

        [Fact]
        public void Validate_UnknownAction_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "destroy" }, new ScreenDescriptionDto());
            Assert.False(r.Success);
            Assert.Contains("Неизвестный action", r.Error);
        }

        // ============ 2. done / fail — OK без проверок ============

        [Theory]
        [InlineData("done")]
        [InlineData("fail")]
        [InlineData("DONE")]
        [InlineData("Done")]
        public void Validate_DoneOrFail_SuccessWithoutChecks(string action)
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = action, Target = "nonexistent" },
                new ScreenDescriptionDto());

            Assert.True(r.Success);
        }

        // ============ 3. press_key — BlockedKeys ============

        [Theory]
        [InlineData("F12")]
        [InlineData("f12")]
        [InlineData("F5")]
        public void Validate_BlockedKey_Rejects(string key)
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "press_key", Key = key },
                new ScreenDescriptionDto());

            Assert.False(r.Success);
            Assert.Contains("запрещена", r.Error);
        }

        [Fact]
        public void Validate_PressKeyMissingKey_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "press_key" }, new ScreenDescriptionDto());
            Assert.False(r.Success);
        }

        [Fact]
        public void Validate_PressKeyAllowed_Success()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "press_key", Key = "Enter" },
                new ScreenDescriptionDto());
            Assert.True(r.Success);
        }

        // ============ 4. hotkey — BlockedHotkeys ============

        [Fact]
        public void Validate_HotkeyAltTab_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto
                {
                    Action = "hotkey",
                    Keys = new List<string> { "Alt", "Tab" }
                },
                new ScreenDescriptionDto());

            Assert.False(r.Success);
            Assert.Contains("запрещена", r.Error);
        }

        [Fact]
        public void Validate_HotkeyCtrlAltDelete_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto
                {
                    Action = "hotkey",
                    Keys = new List<string> { "Ctrl", "Alt", "Delete" }
                },
                new ScreenDescriptionDto());

            Assert.False(r.Success);
        }

        [Fact]
        public void Validate_HotkeyBlockedOrderIndependent_Rejects()
        {
            // Порядок не должен иметь значения.
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto
                {
                    Action = "hotkey",
                    Keys = new List<string> { "Tab", "Alt" }
                },
                new ScreenDescriptionDto());

            Assert.False(r.Success);
        }

        [Fact]
        public void Validate_HotkeyEmpty_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "hotkey", Keys = new List<string>() },
                new ScreenDescriptionDto());

            Assert.False(r.Success);
        }

        [Fact]
        public void Validate_HotkeyValidCtrlC_Success()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto
                {
                    Action = "hotkey",
                    Keys = new List<string> { "Ctrl", "C" }
                },
                new ScreenDescriptionDto());

            Assert.True(r.Success);
        }

        // ============ 5. type — clamp длины текста ============

        [Fact]
        public void Validate_TypeTooLong_Truncates()
        {
            var v = CreateValidator(maxTextLength: 5);
            var r = v.Validate(
                new VisionActionDto { Action = "type", Text = "1234567890" },
                new ScreenDescriptionDto());

            Assert.True(r.Success);
            Assert.NotNull(r.SanitizedAction);
            Assert.Equal("12345", r.SanitizedAction.Text);
        }

        [Fact]
        public void Validate_TypeWithinLimit_NoSanitization()
        {
            var v = CreateValidator(maxTextLength: 100);
            var r = v.Validate(
                new VisionActionDto { Action = "type", Text = "Москва" },
                new ScreenDescriptionDto());

            Assert.True(r.Success);
            Assert.Null(r.SanitizedAction);
        }

        // ============ 6. scroll — clamp deltaY ============

        [Fact]
        public void Validate_ScrollTooBig_Clamps()
        {
            var v = CreateValidator(maxScrollDelta: 500);
            var r = v.Validate(
                new VisionActionDto { Action = "scroll", DeltaY = 5000 },
                new ScreenDescriptionDto());

            Assert.True(r.Success);
            Assert.NotNull(r.SanitizedAction);
            Assert.Equal(500, r.SanitizedAction.DeltaY);
        }

        [Fact]
        public void Validate_ScrollNegativeTooBig_Clamps()
        {
            var v = CreateValidator(maxScrollDelta: 500);
            var r = v.Validate(
                new VisionActionDto { Action = "scroll", DeltaY = -5000 },
                new ScreenDescriptionDto());

            Assert.True(r.Success);
            Assert.Equal(-500, r.SanitizedAction.DeltaY);
        }

        // ============ 7. target — existence ============

        [Fact]
        public void Validate_TargetNotFound_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "click", Target = "btn_x" },
                ScreenWithElements("btn_a", "btn_b"));

            Assert.False(r.Success);
            Assert.Contains("btn_x", r.Error);
        }

        [Fact]
        public void Validate_TargetFound_Success()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "click", Target = "search_btn" },
                ScreenWithElements("from_input", "search_btn"));

            Assert.True(r.Success);
        }

        [Fact]
        public void Validate_NoTargetCoords_Success()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "click", X = 100, Y = 200 },
                new ScreenDescriptionDto());

            Assert.True(r.Success);
        }

        // ============ 8. Координаты — неотрицательность ============

        [Fact]
        public void Validate_NegativeX_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "click", X = -1, Y = 100 },
                new ScreenDescriptionDto());

            Assert.False(r.Success);
            Assert.Contains("X < 0", r.Error);
        }

        [Fact]
        public void Validate_NegativeY_Rejects()
        {
            var v = CreateValidator();
            var r = v.Validate(
                new VisionActionDto { Action = "click", X = 100, Y = -1 },
                new ScreenDescriptionDto());

            Assert.False(r.Success);
        }
    }
}