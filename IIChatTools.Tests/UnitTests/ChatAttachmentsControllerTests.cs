using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Controllers;
using IIChatTools.API.Resources;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Unit-тесты <see cref="ChatAttachmentsController"/>
    /// (v1.5.0, KI-083, Шаг 6B).
    ///
    /// <para>
    /// Через fake-зависимости (без HTTP). Полные HTTP-smoke через
    /// <c>WebApplicationFactory</c> — в Шаге 8 (полная integration-серия).
    /// </para>
    /// </summary>
    public class ChatAttachmentsControllerTests
    {
        // ============================================================
        // Fake-зависимости
        // ============================================================

        /// <summary>
        /// Fake <see cref="IChatAttachmentService"/>.
        /// </summary>
        private sealed class FakeAttachmentService : IChatAttachmentService
        {
            public ChatAttachmentDto NextUploadResult { get; set; }
            public Exception NextUploadException { get; set; }

            public List<ChatAttachmentDto> NextListResult { get; set; } = new List<ChatAttachmentDto>();
            public Exception NextListException { get; set; }

            public bool NextDeleteResult { get; set; } = true;
            public Exception NextDeleteException { get; set; }

            public int NextClearRemoved { get; set; } = 2;
            public Exception NextClearException { get; set; }

            public int UploadCallCount { get; private set; }
            public int ListCallCount { get; private set; }
            public int DeleteCallCount { get; private set; }
            public int ClearCallCount { get; private set; }

            public Task<ChatAttachmentDto> UploadAsync(
                int chatId, int userId, Stream content, string fileName, string contentType,
                CancellationToken cancellationToken = default)
            {
                UploadCallCount++;
                if (NextUploadException != null) throw NextUploadException;
                return Task.FromResult(NextUploadResult ?? new ChatAttachmentDto
                {
                    Id = 1, ChatId = chatId, UserId = userId,
                    FileName = fileName, ContentType = contentType,
                    SizeBytes = 100, ChunksCount = 3, UploadedAt = DateTime.UtcNow
                });
            }

            public Task<IReadOnlyList<ChatAttachmentDto>> GetForChatAsync(
                int chatId, int userId, CancellationToken cancellationToken = default)
            {
                ListCallCount++;
                if (NextListException != null) throw NextListException;
                return Task.FromResult<IReadOnlyList<ChatAttachmentDto>>(NextListResult);
            }

            public Task<bool> DeleteAsync(
                int attachmentId, int userId, CancellationToken cancellationToken = default)
            {
                DeleteCallCount++;
                if (NextDeleteException != null) throw NextDeleteException;
                return Task.FromResult(NextDeleteResult);
            }

            public Task<int> ClearForChatAsync(
                int chatId, int userId, CancellationToken cancellationToken = default)
            {
                ClearCallCount++;
                if (NextClearException != null) throw NextClearException;
                return Task.FromResult(NextClearRemoved);
            }
        }

        /// <summary>
        /// Fake <see cref="IChatService"/> — нужен только GetChatAsync.
        /// Остальные методы — заглушки.
        /// </summary>
        private sealed class FakeChatService : IChatService
        {
            /// <summary>Что вернуть при GetChatAsync. null = чат не найден.</summary>
            public Chat NextGetChatResult { get; set; }

            public Task<Chat> GetChatAsync(int chatId, int userId, CancellationToken cancellationToken = default)
                => Task.FromResult(NextGetChatResult);

            // Не используются в тестах контроллера — заглушки.
            public Task<IReadOnlyList<Chat>> GetUserChatsAsync(int userId, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(int chatId, int userId, int limit = 50, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<Chat> CreateChatAsync(int userId, string model, string title = null, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<Chat> UpdateChatAsync(int chatId, int userId, string title = null, string model = null, string systemPrompt = null, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<bool> DeleteChatAsync(int chatId, int userId, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<ChatMessage> AddMessageAsync(int chatId, int userId, ChatMessage message, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<int> DeleteOldChatsAsync(int retentionDays, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<int> DeleteOldChatsAsync(int retentionDays, IReadOnlyCollection<int> excludedUserIds, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<int> DeleteOldChatsForUserAsync(int userId, int retentionDays, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<int> DeleteLastAssistantExchangeAsync(int chatId, int userId, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<IReadOnlyDictionary<int, int>> GetMessageCountsAsync(int userId, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<IIChatTools.Services.DTO.Chat.EditUserMessageResult> EditUserMessageAsync(int messageId, int userId, string newContent, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<IReadOnlyList<Chat>> SearchUserChatsAsync(int userId, string search, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public Task<IReadOnlyList<IIChatTools.Services.DTO.Chat.ChatSearchResultDto>> SearchUserChatsWithSnippetAsync(int userId, string search, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
        }

        /// <summary>
        /// Простейший IStringLocalizer — возвращает ключ как значение.
        /// (Не хотим тащить реальный SharedResources в unit-тест.)
        /// </summary>
        private sealed class PassThroughLocalizer : IStringLocalizer<SharedResources>
        {
            public LocalizedString this[string name] => new LocalizedString(name, name);

            public LocalizedString this[string name, params object[] arguments]
                => new LocalizedString(name, string.Format(name, arguments));

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
                => Array.Empty<LocalizedString>();
        }

        // ============================================================
        // Хелперы
        // ============================================================

        /// <summary>
        /// Создаёт контроллер с fake-зависимостями.
        /// </summary>
        private static (ChatAttachmentsController Controller,
                        FakeAttachmentService AttachmentService,
                        FakeChatService ChatService)
            CreateController(int currentUserId = 1)
        {
            var attachmentService = new FakeAttachmentService();
            var chatService = new FakeChatService();

            var controller = new ChatAttachmentsController(
                attachmentService,
                chatService,
                NullLogger<ChatAttachmentsController>.Instance,
                new PassThroughLocalizer());

            // Настраиваем HttpContext.User с claim для NameIdentifier.
            var claims = new[]
            {
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier, currentUserId.ToString())
            };
            var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuthType");
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            return (controller, attachmentService, chatService);
        }

        /// <summary>
        /// Создаёт тестовый IFormFile.
        /// </summary>
        private static IFormFile MakeFormFile(string fileName, byte[] content, string contentType = "text/plain")
        {
            var stream = new MemoryStream(content);
            return new FormFile(stream, 0, content.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        /// <summary>
        /// Минимальный Chat-объект (для FakeChatService).
        /// </summary>
        private static Chat MakeChat(int id = 1, int userId = 1) => new Chat
        {
            Id = id,
            UserId = userId,
            Title = "test chat",
            Model = "test-model",
            UpdatedAt = DateTime.UtcNow
        };

        /// <summary>
        /// Достаём поле success из OkObjectResult.
        /// </summary>
        private static bool GetSuccess(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            var token = Newtonsoft.Json.Linq.JObject.Parse(json);
            return (bool?)token["success"] ?? false;
        }

        /// <summary>
        /// Достаём message из OkObjectResult.
        /// </summary>
        private static string GetMessage(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            var token = Newtonsoft.Json.Linq.JObject.Parse(json);
            return token["message"]?.ToString();
        }

        // ============================================================
        // POST /api/chat/{chatId}/attachments
        // ============================================================

        /// <summary>
        /// Загрузка валидного файла → 200 OK + success=true + data=DTO.
        /// </summary>
        [Fact]
        public async Task Upload_ValidFile_ReturnsOkWithDto()
        {
            var (controller, attachmentService, chatService) = CreateController();
            chatService.NextGetChatResult = MakeChat();
            attachmentService.NextUploadResult = new ChatAttachmentDto
            {
                Id = 42, ChatId = 1, UserId = 1,
                FileName = "hello.txt", ContentType = "text/plain",
                SizeBytes = 5, ChunksCount = 1, UploadedAt = DateTime.UtcNow
            };

            var file = MakeFormFile("hello.txt", Encoding.UTF8.GetBytes("hello"));

            var result = await controller.UploadAsync(1, file, CancellationToken.None);

            Assert.True(GetSuccess(result));
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            Assert.Contains("hello.txt", json);
            Assert.Equal(1, attachmentService.UploadCallCount);
        }

        /// <summary>
        /// file == null → success=false, сервис не вызван.
        /// </summary>
        [Fact]
        public async Task Upload_NullFile_ReturnsFail()
        {
            var (controller, attachmentService, _) = CreateController();

            var result = await controller.UploadAsync(1, null, CancellationToken.None);

            Assert.False(GetSuccess(result));
            Assert.Contains("Файл не выбран", GetMessage(result));
            Assert.Equal(0, attachmentService.UploadCallCount);
        }

        /// <summary>
        /// Валидация сервиса падает ArgumentException → success=false + сообщение.
        /// </summary>
        [Fact]
        public async Task Upload_TooLarge_ReturnsFail()
        {
            var (controller, attachmentService, chatService) = CreateController();
            chatService.NextGetChatResult = MakeChat();
            attachmentService.NextUploadException = new ArgumentException("Файл слишком большой: 100 байт. Лимит: 10 байт.");

            var file = MakeFormFile("big.txt", new byte[100]);

            var result = await controller.UploadAsync(1, file, CancellationToken.None);

            Assert.False(GetSuccess(result));
            Assert.Contains("слишком большой", GetMessage(result));
        }

        /// <summary>
        /// Чат не существует / не принадлежит → success=false.
        /// </summary>
        [Fact]
        public async Task Upload_ChatNotOwned_ReturnsFail()
        {
            var (controller, attachmentService, _) = CreateController(currentUserId: 1);
            // FakeChatService вернёт null → «не найден».
            // NextGetChatResult по умолчанию = null.

            var file = MakeFormFile("f.txt", Encoding.UTF8.GetBytes("data"));

            var result = await controller.UploadAsync(999, file, CancellationToken.None);

            Assert.False(GetSuccess(result));
            Assert.Equal(0, attachmentService.UploadCallCount);
        }

        // ============================================================
        // GET /api/chat/{chatId}/attachments
        // ============================================================

        /// <summary>
        /// Валидный GET → success=true + список.
        /// </summary>
        [Fact]
        public async Task GetList_Valid_ReturnsList()
        {
            var (controller, attachmentService, chatService) = CreateController();
            chatService.NextGetChatResult = MakeChat();
            attachmentService.NextListResult = new List<ChatAttachmentDto>
            {
                new ChatAttachmentDto { Id = 1, FileName = "a.txt", SizeBytes = 5, ChunksCount = 1 },
                new ChatAttachmentDto { Id = 2, FileName = "b.txt", SizeBytes = 6, ChunksCount = 2 }
            };

            var result = await controller.GetListAsync(1, CancellationToken.None);

            Assert.True(GetSuccess(result));
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            Assert.Contains("a.txt", json);
            Assert.Contains("b.txt", json);
        }

        /// <summary>
        /// Чат не принадлежит пользователю → success=false.
        /// </summary>
        [Fact]
        public async Task GetList_ChatNotFound_ReturnsFail()
        {
            var (controller, _, _) = CreateController();

            var result = await controller.GetListAsync(999, CancellationToken.None);

            Assert.False(GetSuccess(result));
        }

        // ============================================================
        // DELETE /api/chat/{chatId}/attachments/{attachmentId}
        // ============================================================

        /// <summary>
        /// Валидный DELETE → success=true.
        /// </summary>
        [Fact]
        public async Task Delete_Valid_ReturnsSuccess()
        {
            var (controller, attachmentService, chatService) = CreateController();
            chatService.NextGetChatResult = MakeChat();
            attachmentService.NextDeleteResult = true;

            var result = await controller.DeleteAsync(1, 42, CancellationToken.None);

            Assert.True(GetSuccess(result));
            Assert.Equal(1, attachmentService.DeleteCallCount);
        }

        /// <summary>
        /// Вложение не найдено → success=false.
        /// </summary>
        [Fact]
        public async Task Delete_NotFound_ReturnsFail()
        {
            var (controller, attachmentService, chatService) = CreateController();
            chatService.NextGetChatResult = MakeChat();
            attachmentService.NextDeleteResult = false;

            var result = await controller.DeleteAsync(1, 999, CancellationToken.None);

            Assert.False(GetSuccess(result));
        }

        // ============================================================
        // POST /api/chat/{chatId}/attachments/clear
        // ============================================================

        /// <summary>
        /// Валидный clear → success=true + removed.
        /// </summary>
        [Fact]
        public async Task Clear_Valid_ReturnsRemoved()
        {
            var (controller, attachmentService, chatService) = CreateController();
            chatService.NextGetChatResult = MakeChat();
            attachmentService.NextClearRemoved = 3;

            var result = await controller.ClearAsync(1, CancellationToken.None);

            Assert.True(GetSuccess(result));
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            Assert.Contains("\"removed\":3", json);
            Assert.Equal(1, attachmentService.ClearCallCount);
        }
    }
}