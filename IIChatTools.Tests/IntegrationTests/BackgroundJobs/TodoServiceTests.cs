using System;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Implementation.BackgroundJobs;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests.BackgroundJobs
{
    /// <summary>
    /// Интеграционные тесты <see cref="TodoService"/> (v2.1.0, Фаза 5).
    /// InMemory-БД через <see cref="TestDbContextFactory"/>.
    /// </summary>
    public class TodoServiceTests
    {
        private static async Task<Chat> SeedChatAsync(AppDbContext db, int userId = 1)
        {
            var chat = new Chat
            {
                UserId = userId,
                Title = "Test chat",
                Model = "test-model",
                UpdatedAt = DateTime.UtcNow
            };
            db.Chats.Add(chat);
            await db.SaveChangesAsync();
            return chat;
        }

        /// <summary>SortOrder = 0, 1, 2 при последовательном создании.</summary>
        [Fact]
        public async Task CreateAsync_AssignsSequentialSortOrder()
        {
            using var db = TestDbContextFactory.Create();
            var chat = await SeedChatAsync(db);
            var service = new TodoService(db);

            var first = await service.CreateAsync(chat.Id, 1, "Первый", null);
            var second = await service.CreateAsync(chat.Id, 1, "Второй", "desc");
            var third = await service.CreateAsync(chat.Id, 1, "Третий", null);

            Assert.Equal(0, first.SortOrder);
            Assert.Equal(1, second.SortOrder);
            Assert.Equal(2, third.SortOrder);
            Assert.Equal("Pending", first.Status);
            Assert.Equal("desc", second.Description);
        }

        /// <summary>Список отсортирован по SortOrder.</summary>
        [Fact]
        public async Task GetForChatAsync_ReturnsOrdered()
        {
            using var db = TestDbContextFactory.Create();
            var chat = await SeedChatAsync(db);
            var service = new TodoService(db);

            await service.CreateAsync(chat.Id, 1, "B", null);
            await service.CreateAsync(chat.Id, 1, "A", null);

            var items = await service.GetForChatAsync(chat.Id, 1);

            Assert.Equal(2, items.Count);
            Assert.Equal("B", items[0].Title);
            Assert.Equal("A", items[1].Title);
        }

        /// <summary>Чужой чат → UnauthorizedAccessException.</summary>
        [Fact]
        public async Task CreateAsync_OtherUser_Throws()
        {
            using var db = TestDbContextFactory.Create();
            var chat = await SeedChatAsync(db, userId: 1);
            var service = new TodoService(db);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.CreateAsync(chat.Id, 2, "Чужой", null));
        }

        /// <summary>Completed выставляет CompletedAt; InProgress снимает его.</summary>
        [Fact]
        public async Task UpdateStatusAsync_Completed_SetsAndClearsCompletedAt()
        {
            using var db = TestDbContextFactory.Create();
            var chat = await SeedChatAsync(db);
            var service = new TodoService(db);
            var todo = await service.CreateAsync(chat.Id, 1, "Задача", null);

            await service.UpdateStatusAsync(todo.Id, 1, "Completed");
            var completed = (await service.GetForChatAsync(chat.Id, 1)).Single(t => t.Id == todo.Id);
            Assert.Equal("Completed", completed.Status);
            Assert.NotNull(completed.CompletedAt);

            await service.UpdateStatusAsync(todo.Id, 1, "InProgress");
            var reopened = (await service.GetForChatAsync(chat.Id, 1)).Single(t => t.Id == todo.Id);
            Assert.Equal("InProgress", reopened.Status);
            Assert.Null(reopened.CompletedAt);
        }

        /// <summary>Недопустимый статус → InvalidOperationException.</summary>
        [Fact]
        public async Task UpdateStatusAsync_InvalidStatus_Throws()
        {
            using var db = TestDbContextFactory.Create();
            var chat = await SeedChatAsync(db);
            var service = new TodoService(db);
            var todo = await service.CreateAsync(chat.Id, 1, "Задача", null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.UpdateStatusAsync(todo.Id, 1, "Done"));
        }

        /// <summary>Delete удаляет пункт.</summary>
        [Fact]
        public async Task DeleteAsync_RemovesTodo()
        {
            using var db = TestDbContextFactory.Create();
            var chat = await SeedChatAsync(db);
            var service = new TodoService(db);
            var todo = await service.CreateAsync(chat.Id, 1, "Удалить", null);

            await service.DeleteAsync(todo.Id, 1);

            Assert.Empty(await service.GetForChatAsync(chat.Id, 1));
        }

        /// <summary>Пустой title → InvalidOperationException.</summary>
        [Fact]
        public async Task CreateAsync_EmptyTitle_Throws()
        {
            using var db = TestDbContextFactory.Create();
            var chat = await SeedChatAsync(db);
            var service = new TodoService(db);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CreateAsync(chat.Id, 1, "   ", null));
        }
    }
}
