using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IIChatTools.Services.Implementation.BackgroundJobs
{
    /// <summary>
    /// Сервис плана задач (Todo) для чата (v2.1.0, Фаза 5).
    /// </summary>
    public class TodoService : ITodoService
    {
        /// <summary>Максимум пунктов на чат (защита от «бесконечного» плана).</summary>
        private const int MaxTodosPerChat = 50;

        private readonly AppDbContext _db;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="db">DbContext приложения.</param>
        public TodoService(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <inheritdoc />
        public async Task<ChatTodo> CreateAsync(int chatId, int userId, string title, string description)
        {
            var chatExists = await _db.Chats
                .AsNoTracking()
                .AnyAsync(c => c.Id == chatId && c.UserId == userId);

            if (!chatExists)
                throw new UnauthorizedAccessException("Чат не найден или недоступен.");

            var normalizedTitle = (title ?? string.Empty).Trim();
            if (normalizedTitle.Length == 0)
                throw new InvalidOperationException("Название задачи обязательно.");
            if (normalizedTitle.Length > 200)
                throw new InvalidOperationException("Название задачи не должно превышать 200 символов.");

            var normalizedDescription = string.IsNullOrWhiteSpace(description)
                ? null
                : description.Trim();
            if (normalizedDescription != null && normalizedDescription.Length > 2000)
                throw new InvalidOperationException("Описание задачи не должно превышать 2000 символов.");

            var count = await _db.ChatTodos.CountAsync(t => t.ChatId == chatId);
            if (count >= MaxTodosPerChat)
                throw new InvalidOperationException(
                    $"В чате уже {count} задач — максимум {MaxTodosPerChat}.");

            var maxSortOrder = await _db.ChatTodos
                .Where(t => t.ChatId == chatId)
                .Select(t => (int?)t.SortOrder)
                .MaxAsync() ?? -1;

            var todo = new ChatTodo
            {
                ChatId = chatId,
                Title = normalizedTitle,
                Description = normalizedDescription,
                Status = "Pending",
                SortOrder = maxSortOrder + 1,
                CreatedAt = DateTime.UtcNow
            };

            _db.ChatTodos.Add(todo);
            await _db.SaveChangesAsync();
            return todo;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ChatTodo>> GetForChatAsync(int chatId, int userId)
        {
            var ownsChat = await _db.Chats
                .AsNoTracking()
                .AnyAsync(c => c.Id == chatId && c.UserId == userId);

            if (!ownsChat)
                throw new UnauthorizedAccessException("Чат не найден или недоступен.");

            return await _db.ChatTodos
                .AsNoTracking()
                .Where(t => t.ChatId == chatId)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .ToListAsync();
        }

        /// <inheritdoc />
        public async Task UpdateStatusAsync(int todoId, int userId, string status)
        {
            var todo = await _db.ChatTodos
                .Include(t => t.Chat)
                .FirstOrDefaultAsync(t => t.Id == todoId);

            if (todo == null || todo.Chat == null || todo.Chat.UserId != userId)
                throw new UnauthorizedAccessException("Задача не найдена или недоступна.");

            var normalized = NormalizeStatus(status);
            todo.Status = normalized;
            todo.CompletedAt = normalized == "Completed" ? DateTime.UtcNow : null;

            await _db.SaveChangesAsync();
        }

        /// <inheritdoc />
        public async Task DeleteAsync(int todoId, int userId)
        {
            var todo = await _db.ChatTodos
                .Include(t => t.Chat)
                .FirstOrDefaultAsync(t => t.Id == todoId);

            if (todo == null || todo.Chat == null || todo.Chat.UserId != userId)
                throw new UnauthorizedAccessException("Задача не найдена или недоступна.");

            _db.ChatTodos.Remove(todo);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Приводит статус к каноническому виду (case-insensitive).
        /// </summary>
        private static string NormalizeStatus(string status)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "pending": return "Pending";
                case "inprogress": return "InProgress";
                case "completed": return "Completed";
                default:
                    throw new InvalidOperationException(
                        "Недопустимый статус. Допустимые: Pending, InProgress, Completed.");
            }
        }
    }
}
