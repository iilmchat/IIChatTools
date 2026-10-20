using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Controllers;
using IIChatTools.API.Resources;
using IIChatTools.Services.DTO.Orchestrator;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Orchestrator
{
    /// <summary>
    /// Тесты <see cref="AdminOrchestratorController"/>
    /// (v2.2.2, KI-245).
    /// <para>
    /// Прямой вызов методов контроллера (без <c>WebApplicationFactory</c>) —
    /// консистентно с <c>AdminSqlAgentControllerTests</c>. Fake
    /// <see cref="IAdminOrchestratorService"/> записывает вызовы
    /// и возвращает настраиваемые результаты.
    /// </para>
    /// </summary>
    public class AdminOrchestratorControllerTests
    {
        // ============ GET /settings ============

        [Fact]
        public async Task GetSettingsAsync_ReturnsSettings()
        {
            var fake = new FakeAdminOrchestratorService
            {
                GetResult = BuildSettings(maxSubtasks: 7)
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.GetSettingsAsync(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True(GetAnonymousBool(ok.Value, "success"));
            Assert.NotNull(GetAnonymousProperty(ok.Value, "data"));
            Assert.True(fake.GetCalled);
        }

        [Fact]
        public async Task GetSettingsAsync_ServiceThrows_ReturnsFail()
        {
            var fake = new FakeAdminOrchestratorService
            {
                ThrowOnGet = new InvalidOperationException("boom")
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.GetSettingsAsync(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.False(GetAnonymousBool(ok.Value, "success"));
        }

        // ============ PUT /settings ============

        [Fact]
        public async Task UpdateSettingsAsync_ValidRequest_PassesUserIdAndReturnsData()
        {
            var fake = new FakeAdminOrchestratorService
            {
                UpdateResult = BuildSettings(maxSubtasks: 9, isOverridden: true)
            };
            var controller = BuildController(fake, userId: 42);

            var request = BuildSettings(maxSubtasks: 9);
            var result = await controller.UpdateSettingsAsync(
                request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True(GetAnonymousBool(ok.Value, "success"));
            Assert.True(fake.UpdateCalled);
            Assert.Equal(42, fake.LastUserId);
            Assert.Equal(9, fake.LastRequest.MaxSubtasks);
        }

        [Fact]
        public async Task UpdateSettingsAsync_NullRequest_ReturnsFail()
        {
            var fake = new FakeAdminOrchestratorService();
            var controller = BuildController(fake, userId: 1);

            var result = await controller.UpdateSettingsAsync(
                null, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.False(GetAnonymousBool(ok.Value, "success"));
            Assert.False(fake.UpdateCalled);
        }

        [Fact]
        public async Task UpdateSettingsAsync_ServiceThrowsArgumentException_ReturnsFailWithMessage()
        {
            var fake = new FakeAdminOrchestratorService
            {
                ThrowOnUpdate = new ArgumentException(
                    "MaxSubtasks должен быть в диапазоне 1–10.")
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.UpdateSettingsAsync(
                BuildSettings(maxSubtasks: 0), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.False(GetAnonymousBool(ok.Value, "success"));
            var message = (string)GetAnonymousProperty(ok.Value, "message");
            Assert.Contains("1–10", message);
        }

        [Fact]
        public async Task UpdateSettingsAsync_ServiceThrows_ReturnsFail()
        {
            var fake = new FakeAdminOrchestratorService
            {
                ThrowOnUpdate = new InvalidOperationException("boom")
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.UpdateSettingsAsync(
                BuildSettings(maxSubtasks: 5), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.False(GetAnonymousBool(ok.Value, "success"));
        }

        // ============ POST /reset ============

        [Fact]
        public async Task ResetAsync_ValidRequest_PassesUserIdAndReturnsData()
        {
            var fake = new FakeAdminOrchestratorService
            {
                ResetResult = BuildSettings(maxSubtasks: 5, isOverridden: false)
            };
            var controller = BuildController(fake, userId: 7);

            var result = await controller.ResetAsync(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True(GetAnonymousBool(ok.Value, "success"));
            Assert.True(fake.ResetCalled);
            Assert.Equal(7, fake.LastUserId);
        }

        [Fact]
        public async Task ResetAsync_ServiceThrows_ReturnsFail()
        {
            var fake = new FakeAdminOrchestratorService
            {
                ThrowOnReset = new InvalidOperationException("boom")
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.ResetAsync(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.False(GetAnonymousBool(ok.Value, "success"));
        }

        // ============ Test infrastructure ============

        private static OrchestratorSettingsDto BuildSettings(
            int maxSubtasks = 5, bool isOverridden = false)
        {
            return new OrchestratorSettingsDto
            {
                MaxSubtasks = maxSubtasks,
                MaxParallel = 3,
                TokenBudget = 50_000,
                HumanApproval = "Never",
                AllowedWorkerAgents = new List<string> { "web_agent", "code_agent" },
                IsOverridden = isOverridden
            };
        }

        private static AdminOrchestratorController BuildController(
            IAdminOrchestratorService service, int userId)
        {
            var controller = new AdminOrchestratorController(
                service,
                NullLogger<AdminOrchestratorController>.Instance,
                new FakeStringLocalizer());

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            return controller;
        }

        private static object GetAnonymousProperty(object value, string propertyName)
            => value.GetType().GetProperty(propertyName)?.GetValue(value);

        private static bool GetAnonymousBool(object value, string propertyName)
            => (bool)GetAnonymousProperty(value, propertyName);

        /// <summary>
        /// Простейшая реализация <see cref="IStringLocalizer{T}"/> —
        /// возвращает ключ (не падает на отсутствие ключа в .resx).
        /// </summary>
        private sealed class FakeStringLocalizer : IStringLocalizer<SharedResources>
        {
            public LocalizedString this[string name]
                => new LocalizedString(name, name, resourceNotFound: true);

            public LocalizedString this[string name, params object[] arguments]
                => new LocalizedString(name, name, resourceNotFound: true);

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
                => Array.Empty<LocalizedString>();
        }

        /// <summary>
        /// Fake <see cref="IAdminOrchestratorService"/> — запоминает вызовы,
        /// возвращает настраиваемые результаты, может бросить исключение.
        /// </summary>
        private sealed class FakeAdminOrchestratorService : IAdminOrchestratorService
        {
            public bool GetCalled { get; private set; }
            public bool UpdateCalled { get; private set; }
            public bool ResetCalled { get; private set; }

            public int LastUserId { get; private set; }
            public OrchestratorSettingsDto LastRequest { get; private set; }

            public OrchestratorSettingsDto GetResult { get; set; }
                = BuildSettings();
            public OrchestratorSettingsDto UpdateResult { get; set; }
                = BuildSettings();
            public OrchestratorSettingsDto ResetResult { get; set; }
                = BuildSettings();

            public Exception ThrowOnGet { get; set; }
            public Exception ThrowOnUpdate { get; set; }
            public Exception ThrowOnReset { get; set; }

            public Task<OrchestratorSettingsDto> GetSettingsAsync(
                CancellationToken cancellationToken = default)
            {
                if (ThrowOnGet != null) throw ThrowOnGet;
                GetCalled = true;
                return Task.FromResult(GetResult);
            }

            public Task<OrchestratorSettingsDto> UpdateSettingsAsync(
                OrchestratorSettingsDto settings,
                int userId,
                CancellationToken cancellationToken = default)
            {
                if (ThrowOnUpdate != null) throw ThrowOnUpdate;
                UpdateCalled = true;
                LastRequest = settings;
                LastUserId = userId;
                return Task.FromResult(UpdateResult);
            }

            public Task<OrchestratorSettingsDto> ResetAsync(
                int userId,
                CancellationToken cancellationToken = default)
            {
                if (ThrowOnReset != null) throw ThrowOnReset;
                ResetCalled = true;
                LastUserId = userId;
                return Task.FromResult(ResetResult);
            }
        }
    }
}
