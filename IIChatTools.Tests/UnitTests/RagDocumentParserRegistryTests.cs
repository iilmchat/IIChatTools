using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Rag.Parsers;
using IIChatTools.Services.Interfaces;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты реестра парсеров
    /// (v1.5.0, KI-083, Шаг 4B).
    ///
    /// <para>
    /// 3 теста из DESIGN § 4.5.8. Используют <see cref="PlainTextParser"/>
    /// (реальный) и <see cref="FakePdfParser"/> (для проверки union
    /// расширений без подключения PdfPig).
    /// </para>
    /// </summary>
    public class RagDocumentParserRegistryTests
    {
        /// <summary>
        /// Fake-парсер для проверки union расширений (не тащит PdfPig в тесты).
        /// </summary>
        private sealed class FakePdfParser : IRagDocumentParser
        {
            public string Name => "FakePdf";
            public IReadOnlyList<string> SupportedExtensions => new[] { ".pdf" };

            public bool CanParse(string filePath) =>
                !string.IsNullOrEmpty(filePath)
                && Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

            public Task<ParsedDocument> ParseAsync(string filePath, CancellationToken cancellationToken = default)
                => Task.FromResult(new ParsedDocument { Text = "fake", OriginalSizeBytes = 4 });
        }

        /// <summary>
        /// Resolve для .txt → возвращает PlainTextParser.
        /// </summary>
        [Fact]
        public void Resolve_TxtFile_ReturnsPlainTextParser()
        {
            var registry = new RagDocumentParserRegistry(new IRagDocumentParser[]
            {
                new PlainTextParser()
            });

            var parser = registry.Resolve("notes.txt");

            Assert.NotNull(parser);
            Assert.Equal("PlainText", parser.Name);
        }

        /// <summary>
        /// Resolve для неизвестного расширения (.xyz) → null.
        /// </summary>
        [Fact]
        public void Resolve_UnknownExt_ReturnsNull()
        {
            var registry = new RagDocumentParserRegistry(new IRagDocumentParser[]
            {
                new PlainTextParser()
            });

            var parser = registry.Resolve("archive.xyz");

            Assert.Null(parser);
        }

        /// <summary>
        /// GetAllSupportedExtensions объединяет расширения всех парсеров
        /// (PlainText + FakePdf → .txt, .md, ..., .pdf).
        /// </summary>
        [Fact]
        public void GetAllSupportedExtensions_ReturnsUnion()
        {
            var registry = new RagDocumentParserRegistry(new IRagDocumentParser[]
            {
                new PlainTextParser(),
                new FakePdfParser()
            });

            var extensions = registry.GetAllSupportedExtensions();

            // Из PlainTextParser.
            Assert.Contains(".txt", extensions);
            Assert.Contains(".md", extensions);
            Assert.Contains(".cs", extensions);
            // Из FakePdfParser.
            Assert.Contains(".pdf", extensions);
        }
    }
}