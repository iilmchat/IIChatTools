using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Implementation.Tools.Mail;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Mail
{
    /// <summary>
    /// Unit-тесты 4 mail-tools Фазы 3B (v1.8.0, KI-107).
    /// <c>search_emails</c>, <c>delete_email</c>, <c>move_email</c>, <c>mark_as_read</c>.
    /// </summary>
    public class MailTools3BTests
    {
        // ============================================================
        // Fake IMailClient (свой — не мешает MailToolsTests)
        // ============================================================

        private sealed class FakeMailClient : IMailClient
        {
            public int SearchCallCount { get; private set; }
            public SearchMailRequest LastSearchRequest { get; private set; }
            public int DeleteCallCount { get; private set; }
            public (uint Uid, string Mailbox)? LastDelete { get; private set; }
            public int MoveCallCount { get; private set; }
            public (uint Uid, string From, string To)? LastMove { get; private set; }
            public int MarkCallCount { get; private set; }
            public (uint Uid, string Mailbox)? LastMark { get; private set; }

            public Exception NextException { get; set; }

            public Task<System.Collections.Generic.IReadOnlyList<MailMessageSummaryDto>> SearchAsync(
                SearchMailRequest request, int userId, CancellationToken ct = default)
            {
                SearchCallCount++;
                LastSearchRequest = request;
                if (NextException != null) throw NextException;
                return Task.FromResult<System.Collections.Generic.IReadOnlyList<MailMessageSummaryDto>>(
                    System.Array.Empty<MailMessageSummaryDto>());
            }

            public Task DeleteAsync(uint uid, string mailbox, int userId, CancellationToken ct = default)
            {
                DeleteCallCount++;
                LastDelete = (uid, mailbox);
                if (NextException != null) throw NextException;
                return Task.CompletedTask;
            }

            public Task MoveAsync(uint uid, string fromMailbox, string toMailbox, int userId, CancellationToken ct = default)
            {
                MoveCallCount++;
                LastMove = (uid, fromMailbox, toMailbox);
                if (NextException != null) throw NextException;
                return Task.CompletedTask;
            }

            public Task MarkAsReadAsync(uint uid, string mailbox, int userId, CancellationToken ct = default)
            {
                MarkCallCount++;
                LastMark = (uid, mailbox);
                if (NextException != null) throw NextException;
                return Task.CompletedTask;
            }

            // Не используются в 3B — заглушки.
            public Task<System.Collections.Generic.IReadOnlyList<MailMessageSummaryDto>> ListAsync(
                string mailbox, int count, bool unseenOnly, int userId, CancellationToken ct = default)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<MailMessageSummaryDto>>(
                    System.Array.Empty<MailMessageSummaryDto>());

            public Task<MailMessageDto> ReadAsync(uint uid, string mailbox, bool saveAttachments, int userId, CancellationToken ct = default)
                => Task.FromResult<MailMessageDto>(null);

            public Task SendAsync(SendMailRequest request, int userId, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task<bool> TestConnectionAsync(int userId, CancellationToken ct = default)
                => Task.FromResult(true);
        }

        private static ToolExecutionContext Ctx(int userId = 1)
            => new ToolExecutionContext { UserId = userId, CancellationToken = default };

        // ============================================================
        // SearchEmailsTool
        // ============================================================

        [Fact]
        public async Task SearchEmailsTool_NoFilters_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new SearchEmailsTool(fake, NullLogger<SearchEmailsTool>.Instance);

            var result = await tool.ExecuteAsync(Ctx(1), new JObject());

            Assert.False(result.Success);
            Assert.Contains("Не задан ни один фильтр", result.Message);
            Assert.Equal(0, fake.SearchCallCount);
        }

        [Fact]
        public async Task SearchEmailsTool_FromFilter_CallsClient()
        {
            var fake = new FakeMailClient();
            var tool = new SearchEmailsTool(fake, NullLogger<SearchEmailsTool>.Instance);

            var args = new JObject { ["from"] = "ivanov" };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.True(result.Success);
            Assert.Equal(1, fake.SearchCallCount);
            Assert.Equal("ivanov", fake.LastSearchRequest.From);
            Assert.Equal("INBOX", fake.LastSearchRequest.Mailbox);
        }

        [Fact]
        public async Task SearchEmailsTool_SinceFilter_ParsesDate()
        {
            var fake = new FakeMailClient();
            var tool = new SearchEmailsTool(fake, NullLogger<SearchEmailsTool>.Instance);

            var args = new JObject { ["since"] = "2026-09-01" };
            await tool.ExecuteAsync(Ctx(1), args);

            Assert.NotNull(fake.LastSearchRequest.Since);
            Assert.Equal(2026, fake.LastSearchRequest.Since.Value.Year);
            Assert.Equal(9, fake.LastSearchRequest.Since.Value.Month);
            Assert.Equal(1, fake.LastSearchRequest.Since.Value.Day);
        }

        [Fact]
        public async Task SearchEmailsTool_InvalidSinceFormat_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new SearchEmailsTool(fake, NullLogger<SearchEmailsTool>.Instance);

            var args = new JObject { ["since"] = "01/09/2026" };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("since", result.Message);
        }

        [Fact]
        public async Task SearchEmailsTool_LimitClampedTo100()
        {
            var fake = new FakeMailClient();
            var tool = new SearchEmailsTool(fake, NullLogger<SearchEmailsTool>.Instance);

            var args = new JObject { ["from"] = "x", ["limit"] = 9999 };
            await tool.ExecuteAsync(Ctx(1), args);

            Assert.Equal(100, fake.LastSearchRequest.Limit);
        }

        // ============================================================
        // DeleteEmailTool
        // ============================================================

        [Fact]
        public void DeleteEmailTool_RequiresApproval()
        {
            var fake = new FakeMailClient();
            ITool tool = new DeleteEmailTool(fake, NullLogger<DeleteEmailTool>.Instance);

            Assert.True(tool.RequiresApprovalByDefault);
            Assert.True(tool.RequiresApprovalForCall(new JObject()));
        }

        [Fact]
        public async Task DeleteEmailTool_NoUid_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new DeleteEmailTool(fake, NullLogger<DeleteEmailTool>.Instance);

            var result = await tool.ExecuteAsync(Ctx(1), new JObject());

            Assert.False(result.Success);
            Assert.Equal(0, fake.DeleteCallCount);
        }

        [Fact]
        public async Task DeleteEmailTool_ValidUid_CallsClient()
        {
            var fake = new FakeMailClient();
            var tool = new DeleteEmailTool(fake, NullLogger<DeleteEmailTool>.Instance);

            var args = new JObject { ["uid"] = 42, ["mailbox"] = "INBOX" };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.True(result.Success);
            Assert.Equal(1, fake.DeleteCallCount);
            Assert.Equal((uint)42, fake.LastDelete.Value.Uid);
            Assert.Equal("INBOX", fake.LastDelete.Value.Mailbox);
        }

        // ============================================================
        // MoveEmailTool
        // ============================================================

        [Fact]
        public void MoveEmailTool_RequiresApproval()
        {
            var fake = new FakeMailClient();
            ITool tool = new MoveEmailTool(fake, NullLogger<MoveEmailTool>.Instance);

            Assert.True(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public async Task MoveEmailTool_NoTo_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new MoveEmailTool(fake, NullLogger<MoveEmailTool>.Instance);

            var args = new JObject { ["uid"] = 1 };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("'to'", result.Message);
        }

        [Fact]
        public async Task MoveEmailTool_SameFromAndTo_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new MoveEmailTool(fake, NullLogger<MoveEmailTool>.Instance);

            var args = new JObject { ["uid"] = 1, ["from"] = "INBOX", ["to"] = "INBOX" };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("совпадают", result.Message);
        }

        [Fact]
        public async Task MoveEmailTool_Valid_CallsClient()
        {
            var fake = new FakeMailClient();
            var tool = new MoveEmailTool(fake, NullLogger<MoveEmailTool>.Instance);

            var args = new JObject { ["uid"] = 10, ["from"] = "INBOX", ["to"] = "Archive" };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.True(result.Success);
            Assert.Equal(1, fake.MoveCallCount);
            Assert.Equal((uint)10, fake.LastMove.Value.Uid);
            Assert.Equal("INBOX", fake.LastMove.Value.From);
            Assert.Equal("Archive", fake.LastMove.Value.To);
        }

        // ============================================================
        // MarkAsReadTool
        // ============================================================

        [Fact]
        public void MarkAsReadTool_DoesNotRequireApproval()
        {
            var fake = new FakeMailClient();
            ITool tool = new MarkAsReadTool(fake, NullLogger<MarkAsReadTool>.Instance);

            Assert.False(tool.RequiresApprovalByDefault);
            Assert.False(tool.RequiresApprovalForCall(new JObject()));
        }

        [Fact]
        public async Task MarkAsReadTool_NoUid_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new MarkAsReadTool(fake, NullLogger<MarkAsReadTool>.Instance);

            var result = await tool.ExecuteAsync(Ctx(1), new JObject());

            Assert.False(result.Success);
            Assert.Equal(0, fake.MarkCallCount);
        }

        [Fact]
        public async Task MarkAsReadTool_ValidUid_CallsClient()
        {
            var fake = new FakeMailClient();
            var tool = new MarkAsReadTool(fake, NullLogger<MarkAsReadTool>.Instance);

            var args = new JObject { ["uid"] = 77 };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.True(result.Success);
            Assert.Equal(1, fake.MarkCallCount);
            Assert.Equal((uint)77, fake.LastMark.Value.Uid);
            Assert.Equal("INBOX", fake.LastMark.Value.Mailbox);
        }
    }
}