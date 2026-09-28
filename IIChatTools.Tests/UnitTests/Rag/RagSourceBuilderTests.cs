using System;
using System.Collections.Generic;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Rag;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Rag
{
    /// <summary>
    /// Тесты для <see cref="RagSourceBuilder"/>
    /// (v1.6.0, KI-086, Шаг 5.1).
    ///
    /// <para>
    /// Покрывают: преобразование <see cref="RetrievedChunkDto"/> → <see cref="ChatSourceDto"/>,
    /// формирование label из пути, обрезку snippet до 200 символов.
    /// </para>
    /// </summary>
    public class RagSourceBuilderTests
    {
        // ============================================================
        // Build
        // ============================================================

        /// <summary>
        /// <c>null</c> на входе — пустой список на выходе (без исключения).
        /// </summary>
        [Fact]
        public void Build_NullInput_ReturnsEmpty()
        {
            var result = RagSourceBuilder.Build(null);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        /// <summary>
        /// Пустой список на входе — пустой список на выходе.
        /// </summary>
        [Fact]
        public void Build_EmptyInput_ReturnsEmpty()
        {
            var result = RagSourceBuilder.Build(new List<RetrievedChunkDto>());

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        /// <summary>
        /// Валидный чанк — корректный маппинг во все поля ChatSourceDto.
        /// </summary>
        [Fact]
        public void Build_ValidChunk_MapsAllFields()
        {
            var chunk = new RetrievedChunkDto
            {
                ChunkId = 42,
                Text = "Some text",
                Score = 0.87f,
                DocumentPath = "docs/development/RULES.md",
                ChunkIndex = 15,
                IndexName = "project_docs"
            };

            var result = RagSourceBuilder.Build(new[] { chunk });

            Assert.Single(result);
            var s = result[0];
            Assert.Equal("rag", s.Type);
            Assert.Equal("RULES.md", s.Label);
            Assert.Null(s.Url);
            Assert.Equal("docs/development/RULES.md", s.DocumentPath);
            Assert.Equal(15, s.ChunkIndex);
            Assert.Equal(0.87f, s.Score);
            Assert.Equal("Some text", s.Snippet);
        }

        /// <summary>
        /// Несколько чанков — порядок сохранён, все преобразованы.
        /// </summary>
        [Fact]
        public void Build_MultipleChunks_PreservesOrder()
        {
            var chunks = new[]
            {
                new RetrievedChunkDto { DocumentPath = "a.md", Text = "A", ChunkIndex = 0, Score = 0.9f },
                new RetrievedChunkDto { DocumentPath = "b.md", Text = "B", ChunkIndex = 1, Score = 0.8f },
                new RetrievedChunkDto { DocumentPath = "c.md", Text = "C", ChunkIndex = 2, Score = 0.7f }
            };

            var result = RagSourceBuilder.Build(chunks);

            Assert.Equal(3, result.Count);
            Assert.Equal("a.md", result[0].Label);
            Assert.Equal("b.md", result[1].Label);
            Assert.Equal("c.md", result[2].Label);
        }

        // ============================================================
        // BuildLabel
        // ============================================================

        /// <summary>
        /// <c>null</c> / пустой путь → «(unknown)».
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BuildLabel_NullOrEmpty_ReturnsUnknown(string documentPath)
        {
            var label = RagSourceBuilder.BuildLabel(documentPath);

            Assert.Equal("(unknown)", label);
        }

        /// <summary>
        /// Абсолютный Windows-путь → имя файла (без пути).
        /// </summary>
        [Fact]
        public void BuildLabel_AbsoluteWindowsPath_ReturnsFileName()
        {
            var label = RagSourceBuilder.BuildLabel(
                @"C:\Projects\AI\IIChatTools\docs\development\RULES.md");

            Assert.Equal("RULES.md", label);
        }

        /// <summary>
        /// Относительный путь → имя файла.
        /// </summary>
        [Fact]
        public void BuildLabel_RelativePath_ReturnsFileName()
        {
            var label = RagSourceBuilder.BuildLabel("docs/development/RULES.md");

            Assert.Equal("RULES.md", label);
        }

        /// <summary>
        /// Смешанные разделители (<c>\</c> и <c>/</c>) → имя файла.
        /// Кросс-платформенная проверка: до фикса на Linux этот тест падал
        /// (Path.GetFileName не распознаёт <c>\</c> как разделитель).
        /// </summary>
        [Theory]
        [InlineData(@"docs\development\RULES.md")]        // только backslash
        [InlineData(@"C:/Projects/IIChatTools/docs/development/RULES.md")]  // только forward
        [InlineData(@"C:\Projects/AI\IIChatTools\docs/development\RULES.md")]  // смешанные
        public void BuildLabel_MixedSeparators_ReturnsFileName(string documentPath)
        {
            var label = RagSourceBuilder.BuildLabel(documentPath);

            Assert.Equal("RULES.md", label);
        }

        // ============================================================
        // TruncateSnippet
        // ============================================================

        /// <summary>
        /// Текст короче лимита → возвращается как есть.
        /// </summary>
        [Fact]
        public void TruncateSnippet_ShortText_ReturnsAsIs()
        {
            var text = "Short text under 200 chars";

            var result = RagSourceBuilder.TruncateSnippet(text);

            Assert.Equal(text, result);
        }

        /// <summary>
        /// Текст длиннее лимита → обрезан до 200 + многоточие.
        /// </summary>
        [Fact]
        public void TruncateSnippet_LongText_TruncatesWithEllipsis()
        {
            var text = new string('a', 300);

            var result = RagSourceBuilder.TruncateSnippet(text);

            Assert.Equal(RagSourceBuilder.SnippetMaxLength + 1, result.Length);   // +1 символ «…»
            Assert.EndsWith("…", result);
        }

        /// <summary>
        /// Текст точно по лимиту → без многоточия.
        /// </summary>
        [Fact]
        public void TruncateSnippet_ExactlyMaxLength_NoEllipsis()
        {
            var text = new string('a', RagSourceBuilder.SnippetMaxLength);

            var result = RagSourceBuilder.TruncateSnippet(text);

            Assert.Equal(RagSourceBuilder.SnippetMaxLength, result.Length);
            Assert.DoesNotContain("…", result);
        }

        /// <summary>
        /// <c>null</c> / пустой текст → пустая строка (не падает).
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TruncateSnippet_NullOrEmpty_ReturnsEmpty(string text)
        {
            var result = RagSourceBuilder.TruncateSnippet(text);

            Assert.Equal(string.Empty, result);
        }
    }
}