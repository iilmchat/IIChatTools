using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Chat;
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

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<int, int>> GetMessageCountsAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            // Используем Contains по Id чатов пользователя — портируемо на SQL Server/SQLite/InMemory.
            var chatIds = _dbContext.Chats
                .Where(c => c.UserId == userId)
                .Select(c => c.Id);

            var counts = await _dbContext.ChatMessages
                .AsNoTracking()
                .Where(m => chatIds.Contains(m.ChatId))
                .GroupBy(m => m.ChatId)
                .Select(g => new { ChatId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            return counts.ToDictionary(x => x.ChatId, x => x.Count);
        }

        /// <inheritdoc />
        public async Task<int> DeleteLastAssistantExchangeAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            // Проверка владения чатом
            var chat = await _dbContext.Chats
                .FirstOrDefaultAsync(c => c.Id == chatId && c.UserId == userId, cancellationToken);
            if (chat == null)
            {
                return 0;
            }

            // Загружаем все сообщения чата в порядке создания
            var messages = await _dbContext.ChatMessages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

            // Ищем последний user-message (с конца)
            int lastUserIndex = -1;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (string.Equals(messages[i].Role, "user", StringComparison.Ordinal))
                {
                    lastUserIndex = i;
                    break;
                }
            }

            // Если user нет или это последнее сообщение — нечего регенерировать
            if (lastUserIndex < 0 || lastUserIndex == messages.Count - 1)
            {
                return 0;
            }

            // Удаляем всё после последнего user (assistant + tool)
            var toDelete = messages.Skip(lastUserIndex + 1).ToList();
            if (toDelete.Count == 0)
            {
                return 0;
            }

            _dbContext.ChatMessages.RemoveRange(toDelete);
            chat.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Regenerate: удалено {Count} сообщений после последнего user в чате {ChatId}",
                toDelete.Count, chatId);

            return toDelete.Count;
        }

        /// <inheritdoc />
        public async Task<EditUserMessageResult> EditUserMessageAsync(
            int messageId,
            int userId,
            string newContent,
            CancellationToken cancellationToken = default)
        {
            var fail = new EditUserMessageResult { Success = false, MessageId = messageId };

            // 1. Валидация входных данных
            if (string.IsNullOrWhiteSpace(newContent))
            {
                fail.Error = "Содержимое не может быть пустым.";
                return fail;
            }

            var trimmed = newContent.Trim();

            // 2. Загружаем сообщение
            var message = await _dbContext.ChatMessages
                .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

            if (message == null)
            {
                fail.Error = "Сообщение не найдено.";
                return fail;
            }

            // 3. Проверяем владение через чат
            var chat = await _dbContext.Chats
                .FirstOrDefaultAsync(c => c.Id == message.ChatId && c.UserId == userId, cancellationToken);

            if (chat == null)
            {
                // Намеренно обобщённый ответ — не палим существование чужих чатов.
                fail.Error = "Сообщение не найдено.";
                return fail;
            }

            // 4. Редактировать можно только user-сообщения
            if (!string.Equals(message.Role, "user", StringComparison.Ordinal))
            {
                fail.Error = "Можно редактировать только сообщения пользователя.";
                return fail;
            }

            // 5. Обновляем содержимое
            message.Content = trimmed;

            // 6. Удаляем все сообщения после (assistant + tool + последующие)
            var toDelete = await _dbContext.ChatMessages
                .Where(m => m.ChatId == message.ChatId && m.Id > messageId)
                .ToListAsync(cancellationToken);

            if (toDelete.Count > 0)
            {
                _dbContext.ChatMessages.RemoveRange(toDelete);
            }

            // 7. Обновляем UpdatedAt чата
            chat.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Edit user-message: chatId={ChatId}, messageId={MessageId}, deletedAfter={Count}",
                message.ChatId, messageId, toDelete.Count);

            return new EditUserMessageResult
            {
                Success = true,
                MessageId = messageId,
                DeletedCount = toDelete.Count
            };
        }
    }
}