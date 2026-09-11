using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация файлового аудита с записью в JSONL-файл и ротацией по дням.
    /// Формат файла: logs/audit/audit-YYYY-MM-DD.jsonl
    /// </summary>
    public class FileAuditService : IFileAuditService
    {
        private readonly string _logDirectory;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly ILogger<FileAuditService> _logger;

        /// <summary>
        /// Создаёт сервис файлового аудита.
        /// </summary>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public FileAuditService(IConfiguration configuration, ILogger<FileAuditService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var dir = configuration?["Audit:LogDirectory"] ?? "logs/audit";
            _logDirectory = Path.IsPathRooted(dir)
                ? dir
                : Path.Combine(AppContext.BaseDirectory, dir);

            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        /// <inheritdoc />
        public async Task WriteAsync(AuditLog entry)
        {
            if (entry == null) return;

            var filePath = BuildFilePath(DateTime.UtcNow);

            try
            {
                // Формат: одна строка = один JSON-объект
                var json = JsonConvert.SerializeObject(new
                {
                    timestamp = entry.CreatedAt == default ? DateTime.UtcNow : entry.CreatedAt,
                    userId = entry.UserId,
                    toolName = entry.ToolName,
                    parameters = entry.ParametersJson,
                    result = entry.ResultJson,
                    status = entry.Status,
                    durationMs = entry.DurationMs,
                    clientIp = entry.ClientIp
                });

                await _writeLock.WaitAsync();
                try
                {
                    // AppendAllTextAsync — потокобезопасен в рамках процесса
                    await File.AppendAllTextAsync(filePath, json + Environment.NewLine, Encoding.UTF8);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch (Exception ex)
            {
                // Не пробрасываем наружу — аудит не должен ломать основной поток
                _logger.LogError(ex, "Ошибка записи в файловый аудит: {Path}", filePath);
            }
        }

        /// <summary>
        /// Формирует путь к файлу аудита на указанную дату.
        /// </summary>
        /// <param name="utcDate">Дата (UTC)</param>
        /// <returns>Полный путь к файлу</returns>
        private string BuildFilePath(DateTime utcDate)
        {
            var name = $"audit-{utcDate:yyyy-MM-dd}.jsonl";
            return Path.Combine(_logDirectory, name);
        }
    }
}