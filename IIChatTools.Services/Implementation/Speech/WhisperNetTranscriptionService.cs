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

            // 2. Читаем длительность из WAV-заголовка (заодно закрывает § 11.3 DESIGN).
            //    Если короче MinAudioDurationMs — не тратим CPU на Whisper.
            var durationMs = TryReadWavDurationMs(ms);
            if (durationMs.HasValue && durationMs.Value < _options.MinAudioDurationMs)
            {
                _logger.LogDebug(
                    "Whisper: аудио слишком короткое ({Ms} мс < {Min} мс) — пропускаем",
                    durationMs.Value, _options.MinAudioDurationMs);
                ms.Position = 0;
                return new TranscriptionResult
                {
                    Text = string.Empty,
                    Language = _options.Language,
                    DurationMs = durationMs.Value,
                    ProcessingMs = sw.ElapsedMilliseconds
                };
            }
            ms.Position = 0;

            // 3. Создаём процессор (thread-safe паттерн Whisper.net).
            var builder = _factory.CreateBuilder();

            // 3.1. Язык: "auto" → авто-детект; иначе — ISO-код.
            if (string.Equals(_options.Language, "auto", StringComparison.OrdinalIgnoreCase))
            {
                builder = builder.WithLanguageDetection();
            }
            else
            {
                builder = builder.WithLanguage(_options.Language);
            }

            // 3.2. Порог «no-speech» (v1.13.0-fix). По умолчанию 0.6 —
            //      Whisper часто «галлюцинирует» на тишине: [BLANK_AUDIO],
            //      [MUSIC], [SOUND]. Поднимаем до 0.8 — модель чаще
            //      классифицирует тишину как «не речь» и возвращает пустоту.
            builder = builder.WithNoSpeechThreshold(_options.NoSpeechThreshold);

            using var processor = builder.Build();

            var sb = new StringBuilder();

            // 4. Стрим сегментов — Whisper.net отдаёт по мере обработки.
            await foreach (var segment in processor.ProcessAsync(ms, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(segment.Text)) continue;

                // 4.1. Фильтр служебных маркеров Whisper (v1.13.0-fix).
                //      Whisper на тишине/шуме выдаёт текст вида [BLANK_AUDIO],
                //      [MUSIC], [SOUND] — это НЕ контент, это маркеры.
                var cleaned = StripWhisperMarkers(segment.Text);
                if (!string.IsNullOrEmpty(cleaned))
                {
                    sb.Append(cleaned);
                }
            }

            sw.Stop();

            var text = sb.ToString().Trim();

            _logger.LogInformation(
                "Whisper: распознано {Chars} символов за {Ms} мс (язык: {Lang}, длительность: {Dur} мс)",
                text.Length, sw.ElapsedMilliseconds, _options.Language, durationMs ?? 0);

            return new TranscriptionResult
            {
                Text = text,
                Language = _options.Language,
                DurationMs = durationMs ?? 0,
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }

        /// <summary>
        /// Список служебных маркеров Whisper, которые могут попасть в результат
        /// на тишине/шуме. Не являются контентом. v1.13.0-fix.
        /// </summary>
        private static readonly string[] WhisperArtifactMarkers =
        {
            "[BLANK_AUDIO]", "[blank_audio]",
            "[ Silence ]", "[SILENCE]", "[silence]",
            "[MUSIC]", "[music]", "[ Music ]",
            "[SOUND]", "[sound]", "[ Sound ]",
            "[NOISE]", "[noise]",
            "[INAUDIBLE]", "[inaudible]",
            "[LAUGHTER]", "[laughter]",
            "[APPLAUSE]", "[applause]",
            "[Sighs]", "[sighs]",
            "(тишина)", "(музыка)", "(шум)",
        };

        /// <summary>
        /// Удаляет из текста служебные маркеры Whisper. Регистронезависимо.
        /// Возвращает строку без маркеров (возможно, пустую). v1.13.0-fix.
        /// </summary>
        /// <param name="text">Сырой текст сегмента от Whisper.</param>
        /// <returns>Текст без служебных маркеров.</returns>
        private static string StripWhisperMarkers(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var result = text;
            foreach (var marker in WhisperArtifactMarkers)
            {
                if (result.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = result.Replace(marker, string.Empty, StringComparison.OrdinalIgnoreCase);
                }
            }
            return result;
        }

        /// <summary>
        /// Пытается прочитать длительность WAV-файла из заголовка (без полного парсинга).
        /// WAV = RIFF (12 байт) + fmt-chunk (24 байта) + data-chunk (8 байт + данные).
        /// Формула: <c>dataSize / byteRate</c> (сек) × 1000 = мс.
        /// Возвращает <c>null</c>, если заголовок невалидный (не WAV / битый).
        /// v1.13.0-fix (закрывает технический долг § 11.3 DESIGN).
        /// </summary>
        /// <param name="stream">Поток с WAV (позиция будет изменена; после вызова — сброшена в 0).</param>
        /// <returns>Длительность в мс или null.</returns>
        private static long? TryReadWavDurationMs(Stream stream)
        {
            if (!stream.CanSeek) return null;
            if (stream.Length < 44) return null;

            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

                // RIFF + WAVE
                var riff = new string(reader.ReadChars(4));
                if (riff != "RIFF") return null;
                _ = reader.ReadUInt32();   // file size
                var wave = new string(reader.ReadChars(4));
                if (wave != "WAVE") return null;

                // fmt chunk
                var fmt = new string(reader.ReadChars(4));
                if (fmt != "fmt ") return null;
                _ = reader.ReadUInt32();   // fmt chunk size (обычно 16)
                _ = reader.ReadUInt16();   // audio format (1 = PCM)
                _ = reader.ReadUInt16();   // channels
                _ = reader.ReadUInt32();   // sample rate
                var byteRate = reader.ReadUInt32();   // sampleRate * channels * bytesPerSample
                _ = reader.ReadUInt16();   // block align
                _ = reader.ReadUInt16();   // bits per sample

                if (byteRate == 0) return null;

                // data chunk — может быть сразу после fmt, либо через дополнительные чанки.
                // Для простоты: ищем "data" в ближайших 1024 байтах.
                for (int i = 0; i < 1024 && stream.Position + 8 <= stream.Length; i++)
                {
                    var chunkId = new string(reader.ReadChars(4));
                    var chunkSize = reader.ReadUInt32();
                    if (chunkId == "data")
                    {
                        var seconds = (double)chunkSize / byteRate;
                        return (long)Math.Round(seconds * 1000);
                    }
                    // Пропускаем неизвестный чанк (выравнивание по чётности).
                    var skip = (int)chunkSize + (chunkSize % 2);
                    stream.Position += skip;
                }
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                stream.Position = originalPosition;
            }
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