using System;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Implementation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты сервиса подтверждений действий.
    /// </summary>
    public class ApprovalServiceTests
    {
        /// <summary>
        /// Проверяет создание запроса на подтверждение с правильным сроком действия.
        /// </summary>
        [Fact]
        public async Task CreatePendingActionAsync_SetsCorrectExpiration()
        {
            using var db = TestDbContextFactory.Create();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>(
                        "Security:ApprovalExpirationMinutes", "5")
                })
                .Build();

            var service = new ApprovalService(db, config, NullLogger<ApprovalService>.Instance);

            var action = await service.CreatePendingActionAsync(1, "test_tool", "{}");

            Assert.NotNull(action);
            Assert.Equal("Pending", action.Status);
            Assert.Equal("test_tool", action.ToolName);
            Assert.True(action.ExpiresAt > DateTime.UtcNow);
            Assert.True(action.ExpiresAt <= DateTime.UtcNow.AddMinutes(6));
        }

        /// <summary>
        /// Проверяет подтверждение действия.
        /// </summary>
        [Fact]
        public async Task ApproveAsync_UpdatesStatusAndApprover()
        {
            using var db = TestDbContextFactory.Create();
            var config = new ConfigurationBuilder().Build();
            var service = new ApprovalService(db, config, NullLogger<ApprovalService>.Instance);

            var action = await service.CreatePendingActionAsync(1, "test_tool", "{}");
            var ok = await service.ApproveAsync(action.Id, 2);

            Assert.True(ok);
            var updated = await service.GetByIdAsync(action.Id);
            Assert.Equal("Approved", updated.Status);
            Assert.Equal(2, updated.ApprovedByUserId);
            Assert.NotNull(updated.ApprovedAt);
        }

        /// <summary>
        /// Проверяет отклонение действия с причиной.
        /// </summary>
        [Fact]
        public async Task RejectAsync_SavesRejectionReason()
        {
            using var db = TestDbContextFactory.Create();
            var config = new ConfigurationBuilder().Build();
            var service = new ApprovalService(db, config, NullLogger<ApprovalService>.Instance);

            var action = await service.CreatePendingActionAsync(1, "test_tool", "{}");
            var ok = await service.RejectAsync(action.Id, 2, "Слишком опасно");

            Assert.True(ok);
            var updated = await service.GetByIdAsync(action.Id);
            Assert.Equal("Rejected", updated.Status);
            Assert.Equal("Слишком опасно", updated.RejectionReason);
        }

        /// <summary>
        /// Проверяет, что повторное подтверждение не проходит.
        /// </summary>
        [Fact]
        public async Task ApproveAsync_AlreadyApproved_ReturnsFalse()
        {
            using var db = TestDbContextFactory.Create();
            var config = new ConfigurationBuilder().Build();
            var service = new ApprovalService(db, config, NullLogger<ApprovalService>.Instance);

            var action = await service.CreatePendingActionAsync(1, "test_tool", "{}");
            await service.ApproveAsync(action.Id, 2);

            var second = await service.ApproveAsync(action.Id, 3);
            Assert.False(second);
        }
    }
}