using System;
using System.Collections.Concurrent;
using System.Threading;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Реализация <see cref="IOcrProgressTracker"/> (KI-204). Singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Хранилище:</b> <c>ConcurrentDictionary&lt;string, ProgressState&gt;</c>.
    /// Записи живут до 5 минут после последнего обновления (cleanup timer).
    /// </para>
    /// <para>
    /// <b>AsyncLocal:</b> <see cref="Report"/> читает ключ из <c>_currentKey.Value</c>.
    /// Устанавливается в <see cref="BeginScope"/>.
    /// </para>
    /// </remarks>
    public sealed class OcrProgressTracker : IOcrProgressTracker, IDisposable
    {
        /// <summary>TTL записи после последнего обновления (минуты).</summary>
        private const int TtlMinutes = 5;

        /// <summary>Интервал cleanup'а (минуты).</summary>
        private const int CleanupIntervalMinutes = 2;

        private readonly ConcurrentDictionary<string, ProgressState> _states
            = new ConcurrentDictionary<string, ProgressState>(StringComparer.Ordinal);

        private readonly AsyncLocal<string> _currentKey = new AsyncLocal<string>();
        private readonly Timer _cleanupTimer;
        private readonly ILogger<OcrProgressTracker> _logger;
        private bool _disposed;

        /// <summary>
        /// Создаёт трекер. Запускает cleanup timer.
        /// </summary>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если logger = null.</exception>
        public OcrProgressTracker(ILogger<OcrProgressTracker> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cleanupTimer = new Timer(
                CleanupStale,
                state: null,
                dueTime: TimeSpan.FromMinutes(CleanupIntervalMinutes),
                period: TimeSpan.FromMinutes(CleanupIntervalMinutes));
        }

        /// <inheritdoc />
        public IDisposable BeginScope(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return NoopScope.Instance;
            }

            var previous = _currentKey.Value;
            _currentKey.Value = key;

            var fileName = ExtractFileName(key);
            _states[key] = new ProgressState
            {
                FileName = fileName,
                CurrentPage = 0,
                TotalPages = 0,
                IsProcessing = true,
                LastUpdateUtc = DateTime.UtcNow
            };

            _logger.LogDebug(
                "OcrProgress: BeginScope key={Key}, fileName={FileName}",
                key, fileName);

            return new Scope(this, key, previous);
        }

        /// <inheritdoc />
        public void Report(int currentPage, int totalPages)
        {
            var key = _currentKey.Value;
            if (string.IsNullOrEmpty(key)) return;

            if (_states.TryGetValue(key, out var state))
            {
                state.CurrentPage = currentPage;
                state.TotalPages = totalPages;
                state.LastUpdateUtc = DateTime.UtcNow;

                _logger.LogDebug(
                    "OcrProgress: Report key={Key}, page={Page}/{Total}",
                    key, currentPage, totalPages);
            }
        }

        /// <inheritdoc />
        public void Complete(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (_states.TryGetValue(key, out var state))
            {
                state.IsProcessing = false;
                state.LastUpdateUtc = DateTime.UtcNow;

                _logger.LogDebug("OcrProgress: Complete key={Key}", key);
            }
        }

        /// <inheritdoc />
        public OcrProgressDto Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_states.TryGetValue(key, out var state)) return null;

            // Не отдаём пустые (TotalPages == 0) — клиент не показывает прогресс.
            if (state.TotalPages == 0) return null;

            return new OcrProgressDto
            {
                IsProcessing = state.IsProcessing,
                CurrentPage = state.CurrentPage,
                TotalPages = state.TotalPages,
                FileName = state.FileName
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cleanupTimer?.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "OcrProgress: cleanup dispose"); }
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Извлекает имя файла из ключа <c>{chatId}:{fileName}</c>.
        /// </summary>
        private static string ExtractFileName(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var idx = key.IndexOf(':');
            return idx >= 0 && idx < key.Length - 1
                ? key.Substring(idx + 1)
                : key;
        }

        /// <summary>
        /// Удаляет записи, не обновлявшиеся больше <see cref="TtlMinutes"/>.
        /// </summary>
        private void CleanupStale(object state)
        {
            if (_disposed) return;

            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-TtlMinutes);
                var removed = 0;

                foreach (var kvp in _states)
                {
                    if (kvp.Value.LastUpdateUtc < cutoff)
                    {
                        if (_states.TryRemove(kvp.Key, out _)) removed++;
                    }
                }

                if (removed > 0)
                {
                    _logger.LogDebug("OcrProgress: cleanup удалил {Count} записей", removed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "OcrProgress: cleanup упал (игнорируем)");
            }
        }

        /// <summary>
        /// Восстанавливает предыдущий AsyncLocal и помечает запись как завершённую.
        /// </summary>
        private sealed class Scope : IDisposable
        {
            private readonly OcrProgressTracker _owner;
            private readonly string _key;
            private readonly string _previous;
            private bool _disposed;

            public Scope(OcrProgressTracker owner, string key, string previous)
            {
                _owner = owner;
                _key = key;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                _owner._currentKey.Value = _previous;
                _owner.Complete(_key);
            }
        }

        /// <summary>Заглушка для пустого/невалидного ключа.</summary>
        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new NoopScope();
            public void Dispose() { }
        }

        /// <summary>Внутреннее состояние одного файла.</summary>
        private sealed class ProgressState
        {
            public string FileName { get; set; }
            public int CurrentPage { get; set; }
            public int TotalPages { get; set; }
            public bool IsProcessing { get; set; }
            public DateTime LastUpdateUtc { get; set; }
        }
    }
}