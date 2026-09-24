using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса per-user настроек (v1.4.x, KI-067).
    ///
    /// <para>
    /// Хранит настройки в таблице <c>UserSettings</c> (UserId + Key — уникальный индекс).
    /// Ключи — snake_case с точками: <c>Chat.RetentionDays</c>, <c>Chat.DoNotDelete</c>.
    /// </para>
    /// </summary>
    public class UserSettingsService : IUserSettingsService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<UserSettingsService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public UserSettingsService(
            AppDbContext dbContext,
            ILogger<UserSettingsService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<UserSetting>> GetAllForUserAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.UserSettings
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.Key)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<UserSetting>> GetAllWithKeyPrefixAsync(
            string keyPrefix,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyPrefix))
            {
                return Array.Empty<UserSetting>();
            }

            // EF Core транслирует StartsWith в LIKE 'prefix%'.
            // Для SqlServer/Sqlite/InMemory работает одинаково.
            return await _dbContext.UserSettings
                .AsNoTracking()
                .Where(s => s.Key.StartsWith(keyPrefix))
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<string> GetStringAsync(
            int userId,
            string key,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            return await _dbContext.UserSettings
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.Key == key)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> GetIntAsync(
            int userId,
            string key,
            int defaultValue = 0,
            CancellationToken cancellationToken = default)
        {
            var raw = await GetStringAsync(userId, key, cancellationToken);
            return int.TryParse(raw, out var value) ? value : defaultValue;
        }

        /// <inheritdoc />
        public async Task<bool> GetBoolAsync(
            int userId,
            string key,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
        {
            var raw = await GetStringAsync(userId, key, cancellationToken);
            return bool.TryParse(raw, out var value) ? value : defaultValue;
        }

        /// <inheritdoc />
        public async Task SetAsync(
            int userId,
            string key,
            string value,
            string type = "string",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Ключ настройки обязателен.", nameof(key));

            var existing = await _dbContext.UserSettings
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Key == key, cancellationToken);

            if (existing != null)
            {
                existing.Value = value;
                existing.Type = type ?? "string";
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _dbContext.UserSettings.Add(new UserSetting
                {
                    UserId = userId,
                    Key = key,
                    Value = value,
                    Type = type ?? "string"
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "UserSetting: userId={UserId}, key=\"{Key}\", type={Type}",
                userId, key, type);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(
            int userId,
            string key,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            var existing = await _dbContext.UserSettings
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Key == key, cancellationToken);

            if (existing == null) return false;

            _dbContext.UserSettings.Remove(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("UserSetting удалена: userId={UserId}, key=\"{Key}\"", userId, key);
            return true;
        }
    }
}