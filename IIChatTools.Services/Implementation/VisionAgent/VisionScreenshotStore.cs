using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Сохраняет PNG-скриншоты Vision Agent в workspace пользователя.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.5). См. DESIGN § 6.5, § 7.6.
    /// </para>
    /// <para>
    /// <b>Путь:</b> <c>{user_workspace}/screenshots/{taskId}/step-NNN.png</c>.
    /// <c>taskId</c> нормализуется через regex (только <c>[a-zA-Z0-9_-]</c>) —
    /// защита от path-traversal (RULES § 1.9).
    /// </para>
    /// <para>
    /// <b>Scoped.</b> Зависит от <see cref="IWorkspaceResolver"/> (Scoped) и
    /// <see cref="IOptions{VisionAgentOptions}"/> (Singleton).
    /// </para>
    /// </remarks>
    public sealed class VisionScreenshotStore : IVisionScreenshotStore
    {
        /// <summary>Допустимые символы в taskId. Всё остальное — в «_».</summary>
        private static readonly Regex TaskIdSanitizer = new Regex(
            "[^a-zA-Z0-9_-]", RegexOptions.Compiled);

        /// <summary>Подпапка в workspace.</summary>
        private const string ScreenshotsSubfolder = "screenshots";

        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly VisionPrivacyOptions _privacyOptions;
        private readonly ILogger<VisionScreenshotStore> _logger;

        /// <summary>
        /// Создаёт store.
        /// </summary>
        /// <param name="workspaceResolver">Резолвер workspace пользователя.</param>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent:Privacy</c>).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров null.</exception>
        public VisionScreenshotStore(
            IWorkspaceResolver workspaceResolver,
            IOptions<VisionAgentOptions> options,
            ILogger<VisionScreenshotStore> logger)
        {
            _workspaceResolver = workspaceResolver
                ?? throw new ArgumentNullException(nameof(workspaceResolver));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _privacyOptions = options.Value.Privacy ?? new VisionPrivacyOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<string> SaveAsync(
            int userId,
            string taskId,
            int stepIndex,
            byte[] pngBytes,
            CancellationToken cancellationToken = default)
        {
            if (userId <= 0)
            {
                _logger.LogWarning(
                    "VisionScreenshotStore: невалидный userId={UserId}, сохранение пропущено",
                    userId);
                return null;
            }

            if (!_privacyOptions.SaveToWorkspace)
            {
                // Saving отключён в конфиге — тихо возвращаем null.
                return null;
            }

            if (pngBytes == null || pngBytes.Length == 0)
            {
                _logger.LogWarning(
                    "VisionScreenshotStore: пустой PNG, сохранение пропущено (taskId={TaskId}, step={Step})",
                    taskId, stepIndex);
                return null;
            }

            var safeTaskId = NormalizeTaskId(taskId);
            var safeStep = Math.Max(1, stepIndex);

            try
            {
                // 1. Резолвим workspace root.
                var workspaceRoot = await _workspaceResolver
                    .GetWorkspacePathAsync(userId)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(workspaceRoot))
                {
                    _logger.LogWarning(
                        "VisionScreenshotStore: workspace root не задан для user={UserId}",
                        userId);
                    return null;
                }

                // 2. Формируем относительный путь + резолвим безопасный absolute.
                var relativePath = Path.Combine(
                    ScreenshotsSubfolder, safeTaskId, $"step-{safeStep:000}.png");

                // ВАЖНО: PathHelper.TryGetSafeFullPath(userPath, workspaceRoot, out safePath) —
                // порядок аргументов: СНАЧАЛА относительный путь, ПОТОМ корень workspace.
                if (!PathHelper.TryGetSafeFullPath(
                        relativePath, workspaceRoot, out var fullPath))
                {
                    _logger.LogWarning(
                        "VisionScreenshotStore: PathHelper отклонил путь '{Path}' в '{Root}'",
                        relativePath, workspaceRoot);
                    return null;
                }

                // 3. Создаём папку + пишем файл.
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(fullPath, pngBytes, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogDebug(
                    "VisionScreenshotStore: сохранён {Bytes} байт в '{Path}'",
                    pngBytes.Length, relativePath);

                return relativePath.Replace('\\', '/');
            }
            catch (Exception ex)
            {
                // Никогда не падаем из-за screenshot-сохранения.
                _logger.LogWarning(ex,
                    "VisionScreenshotStore: ошибка сохранения (taskId={TaskId}, step={Step})",
                    taskId, stepIndex);
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<string> GetTaskDirectoryAsync(int userId, string taskId)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(taskId))
                return null;

            try
            {
                var workspaceRoot = await _workspaceResolver
                    .GetWorkspacePathAsync(userId)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(workspaceRoot))
                    return null;

                var safeTaskId = NormalizeTaskId(taskId);
                var relativePath = Path.Combine(ScreenshotsSubfolder, safeTaskId);

                // ВАЖНО: PathHelper.TryGetSafeFullPath(userPath, workspaceRoot, out safePath).
                if (!PathHelper.TryGetSafeFullPath(
                        relativePath, workspaceRoot, out var fullPath))
                {
                    return null;
                }

                return Directory.Exists(fullPath) ? fullPath : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VisionScreenshotStore: ошибка GetTaskDirectory (taskId={TaskId})", taskId);
                return null;
            }
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Нормализует taskId: оставляет <c>[a-zA-Z0-9_-]</c>, остальное → «_».
        /// Пустой результат → «unknown».
        /// </summary>
        private static string NormalizeTaskId(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return "unknown";
            var sanitized = TaskIdSanitizer.Replace(taskId.Trim(), "_");
            return string.IsNullOrEmpty(sanitized) ? "unknown" : sanitized;
        }
    }
}