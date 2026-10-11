using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Rag.Parsers
{
    /// <summary>
    /// Парсер изображений для RAG через OCR (Tesseract)
    /// (v1.13.14, KI-238).
    ///
    /// <para>
    /// Поддерживает <c>.png</c>, <c>.jpg</c>, <c>.jpeg</c>, <c>.webp</c>,
    /// <c>.gif</c>, <c>.bmp</c>. Извлекает текст через
    /// <see cref="IOcrService.RecognizeAsync"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Graceful degradation:</b> если <see cref="IOcrService.IsReady"/> = false
    /// (Linux / native lib Tesseract не загружен / OCR выключен в конфиге) —
    /// возвращает <see cref="ParsedDocument"/> с пустым текстом и метаданным
    /// <c>ocrAvailable=false</c>. Attachment создастся с 0 чанков, ingest
    /// не упадёт. Тот же паттерн, что у <c>PdfParser</c> для сканов PDF (KI-203).
    /// </para>
    ///
    /// <para>
    /// <b>Stateless, регистрируется как Singleton.</b> <see cref="IOcrService"/>
    /// (Singleton) инжектится через конструктор.
    /// </para>
    /// </summary>
    public sealed class ImageOcrParser : IRagDocumentParser
    {
        /// <summary>
        /// Поддерживаемые расширения (нижний регистр, с точкой).
        /// </summary>
        private static readonly string[] _extensions = new[]
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
        };

        /// <summary>
        /// HashSet для O(1) проверки расширений в <see cref="CanParse"/>.
        /// </summary>
        private static readonly HashSet<string> _extensionSet =
            new HashSet<string>(_extensions, StringComparer.OrdinalIgnoreCase);

        private readonly IOcrService _ocrService;
        private readonly ILogger<ImageOcrParser> _logger;

        /// <summary>
        /// Создаёт парсер.
        /// </summary>
        /// <param name="ocrService">Сервис OCR (Tesseract).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров null.</exception>
        public ImageOcrParser(
            IOcrService ocrService,
            ILogger<ImageOcrParser> logger)
        {
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "ImageOcr";

        /// <inheritdoc />
        public IReadOnlyList<string> SupportedExtensions => _extensions;

        /// <inheritdoc />
        public bool CanParse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext) && _extensionSet.Contains(ext);
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
                    $"Расширение '{Path.GetExtension(filePath)}' не поддерживается ImageOcrParser");

            var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var originalSize = bytes.LongLength;

            var metadata = new Dictionary<string, string>
            {
                ["format"] = ext,
                ["parser"] = Name
            };

            // 1. OCR недоступен (Linux / выключен в конфиге) — graceful пустой текст.
            if (!_ocrService.IsReady)
            {
                metadata["ocrAvailable"] = "false";
                _logger.LogDebug(
                    "ImageOcrParser: OCR недоступен (IsReady=false), " +
                    "файл '{File}' пропущен без текста.", filePath);

                return new ParsedDocument
                {
                    Text = string.Empty,
                    Metadata = metadata,
                    OriginalSizeBytes = originalSize,
                    PageCount = 1
                };
            }

            // 2. OCR через Tesseract.
            string text;
            try
            {
                text = await _ocrService.RecognizeAsync(bytes, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                // Пустой imageBytes — по контракту IOcrService.
                _logger.LogWarning(ex,
                    "ImageOcrParser: пустое изображение '{File}'.", filePath);
                text = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ImageOcrParser: ошибка OCR для '{File}'. " +
                    "Attachment создастся с 0 чанков.", filePath);
                text = string.Empty;
            }

            // 3. Нормализация.
            text = (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim();

            metadata["ocrAvailable"] = "true";
            metadata["ocrChars"] = text.Length.ToString();

            return new ParsedDocument
            {
                Text = text,
                Metadata = metadata,
                OriginalSizeBytes = originalSize,
                PageCount = 1
            };
        }
    }
}