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

        // ============================================================
        // KI-078B: SearchUserChatsWithSnippetAsync — превью для ⌘K-модалки
        // ============================================================

        /// <summary>
        /// KI-078B: пустой запрос → пустой список (в отличие от SearchUserChatsAsync,
        /// который возвращает все чаты — семантика «вернуть всё»).
        /// </summary>
        [Fact]
        public async Task SearchUserChatsWithSnippetAsync_EmptyQuery_ReturnsEmpty()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "Чат A");
            await service.CreateChatAsync(1, "test-model", "Чат B");

            var result = await service.SearchUserChatsWithSnippetAsync(1, "");

            Assert.Empty(result);
        }

        /// <summary>
        /// KI-078B: совпадение в title → MatchedField="title", Snippet=null,
        /// SnippetMatchStart/Length=null.
        /// </summary>
        [Fact]
        public async Task SearchUserChatsWithSnippetAsync_ByTitle_ReturnsMatchedFieldTitle()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "Приветствие в чате");

            var result = await service.SearchUserChatsWithSnippetAsync(1, "прив");

            Assert.Single(result);
            var r = result[0];
            Assert.Equal("title", r.MatchedField);
            Assert.Null(r.Snippet);
            Assert.Null(r.SnippetMatchStart);
            Assert.Null(r.SnippetMatchLength);
            Assert.Equal("Приветствие в чате", r.Title);
        }

        /// <summary>
        /// KI-078B: совпадение в content → MatchedField="content",
        /// Snippet заполнен, offsets указывают на правильную позицию.
        /// </summary>
        [Fact]
        public async Task SearchUserChatsWithSnippetAsync_ByContent_ReturnsSnippetWithOffsets()
        {
            var service = CreateService();
            var chat = await service.CreateChatAsync(1, "test-model", "Обычный чат");

            await service.AddMessageAsync(chat.Id, 1, new ChatMessage
            {
                Role = "user",
                Content = "Расскажи пожалуйста про Dockerfile best practices и как его писать"
            });

            var result = await service.SearchUserChatsWithSnippetAsync(1, "Dockerfile");

            Assert.Single(result);
            var r = result[0];
            Assert.Equal("content", r.MatchedField);
            Assert.NotNull(r.Snippet);
            Assert.NotNull(r.SnippetMatchStart);
            Assert.NotNull(r.SnippetMatchLength);

            // Проверяем, что SnippetMatchStart действительно указывает
            // на «Dockerfile» в snippet.
            var start = r.SnippetMatchStart.Value;
            var len = r.SnippetMatchLength.Value;
            var matched = r.Snippet.Substring(start, len);
            Assert.Equal("Dockerfile", matched, ignoreCase: true);
        }

        /// <summary>
        /// KI-078B: если совпадение и в title, и в content —
        /// приоритет у title (MatchedField="title", Snippet=null).
        /// </summary>
        [Fact]
        public async Task SearchUserChatsWithSnippetAsync_PrioritizesTitleOverContent()
        {
            var service = CreateService();
            var chat = await service.CreateChatAsync(1, "test-model", "Dockerfile настройка");

            await service.AddMessageAsync(chat.Id, 1, new ChatMessage
            {
                Role = "user",
                Content = "Расскажи про Dockerfile"
            });

            var result = await service.SearchUserChatsWithSnippetAsync(1, "Dockerfile");

            Assert.Single(result);
            Assert.Equal("title", result[0].MatchedField);
            Assert.Null(result[0].Snippet);
        }

        /// <summary>
        /// KI-078B: snippet содержит «…» по краям, если совпадение не в начале
        /// и не в конце исходного сообщения.
        /// </summary>
        [Fact]
        public async Task SearchUserChatsWithSnippetAsync_SnippetHasEllipsis()
        {
            var service = CreateService();
            var chat = await service.CreateChatAsync(1, "test-model", "Просто чат");

            // 60 символов префикса + ключевое слово + 120 символов суффикса.
            var content = new string('A', 60) + "КЛЮЧ" + new string('B', 120);
            await service.AddMessageAsync(chat.Id, 1, new ChatMessage
            {
                Role = "user",
                Content = content
            });

            var result = await service.SearchUserChatsWithSnippetAsync(1, "КЛЮЧ");

            Assert.Single(result);
            var snippet = result[0].Snippet;
            Assert.NotNull(snippet);

            // Префикс 30 симв. до — значит «…» есть (не с начала).
            Assert.StartsWith("…", snippet);
            // Суффикс 100 симв. — значит «…» в конце есть.
            Assert.EndsWith("…", snippet);
        }

        /// <summary>
        /// KI-078B: не утекают чаты других пользователей.
        /// </summary>
        [Fact]
        public async Task SearchUserChatsWithSnippetAsync_DoesNotLeakOtherUsersChats()
        {
            var service = CreateService();
            var chat1 = await service.CreateChatAsync(1, "test-model", "Мой чат");
            await service.AddMessageAsync(chat1.Id, 1, new ChatMessage
            {
                Role = "user",
                Content = "Секретный файл"
            });

            var chat2 = await service.CreateChatAsync(2, "test-model", "Чужой чат");
            await service.AddMessageAsync(chat2.Id, 2, new ChatMessage
            {
                Role = "user",
                Content = "Секретный файл"
            });

            var result = await service.SearchUserChatsWithSnippetAsync(1, "секретный");

            Assert.Single(result);
            Assert.Equal("Мой чат", result[0].Title);
        }

        /// <summary>
        /// KI-078B: нет совпадений → пустой список.
        /// </summary>
        [Fact]
        public async Task SearchUserChatsWithSnippetAsync_NoMatches_ReturnsEmpty()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "Один чат");

            var result = await service.SearchUserChatsWithSnippetAsync(1, "zzzzzzzzz");

            Assert.Empty(result);
        }

        /// <summary>
        /// KI-078B: слишком длинный запрос (>200 символов) обрезается, без падения.
        /// </summary>
        [Fact]
        public async Task SearchUserChatsWithSnippetAsync_TruncatesLongQuery()
        {
            var service = CreateService();
            await service.CreateChatAsync(1, "test-model", "a");

            var longQuery = new string('x', 300);
            var result = await service.SearchUserChatsWithSnippetAsync(1, longQuery);

            Assert.Empty(result);
        }
    }
}