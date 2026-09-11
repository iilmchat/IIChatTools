using System;
using System.Collections.Generic;

namespace IIChatTools.Services.DTO
{
    /// <summary>
    /// Снимок состояния системы для отображения на странице статуса.
    /// </summary>
    public class StatusSnapshot
    {
        /// <summary>
        /// Версия приложения.
        /// </summary>
        public string AppVersion { get; set; }

        /// <summary>
        /// Время работы приложения в секундах.
        /// </summary>
        public long UptimeSeconds { get; set; }

        /// <summary>
        /// Дата и время запуска приложения (UTC).
        /// </summary>
        public DateTime StartedAtUtc { get; set; }

        /// <summary>
        /// Всего пользователей в системе.
        /// </summary>
        public int TotalUsers { get; set; }

        /// <summary>
        /// Активных пользователей.
        /// </summary>
        public int ActiveUsers { get; set; }

        /// <summary>
        /// Общее число записей аудита.
        /// </summary>
        public int TotalActions { get; set; }

        /// <summary>
        /// Число ожидающих подтверждения действий.
        /// </summary>
        public int PendingApprovals { get; set; }

        /// <summary>
        /// Статус подключения к БД (true = доступна).
        /// </summary>
        public bool DatabaseOnline { get; set; }

        /// <summary>
        /// Словарь внешних зависимостей: имя → версия или null.
        /// </summary>
        public Dictionary<string, string> Dependencies { get; set; }

        /// <summary>
        /// Последние действия (аудит).
        /// </summary>
        public IReadOnlyList<RecentActionDto> RecentActions { get; set; }
    }

    /// <summary>
    /// Краткая информация о действии для отображения в списке последних.
    /// </summary>
    public class RecentActionDto
    {
        /// <summary>
        /// Идентификатор записи аудита.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Имя пользователя.
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Имя инструмента.
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Статус выполнения.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Длительность выполнения в миллисекундах.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Время выполнения (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}