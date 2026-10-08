using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using IIChatTools.API.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.RateLimiting
{
    /// <summary>
    /// Unit-тесты <see cref="RateLimitingMiddleware"/>.
    /// v1.13.6 (KI-212): проверка новой политики <c>pages-viewer</c>
    /// (высокочастотные запросы page-viewer'а не упираются в per-user).
    /// </summary>
    public class RateLimitingMiddlewareTests
    {
        /// <summary>
        /// Создаёт middleware с in-memory конфигом.
        /// </summary>
        /// <param name="perUserPermit">Лимит per-user (default: 100).</param>
        /// <param name="pagesViewerPermit">Лимит pages-viewer (default: 300).</param>
        private static RateLimitingMiddleware CreateMiddleware(
            int perUserPermit = 100,
            int pagesViewerPermit = 300)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["RateLimiting:Enabled"]              = "true",
                    ["RateLimiting:Global:PermitLimit"]   = "300",
                    ["RateLimiting:Global:WindowSeconds"] = "60",
                    ["RateLimiting:Global:QueueLimit"]    = "0",
                    ["RateLimiting:PerUser:PermitLimit"]  = perUserPermit.ToString(),
                    ["RateLimiting:PerUser:WindowSeconds"]= "60",
                    ["RateLimiting:PerUser:QueueLimit"]   = "5",
                    ["RateLimiting:ToolsExecute:PermitLimit"]   = "30",
                    ["RateLimiting:ToolsExecute:WindowSeconds"] = "60",
                    ["RateLimiting:ToolsExecute:QueueLimit"]    = "0",
                    ["RateLimiting:Auth:PermitLimit"]     = "5",
                    ["RateLimiting:Auth:WindowSeconds"]   = "60",
                    ["RateLimiting:Auth:QueueLimit"]      = "0",
                    ["RateLimiting:PagesViewer:PermitLimit"]    = pagesViewerPermit.ToString(),
                    ["RateLimiting:PagesViewer:WindowSeconds"]  = "60",
                    ["RateLimiting:PagesViewer:QueueLimit"]     = "0",
                })
                .Build();

            RequestDelegate next = _ => Task.CompletedTask;
            return new RateLimitingMiddleware(next, config, NullLogger<RateLimitingMiddleware>.Instance);
        }

        /// <summary>
        /// Создаёт HttpContext с заданными path, method и userId.
        /// </summary>
        private static HttpContext CreateHttpContext(string path, string userId, string method = "GET")
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Path = path;
            ctx.Request.Method = method;
            ctx.Request.Headers["Accept"] = "application/json";

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
            ctx.Response.Body = new MemoryStream();
            return ctx;
        }

        [Fact]
        public async Task PagesViewer_Burst120_NotThrottled()
        {
            // Arrange
            var mw = CreateMiddleware();
            const string path = "/api/chat/1/attachments/1/pages/5.png";
            int nonOkCount = 0;

            // Act: 120 запросов от одного пользователя (лимит 300 — с запасом)
            for (int i = 0; i < 120; i++)
            {
                var ctx = CreateHttpContext(path, "42");
                await mw.InvokeAsync(ctx);
                if (ctx.Response.StatusCode != StatusCodes.Status200OK)
                    nonOkCount++;
            }

            // Assert
            Assert.Equal(0, nonOkCount);
        }

        [Fact]
        public async Task PagesViewer_AboveLimit_Throttled()
        {
            // Arrange
            var mw = CreateMiddleware(pagesViewerPermit: 300);
            const string path = "/api/chat/1/attachments/1/pages/5.png";
            int throttled = 0;

            // Act: 301 запрос (лимит 300) от одного пользователя
            for (int i = 0; i < 301; i++)
            {
                var ctx = CreateHttpContext(path, "43");
                await mw.InvokeAsync(ctx);
                if (ctx.Response.StatusCode == StatusCodes.Status429TooManyRequests)
                    throttled++;
            }

            // Assert: ровно 1 отказ (301-й)
            Assert.Equal(1, throttled);
        }

        [Fact]
        public async Task PagesViewer_DoesNotAffectPerUser_OtherEndpoints()
        {
            // Arrange
            var mw = CreateMiddleware(perUserPermit: 100);
            const string otherPath = "/api/chats";
            int throttled = 0;

            // Act: 120 запросов к другому endpoint (лимит per-user 100)
            for (int i = 0; i < 120; i++)
            {
                var ctx = CreateHttpContext(otherPath, "44");
                await mw.InvokeAsync(ctx);
                if (ctx.Response.StatusCode == StatusCodes.Status429TooManyRequests)
                    throttled++;
            }

            // Assert: 20 отказов (101..120). PagesViewer не влияет на per-user.
            Assert.Equal(20, throttled);
        }

        [Fact]
        public async Task PagesViewer_AndPerUser_AreIsolated()
        {
            // Arrange: PagesViewer лимит 5, PerUser лимит 100
            var mw = CreateMiddleware(perUserPermit: 100, pagesViewerPermit: 5);
            const string pagesPath = "/api/chat/1/attachments/1/pages/5.png";
            const string otherPath = "/api/chats";

            // Act 1: 5 запросов к page-viewer (в лимите)
            for (int i = 0; i < 5; i++)
            {
                var ctx = CreateHttpContext(pagesPath, "45");
                await mw.InvokeAsync(ctx);
                Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
            }

            // Act 2: 6-й запрос — throttled
            var ctx6 = CreateHttpContext(pagesPath, "45");
            await mw.InvokeAsync(ctx6);
            Assert.Equal(StatusCodes.Status429TooManyRequests, ctx6.Response.StatusCode);

            // Act 3: запрос к другому endpoint — не задет (per-user отдельно)
            var ctxOther = CreateHttpContext(otherPath, "45");
            await mw.InvokeAsync(ctxOther);
            Assert.Equal(StatusCodes.Status200OK, ctxOther.Response.StatusCode);
        }
    }
}