using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// KI-215: VL-модель (<c>qwen2.5-vl-7b-instruct</c>) иногда возвращает
    /// <c>"center"</c> как арифметическое выражение вместо числа:
    /// <code>
    /// "center": { "x": 546 + (378 / 2), "y": 120 + (49 / 2) }
    /// </code>
    /// Это не валидный JSON — <c>JObject.Parse</c> падает → <c>ui_elements = []</c>.
    /// Fix: <c>ScreenDescriptionParser</c> удаляет <c>"center"</c> regex-ом до
    /// парсинга (KI-199 всё равно пересчитывает center из bounds).
    /// <para>
    /// Эти тесты — фокусированные на KI-215. Полный набор —
    /// в <c>ScreenDescriptionParserTests.cs</c>.
    /// </para>
    /// </summary>
    public class ScreenDescriptionParserCenterStripTests
    {
        [Fact]
        public void Parse_CenterAsArithmeticExpression_StripsCenter_KeepsElement()
        {
            var raw = @"{
              ""description"": ""Главная Wikipedia с языками"",
              ""ui_elements"": [
                {
                  ""id"": ""article_title"",
                  ""type"": ""heading"",
                  ""label"": ""Wikipedia"",
                  ""bounds"": { ""x"": 546, ""y"": 120, ""w"": 378, ""h"": 49 },
                  ""center"": { ""x"": 546 + (378 / 2), ""y"": 120 + (49 / 2) }
                }
              ]
            }";

            var result = ScreenDescriptionParser.Parse(raw);

            Assert.Single(result.UiElements);
            Assert.Equal("article_title", result.UiElements[0].Id);
            Assert.Equal("Wikipedia", result.UiElements[0].Label);
            Assert.NotNull(result.UiElements[0].Bounds);
            Assert.Equal(546, result.UiElements[0].Bounds.X);
            Assert.Equal(378, result.UiElements[0].Bounds.W);
            // center пересчитан парсером из bounds (KI-199)
            Assert.Equal(546 + 378 / 2, result.UiElements[0].Center.X);
            Assert.Equal(120 + 49 / 2, result.UiElements[0].Center.Y);
        }

        [Fact]
        public void Parse_CenterAsLastKey_TrailingCommaStripped()
        {
            var raw = @"{
              ""description"": ""test"",
              ""ui_elements"": [
                {
                  ""id"": ""btn"",
                  ""type"": ""button"",
                  ""bounds"": { ""x"": 10, ""y"": 20, ""w"": 100, ""h"": 30 },
                  ""center"": { ""x"": 10 + 50, ""y"": 20 + 15 }
                }
              ]
            }";

            var result = ScreenDescriptionParser.Parse(raw);

            Assert.Single(result.UiElements);
            Assert.Equal("btn", result.UiElements[0].Id);
            Assert.Equal(10 + 50, result.UiElements[0].Center.X);
            Assert.Equal(20 + 15, result.UiElements[0].Center.Y);
        }

        [Fact]
        public void Parse_CenterAsNumber_StillParses()
        {
            // Регрессия: numeric center не сломался после добавления regex-strip.
            var raw = @"{
              ""description"": ""test"",
              ""ui_elements"": [
                {
                  ""id"": ""btn"",
                  ""type"": ""button"",
                  ""bounds"": { ""x"": 10, ""y"": 20, ""w"": 100, ""h"": 30 },
                  ""center"": { ""x"": 60, ""y"": 35 }
                }
              ]
            }";

            var result = ScreenDescriptionParser.Parse(raw);

            Assert.Single(result.UiElements);
            Assert.Equal(60, result.UiElements[0].Center.X);
            Assert.Equal(35, result.UiElements[0].Center.Y);
        }

        [Fact]
        public void Parse_CenterExpressionWithoutBounds_StripsCenter_ElementKept()
        {
            // Center удаляется, bounds не было — элемент остаётся,
            // но его Center не заполняется (0,0 — по умолчанию).
            var raw = @"{
              ""description"": ""test"",
              ""ui_elements"": [
                {
                  ""id"": ""btn"",
                  ""type"": ""button"",
                  ""label"": ""Submit"",
                  ""center"": { ""x"": 10 + 5, ""y"": 20 + 5 }
                }
              ]
            }";

            var result = ScreenDescriptionParser.Parse(raw);

            Assert.Single(result.UiElements);
            Assert.Equal("btn", result.UiElements[0].Id);
            Assert.Equal("Submit", result.UiElements[0].Label);
            Assert.Null(result.UiElements[0].Bounds);
            // Без bounds парсер не может пересчитать center — оставляет (0,0).
            Assert.Equal(0, result.UiElements[0].Center.X);
            Assert.Equal(0, result.UiElements[0].Center.Y);
        }
    }
}