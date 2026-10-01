using System;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm.Formats;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm.Formats
{
    /// <summary>
    /// Тесты <see cref="AnthropicRequestBuilder"/> (v1.9.0, KI-110a, Фаза 2.1).
    /// </summary>
    public class AnthropicRequestBuilderTests
    {
        private static ExternalProviderOptions Provider() => new ExternalProviderOptions
        {
            DisplayName = "Anthropic Claude",
            Format = ProviderFormat.Anthropic,
            BaseUrl = "https://api.anthropic.com/v1",
            Model = "claude-haiku-4-5",
            MaxTokens = 8192
        };

        private static ExternalLlmRequest Request(string prompt = "Hello")
            => new ExternalLlmRequest { Provider = "anthropic", Prompt = prompt };

        [Fact]
        public void Build_MinimalRequest_HasModelMaxTokensMessages()
        {
            var payload = AnthropicRequestBuilder.Build(Provider(), Request("2+2?"));

            Assert.Equal("claude-haiku-4-5", payload["model"]?.ToString());
            Assert.Equal(8192, payload["max_tokens"]?.Value<int>());

            var messages = payload["messages"] as JArray;
            Assert.NotNull(messages);
            Assert.Single(messages);
            Assert.Equal("user", messages[0]["role"]?.ToString());
            Assert.Equal("2+2?", messages[0]["content"]?.ToString());
        }

        [Fact]
        public void Build_WithSystem_IncludesSystemField()
        {
            var request = Request("hi");
            request.System = "You are a helpful assistant.";

            var payload = AnthropicRequestBuilder.Build(Provider(), request);

            Assert.Equal("You are a helpful assistant.", payload["system"]?.ToString());
        }

        [Fact]
        public void Build_WithoutSystem_OmitsSystemField()
        {
            var payload = AnthropicRequestBuilder.Build(Provider(), Request());

            Assert.Null(payload["system"]);
        }

        [Fact]
        public void Build_WhitespaceSystem_OmitsSystemField()
        {
            var request = Request();
            request.System = "   ";

            var payload = AnthropicRequestBuilder.Build(Provider(), request);

            Assert.Null(payload["system"]);
        }

        [Fact]
        public void Build_MaxTokensOverride_TakesPrecedence()
        {
            var request = Request();
            request.MaxTokens = 1024;

            var payload = AnthropicRequestBuilder.Build(Provider(), request);

            Assert.Equal(1024, payload["max_tokens"]?.Value<int>());
        }

        [Fact]
        public void Build_TemperatureAboveOne_ClampedToOne()
        {
            var request = Request();
            request.Temperature = 2.0;

            var payload = AnthropicRequestBuilder.Build(Provider(), request);

            Assert.Equal(1.0, payload["temperature"]?.Value<double>());
        }

        [Fact]
        public void Build_TemperatureBelowZero_ClampedToZero()
        {
            var request = Request();
            request.Temperature = -0.5;

            var payload = AnthropicRequestBuilder.Build(Provider(), request);

            Assert.Equal(0.0, payload["temperature"]?.Value<double>());
        }

        [Fact]
        public void Build_NoTemperatureOverride_UsesDefault07()
        {
            var payload = AnthropicRequestBuilder.Build(Provider(), Request());

            Assert.Equal(0.7, payload["temperature"]?.Value<double>());
        }

        [Fact]
        public void Build_StreamNotIncluded()
        {
            var payload = AnthropicRequestBuilder.Build(Provider(), Request());

            Assert.Null(payload["stream"]);
        }

        [Fact]
        public void Build_EmptyPrompt_Throws()
        {
            var request = new ExternalLlmRequest { Prompt = "" };

            Assert.Throws<InvalidOperationException>(
                () => AnthropicRequestBuilder.Build(Provider(), request));
        }

        [Fact]
        public void Build_NullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => AnthropicRequestBuilder.Build(null, Request()));
        }

        [Fact]
        public void Build_NullRequest_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => AnthropicRequestBuilder.Build(Provider(), null));
        }
    }
}