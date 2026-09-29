using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IIChatTools.Services.Implementation.Rag.Parsers;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Rag.Parsers
{
    /// <summary>
    /// Тесты <see cref="DocxParser"/> (v1.7.1, KI-104).
    ///
    /// <para>
    /// DOCX генерируется самим OpenXml SDK — тесты проверяют полный путь:
    /// создание валидного .docx → парсинг → извлечённый текст.
    /// </para>
    /// </summary>
    public class DocxParserTests
    {
        private static readonly DocxParser Parser = new DocxParser();

        // ============ CanParse ============

        [Theory]
        [InlineData("file.docx")]
        [InlineData("file.DOCX")]
        [InlineData(@"C:\docs\report.docx")]
        [InlineData("/home/user/doc.docx")]
        public void CanParse_DocxExtensions_ReturnsTrue(string path)
        {
            Assert.True(Parser.CanParse(path));
        }

        [Theory]
        [InlineData("file.pdf")]
        [InlineData("file.doc")]      // старый формат — НЕ поддержан
        [InlineData("file.txt")]
        [InlineData("file")]
        [InlineData("")]
        [InlineData(null)]
        public void CanParse_NonDocx_ReturnsFalse(string path)
        {
            Assert.False(Parser.CanParse(path));
        }

        // ============ Metadata ============

        [Fact]
        public void Name_IsOpenXml()
        {
            Assert.Equal("OpenXml", Parser.Name);
        }

        [Fact]
        public void SupportedExtensions_ContainsDocx()
        {
            Assert.Contains(".docx", Parser.SupportedExtensions);
        }

        // ============ ParseAsync — error cases ============

        [Fact]
        public async Task ParseAsync_NullPath_ThrowsArgumentNull()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => Parser.ParseAsync(null));
        }

        [Fact]
        public async Task ParseAsync_MissingFile_ThrowsFileNotFound()
        {
            var missing = Path.Combine(Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}.docx");

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => Parser.ParseAsync(missing));
        }

        [Fact]
        public async Task ParseAsync_WrongExtension_ThrowsNotSupported()
        {
            var temp = Path.GetTempFileName();
            try
            {
                await Assert.ThrowsAsync<NotSupportedException>(
                    () => Parser.ParseAsync(temp));
            }
            finally
            {
                TryDelete(temp);
            }
        }

        // ============ ParseAsync — success cases ============

        [Fact]
        public async Task ParseAsync_ValidDocx_ExtractsText()
        {
            var temp = CreateTempDocxPath();
            CreateDocx(temp, "Hello, world!", "Second paragraph.");

            try
            {
                var result = await Parser.ParseAsync(temp);

                Assert.NotNull(result);
                Assert.Contains("Hello, world!", result.Text);
                Assert.Contains("Second paragraph.", result.Text);
                Assert.Equal("docx", result.Metadata["format"]);
                Assert.Equal("OpenXml", result.Metadata["parser"]);
                Assert.True(result.OriginalSizeBytes > 0);
                Assert.Null(result.PageCount);   // в DOCX нет страниц
            }
            finally
            {
                TryDelete(temp);
            }
        }

        [Fact]
        public async Task ParseAsync_EmptyDocx_ReturnsEmptyText()
        {
            var temp = CreateTempDocxPath();
            CreateDocx(temp /* без параграфов */);

            try
            {
                var result = await Parser.ParseAsync(temp);

                Assert.NotNull(result);
                Assert.Equal(string.Empty, result.Text.Trim());
                Assert.Equal("docx", result.Metadata["format"]);
            }
            finally
            {
                TryDelete(temp);
            }
        }

        // ============ Helpers ============

        private static string CreateTempDocxPath()
            => Path.Combine(Path.GetTempPath(),
                $"test-{Guid.NewGuid():N}.docx");

        /// <summary>
        /// Генерирует минимальный валидный DOCX с заданными абзацами.
        /// </summary>
        private static void CreateDocx(string filePath, params string[] paragraphs)
        {
            using var doc = WordprocessingDocument.Create(
                filePath, WordprocessingDocumentType.Document);

            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();

            var body = new Body();
            foreach (var text in paragraphs)
            {
                body.AppendChild(new Paragraph(
                    new Run(new Text(text))));
            }
            mainPart.Document.AppendChild(body);
            mainPart.Document.Save();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort */ }
        }
    }
}