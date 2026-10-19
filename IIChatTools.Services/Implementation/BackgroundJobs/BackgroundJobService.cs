using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.BackgroundJobs
{
    /// <summary>
    /// Сервис управления фоновыми задачами.
    /// </summary>
    public class BackgroundJobService : IBackgroundJobService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<BackgroundJobService> _logger;

        public BackgroundJobService(AppDbContext db, ILogger<BackgroundJobService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<BackgroundJob> CreateAsync(int userId, int? chatId, string type, string parametersJson)
        {
            var job = new BackgroundJob
            {
                UserId = userId,
                ChatId = chatId,
                Type = type,
                ParametersJson = parametersJson,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _db.BackgroundJobs.Add(job);
            await _db.SaveChangesAsync();
            return job;
        }

        public async Task<BackgroundJob> GetAsync(int jobId)
        {
            // AsNoTracking обязателен для SSE-поллинга: FindAsync вернул бы
            // закэшированный tracked-инстанс и прогресс «замёрз» бы.
            return await _db.BackgroundJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);
        }

        public async Task<IReadOnlyList<BackgroundJob>> ListAsync(int userId, int? chatId)
        {
            var query = _db.BackgroundJobs
                .AsNoTracking()
                .Where(j => j.UserId == userId);

            if (chatId.HasValue)
            {
                query = query.Where(j => j.ChatId == chatId.Value);
            }

            return await query
                .OrderByDescending(j => j.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task UpdateStatusAsync(int jobId, string status, int progress, string message, string resultJson = null, string error = null)
        {
            var job = await _db.BackgroundJobs.FindAsync(jobId);
            if (job == null) return;

            // Терминальный статус нельзя перетереть другим статусом
            // (pump и runner работают в разных scope'ах; отмена важнее позднего Failed).
            if (IsTerminal(job.Status) && !string.Equals(job.Status, status, StringComparison.Ordinal))
            {
                return;
            }

            job.Status = status;
            job.Progress = progress;
            job.ProgressMessage = message;
            
            if (resultJson != null) job.ResultJson = resultJson;
            if (error != null) job.Error = error;
            
            if (status == "Running" && job.StartedAt == null) job.StartedAt = DateTime.UtcNow;
            if (status == "Completed" || status == "Failed" || status == "Cancelled") 
                job.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private static bool IsTerminal(string status)
            => string.Equals(status, "Completed", StringComparison.Ordinal)
            || string.Equals(status, "Failed", StringComparison.Ordinal)
            || string.Equals(status, "Cancelled", StringComparison.Ordinal);

        public async Task<int> CountActiveByUserAsync(int userId)
        {
            return await _db.BackgroundJobs
                .CountAsync(j => j.UserId == userId && (j.Status == "Pending" || j.Status == "Running"));
        }
    }
}