using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Implementation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты поиска чатов (KI-068): по названию и содержимому сообщений.
    /// Включают регрессионный тест на баг SQLite LOWER + кириллица
    /// (два чата с одинаковым title — поиск по «Прив» должен вернуть оба).
    /// </summary>
    public class ChatServiceSearchTests
    {
        /// <summary>
        /// Создаёт ChatService на InMemory-БД (TestDbContextFactory сидирует
        /// трёх пользователей: Id=1, 2, 3).
        /// </summary>
        private static ChatService CreateService()
        {
            var db = TestDbContextFactory.Create();
            return new ChatService(db, NullLogger<ChatService>.Instance);
        }

        /// <summary>
        /// Пустой запрос → семантика «вернуть всё» (совместимо с GetUserChatsAsync).
        /// </summary>
        [Fact]
        public async Task SearchUserChatsAsync_EmptyQuery_ReturnsAllChats()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "Чат A");
            await service.CreateChatAsync(1, "test-model", "Чат B");

            var result = await service.SearchUserChatsAsync(1, "");

            Assert.Equal(2, result.Count);
        }

        /// <summary>
        /// Поиск по названию — кириллица, case-insensitive.
        /// «прив» (lowercase) должен найти «Приветствие в чате» (с заглавной П).
        /// </summary>
        [Fact]
        public async Task SearchUserChatsAsync_ByTitle_CaseInsensitive_Cyrillic()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "Приветствие в чате");
            await service.CreateChatAsync(1, "test-model", "Другой чат");

            var result = await service.SearchUserChatsAsync(1, "прив");

            Assert.Single(result);
            Assert.Equal("Приветствие в чате", result[0].Title);
        }

        /// <summary>
        /// РЕГРЕССИЯ (KI-068): два чата с одинаковым названием, поиск по
        /// «Прив» (заглавная П) должен вернуть оба.
        /// Ранее в SQLite LOWER() не работал с кириллицей → возвращался один
        /// (только тот, у кого в сообщениях был lowercase).
        /// </summary>
        [Fact]
        public async Task SearchUserChatsAsync_TwoChatsWithSameTitle_ReturnsBoth()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "Приветствие в чате");
            await service.CreateChatAsync(1, "test-model", "Приветствие в чате");

            var result = await service.SearchUserChatsAsync(1, "Прив");

            Assert.Equal(2, result.Count);
            Assert.All(result, c => Assert.Equal("Приветствие в чате", c.Title));
        }

        /// <summary>
        /// Поиск по содержимому сообщения — кириллица, case-insensitive.
        /// «приветствие» (lowercase) должен найти сообщение «Расскажи про ПРИВЕТСТВИЕ».
        /// </summary>
        [Fact]
        public async Task SearchUserChatsAsync_ByMessageContent_CaseInsensitive_Cyrillic()
        {
            var service = CreateService();
            var chat = await service.CreateChatAsync(1, "test-model", "Обычный чат");

            await service.AddMessageAsync(chat.Id, 1, new ChatMessage
            {
                Role = "user",
                Content = "Расскажи про ПРИВЕТСТВИЕ"
            });

            var result = await service.SearchUserChatsAsync(1, "приветствие");

            Assert.Single(result);
            Assert.Equal(chat.Id, result[0].Id);
        }

        /// <summary>
        /// Не утекают чаты других пользователей (проверка userId в фильтре).
        /// </summary>
        [Fact]
        public async Task SearchUserChatsAsync_DoesNotLeakOtherUsersChats()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "Мой секретный чат");
            await service.CreateChatAsync(2, "test-model", "Чужой секретный чат");

            // Пользователь 1 ищет «секретный» — должен найти только свой.
            var result = await service.SearchUserChatsAsync(1, "секретный");

            Assert.Single(result);
            Assert.Equal("Мой секретный чат", result[0].Title);
        }

        /// <summary>
        /// Нет совпадений → пустой список.
        /// </summary>
        [Fact]
        public async Task SearchUserChatsAsync_NoMatches_ReturnsEmpty()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "Один чат");
            await service.CreateChatAsync(1, "test-model", "Другой чат");

            var result = await service.SearchUserChatsAsync(1, "zzzzz");

            Assert.Empty(result);
        }

        /// <summary>
        /// Слишком длинный запрос (>200 символов) обрезается и не падает.
        /// </summary>
        [Fact]
        public async Task SearchUserChatsAsync_TruncatesLongQuery()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "a");

            // 300 символов — должно обрезаться до 200, без исключения.
            var longQuery = new string('x', 300);
            var result = await service.SearchUserChatsAsync(1, longQuery);

            Assert.Empty(result);
        }
    }
}