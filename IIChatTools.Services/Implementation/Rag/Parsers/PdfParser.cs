using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFtoImage;
using UglyToad.PdfPig;

namespace IIChatTools.Services.Implementation.Rag.Parsers
{
    /// <summary>
    /// Парсер PDF-документов для RAG на базе PdfPig
    /// (v1.7.1, KI-104; OCR-fallback — v1.13.x, KI-203).
    ///
    /// <para>
    /// Извлекает текстовый слой PDF. Для сканов без текстового слоя
    /// (страница с &lt; <c>MinTextCharsPerPage</c> символов) применяет
    /// OCR-fallback через <see cref="IOcrService"/> (Tesseract).
    /// </para>
    ///
    /// <para>
    /// <b>Stateless, регистрируется как Singleton.</b> PdfPig / PDFtoImage —
    /// sync API, обёрнуты в <c>Task.Run</c>.
    /// </para>
    /// </summary>
    public sealed class PdfParser : IRagDocumentParser
    {
        /// <summary>Поддерживаемое расширение (с точкой, нижний регистр).</summary>
        private static readonly string[] _extensions = new[] { ".pdf" };

        private readonly IOcrService _ocr;
        private readonly OcrOptions _ocrOptions;
        private readonly ILogger<PdfParser> _logger;

        /// <summary>
        /// Создаёт парсер.
        /// </summary>
        /// <param name="ocr">OCR-сервис (Tesseract) — для fallback сканов.</param>
        /// <param name="ocrOptions">Настройки OCR (Enabled, RenderDpi, MaxPagesToOcr, ...).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр null.</exception>
        public PdfParser(
            IOcrService ocr,
            IOptions<OcrOptions> ocrOptions,
            ILogger<PdfParser> logger)
        {
            _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
            _ocrOptions = (ocrOptions ?? throw new ArgumentNullException(nameof(ocrOptions))).Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

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

            // KI-203: ParseInternalAsync включает OCR-вызовы (async), поэтому
            // не оборачиваем в Task.Run (sync-over-async). PDFtoImage-рендер
            // внутри sync, но короткий (~100-200 мс на страницу).
            return await ParseInternalAsync(bytes, originalSize, cancellationToken);
        }

        /// <summary>
        /// KI-203: решает, применять ли OCR к странице с заданной длиной
        /// текстового слоя. Public static — для unit-тестируемости без
        /// валидного PDF-файла.
        /// </summary>
        /// <param name="textLayerLength">Длина текстового слоя (0 для скана).</param>
        /// <param name="ocrServiceReady">Готов ли OCR-сервис.</param>
        /// <param name="ocrEnabled">Включён ли OCR в конфиге.</param>
        /// <param name="minTextCharsPerPage">Порог, ниже которого страница — скан.</param>
        /// <param name="ocrPagesSoFar">Сколько страниц уже ушло в OCR.</param>
        /// <param name="maxPagesToOcr">Лимит страниц на OCR.</param>
        /// <returns>true — нужен OCR-fallback.</returns>
        public static bool ShouldOcrFallback(
            int textLayerLength,
            bool ocrServiceReady,
            bool ocrEnabled,
            int minTextCharsPerPage,
            int ocrPagesSoFar,
            int maxPagesToOcr)
        {
            if (!ocrEnabled) return false;
            if (!ocrServiceReady) return false;
            if (textLayerLength >= minTextCharsPerPage) return false;
            if (ocrPagesSoFar >= maxPagesToOcr) return false;
            return true;
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Основной парсинг: извлекает текстовый слой, при необходимости —
        /// применяет OCR к отдельным страницам (KI-203).
        /// </summary>
        private async Task<ParsedDocument> ParseInternalAsync(
            byte[] bytes,
            long originalSize,
            CancellationToken cancellationToken)
        {
            PdfDocument doc;
            try
            {
                doc = PdfDocument.Open(bytes);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "Не удалось открыть PDF. Возможные причины: файл повреждён " +
                    "или защищён паролем.", ex);
            }

            using (doc)
            {
                var sb = new StringBuilder();
                int ocrPagesUsed = 0;
                int totalPages = doc.NumberOfPages;

                foreach (var page in doc.GetPages())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var pageText = page.Text ?? string.Empty;
                    string usedText = pageText;

                    // KI-203: OCR-fallback для страниц-сканов.
                    var shouldOcr = ShouldOcrFallback(
                        pageText.Length,
                        _ocr.IsReady,
                        _ocrOptions.Enabled,
                        _ocrOptions.MinTextCharsPerPage,
                        ocrPagesUsed,
                        _ocrOptions.MaxPagesToOcr);

                    if (shouldOcr)
                    {
                        try
                        {
                            // PdfPig: page.Number — 1-based; PDFtoImage: pageIndex — 0-based.
                            var pageIndex = page.Number - 1;
                            var png = RenderPageToPng(bytes, pageIndex, _ocrOptions.RenderDpi);

                            var ocrText = await _ocr
                                .RecognizeAsync(png, cancellationToken)
                                .ConfigureAwait(false);

                            if (!string.IsNullOrWhiteSpace(ocrText))
                            {
                                usedText = ocrText;
                                ocrPagesUsed++;

                                _logger.LogInformation(
                                    "OCR: страница {Page}/{Total} распознана " +
                                    "({Chars} символов, скан)",
                                    page.Number, totalPages, ocrText.Length);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "OCR: страница {Page}/{Total} — ошибка, " +
                                "используем текстовый слой как есть",
                                page.Number, totalPages);
                        }
                    }

                    if (!string.IsNullOrEmpty(usedText))
                    {
                        sb.AppendLine(usedText);
                    }
                    // Разделитель страниц.
                    sb.AppendLine();
                }

                // Нормализация переносов.
                var text = sb.ToString()
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n');

                var metadata = new Dictionary<string, string>
                {
                    ["format"] = "pdf",
                    ["parser"] = "PdfPig",
                    ["pageCount"] = totalPages.ToString()
                };

                if (ocrPagesUsed > 0)
                {
                    metadata["ocrPagesUsed"] = ocrPagesUsed.ToString();
                }

                return new ParsedDocument
                {
                    Text = text,
                    Metadata = metadata,
                    OriginalSizeBytes = originalSize,
                    PageCount = totalPages
                };
            }
        }

        /// <summary>
        /// KI-203: рендерит страницу PDF в PNG-байты через PDFtoImage
        /// (обёртка над PDFium).
        /// </summary>
        /// <param name="pdfBytes">Содержимое PDF.</param>
        /// <param name="pageIndex">0-based индекс страницы.</param>
        /// <param name="dpi">Разрешение рендера (200 — стандарт).</param>
        /// <returns>PNG-байты.</returns>
        private static byte[] RenderPageToPng(byte[] pdfBytes, int pageIndex, int dpi)
        {
            // PDFtoImage 5.x: публичный API работает через файловые пути
            // (Conversion.SavePng(string pdfFile, string outputFile, Index page, ...)).
            // Index — 0-based (default = 0 = первая страница).
            //
            // CA1416: SavePng помечен [SupportedOSPlatform] для Windows /
            // Linux / macOS. Проект таргетит plain net10.0 — анализатор
            // ругается, хотя API работает на всех целевых ОС. Подавляем
            // локально (прецедент — LocalHarnessVisionBackend, KI-131).
#pragma warning disable CA1416
            var tempPdf = Path.GetTempFileName();
            var tempPng = Path.ChangeExtension(tempPdf, ".png");

            try
            {
                File.WriteAllBytes(tempPdf, pdfBytes);

                // PDFtoImage 5.0.0: page: Index (0-based).
                Conversion.SavePng(
                    tempPdf,
                    tempPng,
                    page: new Index(pageIndex),
                    password: null,
                    options: new RenderOptions { Dpi = dpi });

                return File.ReadAllBytes(tempPng);
            }
            finally
            {
                try { if (File.Exists(tempPdf)) File.Delete(tempPdf); }
                catch { /* best-effort */ }

                try { if (File.Exists(tempPng)) File.Delete(tempPng); }
                catch { /* best-effort */ }
            }
#pragma warning restore CA1416
        }
    }
}