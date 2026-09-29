using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using UglyToad.PdfPig;

namespace IIChatTools.Services.Implementation.Rag.Parsers
{
    /// <summary>
    /// Парсер PDF-документов для RAG на базе PdfPig
    /// (v1.7.1, KI-104).
    ///
    /// <para>
    /// Извлекает текстовый слой PDF (без OCR). Для сканов без текстового слоя
    /// возвращает пустой <see cref="ParsedDocument.Text"/> — ограничение MVP,
    /// OCR планируется в v1.9+ (Tesseract).
    /// </para>
    ///
    /// <para>
    /// <b>Stateless, регистрируется как Singleton.</b> PdfPig API — синхронное,
    /// обёрнуто в <c>Task.Run</c> для неблокирующего выполнения в ASP.NET Core.
    /// </para>
    /// </summary>
    public sealed class PdfParser : IRagDocumentParser
    {
        /// <summary>Поддерживаемое расширение (с точкой, нижний регистр).</summary>
        private static readonly string[] _extensions = new[] { ".pdf" };

        /// <inheritdoc />
        public string Name => "PdfPig";

        /// <inheritdoc />
        public IReadOnlyList<string> SupportedExtensions => _extensions;

        /// <inheritdoc />
        public bool CanParse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext)
                && string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase);
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
                    $"Расширение '{Path.GetExtension(filePath)}' не поддерживается PdfParser");

            // Читаем всё в память (уже валидировано по размеру в
            // DocumentIngestionService через Rag:Ingestion:MaxFileSizeBytes).
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var originalSize = bytes.LongLength;

            // PdfPig — sync API, CPU-bound. Task.Run, чтобы не блокировать
            // поток из ThreadPool во время длительного парсинга.
            return await Task.Run(
                () => ParseInternal(bytes, originalSize),
                cancellationToken);
        }

        /// <summary>
        /// Синхронный парсинг (вызывается внутри <see cref="Task.Run(Action)"/>).
        /// </summary>
        /// <param name="bytes">Содержимое PDF-файла</param>
        /// <param name="originalSize">Размер файла в байтах</param>
        /// <returns>Извлечённый текст + метаданные</returns>
        /// <exception cref="InvalidDataException">
        /// Если PDF повреждён, зашифрован или не удаётся открыть.
        /// </exception>
        private static ParsedDocument ParseInternal(byte[] bytes, long originalSize)
        {
            PdfDocument doc;
            try
            {
                doc = PdfDocument.Open(bytes);
            }
            catch (Exception ex)
            {
                // PdfPig бросает разные типы на corrupt/encrypted PDF.
                // Приводим к единому InvalidDataException — для понятного
                // сообщения в DocumentIngestionService.
                throw new InvalidDataException(
                    "Не удалось открыть PDF. Возможные причины: файл повреждён " +
                    "или защищён паролем.", ex);
            }

            using (doc)
            {
                var sb = new StringBuilder();

                foreach (var page in doc.GetPages())
                {
                    var pageText = page.Text;
                    if (!string.IsNullOrEmpty(pageText))
                    {
                        sb.AppendLine(pageText);
                    }
                    // Разделитель страниц — пустая строка. Помогает chunking'у
                    // не «склеивать» конец страницы N и начало N+1.
                    sb.AppendLine();
                }

                // Нормализация переносов (аналогично PlainTextParser).
                var text = sb.ToString()
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n');

                var metadata = new Dictionary<string, string>
                {
                    ["format"] = "pdf",
                    ["parser"] = "PdfPig",
                    ["pageCount"] = doc.NumberOfPages.ToString()
                };

                return new ParsedDocument
                {
                    Text = text,
                    Metadata = metadata,
                    OriginalSizeBytes = originalSize,
                    PageCount = doc.NumberOfPages
                };
            }
        }
    }
}