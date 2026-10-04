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

        /// <summary>Порог «no-speech»: вероятность, выше которой сегмент считается
        /// неречью (для фильтрации галлюцинаций на тишине). Default 0.8.</summary>
        public float NoSpeechThreshold { get; set; } = 0.8f;

        /// <summary>
        /// v1.13.1 (KI-140-fix): начальная temperature Whisper.
        /// <c>0.0</c> — детерминированный вывод (не «фантазирует»).
        /// Default 0.0. Диапазон: 0.0–1.0.
        /// </summary>
        public float Temperature { get; set; } = 0.0f;

        /// <summary>
        /// v1.13.1 (KI-140-fix): LogProb threshold. Сегменты с лог-вероятностью
        /// ниже этого значения отбрасываются как «неуверенные»
        /// (защита от галлюцинаций). Default <c>-1.0</c>
        /// (= whisper.cpp default, но применяется явно).
        /// </summary>
        public float LogprobThreshold { get; set; } = -1.0f;

        /// <summary>
        /// Минимальная длительность аудио (мс) для запуска Whisper.
        /// Файлы короче — не тратят CPU (v1.13.0-fix, KI-140).
        /// </summary>
        public int MinAudioDurationMs { get; set; } = 300;

        /// <summary>
        /// Настройки VAD (Voice Activity Detection) — авто-остановка записи
        /// по тишине (v1.13.1-fix11, KI-140).
        ///
        /// <para>
        /// VAD работает на клиенте (`speech.js`) через `AnalyserNode`
        /// поверх того же `MediaStreamTrack`. Параметры передаются в JS
        /// через `data-speech-vad-*` на `#chat-messages`
        /// (RULES § 4.17 — локализация/конфиг JS через data-атрибуты).
        /// </para>
        /// </summary>
        public SpeechVadOptions Vad { get; set; } = new SpeechVadOptions();
    }

    /// <summary>
    /// Настройки VAD (Voice Activity Detection) — авто-остановка записи
    /// по тишине (v1.13.1-fix11, KI-140).
    /// </summary>
    public class SpeechVadOptions
    {
        /// <summary>
        /// Включён ли VAD. Если <c>false</c> — запись останавливается только
        /// вручную или по лимиту <see cref="SpeechOptions.MaxAudioSeconds"/>.
        /// Default: <c>true</c>.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Порог RMS для определения «тишины» (0..1). Используется только
        /// при <see cref="AdaptiveEnabled"/> = <c>false</c> (legacy-режим).
        /// Типично:
        /// <list type="bullet">
        ///   <item>тихая комната — 0.001–0.005;</item>
        ///   <item>шумная комната — 0.01–0.03;</item>
        ///   <item>речь — 0.05–0.30.</item>
        /// </list>
        /// Default: <c>0.015</c>.
        /// </summary>
        public double SilenceRms { get; set; } = 0.015;

        /// <summary>
        /// Включить адаптивный VAD (v1.13.1-fix11c, KI-140).
        ///
        /// <para>
        /// При <c>true</c> (default) — порог тишины вычисляется **динамически**:
        /// <c>threshold = max(minObservedRms × NoiseMultiplier, AbsoluteMinRms)</c>,
        /// где <c>minObservedRms</c> — минимальный RMS, наблюдавшийся с начала
        /// записи. Это позволяет VAD корректно работать на тихих микрофонах,
        /// где <see cref="SilenceRms"/> (0.015) выше уровня речи.
        /// </para>
        ///
        /// <para>
        /// При <c>false</c> — VAD использует фиксированный <see cref="SilenceRms"/>
        /// (старое поведение, для обратной совместимости).
        /// </para>
        /// </summary>
        public bool AdaptiveEnabled { get; set; } = true;

        /// <summary>
        /// Множитель для адаптивного порога: <c>threshold = minObserved × M</c>.
        /// Меньше — порог ниже (VAD ловит тихую речь, но и шум тоже).
        /// Больше — порог выше (устойчивее к шуму, но может пропустить тихую речь).
        /// Default: <c>2.0</c>.
        /// </summary>
        public double NoiseMultiplier { get; set; } = 2.0;

        /// <summary>
        /// Абсолютный минимум порога RMS. Ниже этого значения VAD никогда
        /// не опускает порог, даже если minObserved = 0.
        /// Защита от ложных срабатываний в тишине.
        /// Default: <c>0.001</c>.
        /// </summary>
        public double AbsoluteMinRms { get; set; } = 0.001;

        /// <summary>
        /// Длительность непрерывной тишины (мс) для auto-stop.
        /// Default: <c>2000</c> (2 секунды).
        /// </summary>
        public int SilenceTimeoutMs { get; set; } = 2000;

        /// <summary>
        /// Минимальная длительность записи до разрешения auto-stop (мс).
        /// Даёт пользователю время начать говорить. Если запись короче —
        /// VAD не сработает, даже если RMS = 0.
        /// Default: <c>700</c>.
        /// </summary>
        public int MinRecordingMs { get; set; } = 700;

        /// <summary>
        /// Интервал опроса VAD (мс). Меньше — точнее, но выше CPU.
        /// Default: <c>200</c>.
        /// </summary>
        public int PollIntervalMs { get; set; } = 200;
    }
}