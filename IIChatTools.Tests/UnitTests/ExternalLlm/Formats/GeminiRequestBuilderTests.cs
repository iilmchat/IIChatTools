using System;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm.Formats;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm.Formats
{
    /// <summary>
    /// Тесты <see cref="GeminiRequestBuilder"/> (v1.10.0, KI-110b, Фаза 1).
    /// </summary>
    public class GeminiRequestBuilderTests
    {
        private static ExternalProviderOptions Provider() => new ExternalProviderOptions
        {
            DisplayName = "Google Gemini",
            Format = ProviderFormat.Gemini,
            BaseUrl = "https://generativelanguage.googleapis.com/v1",
            Model = "gemini-2.0-flash",
            MaxTokens = 8192
        };

        private static ExternalLlmRequest Request(string prompt = "Hello")
            => new ExternalLlmRequest { Provider = "gemini", Prompt = prompt };

        [Fact]
        public void Build_MinimalRequest_HasContentsAndGenerationConfig()
        {
            var payload = GeminiRequestBuilder.Build(Provider(), Request("2+2?"));

            // contents[] — 1 элемент {role: "user", parts:[{text: prompt}]}.
            var contents = payload["contents"] as JArray;
            Assert.NotNull(contents);
            Assert.Single(contents);
            Assert.Equal("user", contents[0]["role"]?.ToString());

            var parts = contents[0]["parts"] as JArray;
            Assert.NotNull(parts);
            Assert.Single(parts);
            Assert.Equal("2+2?", parts[0]["text"]?.ToString());

            // generationConfig.maxOutputTokens = provider.MaxTokens (default 8192).
            var config = payload["generationConfig"] as JObject;
            Assert.NotNull(config);
            Assert.Equal(8192, config["maxOutputTokens"]?.Value<int>());
        }

        [Fact]
        public void Build_WithSystem_IncludesSystemInstruction()
        {
            var request = Request("hi");
            request.System = "You are a helpful assistant.";

            var payload = GeminiRequestBuilder.Build(Provider(), request);

            // systemInstruction — объект {parts:[{text}]}. role НЕ задаём.
            var si = payload["systemInstruction"] as JObject;
            Assert.NotNull(si);
            Assert.Null(si["role"]);

            var siParts = si["parts"] as JArray;
            Assert.NotNull(siParts);
            Assert.Single(siParts);
            Assert.Equal("You are a helpful assistant.", siParts[0]["text"]?.ToString());
        }

        [Fact]
        public void Build_WithoutSystem_OmitsSystemInstruction()
        {
            var payload = GeminiRequestBuilder.Build(Provider(), Request());

            Assert.Null(payload["systemInstruction"]);
        }

        [Fact]
        public void Build_WhitespaceSystem_OmitsSystemInstruction()
        {
            var request = Request();
            request.System = "   ";

            var payload = GeminiRequestBuilder.Build(Provider(), request);

            Assert.Null(payload["systemInstruction"]);
        }

        [Fact]
        public void Build_MaxTokensOverride_TakesPrecedence()
        {
            var request = Request();
            request.MaxTokens = 1024;

            var payload = GeminiRequestBuilder.Build(Provider(), request);

            Assert.Equal(1024, payload["generationConfig"]?["maxOutputTokens"]?.Value<int>());
        }

        [Fact]
        public void Build_TemperatureAboveTwo_ClampedToTwo()
        {
            var request = Request();
            request.Temperature = 5.0;

            var payload = GeminiRequestBuilder.Build(Provider(), request);

            Assert.Equal(2.0, payload["generationConfig"]?["temperature"]?.Value<double>());
        }

        [Fact]
        public void Build_TemperatureBelowZero_ClampedToZero()
        {
            var request = Request();
            request.Temperature = -1.0;

            var payload = GeminiRequestBuilder.Build(Provider(), request);

            Assert.Equal(0.0, payload["generationConfig"]?["temperature"]?.Value<double>());
        }

        [Fact]
        public void Build_NoTemperatureOverride_UsesDefault07()
        {
            var payload = GeminiRequestBuilder.Build(Provider(), Request());

            Assert.Equal(0.7, payload["generationConfig"]?["temperature"]?.Value<double>());
        }

        [Fact]
        public void Build_StreamNotIncluded()
        {
            var payload = GeminiRequestBuilder.Build(Provider(), Request());

            Assert.Null(payload["stream"]);
        }

        [Fact]
        public void Build_ModelNotInBody()
        {
            // Модель в URL, не в body — builder её не возвращает.
            var payload = GeminiRequestBuilder.Build(Provider(), Request());

            Assert.Null(payload["model"]);
        }

        [Fact]
        public void Build_EmptyPrompt_Throws()
        {
            var request = new ExternalLlmRequest { Prompt = "" };

            Assert.Throws<InvalidOperationException>(
                () => GeminiRequestBuilder.Build(Provider(), request));
        }

        [Fact]
        public void Build_NullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => GeminiRequestBuilder.Build(null, Request()));
        }

        [Fact]
        public void Build_NullRequest_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => GeminiRequestBuilder.Build(Provider(), null));
        }
    }
}