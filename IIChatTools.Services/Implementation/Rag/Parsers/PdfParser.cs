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
        private readonly IOcrProgressTracker _progressTracker;
        private readonly ILogger<PdfParser> _logger;

        /// <summary>
        /// Создаёт парсер.
        /// </summary>
        /// <param name="ocr">OCR-сервис (Tesseract) — для fallback сканов.</param>
        /// <param name="ocrOptions">Настройки OCR (Enabled, RenderDpi, MaxPagesToOcr, ...).</param>
        /// <param name="progressTracker">
        /// Трекер прогресса OCR (KI-204) — для индикации «OCR: 2/3 страниц».
        /// </param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр null.</exception>
        public PdfParser(
            IOcrService ocr,
            IOptions<OcrOptions> ocrOptions,
            IOcrProgressTracker progressTracker,
            ILogger<PdfParser> logger)
        {
            _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
            _ocrOptions = (ocrOptions ?? throw new ArgumentNullException(nameof(ocrOptions))).Value;
            _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
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
        public Task<ParsedDocument> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return ParseAsync(filePath, options: null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<ParsedDocument> ParseAsync(
            string filePath,
            ParseOptions options,
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
            return await ParseInternalAsync(bytes, originalSize, options, cancellationToken);
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
            ParseOptions options,
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
                int savedPagesCount = 0;

                // v1.13.x (KI-205): если задана директория — сохраняем PNG каждой
                // страницы (независимо от OCR, чтобы пользователь мог посмотреть
                // оригинал любой страницы).
                var savePagesDir = options?.SavePagesDirectory;
                var shouldSavePages = !string.IsNullOrWhiteSpace(savePagesDir);
                var renderDpi = options?.RenderDpi ?? _ocrOptions.RenderDpi;

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

                    // KI-204: обновляем прогресс для UI (chip «OCR: 2/3»).
                    if (shouldOcr)
                    {
                        _progressTracker.Report(page.Number, totalPages);
                    }

                    // v1.13.x (KI-205): рендер PNG страницы — один раз.
                    // Используется или для OCR, или для просмотра (или для обоих).
                    byte[] pagePng = null;

                    if (shouldOcr || shouldSavePages)
                    {
                        try
                        {
                            // PdfPig: page.Number — 1-based; PDFtoImage: pageIndex — 0-based.
                            var pageIndex = page.Number - 1;
                            pagePng = RenderPageToPng(bytes, pageIndex, renderDpi);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "PDF: страница {Page}/{Total} — рендер PNG упал",
                                page.Number, totalPages);
                        }
                    }

                    // Сохранение PNG в workspace (KI-205).
                    if (shouldSavePages && pagePng != null)
                    {
                        try
                        {
                            var pagePath = Path.Combine(
                                savePagesDir, $"page-{page.Number}.png");
                            await File.WriteAllBytesAsync(
                                    pagePath, pagePng, cancellationToken)
                                .ConfigureAwait(false);
                            savedPagesCount++;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "PDF: не удалось сохранить PNG страницы {Page} в {Path}",
                                page.Number, savePagesDir);
                        }
                    }

                    // OCR-fallback.
                    if (shouldOcr && pagePng != null)
                    {
                        try
                        {
                            var ocrText = await _ocr
                                .RecognizeAsync(pagePng, cancellationToken)
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

                if (savedPagesCount > 0)
                {
                    _logger.LogInformation(
                        "PDF: сохранено {Count} PNG-страниц в {Dir}",
                        savedPagesCount, savePagesDir);
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

                // v1.13.x (KI-205): если PNG сохранялись — фиксируем количество.
                if (savedPagesCount > 0)
                {
                    metadata["savedPagesCount"] = savedPagesCount.ToString();
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
            // PDFtoImage 5.x: перегрузка
            // SavePng(string imageFilename, string pdfAsBase64String,
            //         Index page, string? password, RenderOptions options)
            // Первый параметр — путь к выходному PNG, второй — PDF
            // в виде base64-строки. PDF хранится в памяти (pdfBytes),
            // промежуточный файл не нужен.
            //
            // CA1416: SavePng помечен [SupportedOSPlatform] для Windows /
            // Linux / macOS. Проект таргетит plain net10.0 — анализатор
            // ругается, хотя API работает на всех целевых ОС. Подавляем
            // локально (прецедент — LocalHarnessVisionBackend, KI-131).
#pragma warning disable CA1416
            var tempPng = Path.ChangeExtension(Path.GetTempFileName(), ".png");

            try
            {
                Conversion.SavePng(
                    tempPng,
                    Convert.ToBase64String(pdfBytes),
                    page: new Index(pageIndex),
                    password: null,
                    options: new RenderOptions { Dpi = dpi });

                return File.ReadAllBytes(tempPng);
            }
            finally
            {
                try { if (File.Exists(tempPng)) File.Delete(tempPng); }
                catch { /* best-effort */ }
            }
#pragma warning restore CA1416
        }
    }
}