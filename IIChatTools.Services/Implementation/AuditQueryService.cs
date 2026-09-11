using System;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса чтения аудита.
    /// </summary>
    public class AuditQueryService : IAuditQueryService
    {
        private const int MaxPageSize = 200;

        private readonly AppDbContext _dbContext;
        private readonly ILogger<AuditQueryService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="logger">Логгер</param>
        public AuditQueryService(AppDbContext dbContext, ILogger<AuditQueryService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<PagedResult<AuditLog>> GetPagedAsync(int page, int pageSize, int? userId = null, string toolName = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > MaxPageSize) pageSize = MaxPageSize;

            var query = _dbContext.AuditLogs
                .AsNoTracking()
                .Include(a => a.User)
                .AsQueryable();

            if (userId.HasValue)
                query = query.Where(a => a.UserId == userId.Value);

            if (!string.IsNullOrWhiteSpace(toolName))
                query = query.Where(a => a.ToolName == toolName);

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<AuditLog>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };
        }
    }
}