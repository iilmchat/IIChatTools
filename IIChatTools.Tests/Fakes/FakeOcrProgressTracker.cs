using System;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake <see cref="IOcrProgressTracker"/> для тестов (KI-204).
    /// </summary>
    internal sealed class FakeOcrProgressTracker : IOcrProgressTracker
    {
        public int BeginScopeCallCount { get; private set; }
        public int ReportCallCount { get; private set; }
        public int CompleteCallCount { get; private set; }
        public string LastKey { get; private set; }

        public IDisposable BeginScope(string key)
        {
            BeginScopeCallCount++;
            LastKey = key;
            return new NoopDisposable();
        }

        public void Report(int currentPage, int totalPages)
        {
            ReportCallCount++;
        }

        public void Complete(string key)
        {
            CompleteCallCount++;
        }

        public OcrProgressDto Get(string key) => null;

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}