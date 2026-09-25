using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Реализация сервиса управления per-user Workspace-индексом
    /// (v1.5.0, KI-083, Шаг 7C.1).
    ///
    /// <para>
    /// <b>Singleton.</b> Фоновая индексация запускается через <c>Task.Run</c> и
    /// живёт дольше HTTP-запроса. Для доступа к Scoped-зависимостям
    /// (<see cref="IUserSettingsService"/>, <see cref="IDocumentIngestionService"/>,
    /// <see cref="AppDbContext"/>, <see cref="IWorkspaceResolver"/>) используется
    /// <see cref="IServiceScopeFactory"/>.
    /// </para>
    ///
    /// <para>
    /// Прогресс per-user хранится в <c>ConcurrentDictionary&lt;int, WorkspaceIndexProgress&gt;</c>
    /// (in-memory, теряется при рестарте — после перезапуска приложения
    /// пользователь просто нажмёт «Переиндексировать» повторно).
    /// </para>
    /// </summary>
    public sealed class WorkspaceIndexService : IWorkspaceIndexService
    {
        /// <summary>Имя индекса в <c>DocumentChunks</c> для workspace.</summary>
        public const string IndexName = "workspace";

        /// <summary>Ключ per-user настройки: включён ли Workspace index.</summary>
        public const string EnabledSettingKey = "Workspace.Index.Enabled";

        /// <summary>Тип значения в UserSettings для <see cref="EnabledSettingKey"/>.</summary>
        private const string EnabledSettingType = "bool";

        /// <summary>
        /// Служебные подпапки, которые НЕ индексируются даже если находятся внутри
        /// workspace. Сравнение — case-insensitive.
        /// </summary>
        private static readonly HashSet<string> SkippedSubfolders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "chat-attachments",
                "logs",
                "bin",
                "obj",
                ".git",
                "node_modules",
                ".vs",
                ".idea"
            };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IRagDocumentParserRegistry _parserRegistry;
        private readonly ILogger<WorkspaceIndexService> _logger;

        /// <summary>
        /// Прогресс текущего фонового прогона по каждому пользователю.
        /// Записи не удаляются (небольшой объём — по числу уникальных пользователей).
        /// </summary>
        private readonly ConcurrentDictionary<int, WorkspaceIndexProgress> _progress =
            new ConcurrentDictionary<int, WorkspaceIndexProgress>();

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="scopeFactory">Фабрика scope (для Scoped-зависимостей)</param>
        /// <param name="parserRegistry">
        /// Реестр парсеров (Singleton) — источник списка поддерживаемых расширений
        /// для фильтрации файлов при обходе workspace.
        /// </param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public WorkspaceIndexService(
            IServiceScopeFactory scopeFactory,
            IRagDocumentParserRegistry parserRegistry,
            ILogger<WorkspaceIndexService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _parserRegistry = parserRegistry ?? throw new ArgumentNullException(nameof(parserRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ============================================================
        // GetStatusAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<WorkspaceIndexStatusDto> GetStatusAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var userSettings = scope.ServiceProvider.GetRequiredService<IUserSettingsService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var enabled = await userSettings.GetBoolAsync(
                userId, EnabledSettingKey, defaultValue: false, cancellationToken);

            // Метрики из БД: чанки + уникальные файлы + last indexed.
            var chunkStats = await db.DocumentChunks
                .AsNoTracking()
                .Where(c => c.IndexName == IndexName && c.UserId == userId)
                .GroupBy(c => 1)
                .Select(g => new
                {
                    ChunkCount = g.Count(),
                    DocumentCount = g.Select(c => c.DocumentPath).Distinct().Count(),
                    LastIndexedAt = g.Max(c => (DateTime?)c.CreatedAt)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var dto = new WorkspaceIndexStatusDto
            {
                Enabled = enabled,
                ChunkCount = chunkStats?.ChunkCount ?? 0,
                FilesIndexed = chunkStats?.DocumentCount ?? 0,
                LastIndexedAt = chunkStats?.LastIndexedAt
            };

            // Прогресс текущего фонового прогона (если был).
            if (_progress.TryGetValue(userId, out var progress))
            {
                lock (progress)
                {
                    dto.IsIndexing = progress.IsIndexing;
                    dto.TotalFiles = progress.TotalFiles;
                    dto.FilesProcessed = progress.FilesProcessed;
                    dto.ChunksCreated = progress.ChunksCreated;
                    dto.LastError = progress.LastError;
                }
            }

            return dto;
        }

        // ============================================================
        // EnableAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<WorkspaceIndexStatusDto> EnableAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var userSettings = scope.ServiceProvider.GetRequiredService<IUserSettingsService>();
                await userSettings.SetAsync(
                    userId, EnabledSettingKey, "true", EnabledSettingType, cancellationToken);
            }

            StartIndexingInBackground(userId);
            return await GetStatusAsync(userId, cancellationToken);
        }

        // ============================================================
        // DisableAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<WorkspaceIndexStatusDto> DisableAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var userSettings = scope.ServiceProvider.GetRequiredService<IUserSettingsService>();
                var ingestion = scope.ServiceProvider.GetRequiredService<IDocumentIngestionService>();

                await userSettings.SetAsync(
                    userId, EnabledSettingKey, "false", EnabledSettingType, cancellationToken);

                // Очистка чанков (БД + VectorStore) для этого пользователя.
                var removed = await ingestion.ClearIndexForUserAsync(
                    IndexName, userId, cancellationToken);

                _logger.LogInformation(
                    "Workspace index отключён: userId={UserId}, удалено чанков={Removed}",
                    userId, removed);
            }

            // Сброс прогресса.
            if (_progress.TryGetValue(userId, out var progress))
            {
                lock (progress)
                {
                    progress.IsIndexing = false;
                    progress.TotalFiles = 0;
                    progress.FilesProcessed = 0;
                    progress.ChunksCreated = 0;
                    progress.LastError = null;
                }
            }

            return await GetStatusAsync(userId, cancellationToken);
        }

        // ============================================================
        // ReindexAsync
        // ============================================================

        /// <inheritdoc />
        public async Task<WorkspaceIndexStatusDto> ReindexAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            // Ранняя проверка: если индексация уже идёт — не трогаем данные.
            if (_progress.TryGetValue(userId, out var existing) && existing.IsIndexing)
            {
                _logger.LogDebug(
                    "Reindex: индексация уже идёт, возвращаю текущий статус (userId={UserId})",
                    userId);
                return await GetStatusAsync(userId, cancellationToken);
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                var userSettings = scope.ServiceProvider.GetRequiredService<IUserSettingsService>();
                var ingestion = scope.ServiceProvider.GetRequiredService<IDocumentIngestionService>();

                var enabled = await userSettings.GetBoolAsync(
                    userId, EnabledSettingKey, defaultValue: false, cancellationToken);

                if (!enabled)
                {
                    throw new InvalidOperationException(
                        "Workspace index отключён. Сначала включите его.");
                }

                await ingestion.ClearIndexForUserAsync(IndexName, userId, cancellationToken);
            }

            StartIndexingInBackground(userId);
            return await GetStatusAsync(userId, cancellationToken);
        }

        // ============================================================
        // Фоновая индексация
        // ============================================================

        /// <summary>
        /// Запускает фоновую задачу индексации, если она ещё не запущена.
        /// Идемпотентно: повторный вызов при <c>IsIndexing = true</c> — no-op.
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        private void StartIndexingInBackground(int userId)
        {
            var progress = _progress.GetOrAdd(userId, _ => new WorkspaceIndexProgress());

            lock (progress)
            {
                if (progress.IsIndexing)
                {
                    _logger.LogDebug(
                        "Workspace indexing уже идёт, пропускаю запуск (userId={UserId})",
                        userId);
                    return;
                }

                progress.IsIndexing = true;
                progress.StartedAt = DateTime.UtcNow;
                progress.FinishedAt = null;
                progress.TotalFiles = 0;
                progress.FilesProcessed = 0;
                progress.ChunksCreated = 0;
                progress.LastError = null;
            }

            // Fire-and-forget. Все исключения обрабатываются внутри RunIndexingAsync.
            _ = Task.Run(() => RunIndexingAsync(userId, progress));
        }

        /// <summary>
        /// Фоновая задача: обход workspace и индексация каждого файла.
        /// Ошибки отдельных файлов не прерывают прогон — пишутся в
        /// <see cref="WorkspaceIndexProgress.LastError"/>.
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="progress">Объект прогресса (тот же, что в <c>_progress</c>)</param>
        private async Task RunIndexingAsync(int userId, WorkspaceIndexProgress progress)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceResolver>();
                var ingestion = scope.ServiceProvider.GetRequiredService<IDocumentIngestionService>();

                var root = await workspace.GetWorkspacePathAsync(userId);
                var files = EnumerateWorkspaceFiles(root).ToList();

                lock (progress) { progress.TotalFiles = files.Count; }

                _logger.LogInformation(
                    "Workspace indexing start: userId={UserId}, root={Root}, files={Files}",
                    userId, root, files.Count);

                foreach (var file in files)
                {
                    try
                    {
                        var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');

                        var result = await ingestion.IngestAsync(new IngestionRequest
                        {
                            IndexName = IndexName,
                            SourceType = IngestionSourceType.File,
                            FilePath = file,
                            UserId = userId,
                            ChatId = null,
                            ForceReindex = true,
                            Source = relativePath
                        });

                        lock (progress)
                        {
                            progress.FilesProcessed++;
                            progress.ChunksCreated += result.DocumentChunksCreated;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Workspace indexing: ошибка на файле {Path} (userId={UserId})",
                            file, userId);

                        lock (progress)
                        {
                            progress.LastError = $"{Path.GetFileName(file)}: {ex.Message}";
                        }
                    }
                }

                _logger.LogInformation(
                    "Workspace indexing done: userId={UserId}, files={Files}, chunks={Chunks}",
                    userId, progress.FilesProcessed, progress.ChunksCreated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Workspace indexing упал (userId={UserId})", userId);

                lock (progress) { progress.LastError = ex.Message; }
            }
            finally
            {
                lock (progress)
                {
                    progress.IsIndexing = false;
                    progress.FinishedAt = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Обходит файлы workspace, пропуская служебные подпапки и
        /// неподдерживаемые расширения (фильтр — по
        /// <see cref="IRagDocumentParserRegistry.GetAllSupportedExtensions"/>).
        /// </summary>
        /// <param name="root">Абсолютный путь к workspace пользователя</param>
        /// <returns>Последовательность абсолютных путей к файлам</returns>
        private IEnumerable<string> EnumerateWorkspaceFiles(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                yield break;

            var allowedExts = new HashSet<string>(
                _parserRegistry.GetAllSupportedExtensions(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                // Пропускаем служебные подпапки (любой сегмент пути, кроме имени файла).
                var rel = Path.GetRelativePath(root, file);
                var parts = rel.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                var skip = false;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (SkippedSubfolders.Contains(parts[i]))
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip) continue;

                // Фильтр по расширению.
                var ext = Path.GetExtension(file);
                if (string.IsNullOrEmpty(ext) || !allowedExts.Contains(ext))
                    continue;

                yield return file;
            }
        }

        /// <summary>
        /// Прогресс фонового прогона для одного пользователя (in-memory).
        /// </summary>
        private sealed class WorkspaceIndexProgress
        {
            /// <summary>Идёт ли прогон прямо сейчас.</summary>
            public bool IsIndexing { get; set; }

            /// <summary>UTC-время старта текущего (последнего) прогона.</summary>
            public DateTime? StartedAt { get; set; }

            /// <summary>UTC-время завершения текущего (последнего) прогона.</summary>
            public DateTime? FinishedAt { get; set; }

            /// <summary>Всего файлов к обработке.</summary>
            public int TotalFiles { get; set; }

            /// <summary>Обработано файлов.</summary>
            public int FilesProcessed { get; set; }

            /// <summary>Создано чанков.</summary>
            public int ChunksCreated { get; set; }

            /// <summary>Ошибка последнего неудачного файла (или null).</summary>
            public string LastError { get; set; }
        }
    }
}