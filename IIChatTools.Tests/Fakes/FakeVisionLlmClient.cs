using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake IVisionLlmClient для тестов VisionAgentService.
    /// </summary>
    internal sealed class FakeVisionLlmClient : IVisionLlmClient
    {
        public bool IsReady { get; set; } = true;
        public Queue<ScreenDescriptionDto> Responses { get; } = new();
        public ScreenDescriptionDto DefaultResponse { get; set; } = new ScreenDescriptionDto();
        public Exception Throws { get; set; }
        public int CallCount { get; private set; }

        // KI-162 (Coordinate-then-Verify): отдельные поля для VerifyTargetAsync.
        // Отдельный Throws (VerifyThrows) — чтобы существующие тесты, где
        // Throws задан для Describe, не падали на Verify (Verify вызывается
        // только при Verify:Enabled=true, но fake стабильнее сделать изолированно).
        public Queue<VerifyTargetResultDto> VerifyResponses { get; } = new();
        public VerifyTargetResultDto VerifyDefaultResponse { get; set; }
            = new VerifyTargetResultDto { Found = false };
        public Exception VerifyThrows { get; set; }
        public int VerifyCallCount { get; private set; }

        public Task<ScreenDescriptionDto> DescribeAsync(
            byte[] screenshotPng,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Throws != null) throw Throws;
            var r = Responses.Count > 0 ? Responses.Dequeue() : DefaultResponse;
            return Task.FromResult(r);
        }

        /// <summary>
        /// KI-162 (Coordinate-then-Verify): второй VL-вызов на кропе.
        /// Возвращает <see cref="VerifyTargetResultDto"/> из <see cref="VerifyResponses"/>
        /// (или <see cref="VerifyDefaultResponse"/>, если очередь пуста).
        /// По умолчанию — <c>Found = false</c> (force fallback на bounds center).
        /// </summary>
        public Task<VerifyTargetResultDto> VerifyTargetAsync(
            byte[] croppedPng,
            string targetDescription,
            UiElementBoundsDto originalBounds,
            CancellationToken cancellationToken = default)
        {
            VerifyCallCount++;
            if (VerifyThrows != null) throw VerifyThrows;
            var r = VerifyResponses.Count > 0
                ? VerifyResponses.Dequeue()
                : VerifyDefaultResponse;
            return Task.FromResult(r);
        }
    }
}