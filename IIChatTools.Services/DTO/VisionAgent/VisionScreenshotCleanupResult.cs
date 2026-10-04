namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Результат одного прогона очистки скриншотов Vision Agent.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф6.6). См. DESIGN § 6.5.
    /// </remarks>
    public class VisionScreenshotCleanupResult
    {
        /// <summary>Сколько папок задач удалено.</summary>
        public int DeletedTaskFolders { get; set; }

        /// <summary>Сколько PNG-файлов удалено (суммарно).</summary>
        public int DeletedFiles { get; set; }

        /// <summary>Освобождено байт (суммарно по удалённым файлам).</summary>
        public long FreedBytes { get; set; }

        /// <summary>Число ошибок (лог + счётчик; cleanup продолжается).</summary>
        public int ErrorCount { get; set; }

        /// <summary>Длительность одного прогона, мс.</summary>
        public long DurationMs { get; set; }
    }
}