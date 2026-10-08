using System.Collections.Generic;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="OcrVlMergeHelper"/>
    /// (v1.13.x, KI-137, Ф7).
    /// <para>
    /// Проверяют:
    /// <list type="bullet">
    ///   <item>защиту от null / пустого OCR (graceful fallback);</item>
    ///   <item>матчинг OCR-слов с ui_elements через центр bbox (max distance);</item>
    ///   <item>установку <c>Source</c> = <c>"ocr"</c> / <c>"merged"</c> / <c>"vl"</c>;</item>
    ///   <item>конвертацию координат по screenshot-scale;</item>
    ///   <item>сортировку слов по позиции в пределах элемента;</item>
    ///   <item>склейку <c>OcrText</c> в строки по overlap Y-диапазона.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class OcrVlMergeHelperTests
    {
        // ============ Helpers ============

        private static WordBoxDto W(
            string text, double x, double y, double w = 20, double h = 10)
        {
            return new WordBoxDto { Text = text, X = x, Y = y, W = w, H = h };
        }

        private static UiElementDto Element(
            string id, int cx, int cy, string label = null)
        {
            return new UiElementDto
            {
                Id = id,
                Type = "button",
                Label = label,
                Center = new UiElementCenterDto { X = cx, Y = cy },
                Bounds = new UiElementBoundsDto { X = cx - 20, Y = cy - 10, W = 40, H = 20 }
            };
        }

        private static PageTextLayerDto Layer(params WordBoxDto[] words)
        {
            return new PageTextLayerDto { Words = new List<WordBoxDto>(words) };
        }

        // ============ 1. Null screen ============

        [Fact]
        public void Merge_NullScreen_CreatesNewAndFillsOcrText()
        {
            var layer = Layer(W("Hello", 10, 20));

            var result = OcrVlMergeHelper.Merge(null, layer, 1.0, 1.0, 30);

            Assert.NotNull(result);
            Assert.Equal("Hello", result.OcrText);
            Assert.Equal(1, result.OcrWordsCount);
            Assert.Empty(result.UiElements);
        }

        // ============ 2. Null ocrLayer ============

        [Fact]
        public void Merge_NullOcrLayer_ReturnsScreenWithZeroCount()
        {
            var screen = new ScreenDescriptionDto { Description = "x" };

            var result = OcrVlMergeHelper.Merge(screen, null, 1.0, 1.0, 30);

            Assert.Same(screen, result);
            Assert.Equal(0, result.OcrWordsCount);
            Assert.Null(result.OcrText);
        }

        // ============ 3. Empty words ============

        [Fact]
        public void Merge_EmptyWords_ReturnsScreenUnchanged()
        {
            var screen = new ScreenDescriptionDto { Description = "x" };
            var layer = new PageTextLayerDto { Words = new List<WordBoxDto>() };

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            Assert.Same(screen, result);
            Assert.Equal(0, result.OcrWordsCount);
            Assert.Null(result.OcrText);
        }

        // ============ 4. Match by distance — empty label → "ocr" ============

        [Fact]
        public void Merge_MatchByDistance_EmptyLabel_SetsSourceOcr()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto> { Element("btn", 100, 100) }
            };
            // Word в центре (105, 100) — 5 px от центра элемента.
            var layer = Layer(W("Submit", 95, 95));

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            Assert.Single(result.UiElements);
            Assert.Equal("Submit", result.UiElements[0].Label);
            Assert.Equal("ocr", result.UiElements[0].Source);
        }

        // ============ 5. Match by distance — existing label → "merged" ============

        [Fact]
        public void Merge_MatchByDistance_ExistingLabel_SetsSourceMerged()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto> { Element("btn", 100, 100, label: "Sub") }
            };
            var layer = Layer(W("Submit", 95, 95));

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            Assert.Single(result.UiElements);
            Assert.Equal("Submit", result.UiElements[0].Label);
            Assert.Equal("merged", result.UiElements[0].Source);
        }

        // ============ 6. Exceeds max distance → no match, but in OcrText ============

        [Fact]
        public void Merge_ExceedsMaxDistance_NoMatchButOcrTextFilled()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto> { Element("btn", 100, 100) }
            };
            // Word в центре (510, 505) — далеко от (100, 100).
            var layer = Layer(W("FarAway", 500, 500));

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            Assert.Single(result.UiElements);
            Assert.Null(result.UiElements[0].Label);            // не тронут
            Assert.Equal("vl", result.UiElements[0].Source);    // default
            Assert.Equal("FarAway", result.OcrText);            // в общий текст
            Assert.Equal(1, result.OcrWordsCount);
        }

        // ============ 7. Respects screenshot scale ============

        [Fact]
        public void Merge_RespectsScreenshotScale()
        {
            // scale = 2: OCR-слово в full-res (200, 200) → screenshot-space (100, 100).
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto> { Element("btn", 100, 100) }
            };
            // Word: X=190, Y=190, W=20, H=20 → центр (200, 200) в full-res.
            var layer = Layer(W("Scaled", 190, 190, 20, 20));

            var result = OcrVlMergeHelper.Merge(screen, layer, 2.0, 2.0, 30);

            Assert.Equal("Scaled", result.UiElements[0].Label);
            Assert.Equal("ocr", result.UiElements[0].Source);
        }

        // ============ 8. Multiple words per element — sorted by position ============

        [Fact]
        public void Merge_MultipleWordsPerElement_SortsByPosition()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto> { Element("btn", 100, 100) }
            };
            // Все слова на Y=95 (центр Y=100). По X: 90 / 110 / 125.
            // Расположены в массиве в неправильном порядке.
            var layer = Layer(
                W("world", 110, 95),
                W("Hello", 90, 95),
                W("big", 125, 95));

            // maxDistancePx = 40, чтобы "big" (35 px от центра) тоже матчился.
            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 40);

            Assert.Equal("Hello world big", result.UiElements[0].Label);
        }

        // ============ 9. BuildFullText — groups into lines ============

        [Fact]
        public void Merge_BuildFullText_GroupsIntoLines()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto>()   // нет элементов — только OcrText
            };
            var layer = Layer(
                W("Line1A", 0, 0),
                W("Line1B", 50, 0),
                W("Line2A", 0, 100),
                W("Line2B", 50, 100));

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            // Строки: Line1 (Y=0), Line2 (Y=100) — tolerance 5 px.
            var lines = result.OcrText
                .Replace("\r\n", "\n")
                .Split('\n');

            Assert.Equal(2, lines.Length);
            Assert.Equal("Line1A Line1B", lines[0]);
            Assert.Equal("Line2A Line2B", lines[1]);
        }

        // ============ 10. No UI elements → only OcrText ============

        [Fact]
        public void Merge_NoUiElements_FillsOnlyOcrText()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto>()
            };
            var layer = Layer(W("Hello", 10, 20), W("World", 50, 20));

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            Assert.Empty(result.UiElements);
            Assert.Equal("Hello World", result.OcrText);
            Assert.Equal(2, result.OcrWordsCount);
        }

        // ============ 11. Null Center → skipped for matching ============

        [Fact]
        public void Merge_NullCenterElement_SkippedForMatching()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto>
                {
                    new UiElementDto { Id = "no-center", Type = "button" }  // Center = null
                }
            };
            var layer = Layer(W("Hello", 10, 20));

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            Assert.Single(result.UiElements);
            Assert.Null(result.UiElements[0].Label);                // не тронут
            Assert.Equal("vl", result.UiElements[0].Source);
            Assert.Equal("Hello", result.OcrText);                  // в общий текст попал
        }

        // ============ 12. Zero scale → treated as 1.0 ============

        [Fact]
        public void Merge_ZeroScale_TreatedAsOne()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto> { Element("btn", 100, 100) }
            };
            var layer = Layer(W("Test", 95, 95));   // центр (105, 100) → 5 px от элемента

            var result = OcrVlMergeHelper.Merge(screen, layer, 0, 0, 30);

            Assert.Equal("Test", result.UiElements[0].Label);
            Assert.Equal("ocr", result.UiElements[0].Source);
        }

        // ============ 13. Two elements — each gets own words ============

        [Fact]
        public void Merge_TwoElements_EachGetsOwnWords()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto>
                {
                    Element("btn1", 100, 100),
                    Element("btn2", 300, 100)
                }
            };
            var layer = Layer(
                W("First", 95, 95),
                W("Second", 295, 95));

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            Assert.Equal(2, result.UiElements.Count);
            Assert.Equal("First", result.UiElements[0].Label);
            Assert.Equal("Second", result.UiElements[1].Label);
            Assert.Equal("ocr", result.UiElements[0].Source);
            Assert.Equal("ocr", result.UiElements[1].Source);
        }

        // ============ 14. Whitespace-only words skipped ============

        [Fact]
        public void Merge_WhitespaceWords_SkippedInCountAndMerge()
        {
            var screen = new ScreenDescriptionDto
            {
                UiElements = new List<UiElementDto> { Element("btn", 100, 100) }
            };
            var layer = Layer(
                W("", 95, 95),
                W("   ", 100, 95),
                W("Real", 95, 95));

            var result = OcrVlMergeHelper.Merge(screen, layer, 1.0, 1.0, 30);

            Assert.Equal(1, result.OcrWordsCount);     // пустые не считаются
            Assert.Equal("Real", result.UiElements[0].Label);
        }
    }
}