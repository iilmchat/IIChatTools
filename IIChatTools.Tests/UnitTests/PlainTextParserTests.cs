using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using IIChatTools.Services.Implementation.Rag.Parsers;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты <see cref="PlainTextParser"/>
    /// (v1.5.0, KI-083, Шаг 4A).
    ///
    /// <para>
    /// 8 тестов из DESIGN § 4.5.8. Все используют временные файлы
    /// (создаются с уникальным GUID, удаляются в finally).
    /// </para>
    /// </summary>
    public class PlainTextParserTests
    {
        /// <summary>Парсер без состояния — переиспользуем между тестами.</summary>
        private static readonly PlainTextParser Parser = new PlainTextParser();

        /// <summary>
        /// Хелпер: создаёт временный файл с заданным расширением и текстом.
        /// </summary>
        private static string WriteTempFile(string extension, string content, Encoding encoding = null)
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + extension);
            File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// Хелпер: создаёт временный файл с заданными байтами.
        /// </summary>
        private static string WriteTempFile(string extension, byte[] bytes)
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        // ============ CanParse ============

        /// <summary>
        /// .txt / .md / .csv — поддерживаются.
        /// </summary>
        [Fact]
        public void CanParse_TxtMdCsv_ReturnsTrue()
        {
            Assert.True(Parser.CanParse("file.txt"));
            Assert.True(Parser.CanParse("notes.md"));
            Assert.True(Parser.CanParse("data.csv"));
            Assert.True(Parser.CanParse("D:\\some\\path\\file.log"));
        }

        /// <summary>
        /// .pdf / .docx / .doc / .xlsx — НЕ поддерживаются в MVP.
        /// </summary>
        [Fact]
        public void CanParse_PdfDocx_ReturnsFalse()
        {
            Assert.False(Parser.CanParse("document.pdf"));
            Assert.False(Parser.CanParse("report.docx"));
            Assert.False(Parser.CanParse("old.doc"));
            Assert.False(Parser.CanParse("table.xlsx"));
        }

        // ============ ParseAsync ============

        /// <summary>
        /// UTF-8 без BOM — текст читается корректно (кириллица тоже).
        /// </summary>
        [Fact]
        public async Task ParseAsync_Utf8File_ReadsCorrectly()
        {
            var path = WriteTempFile(".txt", "Привет, мир! Hello, world.", new UTF8Encoding(false));
            try
            {
                var result = await Parser.ParseAsync(path);

                Assert.Equal("Привет, мир! Hello, world.", result.Text);
                Assert.True(result.OriginalSizeBytes > 0);
                Assert.Equal("txt", result.Metadata["format"]);
                Assert.Equal("PlainText", result.Metadata["parser"]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// UTF-8 с BOM — BOM не попадает в результат.
        /// </summary>
        [Fact]
        public async Task ParseAsync_Utf8BomFile_StripsBom()
        {
            // BOM (EF BB BF) + "Hello" в UTF-8.
            var bytes = new byte[]
            {
                0xEF, 0xBB, 0xBF,                       // UTF-8 BOM
                (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o'
            };
            var path = WriteTempFile(".txt", bytes);
            try
            {
                var result = await Parser.ParseAsync(path);

                // Единственная проверка: текст должен быть ровно "Hello".
                Assert.Equal("Hello", result.Text);

                // И на всякий случай: в тексте вообще нет BOM-символа.
                Assert.DoesNotContain('\uFEFF', result.Text);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Windows-1251 файл — кириллица читается корректно.
        /// </summary>
        [Fact]
        public async Task ParseAsync_Windows1251File_ReadsCyrillic()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var cp1251 = Encoding.GetEncoding(1251);
            var original = "Привет, мир! Это тест Windows-1251.";

            var path = WriteTempFile(".txt", original, cp1251);
            try
            {
                var result = await Parser.ParseAsync(path);

                Assert.Equal(original, result.Text);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Разные переносы строк (\r\n, \n, \r) — нормализуются в \n.
        /// </summary>
        [Fact]
        public async Task ParseAsync_NormalizesLineEndings()
        {
            var content = "line1\r\nline2\nline3\rline4";
            var path = WriteTempFile(".txt", content);
            try
            {
                var result = await Parser.ParseAsync(path);

                Assert.Equal("line1\nline2\nline3\nline4", result.Text);
                Assert.DoesNotContain("\r", result.Text);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Markdown с frontmatter (--- ... ---) — frontmatter удаляется.
        /// </summary>
        [Fact]
        public async Task ParseAsync_MarkdownWithFrontmatter_Strips()
        {
            var content =
                "---\n" +
                "title: Test\n" +
                "author: Someone\n" +
                "---\n" +
                "\n" +
                "# Header\n" +
                "Body content.";

            var path = WriteTempFile(".md", content);
            try
            {
                var result = await Parser.ParseAsync(path);

                Assert.DoesNotContain("title: Test", result.Text);
                Assert.DoesNotContain("author: Someone", result.Text);
                Assert.Contains("# Header", result.Text);
                Assert.Contains("Body content.", result.Text);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Пустой файл — пустой текст.
        /// </summary>
        [Fact]
        public async Task ParseAsync_EmptyFile_ReturnsEmpty()
        {
            var path = WriteTempFile(".txt", "");
            try
            {
                var result = await Parser.ParseAsync(path);

                Assert.Equal(string.Empty, result.Text);
                Assert.Equal(0, result.OriginalSizeBytes);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}