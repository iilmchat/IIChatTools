using System.Collections.Generic;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm
{
    /// <summary>
    /// Тесты <see cref="ExternalProviderRegistry"/> (v1.8.1, KI-109, Фаза 2.1).
    /// </summary>
    public class ExternalProviderRegistryTests
    {
        private static ExternalProviderOptions ValidProvider(string model = "test-model")
            => new ExternalProviderOptions
            {
                DisplayName = "Test",
                BaseUrl = "https://api.example.com/v1",
                Model = model,
                ApiKeySecretName = null,    // ключ не обязателен
                CostPer1kInputUsd = 0.001m,
                CostPer1kOutputUsd = 0.002m,
                MaxTokens = 4096,
                TimeoutSeconds = 60
            };

        private static IConfiguration Config(params (string key, string value)[] entries)
        {
            var dict = new Dictionary<string, string>();
            foreach (var (k, v) in entries) dict[k] = v;
            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        [Fact]
        public void EnabledFalse_RegistryIsEmpty_NoValidation()
        {
            var opts = new ExternalLlmOptions { Enabled = false };
            var registry = new ExternalProviderRegistry(
                Options.Create(opts),
                Config(),
                NullLogger<ExternalProviderRegistry>.Instance);

            Assert.Empty(registry.GetNames());
            Assert.Null(registry.Get("any"));
        }

        [Fact]
        public void EnabledTrue_EmptyProviders_Throws()
        {
            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "deepseek",
                Providers = new Dictionary<string, ExternalProviderOptions>()
            };

            Assert.Throws<System.InvalidOperationException>(() =>
                new ExternalProviderRegistry(
                    Options.Create(opts),
                    Config(),
                    NullLogger<ExternalProviderRegistry>.Instance));
        }

        [Fact]
        public void EnabledTrue_MissingDefaultProvider_Throws()
        {
            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["deepseek"] = ValidProvider()
                }
            };

            Assert.Throws<System.InvalidOperationException>(() =>
                new ExternalProviderRegistry(
                    Options.Create(opts),
                    Config(),
                    NullLogger<ExternalProviderRegistry>.Instance));
        }

        [Fact]
        public void EnabledTrue_DefaultProviderNotInProviders_Throws()
        {
            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "openai",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["deepseek"] = ValidProvider()
                }
            };

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                new ExternalProviderRegistry(
                    Options.Create(opts),
                    Config(),
                    NullLogger<ExternalProviderRegistry>.Instance));

            Assert.Contains("openai", ex.Message);
        }

        [Fact]
        public void EnabledTrue_InvalidBaseUrl_Throws()
        {
            var bad = ValidProvider();
            bad.BaseUrl = "not-a-url";

            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "deepseek",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["deepseek"] = bad
                }
            };

            Assert.Throws<System.InvalidOperationException>(() =>
                new ExternalProviderRegistry(
                    Options.Create(opts),
                    Config(),
                    NullLogger<ExternalProviderRegistry>.Instance));
        }

        [Fact]
        public void EnabledTrue_HttpNonLocalhost_Throws()
        {
            var bad = ValidProvider();
            bad.BaseUrl = "http://api.example.com/v1";   // http, не localhost

            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "deepseek",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["deepseek"] = bad
                }
            };

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                new ExternalProviderRegistry(
                    Options.Create(opts),
                    Config(),
                    NullLogger<ExternalProviderRegistry>.Instance));

            Assert.Contains("https", ex.Message);
        }

        [Fact]
        public void EnabledTrue_ApiKeySecretNameNotResolved_Throws()
        {
            var bad = ValidProvider();
            bad.ApiKeySecretName = "ExternalLlm:DeepSeek:ApiKey";

            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "deepseek",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["deepseek"] = bad
                }
            };

            // Конфиг без ключа ApiKeySecretName → throw
            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                new ExternalProviderRegistry(
                    Options.Create(opts),
                    Config(),
                    NullLogger<ExternalProviderRegistry>.Instance));

            Assert.Contains("ApiKeySecretName", ex.Message);
        }

        [Fact]
        public void EnabledTrue_ApiKeyResolved_Ok()
        {
            var withKey = ValidProvider();
            withKey.ApiKeySecretName = "ExternalLlm:DeepSeek:ApiKey";

            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "deepseek",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["deepseek"] = withKey,
                    ["openai"] = ValidProvider()
                }
            };

            var registry = new ExternalProviderRegistry(
                Options.Create(opts),
                Config(("ExternalLlm:DeepSeek:ApiKey", "sk-test-key")),
                NullLogger<ExternalProviderRegistry>.Instance);

            Assert.Equal("deepseek", registry.DefaultProvider);
            Assert.Equal(2, registry.GetNames().Count);
            Assert.Equal("deepseek", registry.GetNames()[0]);  // порядок сохранён
            Assert.Equal("openai", registry.GetNames()[1]);
        }

        [Fact]
        public void Get_IsCaseInsensitive()
        {
            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "deepseek",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["deepseek"] = ValidProvider()
                }
            };

            var registry = new ExternalProviderRegistry(
                Options.Create(opts),
                Config(),
                NullLogger<ExternalProviderRegistry>.Instance);

            Assert.NotNull(registry.Get("DeepSeek"));
            Assert.NotNull(registry.Get("DEEPSEEK"));
            Assert.NotNull(registry.Get("deepseek"));
        }

        [Fact]
        public void Get_Unknown_ReturnsNull()
        {
            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "deepseek",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["deepseek"] = ValidProvider()
                }
            };

            var registry = new ExternalProviderRegistry(
                Options.Create(opts),
                Config(),
                NullLogger<ExternalProviderRegistry>.Instance);

            Assert.Null(registry.Get("unknown"));
            Assert.Null(registry.Get(""));
            Assert.Null(registry.Get(null));
        }

        /// <summary>
        /// v1.9.0 (KI-110a): <see cref="ExternalProviderOptions.Format"/>
        /// не влияет на валидацию <see cref="ExternalProviderRegistry"/>
        /// (DESIGN v1.9 § 3.2). Провайдер с <c>Format=Anthropic</c>
        /// проходит те же проверки, что и <c>Format=OpenAI</c>.
        /// </summary>
        [Fact]
        public void EnabledTrue_AnthropicFormat_ValidatesOk()
        {
            var anthropic = ValidProvider();
            anthropic.Format = ProviderFormat.Anthropic;
            anthropic.BaseUrl = "https://api.anthropic.com/v1";
            anthropic.Model = "claude-haiku-4-5";

            var opts = new ExternalLlmOptions
            {
                Enabled = true,
                DefaultProvider = "anthropic",
                Providers = new Dictionary<string, ExternalProviderOptions>
                {
                    ["anthropic"] = anthropic
                }
            };

            var registry = new ExternalProviderRegistry(
                Options.Create(opts),
                Config(),
                NullLogger<ExternalProviderRegistry>.Instance);

            var provider = registry.Get("anthropic");
            Assert.NotNull(provider);
            Assert.Equal(ProviderFormat.Anthropic, provider.Format);
        }

        /// <summary>
        /// v1.9.0 (KI-110a): значение по умолчанию
        /// <see cref="ExternalProviderOptions.Format"/> — <c>OpenAI</c>.
        ///
        /// <para>
        /// Гарантирует обратную совместимость: 5 существующих провайдеров
        /// (DeepSeek, OpenAI, Groq, Together AI, Ollama) не задают
        /// <c>Format</c> в <c>appsettings.json</c> — работают как раньше
        /// (DESIGN v1.9 § 3.2).
        /// </para>
        /// </summary>
        [Fact]
        public void DefaultFormat_IsOpenAI()
        {
            var provider = new ExternalProviderOptions();
            Assert.Equal(ProviderFormat.OpenAI, provider.Format);
        }
    }
}