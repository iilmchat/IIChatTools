using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Speech;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API офлайн-распознавания речи (Whisper.net).
    /// v1.13.0 (KI-140).
    /// </summary>
    /// <remarks>
    /// См. <c>docs/development/v1.13/DESIGN_SPEECH_RECOGNITION.md</c> § 3.5.
    /// </remarks>
    [ApiController]
    [Route("api/speech")]
    [Authorize]
    public class SpeechController : ControllerBase
    {
        private readonly ISpeechRecognitionService _speech;
        private readonly SpeechOptions _options;
        private readonly ILogger<SpeechController> _logger;

        /// <summary>
        /// Создаёт контроллер.
        /// </summary>
        /// <param name="speech">Сервис распознавания (Singleton)</param>
        /// <param name="options">Настройки секции <c>Speech</c></param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public SpeechController(
            ISpeechRecognitionService speech,
            IOptions<SpeechOptions> options,
            ILogger<SpeechController> logger)
        {
            _speech = speech ?? throw new ArgumentNullException(nameof(speech));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Транскрибирует WAV-аудио в текст.
        /// </summary>
        /// <param name="file">
        /// WAV-файл (16 kHz mono PCM, multipart/form-data).
        /// Клиент формирует его через Web Audio API + OfflineAudioContext.
        /// </param>
        /// <param name="ct">Токен отмены (клиентский abort).</param>
        /// <returns>
        /// <c>{ success, data: { text, language, processingMs } }</c> или
        /// <c>{ success: false, message }</c>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <c>RequestSizeLimit(20 MB)</c> — жёсткий hard cap на уровне ASP.NET Core
        /// (запас над реальным лимитом 10 MB, см. <see cref="SpeechOptions.MaxFileSizeBytes"/>).
        /// </para>
        /// <para>
        /// В <c>Startup.cs</c> (v1.5.0, KI-083) уже установлен глобальный
        /// <c>KestrelServerOptions.Limits.MaxRequestBodySize = 40 MB</c>.
        /// Атрибут применяется <b>поверх</b> — итоговый лимит для endpoint'а = 20 MB.
        /// </para>
        /// </remarks>
        [HttpPost("transcribe")]
        [RequestSizeLimit(20 * 1024 * 1024)]   // hard cap; реальный лимит — Speech:MaxFileSizeBytes
        public async Task<IActionResult> TranscribeAsync(
            [FromForm] IFormFile file,
            CancellationToken ct)
        {
            // 1. Глобальный флаг.
            if (!_options.Enabled)
            {
                return Ok(new { success = false, message = "Распознавание речи отключено." });
            }

            // 2. Наличие файла.
            if (file == null || file.Length == 0)
            {
                return Ok(new { success = false, message = "Файл не передан." });
            }

            // 3. Размер (реальный лимит — из опций).
            if (file.Length > _options.MaxFileSizeBytes)
            {
                var mb = _options.MaxFileSizeBytes / 1024 / 1024;
                return Ok(new { success = false, message = $"Файл больше {mb} MB." });
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await _speech.TranscribeAsync(stream, ct);

                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    return Ok(new { success = false, message = "Речь не обнаружена. Попробуйте ещё раз." });
                }

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        text = result.Text,
                        language = result.Language,
                        processingMs = result.ProcessingMs
                    }
                });
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Модель Whisper не найдена");
                return Ok(new
                {
                    success = false,
                    message = "Модель распознавания речи не загружена. Обратитесь к администратору."
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Транскрибация отменена клиентом");
                return Ok(new { success = false, message = "Транскрибация отменена." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка транскрибации");
                return Ok(new { success = false, message = "Внутренняя ошибка распознавания." });
            }
        }
    }
}