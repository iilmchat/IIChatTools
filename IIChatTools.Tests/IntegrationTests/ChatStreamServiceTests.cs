using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты ChatStreamService: успешный стрим, отсутствие чата, пустое сообщение.
    /// Используют реальный ChatService на InMemory-БД + FakeLmStudioClient.
    /// </summary>
    public class ChatStreamServiceTests
    {
        /// <summary>
        /// Fake ILmStudioClient — отдаёт заданные чанки по порядку.
        /// </summary>
        private sealed class FakeLmStudioClient : ILmStudioClient
        {
            public List<ChatCompletionChunk> Chunks { get; } = new List<ChatCompletionChunk>();

            public Task<ChatCompletionResponse> CompleteAsync(
                JArray messages, JArray tools, CancellationToken cancellationToken)
                => throw new NotImplementedException();

            public Task<IReadOnlyList<string>> GetModelIdsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<string>>(new List<string>());

            public async IAsyncEnumerable<ChatCompletionChunk> ChatStreamAsync(
                JArray messages,
                JArray tools,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (var chunk in Chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return chunk;
                    await Task.Yield();
                }
            }
        }

        /// <summary>
        /// Создаёт сервис с реальным ChatService на InMemory-БД.
        /// </summary>
        private static (ChatStreamService Service, ChatService ChatService, int UserId, int ChatId, FakeLmStudioClient LmClient) CreateService(string model = "test-model")
        {
            var db = TestDbContextFactory.Create();     // уже сидирует 3 пользователя (Id=1,2,3)
            var chatService = new ChatService(db, NullLogger<ChatService>.Instance);
            var fakeLm = new FakeLmStudioClient();
            var service = new ChatStreamService(
                chatService,
                fakeLm,
                NullLogger<ChatStreamService>.Instance);

            // Создаём чат от имени пользователя Id=1
            var chat = chatService.CreateChatAsync(1, model, "Test Chat").GetAwaiter().GetResult();

            return (service, chatService, 1, chat.Id, fakeLm);
        }

        [Fact]
        public async Task StreamAsync_ValidRequest_EmitsStartDeltaDone_AndSavesMessages()
        {
            // Arrange
            var (service, chatService, userId, chatId, fakeLm) = CreateService();

            fakeLm.Chunks.Add(new ChatCompletionChunk { DeltaContent = "Привет" });
            fakeLm.Chunks.Add(new ChatCompletionChunk { DeltaContent = ", мир" });
            fakeLm.Chunks.Add(new ChatCompletionChunk
            {
                IsDone = true,
                FinishReason = "stop",
                Usage = new ChatCompletionUsage { PromptTokens = 10, CompletionTokens = 5 }
            });

            var request = new ChatStreamRequest { ChatId = chatId, Message = "Тест" };

            // Act
            var events = new List<ChatStreamEvent>();
            await foreach (var evt in service.StreamAsync(request, userId))
            {
                events.Add(evt);
            }

            // Assert
            Assert.NotEmpty(events);
            Assert.Equal("start", events[0].Type);
            Assert.Contains(events, e => e.Type == "delta");
            Assert.Equal("done", events.Last().Type);

            // Проверяем, что оба сообщения сохранены
            var messages = await chatService.GetMessagesAsync(chatId, userId, 50);
            Assert.Equal(2, messages.Count);
            Assert.Equal("user", messages[0].Role);
            Assert.Equal("Тест", messages[0].Content);
            Assert.Equal("assistant", messages[1].Role);
            Assert.Equal("Привет, мир", messages[1].Content);
            Assert.Equal(10, messages[1].TokensIn);
            Assert.Equal(5, messages[1].TokensOut);
        }

        [Fact]
        public async Task StreamAsync_ChatNotFound_EmitsError()
        {
            // Arrange
            var (service, _, userId, _, _) = CreateService();
            var request = new ChatStreamRequest { ChatId = 99999, Message = "Тест" };

            // Act
            var events = new List<ChatStreamEvent>();
            await foreach (var evt in service.StreamAsync(request, userId))
            {
                events.Add(evt);
            }

            // Assert
            Assert.Single(events);
            Assert.Equal("error", events[0].Type);
        }

        [Fact]
        public async Task StreamAsync_EmptyMessage_EmitsError()
        {
            // Arrange
            var (service, _, userId, chatId, _) = CreateService();
            var request = new ChatStreamRequest { ChatId = chatId, Message = "" };

            // Act
            var events = new List<ChatStreamEvent>();
            await foreach (var evt in service.StreamAsync(request, userId))
            {
                events.Add(evt);
            }

            // Assert
            Assert.Single(events);
            Assert.Equal("error", events[0].Type);
        }
    }
}