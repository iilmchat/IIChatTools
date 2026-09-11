using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса настроек на основе таблицы AppSettings.
    /// Значения по умолчанию берутся из appsettings.json.
    /// </summary>
    public class AppSettingsService : IAppSettingsService
    {
        private readonly AppDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AppSettingsService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public AppSettingsService(
            AppDbContext dbContext,
            IConfiguration configuration,
            ILogger<AppSettingsService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SettingDto>> GetAllAsync()
        {
            var list = await _dbContext.AppSettings
                .AsNoTracking()
                .OrderBy(s => s.Category)
                .ThenBy(s => s.Key)
                .ToListAsync();

            return list.Select(ToDto).ToList();
        }

        /// <inheritdoc />
        public async Task<SettingDto> GetByIdAsync(int id)
        {
            var entity = await _dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            return entity == null ? null : ToDto(entity);
        }

        /// <inheritdoc />
        public async Task<SettingDto> CreateAsync(SettingDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Key))
                throw new ArgumentException("Ключ настройки обязателен", nameof(dto));

            var exists = await _dbContext.AppSettings.AnyAsync(s => s.Key == dto.Key);
            if (exists)
                throw new InvalidOperationException($"Настройка с ключом '{dto.Key}' уже существует");

            var entity = new AppSetting
            {
                Key = dto.Key,
                Value = dto.Value ?? string.Empty,
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "string" : dto.Type,
                Category = string.IsNullOrWhiteSpace(dto.Category) ? "Общие" : dto.Category,
                IsDefault = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AppSettings.Add(entity);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Создана настройка {Key}", entity.Key);
            return ToDto(entity);
        }

        /// <inheritdoc />
        public async Task<SettingDto> UpdateAsync(int id, SettingDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var entity = await _dbContext.AppSettings.FirstOrDefaultAsync(s => s.Id == id);
            if (entity == null) return null;

            if (!string.Equals(entity.Key, dto.Key, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(dto.Key))
            {
                var dup = await _dbContext.AppSettings.AnyAsync(s => s.Key == dto.Key && s.Id != id);
                if (dup)
                    throw new InvalidOperationException($"Настройка с ключом '{dto.Key}' уже существует");
                entity.Key = dto.Key;
            }

            entity.Value = dto.Value ?? string.Empty;
            entity.Type = string.IsNullOrWhiteSpace(dto.Type) ? entity.Type : dto.Type;
            entity.Category = string.IsNullOrWhiteSpace(dto.Category) ? entity.Category : dto.Category;
            entity.IsDefault = false;
            entity.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Обновлена настройка {Key}", entity.Key);
            return ToDto(entity);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _dbContext.AppSettings.FirstOrDefaultAsync(s => s.Id == id);
            if (entity == null) return false;

            _dbContext.AppSettings.Remove(entity);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Удалена настройка {Key}", entity.Key);
            return true;
        }

        /// <inheritdoc />
        public async Task<SettingDto> ResetToDefaultAsync(int id)
        {
            var entity = await _dbContext.AppSettings.FirstOrDefaultAsync(s => s.Id == id);
            if (entity == null) return null;

            var defaultValue = _configuration[entity.Key];
            if (defaultValue == null)
                throw new InvalidOperationException(
                    $"Для ключа '{entity.Key}' не найдено значение по умолчанию в appsettings.json");

            entity.Value = defaultValue;
            entity.IsDefault = true;
            entity.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Настройка {Key} сброшена к значению по умолчанию", entity.Key);
            return ToDto(entity);
        }

        /// <inheritdoc />
        public async Task<int> SyncDefaultsFromConfigurationAsync()
        {
            var knownKeys = new[]
            {
                "Workspace:RootPath",
                "Workspace:MaxFileSizeBytes",
                "Workspace:MaxCommandOutputBytes",
                "Security:EnableCodeExecution",
                "Security:EnableBrowserAutomation",
                "Security:EnableShellCommands",
                "Security:ApprovalExpirationMinutes",
                "Tools:RequireApprovalByDefault",
                "Tools:Whitelist"
            };

            var count = 0;
            foreach (var key in knownKeys)
            {
                var defaultValue = _configuration[key];
                if (defaultValue == null) continue;

                var existing = await _dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
                if (existing == null)
                {
                    _dbContext.AppSettings.Add(new AppSetting
                    {
                        Key = key,
                        Value = defaultValue,
                        Type = InferType(key),
                        Category = InferCategory(key),
                        IsDefault = true,
                        CreatedAt = DateTime.UtcNow
                    });
                    count++;
                }
            }

            if (count > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Синхронизировано настроек по умолчанию: {Count}", count);
            }

            return count;
        }

        /// <summary>
        /// Преобразует сущность в DTO.
        /// </summary>
        /// <param name="entity">Сущность</param>
        /// <returns>DTO</returns>
        private SettingDto ToDto(AppSetting entity)
        {
            return new SettingDto
            {
                Id = entity.Id,
                Key = entity.Key,
                Value = entity.Value,
                Type = entity.Type,
                Category = entity.Category,
                IsDefault = entity.IsDefault,
                DefaultValue = _configuration[entity.Key]
            };
        }

        /// <summary>
        /// Выводит тип настройки по ключу.
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <returns>Тип</returns>
        private static string InferType(string key)
        {
            if (key.EndsWith("Bytes", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith("Minutes", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith("Seconds", StringComparison.OrdinalIgnoreCase))
                return "int";
            if (key.StartsWith("Security:Enable", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith("ByDefault", StringComparison.OrdinalIgnoreCase))
                return "bool";
            return "string";
        }

        /// <summary>
        /// Выводит категорию по ключу.
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <returns>Категория</returns>
        private static string InferCategory(string key)
        {
            if (key.StartsWith("Workspace", StringComparison.OrdinalIgnoreCase)) return "Рабочее пространство";
            if (key.StartsWith("Security", StringComparison.OrdinalIgnoreCase)) return "Безопасность";
            if (key.StartsWith("Tools", StringComparison.OrdinalIgnoreCase)) return "Инструменты";
            if (key.StartsWith("Jwt", StringComparison.OrdinalIgnoreCase)) return "Аутентификация";
            return "Общие";
        }
    }
}