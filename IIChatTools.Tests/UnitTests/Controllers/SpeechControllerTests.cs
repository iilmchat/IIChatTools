using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Controllers;
using IIChatTools.Services.DTO.Speech;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Controllers
{
    /// <summary>
    /// Unit-тесты для <see cref="SpeechController"/> (v1.13.0, KI-140, Ф3.6).
    /// Прямой вызов контроллера с mock-зависимостями (без WebApplicationFactory),
    /// консистентно с <c>AdminKnowledgeControllerTests</c>.
    /// </summary>
    public class SpeechControllerTests
    {
        // ============ Фабрики ============

        /// <summary>
        /// Создаёт контроллер с указанным mock-сервисом и опциями.
        /// </summary>
        private static SpeechController CreateController(
            ISpeechRecognitionService speech,
            SpeechOptions options = null)
        {
            options ??= new SpeechOptions { Enabled = true };
            return new SpeechController(
                speech,
                Options.Create(options),
                NullLogger<SpeechController>.Instance);
        }

        /// <summary>
        /// Создаёт IFormFile заданного размера для тестов.
        /// </summary>
        private static IFormFile CreateFormFile(long length, string fileName = "audio.wav")
        {
            var stream = new MemoryStream(new byte[Math.Max(0, length)]);
            return new FormFile(stream, 0, stream.Length, "file", fileName);
        }

        private static SpeechOptions EnabledOptions() => new SpeechOptions
        {
            Enabled = true,
            MaxFileSizeBytes = 10 * 1024 * 1024   // 10 MB (как в appsettings).
        };

        // ============ 1. Disabled ============

        [Fact]
        public async Task TranscribeAsync_Disabled_ReturnsFail()
        {
            // Arrange: Enabled = false, MockBehavior.Strict — сервис не должен вызываться.
            var speechMock = new Mock<ISpeechRecognitionService>(MockBehavior.Strict);
            var controller = CreateController(
                speechMock.Object,
                new SpeechOptions { Enabled = false });

            // Act: file = null (не должен использоваться — проверка Enabled идёт первой).
            var result = await controller.TranscribeAsync(null, CancellationToken.None);

            // Assert.
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            Assert.False(json["success"]!.Value<bool>());
            Assert.Contains("отключено", json["message"]!.Value<string>());

            speechMock.Verify(
                s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ============ 2. Empty file ============

        [Fact]
        public async Task TranscribeAsync_NullFile_ReturnsFail()
        {
            var speechMock = new Mock<ISpeechRecognitionService>(MockBehavior.Strict);
            var controller = CreateController(speechMock.Object, EnabledOptions());

            var result = await controller.TranscribeAsync(null, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            Assert.False(json["success"]!.Value<bool>());
            Assert.Contains("не передан", json["message"]!.Value<string>());

            speechMock.Verify(
                s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task TranscribeAsync_ZeroLengthFile_ReturnsFail()
        {
            var speechMock = new Mock<ISpeechRecognitionService>(MockBehavior.Strict);
            var controller = CreateController(speechMock.Object, EnabledOptions());

            var emptyFile = CreateFormFile(0);
            var result = await controller.TranscribeAsync(emptyFile, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            Assert.False(json["success"]!.Value<bool>());
            Assert.Contains("не передан", json["message"]!.Value<string>());

            speechMock.Verify(
                s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ============ 3. Too large ============

        [Fact]
        public async Task TranscribeAsync_TooLargeFile_ReturnsFail()
        {
            var speechMock = new Mock<ISpeechRecognitionService>(MockBehavior.Strict);
            var options = EnabledOptions();
            options.MaxFileSizeBytes = 1024;   // 1 KB — искусственно низкий лимит.
            var controller = CreateController(speechMock.Object, options);

            var bigFile = CreateFormFile(2048);   // 2 KB > 1 KB.
            var result = await controller.TranscribeAsync(bigFile, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            Assert.False(json["success"]!.Value<bool>());
            Assert.Contains("больше", json["message"]!.Value<string>());

            speechMock.Verify(
                s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ============ 4. Valid file ============

        [Fact]
        public async Task TranscribeAsync_ValidFile_ReturnsText()
        {
            var speechMock = new Mock<ISpeechRecognitionService>();
            speechMock
                .Setup(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranscriptionResult
                {
                    Text = "привет мир",
                    Language = "ru",
                    DurationMs = 2500,
                    ProcessingMs = 1234
                });

            var controller = CreateController(speechMock.Object, EnabledOptions());

            var validFile = CreateFormFile(4096);
            var result = await controller.TranscribeAsync(validFile, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            Assert.True(json["success"]!.Value<bool>());
            Assert.Equal("привет мир", json["data"]!["text"]!.Value<string>());
            Assert.Equal("ru", json["data"]!["language"]!.Value<string>());
            Assert.Equal(1234L, json["data"]!["processingMs"]!.Value<long>());
        }

        // ============ 5. Empty result (BLANK_AUDIO case) ============

        [Fact]
        public async Task TranscribeAsync_EmptyResult_ReturnsFail()
        {
            // Whisper вернул пустоту: тишина / [BLANK_AUDIO] отфильтрован (Ф3.5-fix).
            // Регрессионный тест для KI-140 Ф3.5.
            var speechMock = new Mock<ISpeechRecognitionService>();
            speechMock
                .Setup(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranscriptionResult
                {
                    Text = string.Empty,
                    Language = "auto",
                    ProcessingMs = 500
                });

            var controller = CreateController(speechMock.Object, EnabledOptions());

            var validFile = CreateFormFile(4096);
            var result = await controller.TranscribeAsync(validFile, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            Assert.False(json["success"]!.Value<bool>());
            Assert.Contains("Речь не обнаружена", json["message"]!.Value<string>());
        }

        // ============ 6. FileNotFoundException (модель не загружена) ============

        [Fact]
        public async Task TranscribeAsync_ModelNotFound_ReturnsFail()
        {
            var speechMock = new Mock<ISpeechRecognitionService>();
            speechMock
                .Setup(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FileNotFoundException(
                    "Модель Whisper не найдена: /path/to/ggml-base.bin"));

            var controller = CreateController(speechMock.Object, EnabledOptions());

            var validFile = CreateFormFile(4096);
            var result = await controller.TranscribeAsync(validFile, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            Assert.False(json["success"]!.Value<bool>());
            Assert.Contains("не загружена", json["message"]!.Value<string>());
        }

        // ============ 7. OperationCanceledException ============

        [Fact]
        public async Task TranscribeAsync_Cancelled_ReturnsFail()
        {
            var speechMock = new Mock<ISpeechRecognitionService>();
            speechMock
                .Setup(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            var controller = CreateController(speechMock.Object, EnabledOptions());

            var validFile = CreateFormFile(4096);
            var result = await controller.TranscribeAsync(validFile, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            Assert.False(json["success"]!.Value<bool>());
            Assert.Contains("отменена", json["message"]!.Value<string>());
        }
    }
}