using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;   // KI-207-fix: CamelCasePropertyNamesContractResolver
using PDFtoImage;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
// KI-207: WordExtractor — extension-метод GetWords() живёт здесь.
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

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
                int savedTextLayersCount = 0;

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

                    // v1.13.x (KI-207): построение text layer.
                    // Порядок: PNG → OCR (или PdfPig) → textLayer → сохранение.
                    //
                    // ВАЖНО: один вызов OCR — RecognizeWithLayoutAsync даёт
                    // и текст, и word boxes. Раньше вызывались оба метода
                    // (RecognizeAsync для usedText + RecognizeWithLayoutAsync
                    // для слоя) — это двойной OCR, дорого.
                    PageTextLayerDto textLayer = null;

                    if (pagePng != null)
                    {
                        try
                        {
                            var (pngW, pngH) = GetPngDimensions(pagePng);
                            if (pngW > 0 && pngH > 0)
                            {
                                if (shouldOcr)
                                {
                                    // Скан → один OCR-вызов с bbox'ами.
                                    var ocrLayer = await _ocr
                                        .RecognizeWithLayoutAsync(
                                            pagePng, pngW, pngH, cancellationToken)
                                        .ConfigureAwait(false);

                                    if (ocrLayer?.Words?.Count > 0)
                                    {
                                        textLayer = ocrLayer;

                                        // Восстанавливаем текст из слов:
                                        // пробел между словами.
                                        var ocrText = string.Join(" ",
                                            ocrLayer.Words.Select(w => w.Text));

                                        if (!string.IsNullOrWhiteSpace(ocrText))
                                        {
                                            usedText = ocrText;
                                            ocrPagesUsed++;

                                            _logger.LogInformation(
                                                "OCR: страница {Page}/{Total} распознана " +
                                                "({Chars} символов, {Words} слов, скан)",
                                                page.Number, totalPages,
                                                ocrText.Length, ocrLayer.Words.Count);
                                        }
                                    }
                                }
                                else
                                {
                                    // Текстовый PDF → PdfPig word boxes.
                                    textLayer = ExtractWordsFromPdfPage(
                                        page, pngW, pngH);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "PDF: страница {Page}/{Total} — построение " +
                                "text layer упало (не критично)",
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

                    // v1.13.x (KI-207): сохранение text layer рядом с PNG.
                    // KI-207-fix: camelCase — фронт (chat.js) читает
                    // dto.width / dto.words / dto.height без нормализации.
                    if (shouldSavePages && textLayer != null && textLayer.Words.Count > 0)
                    {
                        try
                        {
                            var jsonPath = Path.Combine(
                                savePagesDir, $"page-{page.Number}.json");
                            var jsonSettings = new JsonSerializerSettings
                            {
                                ContractResolver =
                                    new CamelCasePropertyNamesContractResolver()
                            };
                            var json = JsonConvert.SerializeObject(
                                textLayer, jsonSettings);
                            await File.WriteAllTextAsync(
                                    jsonPath, json, Encoding.UTF8,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            savedTextLayersCount++;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "PDF: не удалось сохранить text layer страницы {Page} в {Path}",
                                page.Number, savePagesDir);
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
                        "PDF: сохранено {Count} PNG-страниц в {Dir} " +
                        "(с text layer: {Layers})",
                        savedPagesCount, savePagesDir, savedTextLayersCount);
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
        /// v1.13.x (KI-207): извлекает word boxes из текстового PDF
        /// через PdfPig <see cref="Page.GetWords()"/>.
        ///
        /// <para>
        /// PdfPig отдаёт координаты в PDF-точках (origin — bottom-left, y↑).
        /// Конвертируем в natural PNG pixels (top-left, y↓) через
        /// масштабирование по ширине/высоте страницы.
        /// </para>
        /// </summary>
        /// <param name="page">PdfPig страница.</param>
        /// <param name="pngW">Ширина PNG в пикселях.</param>
        /// <param name="pngH">Высота PNG в пикселях.</param>
        private static PageTextLayerDto ExtractWordsFromPdfPage(
            Page page, int pngW, int pngH)
        {
            var layer = new PageTextLayerDto { Width = pngW, Height = pngH };

            var pdfPageW = page.Width;    // PDF points
            var pdfPageH = page.Height;   // PDF points
            if (pdfPageW <= 0 || pdfPageH <= 0)
                return layer;

            var scaleX = pngW / pdfPageW;
            var scaleY = pngH / pdfPageH;

            // PdfPig 0.1.9: DefaultWordExtractor.Instance — статический singleton,
            // принимает IEnumerable<Letter> (page.Letters).
            // GetWords() extension-метод (page.GetWords()) требует using
            // UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor — добавлен.
            foreach (var word in UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor.NearestNeighbourWordExtractor.Instance.GetWords(page.Letters))
            {
                var text = word.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var rect = word.BoundingBox;   // PdfRectangle (Left, Bottom, Right, Top)
                var w = (rect.Right - rect.Left) * scaleX;
                var h = (rect.Top - rect.Bottom) * scaleY;
                if (w <= 0 || h <= 0) continue;

                layer.Words.Add(new WordBoxDto
                {
                    Text = text,
                    X = rect.Left * scaleX,
                    // PDF y↑: top в точках → PNG y↓: y = (pageHeight - top) * scale
                    Y = (pdfPageH - rect.Top) * scaleY,
                    W = w,
                    H = h
                });
            }

            return layer;
        }

        /// <summary>
        /// v1.13.x (KI-207): читает ширину/высоту PNG из заголовка (IHDR).
        /// </summary>
        /// <param name="png">PNG-байты.</param>
        /// <returns>(width, height) или (0, 0) при ошибке.</returns>
        private static (int width, int height) GetPngDimensions(byte[] png)
        {
            // PNG signature (8 байт) + длина IHDR (4) + "IHDR" (4) + width (4) + height (4).
            if (png == null || png.Length < 24) return (0, 0);

            // Проверка сигнатуры PNG.
            if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47)
                return (0, 0);

            int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return (w, h);
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