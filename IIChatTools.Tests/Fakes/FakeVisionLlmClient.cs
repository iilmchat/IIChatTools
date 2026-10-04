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

        public Task<ScreenDescriptionDto> DescribeAsync(
            byte[] screenshotPng,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Throws != null) throw Throws;
            var r = Responses.Count > 0 ? Responses.Dequeue() : DefaultResponse;
            return Task.FromResult(r);
        }
    }
}