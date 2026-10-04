using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Обходит workspace'ы всех пользователей и удаляет устаревшие папки
    /// со скриншотами Vision Agent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.6). См. DESIGN § 6.5.
    /// </para>
    /// <para>
    /// <b>Scoped.</b> Используется из фонового <c>VisionRetentionService</c>
    /// через <c>IServiceScopeFactory.CreateScope()</c>.
    /// </para>
    /// </remarks>
    public sealed class VisionScreenshotCleaner : IVisionScreenshotCleaner
    {
        /// <summary>Подпапка со скриншотами в workspace (совпадает с Ф6.5).</summary>
        private const string ScreenshotsSubfolder = "screenshots";

        private readonly AppDbContext _db;
        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly VisionPrivacyOptions _privacyOptions;
        private readonly ILogger<VisionScreenshotCleaner> _logger;

        /// <summary>
        /// Создаёт cleaner.
        /// </summary>
        /// <param name="db">Контекст БД (для получения списка userIds).</param>
        /// <param name="workspaceResolver">Резолвер workspace пользователя.</param>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent:Privacy</c>).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если параметр null.</exception>
        public VisionScreenshotCleaner(
            AppDbContext db,
            IWorkspaceResolver workspaceResolver,
            IOptions<VisionAgentOptions> options,
            ILogger<VisionScreenshotCleaner> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _workspaceResolver = workspaceResolver ?? throw new ArgumentNullException(nameof(workspaceResolver));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _privacyOptions = options.Value.Privacy ?? new VisionPrivacyOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<VisionScreenshotCleanupResult> CleanupAsync(
            CancellationToken cancellationToken = default)
        {
            var result = new VisionScreenshotCleanupResult();
            var sw = Stopwatch.StartNew();

            // 1. Проверка: сохранение в workspace выключено → чистить нечего.
            if (!_privacyOptions.SaveToWorkspace)
            {
                _logger.LogDebug(
                    "VisionRetention: SaveToWorkspace=false, cleanup пропущен");
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                return result;
            }

            // 2. Retention (clamp [1, 168] часов — от 1 часа до 1 недели).
            var retentionHours = Math.Clamp(_privacyOptions.WorkspaceRetentionHours, 1, 168);
            var cutoff = DateTime.UtcNow.AddHours(-retentionHours);

            // 3. Список пользователей.
            var userIds = await _db.Users
                .AsNoTracking()
                .Select(u => u.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (userIds.Count == 0)
            {
                _logger.LogDebug("VisionRetention: нет пользователей, cleanup завершён");
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                return result;
            }

            // 4. Для каждого пользователя — обход screenshots/*.
            foreach (var userId in userIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await CleanupForUserAsync(userId, cutoff, result, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    _logger.LogWarning(ex,
                        "VisionRetention: ошибка cleanup для user={UserId}", userId);
                }
            }

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;

            if (result.DeletedTaskFolders > 0 || result.ErrorCount > 0)
            {
                _logger.LogInformation(
                    "VisionRetention: cleanup завершён за {Ms}мс — папок={Folders}, файлов={Files}, " +
                    "освобождено={MB} MB, ошибок={Errors}, cutoff={Cutoff:u}",
                    result.DurationMs, result.DeletedTaskFolders, result.DeletedFiles,
                    Math.Round(result.FreedBytes / 1024.0 / 1024.0, 2),
                    result.ErrorCount, cutoff);
            }
            else
            {
                _logger.LogDebug(
                    "VisionRetention: cleanup завершён за {Ms}мс — нечего удалять (cutoff={Cutoff:u})",
                    result.DurationMs, cutoff);
            }

            return result;
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Обходит screenshots/* для одного пользователя.
        /// </summary>
        private async Task CleanupForUserAsync(
            int userId,
            DateTime cutoff,
            VisionScreenshotCleanupResult result,
            CancellationToken cancellationToken)
        {
            var workspaceRoot = await _workspaceResolver
                .GetWorkspacePathAsync(userId)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            {
                return;
            }

            var screenshotsRoot = Path.Combine(workspaceRoot, ScreenshotsSubfolder);
            if (!Directory.Exists(screenshotsRoot))
            {
                return;
            }

            // Подпапки = задачи. Обрабатываем каждую независимо.
            foreach (var taskDir in Directory.GetDirectories(screenshotsRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Возраст = LastWriteTimeUtc папки (обновляется при добавлении файлов).
                    var lastWrite = Directory.GetLastWriteTimeUtc(taskDir);
                    if (lastWrite >= cutoff)
                    {
                        continue;   // свежая, не трогаем.
                    }

                    // Считаем размеры для метрик, потом удаляем.
                    var (fileCount, bytes) = GetDirectoryStats(taskDir);

                    Directory.Delete(taskDir, recursive: true);

                    result.DeletedTaskFolders++;
                    result.DeletedFiles += fileCount;
                    result.FreedBytes += bytes;

                    _logger.LogDebug(
                        "VisionRetention: удалена папка {Dir} (lastWrite={LastWrite:u}, files={Files}, bytes={Bytes})",
                        Path.GetFileName(taskDir), lastWrite, fileCount, bytes);
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    _logger.LogWarning(ex,
                        "VisionRetention: ошибка удаления папки {Dir}", taskDir);
                }
            }
        }

        /// <summary>
        /// Возвращает (число файлов, суммарный размер в байтах) для папки.
        /// </summary>
        private static (int fileCount, long bytes) GetDirectoryStats(string directory)
        {
            int count = 0;
            long bytes = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Exists)
                        {
                            count++;
                            bytes += info.Length;
                        }
                    }
                    catch
                    {
                        // Не падаем из-за одного файла — пропускаем.
                    }
                }
            }
            catch
            {
                // Не падаем — вернём что есть.
            }

            return (count, bytes);
        }
    }
}