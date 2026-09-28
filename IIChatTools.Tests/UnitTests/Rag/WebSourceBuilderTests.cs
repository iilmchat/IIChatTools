using System;
using System.Collections.Generic;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Rag;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Rag
{
    /// <summary>
    /// Тесты <see cref="WebSourceBuilder"/> (v1.6.1, KI-086-post, Шаг A5).
    ///
    /// <para>
    /// Покрывают: Build (null / empty / valid / limit / skip / dedup / normalize /
    /// snippet-truncate), BuildSingle (url required), BuildLabel (fallback-цепочка).
    /// </para>
    /// </summary>
    public class WebSourceBuilderTests
    {
        // ============================================================
        // Build
        // ============================================================

        /// <summary>
        /// <c>null</c> на входе — пустой список (без исключения).
        /// </summary>
        [Fact]
        public void Build_NullInput_ReturnsEmpty()
        {
            var result = WebSourceBuilder.Build(null, "web");

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        /// <summary>
        /// Пустой список на входе — пустой список на выходе.
        /// </summary>
        [Fact]
        public void Build_EmptyInput_ReturnsEmpty()
        {
            var result = WebSourceBuilder.Build(new List<RetrievedWebResult>(), "web");

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        /// <summary>
        /// Валидный результат — корректный маппинг во все поля ChatSourceDto.
        /// </summary>
        [Fact]
        public void Build_ValidResults_MapsAllFields()
        {
            var results = new[]
            {
                new RetrievedWebResult
                {
                    Title = "Москва — Википедия",
                    Url = "https://ru.wikipedia.org/wiki/Москва",
                    Snippet = "Столица России"
                }
            };

            var sources = WebSourceBuilder.Build(results, "wiki");

            Assert.Single(sources);
            var s = sources[0];
            Assert.Equal("wiki", s.Type);
            Assert.Equal("Москва — Википедия", s.Label);
            Assert.Equal("https://ru.wikipedia.org/wiki/Москва", s.Url);
            Assert.Equal("Столица России", s.Snippet);
            Assert.Null(s.DocumentPath);
            Assert.Null(s.ChunkIndex);
            Assert.Null(s.Score);
        }

        /// <summary>
        /// Ограничение maxCount: 5 результатов, maxCount=2 → 2.
        /// </summary>
        [Fact]
        public void Build_LimitRespected()
        {
            var results = new List<RetrievedWebResult>();
            for (int i = 0; i < 5; i++)
            {
                results.Add(new RetrievedWebResult
                {
                    Title = $"R{i}",
                    Url = $"https://example.com/{i}"
                });
            }

            var sources = WebSourceBuilder.Build(results, "web", maxCount: 2);

            Assert.Equal(2, sources.Count);
            Assert.Equal("R0", sources[0].Label);
            Assert.Equal("R1", sources[1].Label);
        }

        /// <summary>
        /// Результаты без title и url — пропускаются.
        /// </summary>
        [Fact]
        public void Build_SkipsEmptyResults()
        {
            var results = new[]
            {
                new RetrievedWebResult { Title = null, Url = null },            // skip
                new RetrievedWebResult { Title = "X", Url = "https://x.com" },  // keep
                new RetrievedWebResult { Title = "", Url = "  " }               // skip (whitespace)
            };

            var sources = WebSourceBuilder.Build(results, "web");

            Assert.Single(sources);
            Assert.Equal("X", sources[0].Label);
        }

        /// <summary>
        /// Дубликаты по URL — дедуплицируются (case-insensitive).
        /// </summary>
        [Fact]
        public void Build_DeduplicatesByUrl()
        {
            var results = new[]
            {
                new RetrievedWebResult { Title = "A", Url = "https://example.com/x" },
                new RetrievedWebResult { Title = "A2", Url = "https://example.com/x" },
                new RetrievedWebResult { Title = "A3", Url = "HTTPS://EXAMPLE.COM/X" },  // case-insensitive dup
                new RetrievedWebResult { Title = "B", Url = "https://example.com/y" }
            };

            var sources = WebSourceBuilder.Build(results, "web", maxCount: 10);

            Assert.Equal(2, sources.Count);
            Assert.Equal("A", sources[0].Label);
            Assert.Equal("B", sources[1].Label);
        }

        /// <summary>
        /// Нормализация type: trim + lower + fallback "web".
        /// </summary>
        [Theory]
        [InlineData("WIKI", "wiki")]
        [InlineData("  Wiki  ", "wiki")]
        [InlineData(null, "web")]
        [InlineData("", "web")]
        [InlineData("   ", "web")]
        public void Build_NormalizesType(string input, string expected)
        {
            var results = new[]
            {
                new RetrievedWebResult { Title = "X", Url = "https://x.com" }
            };

            var sources = WebSourceBuilder.Build(results, input);

            Assert.Single(sources);
            Assert.Equal(expected, sources[0].Type);
        }

        /// <summary>
        /// Длинный snippet обрезается до 200 символов (с «…»).
        /// </summary>
        [Fact]
        public void Build_TruncatesLongSnippet()
        {
            var longText = new string('a', 300);
            var results = new[]
            {
                new RetrievedWebResult
                {
                    Title = "X",
                    Url = "https://x.com",
                    Snippet = longText
                }
            };

            var sources = WebSourceBuilder.Build(results, "web");

            Assert.Single(sources);
            Assert.Equal(WebSourceBuilder.DefaultMaxCount, 5);   // sanity-check константы
            Assert.Equal(201, sources[0].Snippet.Length);        // 200 + «…»
            Assert.EndsWith("…", sources[0].Snippet);
        }

        // ============================================================
        // BuildSingle
        // ============================================================

        /// <summary>
        /// Пустой URL → <c>null</c> (без URL citation бессмысленна).
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BuildSingle_EmptyUrl_ReturnsNull(string url)
        {
            var result = WebSourceBuilder.BuildSingle(
                title: "Example",
                url: url,
                snippet: "text",
                type: "web");

            Assert.Null(result);
        }

        /// <summary>
        /// Валидный URL → корректный source.
        /// </summary>
        [Fact]
        public void BuildSingle_ValidUrl_ReturnsSource()
        {
            var result = WebSourceBuilder.BuildSingle(
                title: "Example Domain",
                url: "https://example.com",
                snippet: "This domain is for use...",
                type: "WEB");   // проверяем нормализацию

            Assert.NotNull(result);
            Assert.Equal("web", result.Type);
            Assert.Equal("Example Domain", result.Label);
            Assert.Equal("https://example.com", result.Url);
            Assert.Equal("This domain is for use...", result.Snippet);
        }

        // ============================================================
        // BuildLabel
        // ============================================================

        /// <summary>
        /// Приоритет label: title → url → "(unknown)".
        /// </summary>
        [Theory]
        [InlineData("Title", "https://x.com", "Title")]
        [InlineData(null, "https://x.com", "https://x.com")]
        [InlineData("", "https://x.com", "https://x.com")]
        [InlineData("  ", "https://x.com", "https://x.com")]
        [InlineData(null, null, "(unknown)")]
        [InlineData("", "", "(unknown)")]
        public void BuildLabel_FallbackChain(string title, string url, string expected)
        {
            var label = WebSourceBuilder.BuildLabel(title, url);

            Assert.Equal(expected, label);
        }
    }
}