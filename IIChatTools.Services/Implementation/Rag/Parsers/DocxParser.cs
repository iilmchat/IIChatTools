using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag.Parsers
{
    /// <summary>
    /// Парсер DOCX-документов для RAG на базе DocumentFormat.OpenXml
    /// (v1.7.1, KI-104).
    ///
    /// <para>
    /// Поддерживает только <c>.docx</c> (OpenXML). Старый формат <c>.doc</c>
    /// (OLE Compound Document) не поддерживается — при попытке распарсить
    /// такой файл <see cref="CanParse"/> вернёт <c>false</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Stateless, регистрируется как Singleton.</b> OpenXml SDK — синхронный
    /// API, обёрнут в <c>Task.Run</c>.
    /// </para>
    /// </summary>
    public sealed class DocxParser : IRagDocumentParser
    {
        /// <summary>Поддерживаемое расширение (с точкой, нижний регистр).</summary>
        private static readonly string[] _extensions = new[] { ".docx" };

        /// <inheritdoc />
        public string Name => "OpenXml";

        /// <inheritdoc />
        public IReadOnlyList<string> SupportedExtensions => _extensions;

        /// <inheritdoc />
        public bool CanParse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext)
                && string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public async Task<ParsedDocument> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл не найден", filePath);

            if (!CanParse(filePath))
                throw new NotSupportedException(
                    $"Расширение '{Path.GetExtension(filePath)}' не поддерживается DocxParser");

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var originalSize = bytes.LongLength;

            return await Task.Run(
                () => ParseInternal(bytes, originalSize),
                cancellationToken);
        }

        /// <summary>
        /// Синхронный парсинг DOCX из байтов.
        /// </summary>
        /// <param name="bytes">Содержимое .docx</param>
        /// <param name="originalSize">Размер файла в байтах</param>
        /// <returns>Извлечённый текст + метаданные</returns>
        /// <exception cref="InvalidDataException">
        /// Если DOCX повреждён или не является OpenXML-файлом.
        /// </exception>
        private static ParsedDocument ParseInternal(byte[] bytes, long originalSize)
        {
            using var ms = new MemoryStream(bytes, writable: false);

            WordprocessingDocument doc;
            try
            {
                // isEditable: false — read-only, не блокируем файл на запись.
                doc = WordprocessingDocument.Open(ms, isEditable: false);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "Не удалось открыть DOCX. Возможно, файл повреждён " +
                    "или это старый формат .doc (не поддерживается).", ex);
            }

            using (doc)
            {
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null)
                {
                    // Пустой DOCX — валидный сценарий (без body).
                    return new ParsedDocument
                    {
                        Text = string.Empty,
                        Metadata = new Dictionary<string, string>
                        {
                            ["format"] = "docx",
                            ["parser"] = "OpenXml"
                        },
                        OriginalSizeBytes = originalSize,
                        PageCount = null   // в DOCX нет страниц (потоковый формат)
                    };
                }

                var sb = new StringBuilder();
                int paragraphCount = 0;

                foreach (var paragraph in body.Descendants<Paragraph>())
                {
                    var text = paragraph.InnerText;
                    if (!string.IsNullOrEmpty(text))
                    {
                        sb.AppendLine(text);
                        paragraphCount++;
                    }
                }

                // Нормализация переносов.
                var fullText = sb.ToString()
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n');

                var metadata = new Dictionary<string, string>
                {
                    ["format"] = "docx",
                    ["parser"] = "OpenXml",
                    ["paragraphCount"] = paragraphCount.ToString()
                };

                return new ParsedDocument
                {
                    Text = fullText,
                    Metadata = metadata,
                    OriginalSizeBytes = originalSize,
                    PageCount = null
                };
            }
        }
    }
}