using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake IVisionScreenshotStore для тестов VisionAgentService.
    /// </summary>
    internal sealed class FakeVisionScreenshotStore : IVisionScreenshotStore
    {
        public List<string> SavedPaths { get; } = new();

        public Task<string> SaveAsync(
            int userId, string taskId, int stepIndex, byte[] pngBytes,
            CancellationToken cancellationToken = default)
        {
            var path = $"screenshots/{taskId}/step-{stepIndex:000}.png";
            SavedPaths.Add(path);
            return Task.FromResult(path);
        }

        public Task<string> GetTaskDirectoryAsync(int userId, string taskId)
            => Task.FromResult<string>(null);
    }
}