using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Controllers;
using IIChatTools.API.Resources;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.UnitTests.SqlAgent
{
    /// <summary>
    /// Тесты <see cref="AdminSqlAgentController"/>
    /// (v1.7.0, KI-097, Фаза 7B).
    /// <para>
    /// Прямой вызов методов контроллера (без <c>WebApplicationFactory</c>) —
    /// консистентно с <c>AdminKnowledgeControllerTests</c>.
    /// Fake <see cref="IAdminSqlAgentService"/> записывает вызовы и
    /// возвращает настраиваемые результаты.
    /// </para>
    /// </summary>
    public class AdminSqlAgentControllerTests
    {
        // ============ GET /connections ============

        [Fact]
        public async Task GetConnectionsAsync_ReturnsFourDtoItems()
        {
            var fake = new FakeAdminSqlAgentService
            {
                ConnectionsResult = new List<SqlAgentConnectionItemDto>
                {
                    new SqlAgentConnectionItemDto
                    {
                        Name = "internal",
                        DisplayName = "Test DB",
                        Provider = "Sqlite",
                        Enabled = true,
                        IsOverridden = false
                    }
                }
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.GetConnectionsAsync(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(ok.Value);
            // Проверяем структуру { success, data } через рефлексию анонимного типа
            var valueType = ok.Value.GetType();
            var successProp = valueType.GetProperty("success");
            var dataProp = valueType.GetProperty("data");
            Assert.True((bool)successProp.GetValue(ok.Value));
            Assert.NotNull(dataProp.GetValue(ok.Value));
            Assert.True(fake.GetConnectionsCalled);
        }

        // ============ PUT /connections/{name} ============

        [Fact]
        public async Task UpdateConnectionAsync_ValidRequest_ReturnsDto()
        {
            var fake = new FakeAdminSqlAgentService
            {
                UpdateResult = new SqlAgentConnectionItemDto
                {
                    Name = "internal",
                    MaxRows = 42,
                    IsOverridden = true
                }
            };
            var controller = BuildController(fake, userId: 7);

            var request = new UpdateSqlAgentConnectionRequest { MaxRows = 42 };
            var result = await controller.UpdateConnectionAsync("internal", request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True(fake.UpdateCalled);
            Assert.Equal("internal", fake.LastName);
            Assert.Equal(42, fake.LastRequest.MaxRows);
            Assert.Equal(7, fake.LastUserId);
        }

        [Fact]
        public async Task UpdateConnectionAsync_NullRequest_ReturnsFail()
        {
            var fake = new FakeAdminSqlAgentService();
            var controller = BuildController(fake, userId: 1);

            var result = await controller.UpdateConnectionAsync("internal", null, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var successProp = ok.Value.GetType().GetProperty("success");
            Assert.False((bool)successProp.GetValue(ok.Value));
            Assert.False(fake.UpdateCalled);
        }

        [Fact]
        public async Task UpdateConnectionAsync_ServiceThrowsArgumentException_ReturnsFail()
        {
            var fake = new FakeAdminSqlAgentService
            {
                ThrowOnUpdate = new ArgumentException("MaxRows должен быть в диапазоне 1–10000.")
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.UpdateConnectionAsync(
                "internal",
                new UpdateSqlAgentConnectionRequest { MaxRows = 0 },
                CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var successProp = ok.Value.GetType().GetProperty("success");
            var messageProp = ok.Value.GetType().GetProperty("message");
            Assert.False((bool)successProp.GetValue(ok.Value));
            Assert.Contains("1–10000", (string)messageProp.GetValue(ok.Value));
        }

        // ============ POST /connections/{name}/test ============

        [Fact]
        public async Task TestConnectionAsync_Success_ReturnsDto()
        {
            var fake = new FakeAdminSqlAgentService
            {
                TestResult = new SqlAgentTestResultDto
                {
                    Success = true,
                    Message = "Connection OK",
                    DurationMs = 5
                }
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.TestConnectionAsync("internal", CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True(fake.TestCalled);
            Assert.Equal("internal", fake.LastName);
        }

        // ============ POST /connections/{name}/reset ============

        [Fact]
        public async Task ResetConnectionAsync_ValidRequest_ReturnsDto()
        {
            var fake = new FakeAdminSqlAgentService
            {
                ResetResult = new SqlAgentConnectionItemDto
                {
                    Name = "internal",
                    IsOverridden = false
                }
            };
            var controller = BuildController(fake, userId: 5);

            var result = await controller.ResetConnectionAsync("internal", CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True(fake.ResetCalled);
            Assert.Equal(5, fake.LastUserId);
        }

        [Fact]
        public async Task ResetConnectionAsync_ServiceThrows_ReturnsFail()
        {
            var fake = new FakeAdminSqlAgentService
            {
                ThrowOnReset = new ArgumentException("Подключение 'no-such' не зарегистрировано.")
            };
            var controller = BuildController(fake, userId: 1);

            var result = await controller.ResetConnectionAsync("no-such", CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var successProp = ok.Value.GetType().GetProperty("success");
            Assert.False((bool)successProp.GetValue(ok.Value));
        }

        // ============ Test infrastructure ============

        private static AdminSqlAgentController BuildController(
            IAdminSqlAgentService service, int userId)
        {
            var controller = new AdminSqlAgentController(
                service,
                NullLogger<AdminSqlAgentController>.Instance,
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

            public System.Collections.Generic.IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
                => System.Array.Empty<LocalizedString>();
        }

        /// <summary>
        /// Fake <see cref="IAdminSqlAgentService"/> — запоминает вызовы,
        /// возвращает настраиваемые результаты, может бросить исключение.
        /// </summary>
        private sealed class FakeAdminSqlAgentService : IAdminSqlAgentService
        {
            public bool GetConnectionsCalled { get; private set; }
            public bool UpdateCalled { get; private set; }
            public bool TestCalled { get; private set; }
            public bool ResetCalled { get; private set; }

            public string LastName { get; private set; }
            public UpdateSqlAgentConnectionRequest LastRequest { get; private set; }
            public int LastUserId { get; private set; }

            public IReadOnlyList<SqlAgentConnectionItemDto> ConnectionsResult { get; set; }
                = new List<SqlAgentConnectionItemDto>();
            public SqlAgentConnectionItemDto UpdateResult { get; set; }
                = new SqlAgentConnectionItemDto();
            public SqlAgentTestResultDto TestResult { get; set; }
                = new SqlAgentTestResultDto();
            public SqlAgentConnectionItemDto ResetResult { get; set; }
                = new SqlAgentConnectionItemDto();

            public Exception ThrowOnUpdate { get; set; }
            public Exception ThrowOnReset { get; set; }

            public Task<IReadOnlyList<SqlAgentConnectionItemDto>> GetAllConnectionsAsync(
                CancellationToken cancellationToken = default)
            {
                GetConnectionsCalled = true;
                return Task.FromResult(ConnectionsResult);
            }

            public Task<SqlAgentConnectionItemDto> UpdateConnectionAsync(
                string name,
                UpdateSqlAgentConnectionRequest request,
                int userId,
                CancellationToken cancellationToken = default)
            {
                if (ThrowOnUpdate != null) throw ThrowOnUpdate;
                UpdateCalled = true;
                LastName = name;
                LastRequest = request;
                LastUserId = userId;
                return Task.FromResult(UpdateResult);
            }

            public Task<SqlAgentTestResultDto> TestConnectionAsync(
                string name,
                CancellationToken cancellationToken = default)
            {
                TestCalled = true;
                LastName = name;
                return Task.FromResult(TestResult);
            }

            public Task<SqlAgentConnectionItemDto> ResetConnectionAsync(
                string name,
                int userId,
                CancellationToken cancellationToken = default)
            {
                if (ThrowOnReset != null) throw ThrowOnReset;
                ResetCalled = true;
                LastName = name;
                LastUserId = userId;
                return Task.FromResult(ResetResult);
            }
        }
    }
}