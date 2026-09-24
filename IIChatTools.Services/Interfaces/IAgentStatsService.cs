using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Admin;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис агрегированной статистики запусков специализированных суб-агентов
    /// (v1.4.x, KI-076).
    ///
    /// Источник данных — <c>AuditLogs</c>, куда пишет <c>AgentToolBase</c> при
    /// каждом запуске (<c>ToolName = "agent.{name}"</c>).
    /// </summary>
    public interface IAgentStatsService
    {
        /// <summary>
        /// Возвращает статистику по всем агентам, у которых есть хотя бы один запуск.
        /// Джойнит с <see cref="ISubAgentRegistry"/> для получения отображаемого имени.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список статистик (сортировка по TotalRuns desc)</returns>
        Task<IReadOnlyList<AgentStatsDto>> GetAllStatsAsync(
            CancellationToken cancellationToken = default);
    }
}