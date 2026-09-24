using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.Implementation.Tools.SubAgent;   // AgentToolBase (cref)
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Агрегация статистики запусков агентов из <c>AuditLogs</c> (v1.4.x, KI-076).
    ///
    /// Convention: <see cref="AgentToolBase"/> пишет <c>ToolName = "agent.{AgentName}"</c>.
    /// Этот сервис фильтрует логи по префиксу <c>"agent."</c> и группирует по имени.
    /// </summary>
    public class AgentStatsService : IAgentStatsService
    {
        /// <summary>Префикс ToolName для записей о запусках агентов.</summary>
        private const string AgentToolPrefix = "agent.";

        private readonly AppDbContext _dbContext;
        private readonly ISubAgentRegistry _registry;
        private readonly ILogger<AgentStatsService> _logger;

        /// <summary>
        /// Создаёт сервис статистики агентов.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="registry">Реестр суб-агентов (для DisplayName)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public AgentStatsService(
            AppDbContext dbContext,
            ISubAgentRegistry registry,
            ILogger<AgentStatsService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<AgentStatsDto>> GetAllStatsAsync(
            CancellationToken cancellationToken = default)
        {
            // 1. Группировка в SQL: ToolName → count/avg/max.
            //    EF Core 10 транслирует GroupBy + aggregate в один SQL-запрос
            //    (работает на SqlServer / Sqlite / InMemory).
            var grouped = await _dbContext.AuditLogs
                .AsNoTracking()
                .Where(a => a.ToolName.StartsWith(AgentToolPrefix))
                .GroupBy(a => a.ToolName)
                .Select(g => new
                {
                    ToolName = g.Key,
                    Total = g.Count(),
                    Success = g.Count(x => x.Status == "Success"),
                    Error = g.Count(x => x.Status == "Error"),
                    AvgDuration = g.Average(x => (double)x.DurationMs),
                    LastRun = (DateTime?)g.Max(x => x.CreatedAt)
                })
                .ToListAsync(cancellationToken);

            if (grouped.Count == 0)
            {
                return Array.Empty<AgentStatsDto>();
            }

            // 2. Дедупликация по DisplayName из реестра.
            var descriptors = _registry.GetAll()
                .ToDictionary(d => d.Name, d => d.DisplayName, StringComparer.OrdinalIgnoreCase);

            var result = new List<AgentStatsDto>(grouped.Count);

            foreach (var g in grouped)
            {
                if (string.IsNullOrEmpty(g.ToolName)
                    || !g.ToolName.StartsWith(AgentToolPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var agentName = g.ToolName.Substring(AgentToolPrefix.Length);
                if (string.IsNullOrEmpty(agentName))
                {
                    continue;
                }

                result.Add(new AgentStatsDto
                {
                    AgentName = agentName,
                    DisplayName = descriptors.TryGetValue(agentName, out var dn) && !string.IsNullOrWhiteSpace(dn)
                        ? dn
                        : agentName,
                    TotalRuns = g.Total,
                    SuccessRuns = g.Success,
                    ErrorRuns = g.Error,
                    AvgDurationMs = (long)Math.Round(g.AvgDuration),
                    LastRunAt = g.LastRun
                });
            }

            // 3. Сортировка: сначала самые активные.
            return result
                .OrderByDescending(s => s.TotalRuns)
                .ThenBy(s => s.AgentName, StringComparer.Ordinal)
                .ToList();
        }
    }
}