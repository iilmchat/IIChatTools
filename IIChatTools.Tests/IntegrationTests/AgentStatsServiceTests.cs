using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты агрегации статистики запусков агентов (v1.4.x, KI-076).
    /// </summary>
    public class AgentStatsServiceTests
    {
        /// <summary>
        /// Fake-реестр: возвращает заданные дескрипторы.
        /// </summary>
        private sealed class FakeSubAgentRegistry : ISubAgentRegistry
        {
            private readonly List<SubAgentDescriptor> _agents = new List<SubAgentDescriptor>();

            public FakeSubAgentRegistry(params (string Name, string DisplayName)[] agents)
            {
                foreach (var (n, dn) in agents)
                {
                    _agents.Add(new SubAgentDescriptor
                    {
                        Name = n,
                        DisplayName = dn,
                        Disabled = false
                    });
                }
            }

            public IReadOnlyList<SubAgentDescriptor> GetAll() => _agents;
            public IReadOnlyList<SubAgentDescriptor> GetEnabled() => _agents;
            public SubAgentDescriptor Get(string name)
                => _agents.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            public void Update(SubAgentDescriptor descriptor) { }
            public void Reset(string name) { }
        }

        /// <summary>
        /// Пустой AuditLogs → пустой список.
        /// </summary>
        [Fact]
        public async Task GetAllStatsAsync_NoLogs_ReturnsEmpty()
        {
            var db = TestDbContextFactory.Create();
            var registry = new FakeSubAgentRegistry(("file_system_agent", "Агент ФС"));
            var service = new AgentStatsService(db, registry, NullLogger<AgentStatsService>.Instance);

            var result = await service.GetAllStatsAsync();

            Assert.Empty(result);
        }

        /// <summary>
        /// Смешанные логи (agent.* и прочие) → группируются только agent.*.
        /// </summary>
        [Fact]
        public async Task GetAllStatsAsync_OnlyAgentLogs_IgnoringOtherTools()
        {
            var db = TestDbContextFactory.Create();
            db.AuditLogs.AddRange(
                new AuditLog { UserId = 1, ToolName = "agent.file_system_agent", Status = "Success", DurationMs = 100 },
                new AuditLog { UserId = 1, ToolName = "agent.code_agent",          Status = "Success", DurationMs = 200 },
                new AuditLog { UserId = 1, ToolName = "save_file",                Status = "Success", DurationMs = 50 },
                new AuditLog { UserId = 1, ToolName = "list_directory",           Status = "Success", DurationMs = 30 }
            );
            await db.SaveChangesAsync();

            var registry = new FakeSubAgentRegistry(
                ("file_system_agent", "Агент ФС"),
                ("code_agent", "Агент кода"));
            var service = new AgentStatsService(db, registry, NullLogger<AgentStatsService>.Instance);

            var result = await service.GetAllStatsAsync();

            Assert.Equal(2, result.Count);
            Assert.Contains(result, s => s.AgentName == "file_system_agent");
            Assert.Contains(result, s => s.AgentName == "code_agent");
            Assert.DoesNotContain(result, s => s.AgentName == "save_file");
            Assert.DoesNotContain(result, s => s.AgentName == "list_directory");
        }

        /// <summary>
        /// Агрегация: TotalRuns / SuccessRuns / ErrorRuns / AvgDurationMs / LastRunAt / SuccessRate.
        /// </summary>
        [Fact]
        public async Task GetAllStatsAsync_AggregatesCorrectly()
        {
            var db = TestDbContextFactory.Create();
            var baseTime = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

            // 5 запусков file_system_agent: 3 Success, 1 Error, 1 Cancelled.
            // Длительности: 100, 200, 300, 400, 500 → avg = 300.
            db.AuditLogs.AddRange(
                new AuditLog { UserId = 1, ToolName = "agent.file_system_agent", Status = "Success",   DurationMs = 100, CreatedAt = baseTime.AddMinutes(1) },
                new AuditLog { UserId = 1, ToolName = "agent.file_system_agent", Status = "Success",   DurationMs = 200, CreatedAt = baseTime.AddMinutes(2) },
                new AuditLog { UserId = 1, ToolName = "agent.file_system_agent", Status = "Success",   DurationMs = 300, CreatedAt = baseTime.AddMinutes(3) },
                new AuditLog { UserId = 1, ToolName = "agent.file_system_agent", Status = "Error",     DurationMs = 400, CreatedAt = baseTime.AddMinutes(4) },
                new AuditLog { UserId = 1, ToolName = "agent.file_system_agent", Status = "Cancelled", DurationMs = 500, CreatedAt = baseTime.AddMinutes(5) }
            );
            await db.SaveChangesAsync();

            var registry = new FakeSubAgentRegistry(("file_system_agent", "Агент файловой системы"));
            var service = new AgentStatsService(db, registry, NullLogger<AgentStatsService>.Instance);

            var result = await service.GetAllStatsAsync();

            Assert.Single(result);
            var s = result[0];
            Assert.Equal("file_system_agent", s.AgentName);
            Assert.Equal("Агент файловой системы", s.DisplayName);
            Assert.Equal(5, s.TotalRuns);
            Assert.Equal(3, s.SuccessRuns);
            Assert.Equal(1, s.ErrorRuns);
            Assert.Equal(300, s.AvgDurationMs);
            Assert.NotNull(s.LastRunAt);
            Assert.Equal(60.0, s.SuccessRate, precision: 1);
        }
    }
}