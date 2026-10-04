using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// No-op overlay launcher — используется до реализации WPF overlay'я (Ф6.7).
    /// <c>IsAvailable = false</c>, все методы — пустышки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.8).
    /// </para>
    /// <para>
    /// <b>Зачем:</b> контракт <see cref="IVisionOverlayLauncher"/> уже нужен
    /// в <c>VisionAgentService</c>, чтобы loop мог эмитить прогресс. Реальный
    /// overlay появится позже — заменим регистрацию в DI.
    /// </para>
    /// </remarks>
    public sealed class NoopVisionOverlayLauncher : IVisionOverlayLauncher
    {
        private readonly ILogger<NoopVisionOverlayLauncher> _logger;

        /// <summary>
        /// Создаёт launcher.
        /// </summary>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="logger"/> = null.</exception>
        public NoopVisionOverlayLauncher(ILogger<NoopVisionOverlayLauncher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsAvailable => false;

        /// <inheritdoc />
        public Task<IVisionOverlayHandle> StartAsync(
            string taskId,
            int maxSteps,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug(
                "VisionAgent: overlay отключён (Noop), StartAsync пропущен (taskId={TaskId})",
                taskId);
            return Task.FromResult<IVisionOverlayHandle>(NoopVisionOverlayHandle.Instance);
        }

        /// <summary>
        /// Singleton-инстанс no-op handle (не хранит состояния).
        /// </summary>
        private sealed class NoopVisionOverlayHandle : IVisionOverlayHandle
        {
            public static readonly NoopVisionOverlayHandle Instance =
                new NoopVisionOverlayHandle();

            private NoopVisionOverlayHandle() { }

            public void UpdateProgress(int stepIndex, int maxSteps, string action) { }

            public void SetFinalStatus(string summary, bool success) { }

            public void Dispose() { }
        }
    }
}