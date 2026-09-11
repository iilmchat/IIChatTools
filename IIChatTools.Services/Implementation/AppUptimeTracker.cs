using System;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Синглтон-трекер времени запуска приложения.
    /// Регистрируется как singleton в DI.
    /// </summary>
    public class AppUptimeTracker
    {
        /// <summary>
        /// Создаёт трекер и фиксирует момент запуска.
        /// </summary>
        public AppUptimeTracker()
        {
            StartedAtUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Момент запуска приложения (UTC).
        /// </summary>
        public DateTime StartedAtUtc { get; }

        /// <summary>
        /// Возвращает длительность работы приложения.
        /// </summary>
        /// <returns>Длительность работы</returns>
        public TimeSpan GetUptime() => DateTime.UtcNow - StartedAtUtc;
    }
}