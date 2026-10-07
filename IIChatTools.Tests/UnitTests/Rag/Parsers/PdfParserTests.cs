using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Rag.Parsers;
using IIChatTools.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Rag.Parsers
{
    /// <summary>
    /// Тесты <see cref="PdfParser"/> (v1.7.1, KI-104; OCR-fallback — KI-203).
    ///
    /// <para>
    /// Реальный PDF-контент в тестах не проверяется — PdfPig read-only,
    /// генерация PDF требует отдельной библиотеки (iTextSharp). Проверка
    /// end-to-end с валидным PDF — smoke-сценарий.
    /// </para>
    ///
    /// <para>
    /// Здесь — shape-тесты (<c>CanParse</c>, throws) + unit-тесты
    /// <see cref="PdfParser.ShouldOcrFallback"/> (KI-203) с fake OCR.
    /// </para>
    /// </summary>
    public class PdfParserTests
    {
        private static PdfParser CreateParser(
            FakeOcrService ocr = null,
            OcrOptions ocrOptions = null,
            FakeOcrProgressTracker progressTracker = null)
        {
            ocr ??= new FakeOcrService();
            ocrOptions ??= new OcrOptions { Enabled = true };
            progressTracker ??= new FakeOcrProgressTracker();
            return new PdfParser(
                ocr,
                Options.Create(ocrOptions),
                progressTracker,
                NullLogger<PdfParser>.Instance);
        }

        private static readonly PdfParser Parser = CreateParser();

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

        // ============ KI-203: конструктор ============

        [Fact]
        public void Constructor_NullOcr_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PdfParser(
                    null,
                    Options.Create(new OcrOptions()),
                    new FakeOcrProgressTracker(),
                    NullLogger<PdfParser>.Instance));
        }

        [Fact]
        public void Constructor_NullOptions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PdfParser(
                    new FakeOcrService(),
                    null,
                    new FakeOcrProgressTracker(),
                    NullLogger<PdfParser>.Instance));
        }

        [Fact]
        public void Constructor_NullProgressTracker_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PdfParser(
                    new FakeOcrService(),
                    Options.Create(new OcrOptions()),
                    null,
                    NullLogger<PdfParser>.Instance));
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PdfParser(
                    new FakeOcrService(),
                    Options.Create(new OcrOptions()),
                    new FakeOcrProgressTracker(),
                    null));
        }

        // ============ KI-203: ShouldOcrFallback ============

        [Theory]
        // (textLen, ocrReady, ocrEnabled, minChars, ocrPagesUsed, maxPages) → expected
        // (а) Всё включено, страница-скан (0 символов) → OCR.
        [InlineData(0,   true, true, 50, 0,   100, true)]
        // (б) Страница с текстом 100 ≥ 50 → OCR не нужен.
        [InlineData(100, true, true, 50, 0,   100, false)]
        // (в) Граница: 49 < 50 → OCR.
        [InlineData(49,  true, true, 50, 0,   100, true)]
        // (г) Граница: 50 ≥ 50 → не OCR.
        [InlineData(50,  true, true, 50, 0,   100, false)]
        // (д) OCR отключён в конфиге → не OCR.
        [InlineData(0,   true, false, 50, 0,  100, false)]
        // (е) OCR-сервис не готов (Linux / tessdata нет) → не OCR.
        [InlineData(0,   false, true, 50, 0,  100, false)]
        // (ж) Лимит страниц достигнут → не OCR.
        [InlineData(0,   true, true, 50, 100, 100, false)]
        // (з) Лимит не достигнут (99 из 100) → OCR.
        [InlineData(0,   true, true, 50, 99,  100, true)]
        public void ShouldOcrFallback_VariousInputs_ReturnsExpected(
            int textLayerLength,
            bool ocrReady,
            bool ocrEnabled,
            int minChars,
            int ocrPagesUsed,
            int maxPages,
            bool expected)
        {
            var actual = PdfParser.ShouldOcrFallback(
                textLayerLength, ocrReady, ocrEnabled,
                minChars, ocrPagesUsed, maxPages);

            Assert.Equal(expected, actual);
        }
    }
}