namespace IIChatTools.Services.DTO.Speech
{
    /// <summary>
    /// Настройки офлайн-распознавания речи (Whisper.net).
    /// Секция <c>Speech</c> в appsettings.json.
    /// </summary>
    /// <remarks>
    /// v1.13.0 (KI-140). См. <c>docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md</c> § 3.2.
    /// </remarks>
    public class SpeechOptions
    {
        /// <summary>
        /// Включено ли распознавание речи.
        /// При <c>false</c> endpoint <c>POST /api/speech/transcribe</c>
        /// возвращает <c>{ success: false, message: "отключено" }</c>.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Путь к файлу модели (GGML). Относительный — от <c>ContentRootPath</c>.
        /// Абсолютный — используется как есть.
        /// </summary>
        public string ModelPath { get; set; } = "tools/whisper/ggml-base.bin";

        /// <summary>
        /// Язык распознавания: ISO-код (<c>ru</c>, <c>en</c>, ...) или <c>auto</c>
        /// для авто-детекта. По умолчанию — <c>ru</c>.
        /// </summary>
        public string Language { get; set; } = "ru";

        /// <summary>
        /// Максимальная длительность аудио (секунд). Enforced на клиенте
        /// (<c>MAX_RECORDING_MS</c> в <c>speech.js</c>). Серверный hard-cap —
        /// <see cref="MaxFileSizeBytes"/>.
        /// </summary>
        public int MaxAudioSeconds { get; set; } = 60;

        /// <summary>
        /// Максимальный размер WAV-файла (байт). Default — 10 MB.
        /// Валидируется в <c>SpeechController</c> до вызова сервиса.
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 10_485_760;

        /// <summary>
        /// Таймаут транскрибации (секунд). Резерв — используется в Phase 4,
        /// если добавим <c>CancellationTokenSource.CancelAfter</c>.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Порог «no-speech» для Whisper.net (0.0..1.0). По умолчанию — 0.8
        /// (выше, чем whisper.cpp default 0.6). Чем выше — тем чаще модель
        /// считает вход тишиной/шумом и не выдаёт галлюцинации
        /// типа <c>[BLANK_AUDIO]</c>, <c>[MUSIC]</c>.
        /// </summary>
        public float NoSpeechThreshold { get; set; } = 0.8f;

        /// <summary>
        /// Минимальная длительность аудио (мс). Короче — сразу возвращаем
        /// пустой результат, не тратим CPU на Whisper.
        /// Защита от одиночных кликов и случайных записей.
        /// </summary>
        public int MinAudioDurationMs { get; set; } = 300;
    }
}