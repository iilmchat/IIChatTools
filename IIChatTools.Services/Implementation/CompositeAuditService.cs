using System;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Композитный сервис аудита: пишет в БД и/или в файл в зависимости от конфигурации.
    /// Управляется флагами Audit:ToDatabase и Audit:ToFile.
    /// </summary>
    public class CompositeAuditService : IAuditService
    {
        private readonly AppDbContext _dbContext;
        private readonly IFileAuditService _fileAuditService;
        private readonly bool _toDatabase;
        private readonly bool _toFile;
        private readonly ILogger<CompositeAuditService> _logger;

        /// <summary>
        /// Создаёт композитный сервис аудита.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="fileAuditService">Файловый аудит</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public CompositeAuditService(
            AppDbContext dbContext,
            IFileAuditService fileAuditService,
            IConfiguration configuration,
            ILogger<CompositeAuditService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _fileAuditService = fileAuditService ?? throw new ArgumentNullException(nameof(fileAuditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            _toDatabase = !string.Equals(configuration["Audit:ToDatabase"], "false", StringComparison.OrdinalIgnoreCase);
            _toFile = string.Equals(configuration["Audit:ToFile"], "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public async Task LogActionAsync(AuditLog logEntry)
        {
            if (logEntry == null) throw new ArgumentNullException(nameof(logEntry));

            if (_toDatabase)
            {
                try
                {
                    _dbContext.AuditLogs.Add(logEntry);
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка записи аудита в БД");
                }
            }

            if (_toFile)
            {
                await _fileAuditService.WriteAsync(logEntry);
            }
        }
    }
}