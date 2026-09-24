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
    /// Тесты per-user настроек (v1.4.x, KI-067).
    /// Используют InMemory-БД через <see cref="TestDbContextFactory"/>
    /// (сидирует трёх пользователей: Id=1, 2, 3).
    /// </summary>
    public class UserSettingsServiceTests
    {
        private static UserSettingsService CreateService()
        {
            var db = TestDbContextFactory.Create();
            return new UserSettingsService(db, NullLogger<UserSettingsService>.Instance);
        }

        /// <summary>
        /// Пустая таблица → пустой список.
        /// </summary>
        [Fact]
        public async Task GetAllForUserAsync_Empty_ReturnsEmpty()
        {
            var service = CreateService();
            var result = await service.GetAllForUserAsync(1);
            Assert.Empty(result);
        }

        /// <summary>
        /// SetAsync создаёт настройку, GetStringAsync её читает.
        /// </summary>
        [Fact]
        public async Task SetAsync_ThenGetString_ReturnsValue()
        {
            var service = CreateService();

            await service.SetAsync(1, "Chat.RetentionDays", "30", "int");

            var result = await service.GetStringAsync(1, "Chat.RetentionDays");
            Assert.Equal("30", result);
        }

        /// <summary>
        /// SetAsync дважды — upsert (не дублирует запись).
        /// </summary>
        [Fact]
        public async Task SetAsync_Twice_UpdatesExisting()
        {
            var service = CreateService();

            await service.SetAsync(1, "Chat.RetentionDays", "30", "int");
            await service.SetAsync(1, "Chat.RetentionDays", "60", "int");

            var all = await service.GetAllForUserAsync(1);
            Assert.Single(all);
            Assert.Equal("60", all[0].Value);
        }

        /// <summary>
        /// GetIntAsync с заданной настройкой.
        /// </summary>
        [Fact]
        public async Task GetIntAsync_ReturnsStoredValue()
        {
            var service = CreateService();
            await service.SetAsync(1, "Chat.RetentionDays", "45", "int");

            var result = await service.GetIntAsync(1, "Chat.RetentionDays", defaultValue: 30);

            Assert.Equal(45, result);
        }

        /// <summary>
        /// GetIntAsync с отсутствующей настройкой → defaultValue.
        /// </summary>
        [Fact]
        public async Task GetIntAsync_MissingKey_ReturnsDefault()
        {
            var service = CreateService();
            var result = await service.GetIntAsync(1, "Chat.RetentionDays", defaultValue: 30);
            Assert.Equal(30, result);
        }

        /// <summary>
        /// GetBoolAsync с заданной настройкой.
        /// </summary>
        [Fact]
        public async Task GetBoolAsync_ReturnsStoredValue()
        {
            var service = CreateService();
            await service.SetAsync(1, "Chat.DoNotDelete", "true", "bool");

            var result = await service.GetBoolAsync(1, "Chat.DoNotDelete");

            Assert.True(result);
        }

        /// <summary>
        /// GetBoolAsync с невалидным значением → defaultValue.
        /// </summary>
        [Fact]
        public async Task GetBoolAsync_InvalidValue_ReturnsDefault()
        {
            var service = CreateService();
            await service.SetAsync(1, "Chat.DoNotDelete", "not_a_bool", "bool");

            var result = await service.GetBoolAsync(1, "Chat.DoNotDelete", defaultValue: false);

            Assert.False(result);
        }

        /// <summary>
        /// DeleteAsync удаляет настройку.
        /// </summary>
        [Fact]
        public async Task DeleteAsync_RemovesSetting()
        {
            var service = CreateService();
            await service.SetAsync(1, "Chat.RetentionDays", "30", "int");

            var deleted = await service.DeleteAsync(1, "Chat.RetentionDays");

            Assert.True(deleted);
            var result = await service.GetStringAsync(1, "Chat.RetentionDays");
            Assert.Null(result);
        }

        /// <summary>
        /// DeleteAsync для несуществующей настройки → false.
        /// </summary>
        [Fact]
        public async Task DeleteAsync_NotExisting_ReturnsFalse()
        {
            var service = CreateService();
            var deleted = await service.DeleteAsync(1, "Chat.RetentionDays");
            Assert.False(deleted);
        }

        /// <summary>
        /// Настройки пользователей не пересекаются (userId-фильтр).
        /// </summary>
        [Fact]
        public async Task Settings_AreIsolatedPerUser()
        {
            var service = CreateService();

            await service.SetAsync(1, "Chat.RetentionDays", "30", "int");
            await service.SetAsync(2, "Chat.RetentionDays", "60", "int");

            var forUser1 = await service.GetStringAsync(1, "Chat.RetentionDays");
            var forUser2 = await service.GetStringAsync(2, "Chat.RetentionDays");

            Assert.Equal("30", forUser1);
            Assert.Equal("60", forUser2);
        }

        /// <summary>
        /// KI-067-2: GetAllWithKeyPrefixAsync возвращает только настройки
        /// с указанным префиксом (по всем пользователям).
        /// </summary>
        [Fact]
        public async Task GetAllWithKeyPrefixAsync_ReturnsOnlyMatchingPrefix()
        {
            var service = CreateService();

            await service.SetAsync(1, "Chat.RetentionDays", "30", "int");
            await service.SetAsync(1, "Chat.DoNotDelete", "true", "bool");
            await service.SetAsync(2, "Chat.RetentionDays", "60", "int");
            await service.SetAsync(1, "Other.SomeKey", "value", "string");

            var result = await service.GetAllWithKeyPrefixAsync("Chat.");

            Assert.Equal(3, result.Count);
            Assert.All(result, s => Assert.StartsWith("Chat.", s.Key));
        }

        /// <summary>
        /// KI-067-2: GetAllWithKeyPrefixAsync с пустым префиксом → пустой результат.
        /// </summary>
        [Fact]
        public async Task GetAllWithKeyPrefixAsync_EmptyPrefix_ReturnsEmpty()
        {
            var service = CreateService();
            await service.SetAsync(1, "Chat.RetentionDays", "30", "int");

            var result = await service.GetAllWithKeyPrefixAsync("");

            Assert.Empty(result);
        }
    }
}