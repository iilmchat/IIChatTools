using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Implementation.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты реестра специализированных суб-агентов (v1.4.0 Фаза 1, KI-052).
    /// Проверяют загрузку из конфигурации, поиск, фильтрацию по Disabled,
    /// Update/Reset (in-memory, без БД).
    /// </summary>
    public class SubAgentRegistryTests
    {
        /// <summary>
        /// Создаёт реестр с заданными in-memory ключами конфигурации.
        /// </summary>
        /// <param name="pairs">Плоские ключи конфигурации (SubAgents:xxx:yyy)</param>
        private static SubAgentRegistry CreateRegistry(params (string Key, string Value)[] pairs)
        {
            var dict = pairs.ToDictionary(p => p.Key, p => p.Value);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();

            return new SubAgentRegistry(config, NullLogger<SubAgentRegistry>.Instance);
        }

        /// <summary>
        /// GetAll возвращает всех агентов, включая отключённых, сортировка по имени.
        /// </summary>
        [Fact]
        public void GetAll_ReturnsConfiguredAgents()
        {
            var registry = CreateRegistry(
                ("SubAgents:file_system_agent:DisplayName", "Агент файловой системы"),
                ("SubAgents:file_system_agent:Model", "qwen/qwen3-4b-2507"),
                ("SubAgents:file_system_agent:MaxSteps", "10"),
                ("SubAgents:file_system_agent:RequiresApproval", "true"),
                ("SubAgents:file_system_agent:Enabled", "true"),

                ("SubAgents:web_agent:DisplayName", "Веб-агент"),
                ("SubAgents:web_agent:RequiresApproval", "false"),
                ("SubAgents:web_agent:Enabled", "true")
            );

            var all = registry.GetAll();

            Assert.Equal(2, all.Count);
            Assert.Equal("file_system_agent", all[0].Name);   // сортировка по имени
            Assert.Equal("web_agent", all[1].Name);
            Assert.Equal("Агент файловой системы", all[0].DisplayName);
            Assert.True(all[0].RequiresApprovalByDefault);
            Assert.False(all[1].RequiresApprovalByDefault);
        }

        /// <summary>
        /// Get возвращает null для несуществующего имени.
        /// </summary>
        [Fact]
        public void Get_UnknownName_ReturnsNull()
        {
            var registry = CreateRegistry(
                ("SubAgents:web_agent:Enabled", "true"));

            var result = registry.Get("no_such_agent");

            Assert.Null(result);
        }

        /// <summary>
        /// GetEnabled исключает отключённых агентов.
        /// </summary>
        [Fact]
        public void GetEnabled_ExcludesDisabled()
        {
            var registry = CreateRegistry(
                ("SubAgents:web_agent:Enabled", "true"),
                ("SubAgents:github_agent:Enabled", "false"),   // отключён
                ("SubAgents:file_system_agent:Enabled", "true")
            );

            var enabled = registry.GetEnabled();

            Assert.Equal(2, enabled.Count);
            Assert.DoesNotContain(enabled, d => d.Name == "github_agent");
            Assert.Contains(enabled, d => d.Name == "web_agent");
            Assert.Contains(enabled, d => d.Name == "file_system_agent");
        }

        /// <summary>
        /// Update заменяет дескриптор; Reset возвращает к значению из конфига.
        /// </summary>
        [Fact]
        public void Update_OverridesConfig_AndResetRestoresDefaults()
        {
            var registry = CreateRegistry(
                ("SubAgents:web_agent:DisplayName", "Веб-агент"),
                ("SubAgents:web_agent:MaxSteps", "10"),
                ("SubAgents:web_agent:Enabled", "true")
            );

            var original = registry.Get("web_agent");
            Assert.Equal("Веб-агент", original.DisplayName);
            Assert.Equal(10, original.MaxSteps);

            // Update
            var updated = new SubAgentDescriptor
            {
                Name = "web_agent",
                DisplayName = "Обновлённый веб-агент",
                MaxSteps = 20,
                AllowedTools = new List<string> { "web_search" }
            };
            registry.Update(updated);

            var afterUpdate = registry.Get("web_agent");
            Assert.Equal("Обновлённый веб-агент", afterUpdate.DisplayName);
            Assert.Equal(20, afterUpdate.MaxSteps);

            // Reset
            registry.Reset("web_agent");

            var afterReset = registry.Get("web_agent");
            Assert.Equal("Веб-агент", afterReset.DisplayName);
            Assert.Equal(10, afterReset.MaxSteps);
        }
    }
}