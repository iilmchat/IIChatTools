// AuditService.cs
using System;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;


namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса аудита
    /// </summary>
    public class AuditService : IAuditService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<AuditService> _logger;

        public AuditService(AppDbContext dbContext, ILogger<AuditService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task LogActionAsync(AuditLog logEntry)
        {
            if (logEntry == null)
                throw new ArgumentNullException(nameof(logEntry));

            try
            {
                _dbContext.AuditLogs.Add(logEntry);
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при записи аудита");
                // Не пробрасываем, чтобы не нарушить основной поток
            }
        }
    }
}