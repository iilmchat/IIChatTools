namespace IIChatTools.Services.DTO.Speech
{
    /// <summary>
    /// Результат транскрибации аудио.
    /// v1.13.0 (KI-140).
    /// </summary>
    public class TranscriptionResult
    {
        /// <summary>
        /// Распознанный текст (может быть пустым, если речь не обнаружена
        /// или запись содержала только тишину/шум).
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Язык распознавания. Если в опциях был зафиксирован ISO-код —
        /// возвращаем его. Если <c>auto</c> — возвращаем <c>auto</c>
        /// (Whisper.net не отдаёт определённый язык в текущей версии через
        /// <c>SegmentData</c>; см. § 11.3 DESIGN).
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// Длительность аудио (мс). В v1.13.0 — не заполняется (0).
        /// Технический долг: парсинг WAV-заголовка — см. § 11.3 DESIGN.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Длительность обработки (мс). Замеряется <c>Stopwatch</c> в
        /// <c>WhisperNetTranscriptionService</c>.
        /// </summary>
        public long ProcessingMs { get; set; }
    }
}