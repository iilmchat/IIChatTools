using System;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Implementation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты per-user retention (v1.4.x, KI-067-2).
    /// Проверяют <c>DeleteOldChatsForUserAsync</c> и
    /// <c>DeleteOldChatsAsync(retentionDays, excludedUserIds)</c>.
    /// </summary>
    public class ChatServiceRetentionTests
    {
        private static ChatService CreateService(out Data.AppDbContext db)
        {
            db = TestDbContextFactory.Create();
            return new ChatService(db, NullLogger<ChatService>.Instance);
        }

        /// <summary>
        /// Создаёт чат с заданным UpdatedAt (в прошлом).
        /// </summary>
        private static async Task<Chat> CreateOldChatAsync(
            Data.AppDbContext db,
            ChatService service,
            int userId,
            int ageInDays)
        {
            var chat = await service.CreateChatAsync(userId, "test-model", $"Old chat {ageInDays}d");
            chat.UpdatedAt = DateTime.UtcNow.AddDays(-ageInDays);
            await db.SaveChangesAsync();
            return chat;
        }

        /// <summary>
        /// KI-067-2: DeleteOldChatsForUserAsync удаляет только чаты указанного пользователя.
        /// </summary>
        [Fact]
        public async Task DeleteOldChatsForUserAsync_RemovesOnlySpecifiedUser()
        {
            var service = CreateService(out var db);

            await CreateOldChatAsync(db, service, userId: 1, ageInDays: 40);
            await CreateOldChatAsync(db, service, userId: 2, ageInDays: 40);

            var deleted = await service.DeleteOldChatsForUserAsync(userId: 1, retentionDays: 30);

            Assert.Equal(1, deleted);
            var remaining = await service.GetUserChatsAsync(2);
            Assert.Single(remaining);
        }

        /// <summary>
        /// KI-067-2: DeleteOldChatsForUserAsync не трогает свежие чаты.
        /// </summary>
        [Fact]
        public async Task DeleteOldChatsForUserAsync_KeepsRecentChats()
        {
            var service = CreateService(out var db);

            await CreateOldChatAsync(db, service, userId: 1, ageInDays: 5);
            await CreateOldChatAsync(db, service, userId: 1, ageInDays: 40);

            var deleted = await service.DeleteOldChatsForUserAsync(userId: 1, retentionDays: 30);

            Assert.Equal(1, deleted);
            var remaining = await service.GetUserChatsAsync(1);
            Assert.Single(remaining);
        }

        /// <summary>
        /// KI-067-2: retentionDays = 0 → ничего не удаляет.
        /// </summary>
        [Fact]
        public async Task DeleteOldChatsForUserAsync_ZeroDays_NoOp()
        {
            var service = CreateService(out var db);
            await CreateOldChatAsync(db, service, userId: 1, ageInDays: 100);

            var deleted = await service.DeleteOldChatsForUserAsync(userId: 1, retentionDays: 0);

            Assert.Equal(0, deleted);
        }

        /// <summary>
        /// KI-067-2: DeleteOldChatsAsync с excludedUserIds исключает указанных пользователей.
        /// </summary>
        [Fact]
        public async Task DeleteOldChatsAsync_WithExcludedUsers_SkipsThem()
        {
            var service = CreateService(out var db);

            await CreateOldChatAsync(db, service, userId: 1, ageInDays: 40);
            await CreateOldChatAsync(db, service, userId: 2, ageInDays: 40);
            await CreateOldChatAsync(db, service, userId: 3, ageInDays: 40);

            var deleted = await service.DeleteOldChatsAsync(
                retentionDays: 30,
                excludedUserIds: new[] { 2 });

            Assert.Equal(2, deleted);   // удалены чаты user 1 и 3
            var user2Chats = await service.GetUserChatsAsync(2);
            Assert.Single(user2Chats);
        }
    }
}