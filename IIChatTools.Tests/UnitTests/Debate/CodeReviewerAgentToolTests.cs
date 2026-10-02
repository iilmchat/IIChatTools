using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.Implementation.Agents;
using IIChatTools.Services.Implementation.Tools.SubAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Debate
{
    /// <summary>
    /// Unit-тесты <see cref="CodeReviewerAgentTool"/>
    /// (v1.11.0, KI-126, Шаг 1C).
    ///
    /// <para>
    /// Проверяют: имя, резолв <c>RequiresApprovalByDefault</c> из
    /// <see cref="ISubAgentRegistry"/> (по образцу 7 других агентов),
    /// наличие ключевых слов в Description. Не проверяют вызов
    /// <see cref="ISubAgentService"/> — это работа интеграционных тестов
    /// Шага 1I.
    /// </para>
    /// </summary>
    public class CodeReviewerAgentToolTests
    {
        /// <summary>
        /// Создаёт <see cref="CodeReviewerAgentTool"/> с in-memory
        /// <see cref="SubAgentRegistry"/>. Фабрика <c>ISubAgentService</c>
        /// бросает исключение — в этих тестах она не должна вызываться.
        /// </summary>
        private static CodeReviewerAgentTool CreateTool(
            bool requiresApproval = false,
            bool enabled = true,
            int maxSteps = 3)
        {
            var dict = new Dictionary<string, string>
            {
                ["SubAgents:code_reviewer_agent:DisplayName"] = "Код-ревьюер (Critic)",
                ["SubAgents:code_reviewer_agent:Enabled"] = enabled ? "true" : "false",
                ["SubAgents:code_reviewer_agent:RequiresApproval"] = requiresApproval ? "true" : "false",
                ["SubAgents:code_reviewer_agent:MaxSteps"] = maxSteps.ToString(),
                ["SubAgents:code_reviewer_agent:Model"] = "qwen/qwen3-4b-2507"
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();

            var registry = new SubAgentRegistry(
                config, NullLogger<SubAgentRegistry>.Instance);

            return new CodeReviewerAgentTool(
                subAgentServiceFactory: () => throw new InvalidOperationException(
                    "ISubAgentService не должен вызываться в unit-тестах CodeReviewerAgentTool"),
                registry: registry,
                auditService: Mock.Of<IAuditService>(),
                logger: NullLogger<CodeReviewerAgentTool>.Instance);
        }

        [Fact]
        public void Name_ReturnsCodeReviewerAgent()
        {
            var tool = CreateTool();
            Assert.Equal("code_reviewer_agent", tool.Name);
        }

        [Fact]
        public void RequiresApprovalByDefault_ResolvesFalseFromRegistry()
        {
            // Default для кода-ревьюера — false (read-only анализ).
            var tool = CreateTool(requiresApproval: false);
            Assert.False(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public void RequiresApprovalByDefault_ResolvesTrueFromRegistry_WhenOverridden()
        {
            // Если админ выставит RequiresApproval=true — резолв подхватит.
            var tool = CreateTool(requiresApproval: true);
            Assert.True(tool.RequiresApprovalByDefault);
        }

        [Fact]
        public void Description_MentionsReviewerAndApproval()
        {
            var tool = CreateTool();
            var description = tool.Description;

            Assert.Contains("ревьюер", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("verdict", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("approval", description, StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        // v1.11.0 (KI-126, Шаг 1I): disabled-агент
        // ============================================================

        [Fact]
        public async Task ExecuteAsync_DisabledAgent_ReturnsFail()
        {
            var tool = CreateTool(enabled: false);

            var result = await tool.ExecuteAsync(
                new IIChatTools.Services.DTO.ToolExecutionContext { UserId = 1 },
                new Newtonsoft.Json.Linq.JObject { ["task"] = "Проверь код" });

            Assert.False(result.Success);
            Assert.Contains("отключён", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_EmptyTask_ReturnsFail()
        {
            var tool = CreateTool(enabled: true);

            var result = await tool.ExecuteAsync(
                new IIChatTools.Services.DTO.ToolExecutionContext { UserId = 1 },
                new Newtonsoft.Json.Linq.JObject { ["task"] = "   " });

            Assert.False(result.Success);
            Assert.Contains("задача", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}