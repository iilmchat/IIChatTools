using System;

namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Статистика запусков специализированного суб-агента (v1.4.x, KI-076).
    /// Источник — <c>AuditLogs</c> с <c>ToolName LIKE 'agent.%'</c>.
    /// </summary>
    public class AgentStatsDto
    {
        /// <summary>Техническое имя агента (<c>file_system_agent</c>, ...).</summary>
        public string AgentName { get; set; }

        /// <summary>Русское отображаемое имя (из <c>SubAgentRegistry</c>).</summary>
        public string DisplayName { get; set; }

        /// <summary>Всего запусков.</summary>
        public int TotalRuns { get; set; }

        /// <summary>Успешных запусков (<c>Status == "Success"</c>).</summary>
        public int SuccessRuns { get; set; }

        /// <summary>Запусков с ошибкой (<c>Status == "Error"</c>).</summary>
        public int ErrorRuns { get; set; }

        /// <summary>
        /// Средняя длительность одного запуска (мс).
        /// <c>0</c>, если запусков нет.
        /// </summary>
        public long AvgDurationMs { get; set; }

        /// <summary>
        /// Дата последнего запуска (UTC). <c>null</c>, если запусков нет.
        /// </summary>
        public DateTime? LastRunAt { get; set; }

        /// <summary>
        /// Процент успешных запусков (0–100).
        /// <c>0</c>, если запусков нет.
        /// </summary>
        public double SuccessRate =>
            TotalRuns == 0 ? 0 : Math.Round(SuccessRuns * 100.0 / TotalRuns, 1);
    }
}