using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Speech;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;

namespace IIChatTools.Services.Implementation.Speech
{
    /// <summary>
    /// Реализация распознавания речи через Whisper.net (whisper.cpp bindings).
    /// Singleton: модель загружается лениво при первом вызове, кэшируется.
    /// v1.13.0 (KI-140).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Паттерн:</b> <see cref="WhisperFactory"/> — Singleton (держит модель),
    /// <c>WhisperProcessor</c> создаётся per-request через
    /// <c>factory.CreateBuilder().Build()</c> — thread-safe по документации Whisper.net.
    /// </para>
    /// <para>
    /// <b>Формат входа:</b> WAV 16 kHz mono PCM. Клиент кодирует через
    /// <c>OfflineAudioContext</c> (см. DESIGN § 4.2).
    /// </para>
    /// </remarks>
    public sealed class WhisperNetTranscriptionService : ISpeechRecognitionService, IDisposable
    {
        private readonly SpeechOptions _options;
        private readonly IAppPathProvider _pathProvider;
        private readonly ILogger<WhisperNetTranscriptionService> _logger;

        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private WhisperFactory _factory;
        private bool _disposed;

        /// <inheritdoc />
        public bool IsReady => _factory != null;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="options">Настройки (секция <c>Speech</c>)</param>
        /// <param name="pathProvider">Резолвер путей (ContentRootPath)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public WhisperNetTranscriptionService(
            IOptions<SpeechOptions> options,
            IAppPathProvider pathProvider,
            ILogger<WhisperNetTranscriptionService> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value;
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<TranscriptionResult> TranscribeAsync(
            Stream wavStream,
            CancellationToken cancellationToken = default)
        {
            if (wavStream == null) throw new ArgumentNullException(nameof(wavStream));

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var sw = Stopwatch.StartNew();

            // 1. Копируем в MemoryStream — FormFileStream из ASP.NET Core
            //    может быть не-seekable, а whisper.cpp требует seekable input.
            //    При MaxFileSizeBytes = 10 MB — копия дешёвая.
            using var ms = new MemoryStream();
            await wavStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            ms.Position = 0;

            // 2. Создаём процессор (thread-safe паттерн Whisper.net).
            var builder = _factory.CreateBuilder();

            // 3. Язык: "auto" → авто-детект; иначе — ISO-код.
            if (string.Equals(_options.Language, "auto", StringComparison.OrdinalIgnoreCase))
            {
                builder = builder.WithLanguageDetection();
            }
            else
            {
                builder = builder.WithLanguage(_options.Language);
            }

            using var processor = builder.Build();

            var sb = new StringBuilder();

            // 4. Стрим сегментов — Whisper.net отдаёт по мере обработки.
            await foreach (var segment in processor.ProcessAsync(ms, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(segment.Text))
                {
                    sb.Append(segment.Text);
                }
            }

            sw.Stop();

            var text = sb.ToString().Trim();

            _logger.LogInformation(
                "Whisper: распознано {Chars} символов за {Ms} мс (язык: {Lang})",
                text.Length, sw.ElapsedMilliseconds, _options.Language);

            return new TranscriptionResult
            {
                Text = text,
                // Whisper.net 1.8.x не отдаёт определённый язык в SegmentData —
                // возвращаем то, что было в опциях. Технический долг: § 11.3 DESIGN.
                Language = _options.Language,
                DurationMs = 0,
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }

        /// <summary>
        /// Ленивая инициализация: загружает модель при первом вызове.
        /// Потокобезопасно через <see cref="SemaphoreSlim"/>.
        /// </summary>
        /// <param name="ct">Токен отмены</param>
        /// <exception cref="FileNotFoundException">
        /// Если файл модели отсутствует по резолвленному пути.
        /// </exception>
        private async Task EnsureInitializedAsync(CancellationToken ct)
        {
            if (_factory != null) return;

            await _initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_factory != null) return;

                var modelPath = _options.ModelPath;
                if (!Path.IsPathRooted(modelPath))
                {
                    modelPath = Path.Combine(_pathProvider.ContentRootPath, modelPath);
                }

                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException(
                        $"Модель Whisper не найдена: {modelPath}. " +
                        "Запустите scripts/setup/download-whisper-model.ps1.",
                        modelPath);
                }

                _logger.LogInformation("Загрузка модели Whisper: {Path}", modelPath);
                var sw = Stopwatch.StartNew();
                _factory = WhisperFactory.FromPath(modelPath);
                sw.Stop();
                _logger.LogInformation("Модель Whisper загружена за {Ms} мс", sw.ElapsedMilliseconds);
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _factory?.Dispose();
            _initLock?.Dispose();
        }
    }
}