using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="ScreenDescriptionParser"/>
    /// (v1.12.0, KI-131, Ф5.1). Парсер должен быть устойчивым к:
    /// markdown-обёрткам, case-insensitive именам, битому JSON, отсутствию полей.
    /// </summary>
    public class ScreenDescriptionParserTests
    {
        // ============ 1. Чистый JSON ============

        [Fact]
        public void Parse_CleanJson_ReturnsDto()
        {
            var json = "{\"description\":\"Страница поиска РЖД\",\"ui_elements\":[]}";
            var result = ScreenDescriptionParser.Parse(json);

            Assert.Equal("Страница поиска РЖД", result.Description);
            Assert.Empty(result.UiElements);
        }

        [Fact]
        public void Parse_JsonWithOneElement_ReturnsElement()
        {
            var json = @"{
                ""description"": ""Форма поиска"",
                ""ui_elements"": [{
                    ""id"": ""from_input"",
                    ""type"": ""text_input"",
                    ""label"": ""Откуда"",
                    ""value"": """",
                    ""bounds"": { ""x"": 340, ""y"": 210, ""w"": 200, ""h"": 40 },
                    ""center"": { ""x"": 440, ""y"": 230 }
                }]
            }";
            var result = ScreenDescriptionParser.Parse(json);

            Assert.Equal("Форма поиска", result.Description);
            var el = Assert.Single(result.UiElements);
            Assert.Equal("from_input", el.Id);
            Assert.Equal("text_input", el.Type);
            Assert.Equal("Откуда", el.Label);
            Assert.NotNull(el.Bounds);
            Assert.Equal(340, el.Bounds.X);
            Assert.Equal(230, el.Center.Y);
        }

        [Fact]
        public void Parse_JsonWithEnabled_False_ReturnsElement()
        {
            var json = @"{""ui_elements"":[{""id"":""btn"",""type"":""button"",""enabled"":false}]}";
            var result = ScreenDescriptionParser.Parse(json);
            var el = Assert.Single(result.UiElements);
            Assert.False(el.Enabled);
        }

        // ============ 2. Markdown-обёртка ============

        [Fact]
        public void Parse_JsonInMarkdownWithLang_ReturnsDto()
        {
            var text = "```json\n{\"description\":\"test\",\"ui_elements\":[]}\n```";
            var result = ScreenDescriptionParser.Parse(text);
            Assert.Equal("test", result.Description);
        }

        [Fact]
        public void Parse_JsonInMarkdownWithoutLang_ReturnsDto()
        {
            var text = "```\n{\"description\":\"test\",\"ui_elements\":[]}\n```";
            var result = ScreenDescriptionParser.Parse(text);
            Assert.Equal("test", result.Description);
        }

        [Fact]
        public void Parse_JsonWithTextBeforeAndAfter_ReturnsDto()
        {
            var text = "Вот результат:\n{\"description\":\"ok\",\"ui_elements\":[]}\nКонец.";
            var result = ScreenDescriptionParser.Parse(text);
            Assert.Equal("ok", result.Description);
        }

        // ============ 3. Case-insensitive ============

        [Fact]
        public void Parse_PascalCaseJson_ReturnsDto()
        {
            var json = "{\"Description\":\"Pascal\",\"UiElements\":[]}";
            var result = ScreenDescriptionParser.Parse(json);
            Assert.Equal("Pascal", result.Description);
        }

        [Fact]
        public void Parse_UppercaseJson_ReturnsDto()
        {
            var json = "{\"DESCRIPTION\":\"UPPER\",\"UI_ELEMENTS\":[]}";
            var result = ScreenDescriptionParser.Parse(json);
            Assert.Equal("UPPER", result.Description);
        }

        // ============ 4. Отсутствующие поля ============

        [Fact]
        public void Parse_MissingUiElements_ReturnsEmptyList()
        {
            var json = "{\"description\":\"only desc\"}";
            var result = ScreenDescriptionParser.Parse(json);
            Assert.Equal("only desc", result.Description);
            Assert.Empty(result.UiElements);
        }

        [Fact]
        public void Parse_MissingDescription_ReturnsEmptyString()
        {
            var json = "{\"ui_elements\":[]}";
            var result = ScreenDescriptionParser.Parse(json);
            Assert.Equal(string.Empty, result.Description);
        }

        [Fact]
        public void Parse_EmptyJsonObject_ReturnsEmptyDto()
        {
            var json = "{}";
            var result = ScreenDescriptionParser.Parse(json);
            Assert.Equal(string.Empty, result.Description);
            Assert.Empty(result.UiElements);
        }

        // ============ 5. Невалидные элементы ============

        [Fact]
        public void Parse_ElementWithoutId_Skipped()
        {
            var json = @"{""ui_elements"":[{""type"":""button""}]}";
            var result = ScreenDescriptionParser.Parse(json);
            Assert.Empty(result.UiElements);
        }

        [Fact]
        public void Parse_ElementWithoutType_Skipped()
        {
            var json = @"{""ui_elements"":[{""id"":""btn""}]}";
            var result = ScreenDescriptionParser.Parse(json);
            Assert.Empty(result.UiElements);
        }

        // ============ 6. Fallback ============

        [Fact]
        public void Parse_FreeText_ReturnsFallbackWithRawText()
        {
            var text = "Извините, я не могу описать скриншот.";
            var result = ScreenDescriptionParser.Parse(text);
            Assert.Equal(text, result.Description);
            Assert.Empty(result.UiElements);
        }

        [Fact]
        public void Parse_BrokenJson_ReturnsFallback()
        {
            var text = "{invalid json here";
            var result = ScreenDescriptionParser.Parse(text);
            Assert.False(string.IsNullOrEmpty(result.Description));
            Assert.Empty(result.UiElements);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_NullOrEmpty_ReturnsEmptyDto(string input)
        {
            var result = ScreenDescriptionParser.Parse(input);
            Assert.Equal(string.Empty, result.Description);
            Assert.Empty(result.UiElements);
        }

        [Fact]
        public void Parse_JsonWithBom_ReturnsDto()
        {
            var json = "\uFEFF{\"description\":\"bom\",\"ui_elements\":[]}";
            var result = ScreenDescriptionParser.Parse(json);
            Assert.Equal("bom", result.Description);
        }
    }
}