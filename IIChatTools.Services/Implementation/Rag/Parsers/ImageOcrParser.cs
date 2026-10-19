using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Rag.Parsers
{
    /// <summary>
    /// Парсер изображений для RAG через OCR (Tesseract).
    /// Поддерживает PNG, JPG, WEBP, GIF, BMP и TIFF (включая multipage).
    /// </summary>
    public sealed class ImageOcrParser : IRagDocumentParser
    {
        private static readonly string[] _extensions = new[]
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff"
        };

        private static readonly HashSet<string> _extensionSet =
            new HashSet<string>(_extensions, StringComparer.OrdinalIgnoreCase);

        private readonly IOcrService _ocrService;
        private readonly ILogger<ImageOcrParser> _logger;

        public ImageOcrParser(IOcrService ocrService, ILogger<ImageOcrParser> logger)
        {
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "ImageOcr";
        public IReadOnlyList<string> SupportedExtensions => _extensions;

        public bool CanParse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext) && _extensionSet.Contains(ext);
        }

        public async Task<ParsedDocument> ParseAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Файл не найден", filePath);
            if (!CanParse(filePath)) throw new NotSupportedException($"Расширение '{Path.GetExtension(filePath)}' не поддерживается ImageOcrParser");

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
                _logger.LogDebug("ImageOcrParser: OCR недоступен (IsReady=false), файл '{File}' пропущен.", filePath);
                return new ParsedDocument { Text = string.Empty, Metadata = metadata, OriginalSizeBytes = originalSize, PageCount = 1 };
            }

            // 2. Проверка на multipage TIFF (только для Windows, где доступен System.Drawing).
            if ((ext == "tif" || ext == "tiff") && OperatingSystem.IsWindows())
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    using var image = Image.FromStream(ms);
                    var frameCount = image.GetFrameCount(FrameDimension.Page);

                    if (frameCount > 1)
                    {
                        _logger.LogInformation("ImageOcrParser: multipage TIFF '{File}' ({Count} страниц).", filePath, frameCount);
                        var textBuilder = new System.Text.StringBuilder();
                        
                        for (int i = 0; i < frameCount; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            image.SelectActiveFrame(FrameDimension.Page, i);
                            
                            using var pageMs = new MemoryStream();
                            image.Save(pageMs, ImageFormat.Png);
                            var pageBytes = pageMs.ToArray();
                            
                            var pageText = await _ocrService.RecognizeAsync(pageBytes, cancellationToken);
                            textBuilder.AppendLine($"\n\n--- page {i + 1} ---\n\n");
                            textBuilder.Append(pageText);
                        }

                        metadata["ocrAvailable"] = "true";
                        metadata["pageCount"] = frameCount.ToString();

                        return new ParsedDocument
                        {
                            Text = textBuilder.ToString().Trim(),
                            Metadata = metadata,
                            OriginalSizeBytes = originalSize,
                            PageCount = frameCount
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ImageOcrParser: не удалось обработать multipage TIFF как многстраничный. Fallback на 1 страницу.");
                }
            }

            // 3. OCR для обычных изображений (или fallback для TIFF).
            string text;
            try
            {
                text = await _ocrService.RecognizeAsync(bytes, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ImageOcrParser: ошибка OCR для '{File}'.", filePath);
                text = string.Empty;
            }

            metadata["ocrAvailable"] = "true";
            metadata["ocrChars"] = text.Length.ToString();

            return new ParsedDocument
            {
                Text = (text ?? string.Empty).Replace("\r\n", "\n").Trim(),
                Metadata = metadata,
                OriginalSizeBytes = originalSize,
                PageCount = 1
            };
        }
    }
}