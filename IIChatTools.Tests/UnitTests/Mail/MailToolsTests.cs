using System;
using System.Collections.Generic;
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
    /// Unit-тесты 3 mail-tools (Фаза 3A, v1.8.0, KI-107).
    /// Используют Fake <see cref="IMailClient"/> — без реального IMAP/SMTP.
    /// </summary>
    public class MailToolsTests
    {
        // ============================================================
        // Fake IMailClient
        // ============================================================

        private sealed class FakeMailClient : IMailClient
        {
            public List<(string Mailbox, int Count, bool UnseenOnly, int UserId)> ListCalls { get; } = new();
            public List<(uint Uid, string Mailbox, bool SaveAttachments, int UserId)> ReadCalls { get; } = new();
            public List<(SendMailRequest Req, int UserId)> SendCalls { get; } = new();

            public IReadOnlyList<MailMessageSummaryDto> NextListResult { get; set; }
                = Array.Empty<MailMessageSummaryDto>();
            public MailMessageDto NextReadResult { get; set; }
            public Exception NextException { get; set; }

            public Task<IReadOnlyList<MailMessageSummaryDto>> ListAsync(
                string mailbox, int count, bool unseenOnly, int userId, CancellationToken ct = default)
            {
                ListCalls.Add((mailbox, count, unseenOnly, userId));
                if (NextException != null) throw NextException;
                return Task.FromResult(NextListResult);
            }

            public Task<MailMessageDto> ReadAsync(
                uint uid, string mailbox, bool saveAttachments, int userId, CancellationToken ct = default)
            {
                ReadCalls.Add((uid, mailbox, saveAttachments, userId));
                if (NextException != null) throw NextException;
                return Task.FromResult(NextReadResult);
            }

            public Task<IReadOnlyList<MailMessageSummaryDto>> SearchAsync(
                SearchMailRequest request, int userId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<MailMessageSummaryDto>>(Array.Empty<MailMessageSummaryDto>());

            public Task SendAsync(SendMailRequest request, int userId, CancellationToken ct = default)
            {
                SendCalls.Add((request, userId));
                if (NextException != null) throw NextException;
                return Task.CompletedTask;
            }

            public Task DeleteAsync(uint uid, string mailbox, int userId, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task MoveAsync(uint uid, string fromMailbox, string toMailbox, int userId, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task MarkAsReadAsync(uint uid, string mailbox, int userId, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task<bool> TestConnectionAsync(int userId, CancellationToken ct = default)
                => Task.FromResult(true);
        }

        private static ToolExecutionContext Ctx(int userId = 1)
            => new ToolExecutionContext { UserId = userId, CancellationToken = default };

        // ============================================================
        // ListEmailsTool
        // ============================================================

        [Fact]
        public async Task ListEmailsTool_NoUserId_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new ListEmailsTool(fake, NullLogger<ListEmailsTool>.Instance);

            var result = await tool.ExecuteAsync(Ctx(userId: 0), new JObject());

            Assert.False(result.Success);
            Assert.Contains("UserId", result.Message);
        }

        [Fact]
        public async Task ListEmailsTool_DefaultParams_UsesInbox20()
        {
            var fake = new FakeMailClient();
            var tool = new ListEmailsTool(fake, NullLogger<ListEmailsTool>.Instance);

            await tool.ExecuteAsync(Ctx(1), new JObject());

            Assert.Single(fake.ListCalls);
            Assert.Equal("INBOX", fake.ListCalls[0].Mailbox);
            Assert.Equal(20, fake.ListCalls[0].Count);
            Assert.False(fake.ListCalls[0].UnseenOnly);
        }

        [Fact]
        public async Task ListEmailsTool_CustomParams_AreRespected()
        {
            var fake = new FakeMailClient();
            var tool = new ListEmailsTool(fake, NullLogger<ListEmailsTool>.Instance);

            var args = new JObject
            {
                ["mailbox"] = "Sent",
                ["count"] = 5,
                ["unseenOnly"] = true
            };

            await tool.ExecuteAsync(Ctx(1), args);

            Assert.Single(fake.ListCalls);
            Assert.Equal("Sent", fake.ListCalls[0].Mailbox);
            Assert.Equal(5, fake.ListCalls[0].Count);
            Assert.True(fake.ListCalls[0].UnseenOnly);
        }

        [Fact]
        public async Task ListEmailsTool_CountClampedTo100()
        {
            var fake = new FakeMailClient();
            var tool = new ListEmailsTool(fake, NullLogger<ListEmailsTool>.Instance);

            var args = new JObject { ["count"] = 99999 };
            await tool.ExecuteAsync(Ctx(1), args);

            Assert.Equal(100, fake.ListCalls[0].Count);
        }

        [Fact]
        public async Task ListEmailsTool_ReturnsData_WithMessages()
        {
            var fake = new FakeMailClient
            {
                NextListResult = new[]
                {
                    new MailMessageSummaryDto
                    {
                        Uid = 123,
                        Mailbox = "INBOX",
                        From = "x@example.com",
                        Subject = "Test",
                        Date = DateTime.UtcNow,
                        IsRead = false,
                        HasAttachments = false
                    }
                }
            };
            var tool = new ListEmailsTool(fake, NullLogger<ListEmailsTool>.Instance);

            var result = await tool.ExecuteAsync(Ctx(1), new JObject());

            Assert.True(result.Success);
            Assert.Contains("Писем получено: 1", result.Message);
        }

        [Fact]
        public async Task ListEmailsTool_EmptyResult_ReturnsZeroMessage()
        {
            var fake = new FakeMailClient();
            var tool = new ListEmailsTool(fake, NullLogger<ListEmailsTool>.Instance);

            var result = await tool.ExecuteAsync(Ctx(1), new JObject());

            Assert.True(result.Success);
            Assert.Contains("Писем не найдено", result.Message);
        }

        // ============================================================
        // ReadEmailTool
        // ============================================================

        [Fact]
        public async Task ReadEmailTool_NoUid_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new ReadEmailTool(fake, NullLogger<ReadEmailTool>.Instance);

            var result = await tool.ExecuteAsync(Ctx(1), new JObject());

            Assert.False(result.Success);
            Assert.Contains("uid", result.Message);
        }

        [Fact]
        public async Task ReadEmailTool_ValidUid_CallsClient()
        {
            var fake = new FakeMailClient
            {
                NextReadResult = new MailMessageDto
                {
                    Uid = 42,
                    Mailbox = "INBOX",
                    From = "x@example.com",
                    Subject = "Test",
                    Date = DateTime.UtcNow,
                    BodyText = "Hello"
                }
            };
            var tool = new ReadEmailTool(fake, NullLogger<ReadEmailTool>.Instance);

            var args = new JObject { ["uid"] = 42, ["mailbox"] = "INBOX" };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.True(result.Success);
            Assert.Single(fake.ReadCalls);
            Assert.Equal((uint)42, fake.ReadCalls[0].Uid);
            Assert.Equal("INBOX", fake.ReadCalls[0].Mailbox);
            Assert.True(fake.ReadCalls[0].SaveAttachments);
        }

        [Fact]
        public async Task ReadEmailTool_NullResult_Fails()
        {
            var fake = new FakeMailClient { NextReadResult = null };
            var tool = new ReadEmailTool(fake, NullLogger<ReadEmailTool>.Instance);

            var args = new JObject { ["uid"] = 99 };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("99", result.Message);
        }

        // ============================================================
        // SendEmailTool
        // ============================================================

        [Fact]
        public void SendEmailTool_RequiresApproval()
        {
            var fake = new FakeMailClient();

            // RULES § 4.46: default interface method (ITool.RequiresApprovalForCall)
            // не виден через конкретный тип — только через интерфейсную переменную.
            ITool tool = new SendEmailTool(fake, NullLogger<SendEmailTool>.Instance);

            Assert.True(tool.RequiresApprovalByDefault);
            Assert.True(tool.RequiresApprovalForCall(new JObject()));
        }

        [Fact]
        public async Task SendEmailTool_NoTo_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new SendEmailTool(fake, NullLogger<SendEmailTool>.Instance);

            var args = new JObject
            {
                ["subject"] = "Test",
                ["body"] = "Hello"
            };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("'to'", result.Message);
        }

        [Fact]
        public async Task SendEmailTool_NoBody_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new SendEmailTool(fake, NullLogger<SendEmailTool>.Instance);

            var args = new JObject
            {
                ["to"] = new JArray("x@example.com"),
                ["subject"] = "Test"
            };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("body", result.Message);
        }

        [Fact]
        public async Task SendEmailTool_InvalidEmail_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new SendEmailTool(fake, NullLogger<SendEmailTool>.Instance);

            var args = new JObject
            {
                ["to"] = new JArray("not-an-email"),
                ["subject"] = "Test",
                ["body"] = "Hello"
            };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("Некорректный адрес", result.Message);
        }

        [Fact]
        public async Task SendEmailTool_TooManyRecipients_Fails()
        {
            var fake = new FakeMailClient();
            var tool = new SendEmailTool(fake, NullLogger<SendEmailTool>.Instance);

            var to = new JArray();
            for (int i = 0; i < 11; i++) to.Add($"user{i}@example.com");

            var args = new JObject
            {
                ["to"] = to,
                ["subject"] = "Test",
                ["body"] = "Hello"
            };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("Слишком много получателей", result.Message);
        }

        [Fact]
        public async Task SendEmailTool_ValidRequest_CallsClient()
        {
            var fake = new FakeMailClient();
            var tool = new SendEmailTool(fake, NullLogger<SendEmailTool>.Instance);

            var args = new JObject
            {
                ["to"] = new JArray("x@example.com"),
                ["subject"] = "Test Subject",
                ["body"] = "Hello, world!"
            };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.True(result.Success);
            Assert.Single(fake.SendCalls);
            Assert.Single(fake.SendCalls[0].Req.To);
            Assert.Equal("x@example.com", fake.SendCalls[0].Req.To[0]);
            Assert.Equal("Test Subject", fake.SendCalls[0].Req.Subject);
            Assert.Equal("Hello, world!", fake.SendCalls[0].Req.Body);
            Assert.False(fake.SendCalls[0].Req.IsHtml);
        }

        [Fact]
        public async Task SendEmailTool_ExceptionFromClient_ReturnsFail()
        {
            var fake = new FakeMailClient { NextException = new InvalidOperationException("SMTP unreachable") };
            var tool = new SendEmailTool(fake, NullLogger<SendEmailTool>.Instance);

            var args = new JObject
            {
                ["to"] = new JArray("x@example.com"),
                ["subject"] = "Test",
                ["body"] = "Hello"
            };
            var result = await tool.ExecuteAsync(Ctx(1), args);

            Assert.False(result.Success);
            Assert.Contains("SMTP unreachable", result.Message);
        }
    }
}