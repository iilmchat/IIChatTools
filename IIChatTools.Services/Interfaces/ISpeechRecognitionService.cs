using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Speech;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис офлайн-распознавания речи (Whisper.net).
    /// Singleton — модель загружается один раз, кэшируется на всё время жизни.
    /// v1.13.0 (KI-140).
    /// </summary>
    public interface ISpeechRecognitionService
    {
        /// <summary>
        /// Транскрибирует WAV-поток (16 kHz mono PCM) в текст.
        /// </summary>
        /// <param name="wavStream">
        /// Поток с WAV-файлом. Позиция должна быть в начале.
        /// Формат: WAV PCM 16 kHz mono.
        /// </param>
        /// <param name="cancellationToken">Токен отмены (клиентский abort).</param>
        /// <returns>Результат с текстом и метаданными.</returns>
        /// <exception cref="FileNotFoundException">
        /// Если модель Whisper не найдена по пути <c>Speech:ModelPath</c>.
        /// </exception>
        Task<TranscriptionResult> TranscribeAsync(
            Stream wavStream,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Готов ли сервис (модель загружена). <c>false</c> до первого вызова
        /// <see cref="TranscribeAsync"/>.
        /// </summary>
        bool IsReady { get; }
    }
}