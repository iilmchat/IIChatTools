using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake <see cref="IOcrService"/> для тестов (KI-203, KI-207).
    /// </summary>
    internal sealed class FakeOcrService : IOcrService
    {
        public bool IsReady { get; set; } = true;
        public string EngineName => "fake";

        /// <summary>Сколько раз вызывался RecognizeAsync.</summary>
        public int CallCount { get; private set; }

        /// <summary>Сколько раз вызывался RecognizeWithLayoutAsync.</summary>
        public int LayoutCallCount { get; private set; }

        /// <summary>Очередь ответов (FIFO). Если пусто — DefaultResponse.</summary>
        public Queue<string> Responses { get; } = new();

        /// <summary>Ответ, когда очередь пуста.</summary>
        public string DefaultResponse { get; set; } = string.Empty;

        /// <summary>
        /// Очередь layout-ответов (FIFO). Если пусто — DefaultLayoutResponse.
        /// </summary>
        public Queue<PageTextLayerDto> LayoutResponses { get; } = new();

        /// <summary>Layout-ответ, когда очередь пуста.</summary>
        public PageTextLayerDto DefaultLayoutResponse { get; set; } = new PageTextLayerDto();

        /// <summary>Если задано — RecognizeAsync бросит это исключение.</summary>
        public Exception Throws { get; set; }

        /// <summary>Если задано — RecognizeWithLayoutAsync бросит это исключение.</summary>
        public Exception LayoutThrows { get; set; }

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

        public Task<PageTextLayerDto> RecognizeWithLayoutAsync(
            byte[] imageBytes,
            int imageWidth,
            int imageHeight,
            CancellationToken cancellationToken = default)
        {
            LayoutCallCount++;

            if (LayoutThrows != null)
                throw LayoutThrows;

            var layer = LayoutResponses.Count > 0
                ? LayoutResponses.Dequeue()
                : DefaultLayoutResponse;

            // Заполняем размеры — вызывающий (PdfParser) ожидает их.
            layer ??= new PageTextLayerDto();
            layer.Width = imageWidth;
            layer.Height = imageHeight;

            return Task.FromResult(layer);
        }
    }
}
