namespace IIChatTools.Services.DTO.BackgroundJobs
{
    /// <summary>
    /// Настройки фоновых задач.
    /// </summary>
    public class BackgroundJobOptions
    {
        /// <summary>Максимум одновременных задач в системе.</summary>
        public int MaxConcurrentJobs { get; set; } = 2;

        /// <summary>Максимум активных задач на одного пользователя.</summary>
        public int MaxJobsPerUser { get; set; } = 3;

        /// <summary>Время жизни завершенной задачи (часы).</summary>
        public int RetentionHours { get; set; } = 24;
    }
}