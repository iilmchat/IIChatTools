using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса подтверждений действий.
    /// </summary>
    public class ApprovalService : IApprovalService
    {
        private readonly AppDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ApprovalService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public ApprovalService(
            AppDbContext dbContext,
            IConfiguration configuration,
            ILogger<ApprovalService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<PendingAction> CreatePendingActionAsync(int userId, string toolName, string parametersJson)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Имя инструмента не может быть пустым", nameof(toolName));

            // Проверяем, что пользователь существует — защита от FK-ошибки SQLite
            var userExists = await _dbContext.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                _logger.LogWarning(
                    "Попытка создать PendingAction для несуществующего пользователя {UserId}",
                    userId);
                throw new InvalidOperationException(
                    $"Пользователь с Id={userId} не найден. Возможно, требуется повторный вход в систему.");
            }

            var expirationMinutes = 5;
            var expRaw = _configuration["Security:ApprovalExpirationMinutes"];
            if (!string.IsNullOrWhiteSpace(expRaw) && int.TryParse(expRaw, out var parsed))
                expirationMinutes = parsed;

            var action = new PendingAction
            {
                UserId = userId,
                ToolName = toolName,
                ParametersJson = parametersJson ?? "{}",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes)
            };

            _dbContext.PendingActions.Add(action);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Создан запрос на подтверждение действия {ToolName} (Id={ActionId}, UserId={UserId})",
                toolName, action.Id, userId);

            return action;
        }

        /// <inheritdoc />
        public Task<PendingAction> GetByIdAsync(int id)
        {
            return _dbContext.PendingActions
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<PendingAction>> GetPendingForUserAsync(int userId)
        {
            var now = DateTime.UtcNow;
            var list = await _dbContext.PendingActions
                .Where(a => a.UserId == userId && a.Status == "Pending" && a.ExpiresAt > now)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return list;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<PendingAction>> GetAllPendingAsync()
        {
            var now = DateTime.UtcNow;
            var list = await _dbContext.PendingActions
                .Where(a => a.Status == "Pending" && a.ExpiresAt > now)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return list;
        }

        /// <inheritdoc />
        public async Task<bool> ApproveAsync(int actionId, int approverUserId)
        {
            var action = await _dbContext.PendingActions.FirstOrDefaultAsync(a => a.Id == actionId);
            if (action == null || action.Status != "Pending")
                return false;

            if (action.ExpiresAt <= DateTime.UtcNow)
            {
                action.Status = "Expired";
                await _dbContext.SaveChangesAsync();
                return false;
            }

            action.Status = "Approved";
            action.ApprovedByUserId = approverUserId;
            action.ApprovedAt = DateTime.UtcNow;
            action.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Действие {ActionId} подтверждено пользователем {ApproverId}", actionId, approverUserId);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> RejectAsync(int actionId, int approverUserId, string reason)
        {
            var action = await _dbContext.PendingActions.FirstOrDefaultAsync(a => a.Id == actionId);
            if (action == null || action.Status != "Pending")
                return false;

            action.Status = "Rejected";
            action.ApprovedByUserId = approverUserId;
            action.ApprovedAt = DateTime.UtcNow;
            action.RejectionReason = reason;
            action.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Действие {ActionId} отклонено пользователем {ApproverId}", actionId, approverUserId);
            return true;
        }

        /// <inheritdoc />
        public Task<bool> IsWhitelistedAsync(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return Task.FromResult(false);

            // Приоритет: значение из БД, затем из конфига
            var setting = _dbContext.AppSettings
                .AsNoTracking()
                .FirstOrDefault(s => s.Key == "Tools.Whitelist");

            var raw = setting?.Value ?? _configuration["Tools:Whitelist"];

            if (string.IsNullOrWhiteSpace(raw))
                return Task.FromResult(false);

            var items = raw
                .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            return Task.FromResult(items.Contains(toolName, StringComparer.OrdinalIgnoreCase));
        }
    }
}