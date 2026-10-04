using System.Linq;
using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionActionParser"/>
    /// (v1.12.0, KI-131, Ф5.2).
    /// </summary>
    public class VisionActionParserTests
    {
        // ============ 1. Базовые actions ============

        [Fact]
        public void Parse_CleanClick_ReturnsDto()
        {
            var json = "{\"action\":\"click\",\"target\":\"search_btn\",\"reason\":\"клик по кнопке\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("click", dto.Action);
            Assert.Equal("search_btn", dto.Target);
            Assert.Equal("клик по кнопке", dto.Reason);
        }

        [Fact]
        public void Parse_TypeWithText_ReturnsDto()
        {
            var json = "{\"action\":\"type\",\"target\":\"from_input\",\"text\":\"Москва\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("type", dto.Action);
            Assert.Equal("Москва", dto.Text);
        }

        [Fact]
        public void Parse_HotkeyWithKeys_ReturnsDto()
        {
            var json = "{\"action\":\"hotkey\",\"keys\":[\"Ctrl\",\"C\"]}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("hotkey", dto.Action);
            Assert.NotNull(dto.Keys);
            Assert.Equal(2, dto.Keys.Count);
            Assert.Equal("Ctrl", dto.Keys[0]);
            Assert.Equal("C", dto.Keys[1]);
        }

        [Fact]
        public void Parse_ScrollWithDeltaY_ReturnsDto()
        {
            var json = "{\"action\":\"scroll\",\"deltaY\":300}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("scroll", dto.Action);
            Assert.Equal(300, dto.DeltaY);
        }

        [Fact]
        public void Parse_PressKey_ReturnsDto()
        {
            var json = "{\"action\":\"press_key\",\"key\":\"Enter\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("press_key", dto.Action);
            Assert.Equal("Enter", dto.Key);
        }

        [Fact]
        public void Parse_Done_ReturnsDto()
        {
            var json = "{\"action\":\"done\",\"reason\":\"Задача выполнена\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("done", dto.Action);
        }

        // ============ 2. Click с координатами ============

        [Fact]
        public void Parse_ClickWithCoords_ReturnsDto()
        {
            var json = "{\"action\":\"click\",\"x\":440,\"y\":230}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal(440, dto.X);
            Assert.Equal(230, dto.Y);
        }

        [Fact]
        public void Parse_ClickWithoutCoords_XAndYNull()
        {
            var json = "{\"action\":\"click\",\"target\":\"btn\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Null(dto.X);
            Assert.Null(dto.Y);
        }

        // ============ 3. Валидация action ============

        [Theory]
        [InlineData("click")]
        [InlineData("double_click")]
        [InlineData("right_click")]
        [InlineData("move_mouse")]
        [InlineData("type")]
        [InlineData("press_key")]
        [InlineData("hotkey")]
        [InlineData("scroll")]
        [InlineData("wait")]
        [InlineData("done")]
        [InlineData("fail")]
        public void Parse_AllKnownActions_Accepted(string action)
        {
            var json = $"{{\"action\":\"{action}\"}}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal(action, dto.Action);
        }

        [Fact]
        public void Parse_UnknownAction_ReturnsFail()
        {
            var json = "{\"action\":\"destroy_everything\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("fail", dto.Action);
            Assert.Contains("Неизвестный action", dto.Reason);
        }

        [Fact]
        public void Parse_MissingAction_ReturnsFail()
        {
            var json = "{\"target\":\"btn\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("fail", dto.Action);
            Assert.Contains("`action` отсутствует", dto.Reason);
        }

        [Fact]
        public void Parse_UpperCaseAction_Normalized()
        {
            var json = "{\"action\":\"CLICK\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("click", dto.Action);
        }

        // ============ 4. Markdown-обёртки / case-insensitive ============

        [Fact]
        public void Parse_MarkdownJson_ReturnsDto()
        {
            var text = "```json\n{\"action\":\"click\",\"target\":\"btn\"}\n```";
            var dto = VisionActionParser.Parse(text);
            Assert.Equal("click", dto.Action);
        }

        [Fact]
        public void Parse_PascalCaseFields_ReturnsDto()
        {
            var json = "{\"Action\":\"click\",\"Target\":\"btn\",\"Reason\":\"test\"}";
            var dto = VisionActionParser.Parse(json);
            Assert.Equal("click", dto.Action);
            Assert.Equal("btn", dto.Target);
        }

        [Fact]
        public void Parse_TextBeforeAndAfterJson_ReturnsDto()
        {
            var text = "Вот действие:\n{\"action\":\"click\",\"target\":\"btn\"}\nГотово.";
            var dto = VisionActionParser.Parse(text);
            Assert.Equal("click", dto.Action);
        }

        // ============ 5. Fallback ============

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_NullOrEmpty_ReturnsFail(string input)
        {
            var dto = VisionActionParser.Parse(input);
            Assert.Equal("fail", dto.Action);
            Assert.False(string.IsNullOrEmpty(dto.Reason));
        }

        [Fact]
        public void Parse_FreeText_ReturnsFail()
        {
            var dto = VisionActionParser.Parse("Не могу выполнить.");
            Assert.Equal("fail", dto.Action);
            Assert.Contains("не вернул JSON", dto.Reason);
        }

        [Fact]
        public void Parse_BrokenJson_ReturnsFail()
        {
            var dto = VisionActionParser.Parse("{broken json");
            Assert.Equal("fail", dto.Action);
        }
    }
}