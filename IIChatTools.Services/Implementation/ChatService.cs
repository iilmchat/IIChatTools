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
    /// Реализация сервиса управления чатами.
    /// CRUD + работа с сообщениями, персистентность в БД.
    /// </summary>
    public class ChatService : IChatService
    {
        private const string DefaultTitle = "Новый чат";

        private readonly AppDbContext _dbContext;
        private readonly ILogger<ChatService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatService(
            AppDbContext dbContext,
            ILogger<ChatService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Chat>> GetUserChatsAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Chats
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.UpdatedAt)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<Chat> GetChatAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Chats
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => c.Id == chatId && c.UserId == userId,
                    cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
            int chatId,
            int userId,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            // Проверяем, что чат принадлежит пользователю
            var exists = await _dbContext.Chats
                .AnyAsync(c => c.Id == chatId && c.UserId == userId, cancellationToken);
            if (!exists)
            {
                return Array.Empty<ChatMessage>();
            }

            return await _dbContext.ChatMessages
                .AsNoTracking()
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.CreatedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<Chat> CreateChatAsync(
            int userId,
            string model,
            string title = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("Модель обязательна.", nameof(model));
            }

            var now = DateTime.UtcNow;
            var chat = new Chat
            {
                UserId = userId,
                Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim(),
                Model = model.Trim(),
                UpdatedAt = now,
                CreatedAt = now
            };

            _dbContext.Chats.Add(chat);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Создан чат {ChatId} для пользователя {UserId} (модель: {Model})",
                chat.Id, userId, chat.Model);

            return chat;
        }

        /// <inheritdoc />
        public async Task<Chat> UpdateChatAsync(
            int chatId,
            int userId,
            string title = null,
            string model = null,
            string systemPrompt = null,
            CancellationToken cancellationToken = default)
        {
            var chat = await _dbContext.Chats
                .FirstOrDefaultAsync(c => c.Id == chatId && c.UserId == userId, cancellationToken);
            if (chat == null)
            {
                return null;
            }

            if (title != null) chat.Title = title.Trim();
            if (model != null) chat.Model = model.Trim();
            if (systemPrompt != null) chat.SystemPrompt = systemPrompt;

            chat.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return chat;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteChatAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            var chat = await _dbContext.Chats
                .FirstOrDefaultAsync(c => c.Id == chatId && c.UserId == userId, cancellationToken);
            if (chat == null)
            {
                return false;
            }

            // Каскадное удаление ChatMessages — через FK ON DELETE CASCADE в БД.
            // Дополнительно чистим через EF для InMemory-провайдера (у него нет каскада).
            var messages = _dbContext.ChatMessages.Where(m => m.ChatId == chatId);
            _dbContext.ChatMessages.RemoveRange(messages);
            _dbContext.Chats.Remove(chat);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Удалён чат {ChatId} пользователя {UserId}",
                chatId, userId);

            return true;
        }

        /// <inheritdoc />
        public async Task<ChatMessage> AddMessageAsync(
            int chatId,
            int userId,
            ChatMessage message,
            CancellationToken cancellationToken = default)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            // Проверка владения чатом
            var chat = await _dbContext.Chats
                .FirstOrDefaultAsync(c => c.Id == chatId && c.UserId == userId, cancellationToken);
            if (chat == null)
            {
                throw new InvalidOperationException(
                    $"Чат {chatId} не найден или не принадлежит пользователю {userId}.");
            }

            message.ChatId = chatId;
            message.CreatedAt = DateTime.UtcNow;

            _dbContext.ChatMessages.Add(message);

            // Обновляем UpdatedAt чата для сортировки в sidebar
            chat.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return message;
        }

        /// <inheritdoc />
        public async Task<int> DeleteOldChatsAsync(
            int retentionDays,
            CancellationToken cancellationToken = default)
        {
            if (retentionDays <= 0)
            {
                return 0;
            }

            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            // EF Core 7+ — ExecuteDeleteAsync для bulk-DELETE.
            // Сначала удаляем сообщения, потом чаты (FK cascade не всегда работает с ExecuteDeleteAsync).
            var messageCount = await _dbContext.ChatMessages
                .Where(m => m.Chat.UpdatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var chatCount = await _dbContext.Chats
                .Where(c => c.UpdatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (chatCount > 0)
            {
                _logger.LogInformation(
                    "Retention чатов: удалено {ChatCount} чатов и {MessageCount} сообщений старше {Cutoff:u}",
                    chatCount, messageCount, cutoff);
            }

            return chatCount;
        }
    }
}