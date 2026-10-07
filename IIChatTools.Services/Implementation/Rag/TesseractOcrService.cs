using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tesseract;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// OCR-сервис на базе Tesseract.NET (KI-203).
    /// Singleton: один <c>TesseractEngine</c> на всё время жизни приложения.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Платформа:</b> Tesseract native lib — Windows-only в NuGet.
    /// На Linux <see cref="IsReady"/> = false → <c>PdfParser</c> пропускает
    /// OCR. Для Linux-прода — установить <c>libtesseract4</c> + native
    /// libtesseract.so в Dockerfile (отдельная задача).
    /// </para>
    /// <para>
    /// <b>Thread-safety:</b> <c>TesseractEngine</c> не thread-safe. Вызовы
    /// сериализуются через <c>lock</c> (Singleton = один engine).
    /// </para>
    /// <para>
    /// <b>Lazy init:</b> engine создаётся при первом <see cref="RecognizeAsync"/>.
    /// Если tessdata не найдена — <see cref="IsReady"/> останется false,
    /// в лог уйдёт <c>Warning</c>.
    /// </para>
    /// </remarks>
    public sealed class TesseractOcrService : IOcrService, IDisposable
    {
        private readonly OcrOptions _options;
        private readonly ILogger<TesseractOcrService> _logger;
        private readonly string _tessDataPath;
        private readonly Lazy<TesseractEngine> _engine;
        private readonly object _sync = new object();
        private volatile bool _initFailed;
        private bool _disposed;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="options">Настройки OCR.</param>
        /// <param name="pathProvider">Провайдер ContentRoot (для резолва tessdata).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр null.</exception>
        public TesseractOcrService(
            IOptions<OcrOptions> options,
            IAppPathProvider pathProvider,
            ILogger<TesseractOcrService> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (pathProvider == null) throw new ArgumentNullException(nameof(pathProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _options = options.Value;

            // Резолвим tessdata относительно ContentRoot.
            // KI-203: IAppPathProvider (KI-100) — обёртка над
            // IWebHostEnvironment.ContentRootPath, свойство.
            var rel = _options.TessDataPath ?? "tools/tessdata";
            _tessDataPath = Path.IsPathRooted(rel)
                ? rel
                : Path.GetFullPath(Path.Combine(pathProvider.ContentRootPath, rel));

            // Lazy engine — не падаем при старте, если tessdata нет.
            _engine = new Lazy<TesseractEngine>(
                CreateEngineInternal,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc />
        public bool IsReady
        {
            get
            {
                if (!_options.Enabled) return false;
                if (_initFailed) return false;
                if (!Directory.Exists(_tessDataPath)) return false;

                // Проверка наличия хотя бы одного .traineddata.
                try
                {
                    var files = Directory.GetFiles(_tessDataPath, "*.traineddata");
                    if (files.Length == 0) return false;
                }
                catch
                {
                    return false;
                }

                // Force-init (при первом обращении) — но не падаем при ошибке.
                try { _ = _engine.Value; }
                catch { return false; }

                return true;
            }
        }

        /// <inheritdoc />
        public string EngineName => "tesseract";

        /// <inheritdoc />
        public Task<string> RecognizeAsync(
            byte[] imageBytes,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                throw new InvalidOperationException(
                    "OCR отключён в конфиге (Rag:Ingestion:Ocr:Enabled = false).");
            }
            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new ArgumentException("Пустое изображение.", nameof(imageBytes));
            }
            if (_initFailed)
            {
                throw new InvalidOperationException(
                    "OCR engine не инициализирован (tessdata не найдена или native lib недоступна).");
            }

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // TesseractEngine не thread-safe — сериализуем.
                lock (_sync)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var pix = Pix.LoadFromMemory(imageBytes);
                    using var page = _engine.Value.Process(pix);
                    var text = page.GetText() ?? string.Empty;
                    return text;
                }
            }, cancellationToken);
        }

        // ============================================================
        // Private
        // ============================================================

        private TesseractEngine CreateEngineInternal()
        {
            try
            {
                if (!Directory.Exists(_tessDataPath))
                {
                    _logger.LogWarning(
                        "OCR: tessdata не найдена в {Path} — OCR отключён. " +
                        "Запустите scripts/setup/download-tessdata.ps1",
                        _tessDataPath);
                    _initFailed = true;
                    throw new DirectoryNotFoundException(
                        $"tessdata не найдена: {_tessDataPath}");
                }

                _logger.LogInformation(
                    "OCR: инициализация Tesseract (lang={Lang}, datapath={Path})",
                    _options.Languages, _tessDataPath);

                var engine = new TesseractEngine(
                    _tessDataPath,
                    _options.Languages,
                    EngineMode.Default);

                _logger.LogInformation("OCR: Tesseract engine готов");
                return engine;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OCR: не удалось инициализировать Tesseract — OCR будет отключён");
                _initFailed = true;
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_engine.IsValueCreated)
            {
                try { _engine.Value.Dispose(); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "OCR: ошибка Dispose TesseractEngine");
                }
            }
        }
    }
}