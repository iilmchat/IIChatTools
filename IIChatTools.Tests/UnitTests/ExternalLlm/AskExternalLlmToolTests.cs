using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.Tools.ExternalLlm;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm
{
    /// <summary>
    /// Тесты <see cref="AskExternalLlmTool"/> (v1.8.1, KI-109, Фаза 3).
    /// </summary>
    public class AskExternalLlmToolTests
    {
        private const int UserId = 1;

        private static ExternalProviderOptions Provider(string name = "deepseek")
            => new ExternalProviderOptions
            {
                DisplayName = name,
                BaseUrl = "https://api.example.com/v1",
                Model = $"{name}-chat"
            };

        private static (AskExternalLlmTool tool,
                        Mock<IExternalLlmClient> clientMock,
                        Mock<IExternalProviderRegistry> registryMock)
            Create()
        {
            var clientMock = new Mock<IExternalLlmClient>();

            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.DefaultProvider).Returns("deepseek");
            registryMock.Setup(r => r.GetNames()).Returns(new[] { "deepseek", "openai" });
            registryMock.Setup(r => r.Get("deepseek")).Returns(Provider("deepseek"));
            registryMock.Setup(r => r.Get("openai")).Returns(Provider("openai"));
            registryMock.Setup(r => r.Get(It.IsNotIn("deepseek", "openai")))
                .Returns((ExternalProviderOptions)null);

            var tool = new AskExternalLlmTool(
                clientMock.Object,
                registryMock.Object,
                NullLogger<AskExternalLlmTool>.Instance);

            return (tool, clientMock, registryMock);
        }

        private static ExternalLlmResponse Response(string provider, string content = "ok")
            => new ExternalLlmResponse
            {
                Provider = provider,
                Content = content,
                PromptTokens = 100,
                CompletionTokens = 200,
                CostUsd = 0.0005m,
                DurationMs = 1234
            };

        private static ToolExecutionContext Context() => new ToolExecutionContext
        {
            UserId = UserId,
            WorkspaceRoot = "/tmp"
        };

        [Fact]
        public async Task ExecuteAsync_EmptyPrompt_Fail()
        {
            var (tool, _, _) = Create();

            var result = await tool.ExecuteAsync(Context(), new JObject { ["prompt"] = "" });

            Assert.False(result.Success);
            Assert.Contains("prompt", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownProvider_Fail()
        {
            var (tool, _, _) = Create();

            var result = await tool.ExecuteAsync(Context(), new JObject
            {
                ["prompt"] = "test",
                ["provider"] = "unknown"
            });

            Assert.False(result.Success);
            Assert.Contains("unknown", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_Success_ReturnsResult()
        {
            var (tool, clientMock, _) = Create();

            clientMock
                .Setup(c => c.CompleteAsync(
                    UserId,
                    It.Is<ExternalLlmRequest>(r => r.Provider == "deepseek" && r.Prompt == "hi"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response("deepseek", "answer"));

            var result = await tool.ExecuteAsync(Context(), new JObject
            {
                ["prompt"] = "hi"
            });

            Assert.True(result.Success);
            var data = result.Data as dynamic;
            Assert.NotNull(data);
            var json = JObject.FromObject(result.Data);
            Assert.Equal("deepseek", json["provider"]?.ToString());
            Assert.Equal("answer", json["content"]?.ToString());
            Assert.Equal(100, json["promptTokens"]?.Value<int>());
            Assert.Equal(200, json["completionTokens"]?.Value<int>());
        }

        [Fact]
        public async Task ExecuteAsync_UsesDefaultProvider_WhenProviderNotSet()
        {
            var (tool, clientMock, _) = Create();

            clientMock
                .Setup(c => c.CompleteAsync(
                    UserId,
                    It.Is<ExternalLlmRequest>(r => r.Provider == "deepseek"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response("deepseek"));

            var result = await tool.ExecuteAsync(Context(), new JObject { ["prompt"] = "hi" });

            Assert.True(result.Success);
            clientMock.Verify(c => c.CompleteAsync(
                UserId,
                It.Is<ExternalLlmRequest>(r => r.Provider == "deepseek"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_CompareWithSameProvider_Fail()
        {
            var (tool, _, _) = Create();

            var result = await tool.ExecuteAsync(Context(), new JObject
            {
                ["prompt"] = "hi",
                ["provider"] = "deepseek",
                ["compare_with"] = "deepseek"
            });

            Assert.False(result.Success);
            Assert.Contains("совпадает", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_CompareWithUnknownProvider_Fail()
        {
            var (tool, _, _) = Create();

            var result = await tool.ExecuteAsync(Context(), new JObject
            {
                ["prompt"] = "hi",
                ["provider"] = "deepseek",
                ["compare_with"] = "unknown"
            });

            Assert.False(result.Success);
            Assert.Contains("unknown", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_WithCompare_RunsBothParallel()
        {
            var (tool, clientMock, _) = Create();

            clientMock
                .Setup(c => c.CompleteAsync(
                    UserId,
                    It.Is<ExternalLlmRequest>(r => r.Provider == "deepseek"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response("deepseek", "from-deepseek"));

            clientMock
                .Setup(c => c.CompleteAsync(
                    UserId,
                    It.Is<ExternalLlmRequest>(r => r.Provider == "openai"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response("openai", "from-openai"));

            var result = await tool.ExecuteAsync(Context(), new JObject
            {
                ["prompt"] = "hi",
                ["provider"] = "deepseek",
                ["compare_with"] = "openai"
            });

            Assert.True(result.Success);

            // ВНИМАНИЕ (RULES § 4.43): ToolResult.Data в compare-режиме —
            // ExternalLlmComparisonDto (PascalCase-свойства). JObject.FromObject
            // использует дефолтный Newtonsoft-контракт → PascalCase.
            // В реальном API MVC сериализует в camelCase — там всё ок.
            // В тесте — обращаемся по PascalCase-именам, как в DTO.
            var json = JObject.FromObject(result.Data);
            Assert.Equal("deepseek", json["Primary"]?["Provider"]?.ToString());
            Assert.Equal("from-deepseek", json["Primary"]?["Content"]?.ToString());
            Assert.Equal("openai", json["Secondary"]?["Provider"]?.ToString());
            Assert.Equal("from-openai", json["Secondary"]?["Content"]?.ToString());
            Assert.Equal(0.001m, json["TotalCostUsd"]?.Value<decimal>());
        }

        [Fact]
        public async Task ExecuteAsync_CompareWith_SecondFails_Fail()
        {
            var (tool, clientMock, _) = Create();

            clientMock
                .Setup(c => c.CompleteAsync(
                    UserId,
                    It.Is<ExternalLlmRequest>(r => r.Provider == "deepseek"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response("deepseek"));

            clientMock
                .Setup(c => c.CompleteAsync(
                    UserId,
                    It.Is<ExternalLlmRequest>(r => r.Provider == "openai"),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("provider down"));

            var result = await tool.ExecuteAsync(Context(), new JObject
            {
                ["prompt"] = "hi",
                ["provider"] = "deepseek",
                ["compare_with"] = "openai"
            });

            Assert.False(result.Success);
            Assert.Contains("упал", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_ClientThrows_Fail()
        {
            var (tool, clientMock, _) = Create();

            clientMock
                .Setup(c => c.CompleteAsync(
                    UserId,
                    It.IsAny<ExternalLlmRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Превышен дневной бюджет"));

            var result = await tool.ExecuteAsync(Context(), new JObject { ["prompt"] = "hi" });

            Assert.False(result.Success);
            Assert.Contains("бюджет", result.Message);
        }

        [Fact]
        public async Task ExecuteAsync_EmptyUserId_Fail()
        {
            var (tool, _, _) = Create();

            var result = await tool.ExecuteAsync(new ToolExecutionContext { UserId = 0 },
                new JObject { ["prompt"] = "hi" });

            Assert.False(result.Success);
            Assert.Contains("UserId", result.Message);
        }
    }
}