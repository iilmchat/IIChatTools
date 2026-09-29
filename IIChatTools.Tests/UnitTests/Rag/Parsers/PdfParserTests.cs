using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Implementation.Rag.Parsers;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Rag.Parsers
{
    /// <summary>
    /// Тесты <see cref="PdfParser"/> (v1.7.1, KI-104).
    ///
    /// <para>
    /// Реальный PDF-контент в тестах не проверяется — PdfPig read-only,
    /// генерация PDF требует отдельной библиотеки (iTextSharp). Проверка
    /// извлечения текста — smoke-сценарий в <c>docs/TESTING.md</c> § 3.5.
    /// </para>
    ///
    /// <para>
    /// Здесь — shape-тесты: <c>CanParse</c>, throws на отсутствующий файл,
    /// throws на неверное расширение, throws на повреждённый PDF.
    /// </para>
    /// </summary>
    public class PdfParserTests
    {
        private static readonly PdfParser Parser = new PdfParser();

        // ============ CanParse ============

        [Theory]
        [InlineData("file.pdf")]
        [InlineData("file.PDF")]
        [InlineData(@"C:\docs\contract.pdf")]
        [InlineData("/home/user/doc.pdf")]
        public void CanParse_PdfExtensions_ReturnsTrue(string path)
        {
            Assert.True(Parser.CanParse(path));
        }

        [Theory]
        [InlineData("file.txt")]
        [InlineData("file.docx")]
        [InlineData("file.md")]
        [InlineData("file")]
        [InlineData("")]
        [InlineData(null)]
        public void CanParse_NonPdf_ReturnsFalse(string path)
        {
            Assert.False(Parser.CanParse(path));
        }

        // ============ Metadata ============

        [Fact]
        public void Name_IsPdfPig()
        {
            Assert.Equal("PdfPig", Parser.Name);
        }

        [Fact]
        public void SupportedExtensions_ContainsPdf()
        {
            Assert.Contains(".pdf", Parser.SupportedExtensions);
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
                $"missing-{Guid.NewGuid():N}.pdf");

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => Parser.ParseAsync(missing));
        }

        [Fact]
        public async Task ParseAsync_WrongExtension_ThrowsNotSupported()
        {
            var temp = Path.GetTempFileName();      // .tmp — не PDF
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

        [Fact]
        public async Task ParseAsync_CorruptedPdf_ThrowsInvalidData()
        {
            // Пишем мусор с расширением .pdf.
            var temp = Path.Combine(Path.GetTempPath(),
                $"corrupt-{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(temp, Encoding.ASCII.GetBytes("not a pdf"));

            try
            {
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => Parser.ParseAsync(temp));
            }
            finally
            {
                TryDelete(temp);
            }
        }

        // ============ Helpers ============

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort */ }
        }
    }
}