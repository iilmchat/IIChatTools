using System;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Debate;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.Debate;
using IIChatTools.Tests.IntegrationTests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Debate
{
    /// <summary>
    /// Unit-тесты <see cref="AgentDebateSessionService"/>
    /// (v1.11.0, KI-126, Шаг 1B).
    ///
    /// <para>
    /// Используется <see cref="TestDbContextFactory"/> (InMemory) +
    /// реальный <see cref="ChatService"/> (создаёт тестовый чат).
    /// Фоновый цикл actor-critic — заглушка в 1B, поэтому тесты
    /// проверяют только операции над сессией (create / get / cancel).
    /// </para>
    /// </summary>
    public class AgentDebateSessionServiceTests
    {
        private const int TestUserId = 1;
        private const string TestModel = "qwen/qwen3-4b-2507";

        /// <summary>
        /// Создаёт сервис + готовый тестовый чат для пользователя
        /// <see cref="TestUserId"/>.
        /// </summary>
        private static (AgentDebateSessionService Service, AppDbContext Db, Chat Chat)
            CreateServiceWithChat()
        {
            var db = TestDbContextFactory.Create();
            var chatService = new ChatService(db, NullLogger<ChatService>.Instance);

            // Создаём тестовый чат (seed-пользователь Id=1 уже есть в TestDbContextFactory).
            var chat = chatService.CreateChatAsync(TestUserId, TestModel, "Тест")
                .GetAwaiter().GetResult();

            var coordinator = new AgentDebateCoordinator(
                NullLogger<AgentDebateCoordinator>.Instance);

            var service = new AgentDebateSessionService(
                db,
                chatService,
                coordinator,
                NullLogger<AgentDebateSessionService>.Instance);

            return (service, db, chat);
        }

        [Fact]
        public async Task StartAsync_CreatesSession_WithPendingStatus()
        {
            var (service, db, chat) = CreateServiceWithChat();

            var sessionId = await service.StartAsync(
                chat.Id, TestUserId, "Напиши функцию-палиндром", null);

            Assert.True(sessionId > 0);

            var session = await db.AgentDebateSessions.FindAsync(sessionId);
            Assert.NotNull(session);
            Assert.Equal("Pending", session.Status);
            Assert.Equal("ActorCritic", session.PatternType);
            Assert.Equal(chat.Id, session.ChatId);
            Assert.Equal(TestUserId, session.InitiatedByUserId);
            Assert.Equal("Напиши функцию-палиндром", session.Task);
            Assert.NotNull(session.ConfigSnapshotJson);
            Assert.Null(session.CompletedAt);
            Assert.Equal(0m, session.TotalCostUsd);
        }

        [Fact]
        public async Task StartAsync_WhenThreeActive_Throws()
        {
            var (service, _, chat) = CreateServiceWithChat();

            // 3 активные сессии — лимит.
            await service.StartAsync(chat.Id, TestUserId, "task 1", null);
            await service.StartAsync(chat.Id, TestUserId, "task 2", null);
            await service.StartAsync(chat.Id, TestUserId, "task 3", null);

            // 4-я — должна упасть с InvalidOperationException.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StartAsync(chat.Id, TestUserId, "task 4", null));

            Assert.Contains("лимит", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task StartAsync_EmptyTask_Throws()
        {
            var (service, _, chat) = CreateServiceWithChat();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.StartAsync(chat.Id, TestUserId, "   ", null));
        }

        [Fact]
        public async Task GetStatusAsync_ReturnsDto_WithRounds()
        {
            var (service, db, chat) = CreateServiceWithChat();

            var sessionId = await service.StartAsync(chat.Id, TestUserId, "task", null);

            // Кладём раунд напрямую (цикл в 1B ещё заглушка).
            db.AgentDebateRounds.Add(new AgentDebateRound
            {
                SessionId = sessionId,
                RoundNumber = 1,
                ActorOutput = "def foo(): ...",
                CriticVerdict = "Rejected",
                CriticFeedbackJson = "[{\"severity\":\"Major\"}]",
                ActorModel = TestModel,
                CriticModel = TestModel,
                WasEscalated = false,
                TokensIn = 100,
                TokensOut = 50,
                CostUsd = 0.0001m,
                DurationMs = 1234
            });
            await db.SaveChangesAsync();

            var status = await service.GetStatusAsync(sessionId, TestUserId);

            Assert.NotNull(status);
            Assert.Equal(sessionId, status.SessionId);
            Assert.Equal(chat.Id, status.ChatId);
            Assert.Equal("Pending", status.Status);
            Assert.Equal(1, status.TotalRounds);
            Assert.Single(status.Rounds);
            Assert.Equal("Rejected", status.Rounds[0].CriticVerdict);
            Assert.Equal(100, status.Rounds[0].TokensIn);
        }

        [Fact]
        public async Task GetStatusAsync_WrongUser_ReturnsNull()
        {
            var (service, _, chat) = CreateServiceWithChat();

            var sessionId = await service.StartAsync(chat.Id, TestUserId, "task", null);

            // Чужой пользователь (Id=999) — не должен видеть сессию.
            var status = await service.GetStatusAsync(sessionId, userId: 999);

            Assert.Null(status);
        }

        [Fact]
        public async Task CancelAsync_SetsStatusCancelled()
        {
            var (service, db, chat) = CreateServiceWithChat();

            var sessionId = await service.StartAsync(chat.Id, TestUserId, "task", null);

            var ok = await service.CancelAsync(sessionId, TestUserId);

            Assert.True(ok);

            var session = await db.AgentDebateSessions.FindAsync(sessionId);
            Assert.Equal("Cancelled", session.Status);
            Assert.NotNull(session.CompletedAt);
            Assert.NotNull(session.UpdatedAt);
        }

        [Fact]
        public async Task CancelAsync_AlreadyCompleted_ReturnsFalse()
        {
            var (service, db, chat) = CreateServiceWithChat();

            var sessionId = await service.StartAsync(chat.Id, TestUserId, "task", null);

            // Симулируем завершение сессии.
            var session = await db.AgentDebateSessions.FindAsync(sessionId);
            session.Status = "Completed";
            await db.SaveChangesAsync();

            var ok = await service.CancelAsync(sessionId, TestUserId);

            Assert.False(ok);

            var after = await db.AgentDebateSessions.FindAsync(sessionId);
            Assert.Equal("Completed", after.Status);   // не перезаписан на Cancelled
        }

        [Fact]
        public async Task CancelAsync_WrongUser_ReturnsFalse()
        {
            var (service, _, chat) = CreateServiceWithChat();

            var sessionId = await service.StartAsync(chat.Id, TestUserId, "task", null);

            var ok = await service.CancelAsync(sessionId, userId: 999);

            Assert.False(ok);
        }

        [Fact]
        public async Task InjectFeedbackAsync_NoWaitingCoordinator_ReturnsFalse()
        {
            var (service, _, chat) = CreateServiceWithChat();

            var sessionId = await service.StartAsync(chat.Id, TestUserId, "task", null);

            // Шаг 1E: InjectFeedbackAsync — реальный. Но фоновый цикл не запущен,
            // ожидающего в coordinator нет → возвращает false.
            var ok = await service.InjectFeedbackAsync(sessionId, TestUserId, "Учти edge case");

            Assert.False(ok);
        }
    }
}
