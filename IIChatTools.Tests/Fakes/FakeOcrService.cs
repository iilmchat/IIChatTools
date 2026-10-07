using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake <see cref="IOcrService"/> для тестов (KI-203).
    /// </summary>
    internal sealed class FakeOcrService : IOcrService
    {
        public bool IsReady { get; set; } = true;
        public string EngineName => "fake";

        /// <summary>Сколько раз вызывался RecognizeAsync.</summary>
        public int CallCount { get; private set; }

        /// <summary>Очередь ответов (FIFO). Если пусто — DefaultResponse.</summary>
        public Queue<string> Responses { get; } = new();

        /// <summary>Ответ, когда очередь пуста.</summary>
        public string DefaultResponse { get; set; } = string.Empty;

        /// <summary>Если задано — RecognizeAsync бросит это исключение.</summary>
        public Exception Throws { get; set; }

        public Task<string> RecognizeAsync(
            byte[] imageBytes,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (Throws != null)
                throw Throws;

            var r = Responses.Count > 0 ? Responses.Dequeue() : DefaultResponse;
            return Task.FromResult(r);
        }
    }
}