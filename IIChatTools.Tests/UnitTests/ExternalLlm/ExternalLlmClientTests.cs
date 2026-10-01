using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm
{
    /// <summary>
    /// Тесты <see cref="ExternalLlmClient"/> (v1.8.1, KI-109, Фаза 2.5).
    ///
    /// <para>
    /// Используют mock <see cref="HttpMessageHandler"/> + Moq для
    /// <see cref="IExternalProviderRegistry"/>, <see cref="IExternalLlmCircuitBreaker"/>,
    /// <see cref="IExternalLlmBudgetTracker"/>.
    /// </para>
    /// </summary>
    public class ExternalLlmClientTests
    {
        private const int UserId = 1;
        private const string ProviderName = "deepseek";

        private sealed class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public MockHttpMessageHandler(
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }

        private static ExternalProviderOptions DefaultProviderOptions() => new ExternalProviderOptions
        {
            DisplayName = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            Model = "deepseek-chat",
            ApiKeySecretName = "ExternalLlm:DeepSeek:ApiKey",
            CostPer1kInputUsd = 0.00014m,
            CostPer1kOutputUsd = 0.00028m,
            MaxTokens = 8192,
            TimeoutSeconds = 60
        };

        private static string BuildSuccessResponse(
            string content = "Hello from DeepSeek",
            int promptTokens = 245,
            int completionTokens = 512)
        {
            var root = new JObject
            {
                ["choices"] = new JArray
                {
                    new JObject
                    {
                        ["message"] = new JObject
                        {
                            ["role"] = "assistant",
                            ["content"] = content
                        },
                        ["finish_reason"] = "stop"
                    }
                },
                ["usage"] = new JObject
                {
                    ["prompt_tokens"] = promptTokens,
                    ["completion_tokens"] = completionTokens,
                    ["total_tokens"] = promptTokens + completionTokens
                }
            };
            return root.ToString();
        }

        /// <summary>
        /// Настройки Anthropic-провайдера (v1.9.0, KI-110a).
        /// </summary>
        private static ExternalProviderOptions AnthropicProviderOptions() => new ExternalProviderOptions
        {
            DisplayName = "Anthropic Claude",
            Format = ProviderFormat.Anthropic,
            BaseUrl = "https://api.anthropic.com/v1",
            Model = "claude-haiku-4-5",
            ApiKeySecretName = "ExternalLlm:Anthropic:ApiKey",
            CostPer1kInputUsd = 0.001m,
            CostPer1kOutputUsd = 0.005m,
            MaxTokens = 8192,
            TimeoutSeconds = 60
        };

        /// <summary>
        /// Типовой успешный ответ Anthropic Messages API.
        /// </summary>
        private static string BuildAnthropicSuccessResponse(
            string text = "Четыре",
            int inputTokens = 20,
            int outputTokens = 3)
        {
            var root = new JObject
            {
                ["id"] = "msg_01ABC",
                ["type"] = "message",
                ["role"] = "assistant",
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = text
                    }
                },
                ["model"] = "claude-haiku-4-5",
                ["stop_reason"] = "end_turn",
                ["usage"] = new JObject
                {
                    ["input_tokens"] = inputTokens,
                    ["output_tokens"] = outputTokens
                }
            };
            return root.ToString();
        }

        private static ExternalLlmClient Create(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
            ExternalProviderOptions provider = null,
            IExternalLlmCircuitBreaker circuitBreaker = null,
            IExternalLlmBudgetTracker budgetTracker = null,
            bool canSpend = true,
            bool circuitOpen = false,
            string apiKey = "sk-test",
            string providerName = ProviderName)
        {
            provider ??= DefaultProviderOptions();

            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.DefaultProvider).Returns(providerName);
            registryMock.Setup(r => r.Get(It.IsAny<string>())).Returns(provider);
            registryMock.Setup(r => r.GetNames()).Returns(new[] { providerName });

            var cbMock = new Mock<IExternalLlmCircuitBreaker>();
            cbMock.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(circuitOpen);
            cbMock.Setup(c => c.GetStatus(It.IsAny<string>())).Returns(new ProviderHealthStatus
            {
                Provider = providerName,
                Available = !circuitOpen,
                LastError = circuitOpen ? "Circuit breaker open (retry in 3m)" : null
            });
            if (circuitBreaker != null)
                cbMock = new Mock<IExternalLlmCircuitBreaker>();  // не используется — оставлено

            var budgetMock = new Mock<IExternalLlmBudgetTracker>();
            budgetMock.Setup(b => b.CanSpend(It.IsAny<int>())).Returns(canSpend);

            var httpClient = new HttpClient(new MockHttpMessageHandler(handler));

            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            // v1.9.0 (KI-110a): ключ в конфиге — по ApiKeySecretName провайдера,
            // не хардкод "ExternalLlm:DeepSeek:ApiKey". Иначе Anthropic-тесты
            // падают на ResolveApiKey ДО switch по Format.
            var configDict = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(provider.ApiKeySecretName))
            {
                configDict[provider.ApiKeySecretName] = apiKey ?? string.Empty;
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();

            return new ExternalLlmClient(
                registryMock.Object,
                circuitBreaker ?? cbMock.Object,
                budgetTracker ?? budgetMock.Object,
                httpFactory.Object,
                config,
                NullLogger<ExternalLlmClient>.Instance);
        }

        // ============================================================
        // Тесты
        // ============================================================

        [Fact]
        public async Task CompleteAsync_Success_ReturnsResponse()
        {
            HttpRequestMessage captured = null;

            var client = Create((req, ct) =>
            {
                captured = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildSuccessResponse(), Encoding.UTF8, "application/json")
                });
            });

            var response = await client.CompleteAsync(UserId, new ExternalLlmRequest
            {
                Prompt = "Hello, DeepSeek"
            });

            Assert.Equal(ProviderName, response.Provider);
            Assert.Equal("Hello from DeepSeek", response.Content);
            Assert.Equal(245, response.PromptTokens);
            Assert.Equal(512, response.CompletionTokens);
            // (245 / 1000) * 0.00014 + (512 / 1000) * 0.00028 = 0.00017766
            Assert.Equal(0.00017766m, response.CostUsd);

            Assert.NotNull(captured);
            Assert.Equal(HttpMethod.Post, captured.Method);
            Assert.Equal("https://api.deepseek.com/v1/chat/completions", captured.RequestUri.ToString());
            Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
            Assert.Equal("sk-test", captured.Headers.Authorization?.Parameter);
        }

        [Fact]
        public async Task CompleteAsync_EmptyPrompt_Throws()
        {
            var client = Create((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "" }));
        }

        [Fact]
        public async Task CompleteAsync_CircuitOpen_Throws()
        {
            var client = Create(
                (req, ct) => throw new InvalidOperationException("не должен вызываться"),
                circuitOpen: true);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" }));

            Assert.Contains("временно недоступен", ex.Message);
        }

        [Fact]
        public async Task CompleteAsync_BudgetExceeded_Throws()
        {
            var client = Create(
                (req, ct) => throw new InvalidOperationException("не должен вызываться"),
                canSpend: false);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" }));

            Assert.Contains("бюджет", ex.Message);
        }

        [Fact]
        public async Task CompleteAsync_500_ThrowsAfterRetry()
        {
            var attemptCount = 0;

            var client = Create((req, ct) =>
            {
                attemptCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"server\"}", Encoding.UTF8, "application/json")
                });
            });

            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" }));

            // 2 попытки (1 + 1 retry).
            Assert.Equal(2, attemptCount);
        }

        [Fact]
        public async Task CompleteAsync_400_NoRetry()
        {
            var attemptCount = 0;

            var client = Create((req, ct) =>
            {
                attemptCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"bad model\"}", Encoding.UTF8, "application/json")
                });
            });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" }));

            // 4xx (не 429) — без retry.
            Assert.Equal(1, attemptCount);
        }

        [Fact]
        public async Task CompleteAsync_RecordsUsageInBudgetTracker()
        {
            var budgetMock = new Mock<IExternalLlmBudgetTracker>();
            budgetMock.Setup(b => b.CanSpend(It.IsAny<int>())).Returns(true);

            var client = Create(
                (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildSuccessResponse(promptTokens: 100, completionTokens: 200),
                        Encoding.UTF8, "application/json")
                }),
                budgetTracker: budgetMock.Object);

            await client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" });

            budgetMock.Verify(b => b.RecordUsage(
                UserId, 100, 200, It.IsAny<decimal>()), Times.Once);
        }

        [Fact]
        public async Task CompleteAsync_RecordsSuccessInCircuitBreaker()
        {
            var cbMock = new Mock<IExternalLlmCircuitBreaker>();
            cbMock.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);

            var client = Create(
                (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildSuccessResponse(), Encoding.UTF8, "application/json")
                }),
                circuitBreaker: cbMock.Object);

            await client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" });

            cbMock.Verify(c => c.RecordSuccess(ProviderName), Times.Once);
            cbMock.Verify(c => c.RecordFailure(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CompleteAsync_RecordsFailureInCircuitBreaker()
        {
            var cbMock = new Mock<IExternalLlmCircuitBreaker>();
            cbMock.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);

            var client = Create(
                (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                }),
                circuitBreaker: cbMock.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" }));

            cbMock.Verify(c => c.RecordFailure(ProviderName, It.IsAny<string>()), Times.Once);
            cbMock.Verify(c => c.RecordSuccess(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CompleteAsync_ApiKeyNotResolved_Throws()
        {
            var client = Create(
                (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
                apiKey: "");   // пустой ключ в конфиге

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" }));

            Assert.Contains("API-ключ", ex.Message);
        }

        [Fact]
        public async Task CompleteAsync_NoApiKeySecretName_SkipsAuthHeader()
        {
            // Ollama: без ключа.
            var provider = DefaultProviderOptions();
            provider.ApiKeySecretName = null;

            HttpRequestMessage captured = null;

            var client = Create(
                (req, ct) =>
                {
                    captured = req;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(BuildSuccessResponse(), Encoding.UTF8, "application/json")
                    });
                },
                provider: provider,
                apiKey: null);

            await client.CompleteAsync(UserId, new ExternalLlmRequest { Prompt = "test" });

            Assert.NotNull(captured);
            Assert.Null(captured.Headers.Authorization);
        }

        [Fact]
        public async Task TestConnectionAsync_Success_ReturnsTrue()
        {
            var client = Create((req, ct) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
                }));

            var result = await client.TestConnectionAsync(ProviderName);
            Assert.True(result);
        }

        [Fact]
        public async Task TestConnectionAsync_Unauthorized_ReturnsFalse()
        {
            var client = Create((req, ct) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Unauthorized)));

            var result = await client.TestConnectionAsync(ProviderName);
            Assert.False(result);
        }

        [Fact]
        public async Task TestConnectionAsync_UnknownProvider_ReturnsFalse()
        {
            var registryMock = new Mock<IExternalProviderRegistry>();
            registryMock.Setup(r => r.Get(It.IsAny<string>())).Returns((ExternalProviderOptions)null);

            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(new MockHttpMessageHandler((r, c) => throw new Exception())));

            var client = new ExternalLlmClient(
                registryMock.Object,
                Mock.Of<IExternalLlmCircuitBreaker>(),
                Mock.Of<IExternalLlmBudgetTracker>(),
                httpFactory.Object,
                new ConfigurationBuilder().Build(),
                NullLogger<ExternalLlmClient>.Instance);

            var result = await client.TestConnectionAsync("unknown");
            Assert.False(result);
        }

        // ============================================================
        // v1.9.0 (KI-110a) — Anthropic-формат
        // ============================================================

        [Fact]
        public async Task CompleteAsync_AnthropicFormat_UsesXApiKeyHeader()
        {
            HttpRequestMessage captured = null;

            var client = Create(
                (req, ct) =>
                {
                    captured = req;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(BuildAnthropicSuccessResponse(),
                            Encoding.UTF8, "application/json")
                    });
                },
                provider: AnthropicProviderOptions(),
                apiKey: "sk-ant-test",
                providerName: "anthropic");

            await client.CompleteAsync(UserId, new ExternalLlmRequest
            {
                Provider = "anthropic",
                Prompt = "2+2?"
            });

            Assert.NotNull(captured);
            // x-api-key — НЕ Authorization: Bearer.
            Assert.True(captured.Headers.Contains("x-api-key"),
                "Ожидался заголовок x-api-key (не Authorization).");
            Assert.Equal("sk-ant-test", captured.Headers.GetValues("x-api-key").First());
            Assert.Null(captured.Headers.Authorization);
        }

        [Fact]
        public async Task CompleteAsync_AnthropicFormat_UsesAnthropicVersionHeader()
        {
            HttpRequestMessage captured = null;

            var client = Create(
                (req, ct) =>
                {
                    captured = req;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(BuildAnthropicSuccessResponse(),
                            Encoding.UTF8, "application/json")
                    });
                },
                provider: AnthropicProviderOptions(),
                apiKey: "sk-ant-test",
                providerName: "anthropic");

            await client.CompleteAsync(UserId, new ExternalLlmRequest
            {
                Provider = "anthropic",
                Prompt = "hi"
            });

            Assert.NotNull(captured);
            Assert.True(captured.Headers.Contains("anthropic-version"));
            Assert.Equal("2023-06-01", captured.Headers.GetValues("anthropic-version").First());
        }

        [Fact]
        public async Task CompleteAsync_AnthropicFormat_UsesMessagesEndpoint()
        {
            HttpRequestMessage captured = null;

            var client = Create(
                (req, ct) =>
                {
                    captured = req;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(BuildAnthropicSuccessResponse(),
                            Encoding.UTF8, "application/json")
                    });
                },
                provider: AnthropicProviderOptions(),
                apiKey: "sk-ant-test",
                providerName: "anthropic");

            await client.CompleteAsync(UserId, new ExternalLlmRequest
            {
                Provider = "anthropic",
                Prompt = "hi"
            });

            Assert.NotNull(captured);
            Assert.Equal("https://api.anthropic.com/v1/messages", captured.RequestUri.ToString());
        }

        [Fact]
        public async Task CompleteAsync_AnthropicFormat_ParsesContentArray()
        {
            var client = Create(
                (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        BuildAnthropicSuccessResponse(text: "Четыре", inputTokens: 20, outputTokens: 3),
                        Encoding.UTF8, "application/json")
                }),
                provider: AnthropicProviderOptions(),
                apiKey: "sk-ant-test",
                providerName: "anthropic");

            var response = await client.CompleteAsync(UserId, new ExternalLlmRequest
            {
                Provider = "anthropic",
                Prompt = "2+2?"
            });

            Assert.Equal("anthropic", response.Provider);
            Assert.Equal("Четыре", response.Content);
            Assert.Equal(20, response.PromptTokens);
            Assert.Equal(3, response.CompletionTokens);
            // (20 / 1000) * 0.001 + (3 / 1000) * 0.005 = 0.00002 + 0.000015 = 0.000035
            Assert.Equal(0.000035m, response.CostUsd);
        }

        [Fact]
        public async Task CompleteAsync_AnthropicFormat_MultipleTextBlocks_JoinedWithNewline()
        {
            // content[] содержит несколько блоков type=text.
            var multiBlockResponse = new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = "Первая" },
                    new JObject { ["type"] = "text", ["text"] = "Вторая" }
                },
                ["usage"] = new JObject
                {
                    ["input_tokens"] = 10,
                    ["output_tokens"] = 5
                }
            };

            var client = Create(
                (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(multiBlockResponse.ToString(),
                        Encoding.UTF8, "application/json")
                }),
                provider: AnthropicProviderOptions(),
                apiKey: "sk-ant-test",
                providerName: "anthropic");

            var response = await client.CompleteAsync(UserId, new ExternalLlmRequest
            {
                Provider = "anthropic",
                Prompt = "hi"
            });

            Assert.Equal("Первая\nВторая", response.Content);
        }

        [Fact]
        public async Task CompleteAsync_AnthropicFormat_400_NoRetry()
        {
            var attemptCount = 0;

            var client = Create(
                (req, ct) =>
                {
                    attemptCount++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(
                            "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"bad model\"}}",
                            Encoding.UTF8, "application/json")
                    });
                },
                provider: AnthropicProviderOptions(),
                apiKey: "sk-ant-test",
                providerName: "anthropic");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest
                {
                    Provider = "anthropic",
                    Prompt = "hi"
                }));

            // 4xx (не 429) — без retry.
            Assert.Equal(1, attemptCount);
        }

        // ============================================================
        // v1.9.0 (KI-110a) — Gemini-заглушка + unknown format
        // ============================================================

        [Fact]
        public async Task CompleteAsync_GeminiFormat_ThrowsNotSupported()
        {
            var geminiProvider = AnthropicProviderOptions();
            geminiProvider.Format = ProviderFormat.Gemini;
            geminiProvider.BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
            geminiProvider.Model = "gemini-2.0-flash";

            var client = Create(
                (req, ct) => throw new InvalidOperationException("HTTP-запрос не должен выполняться"),
                provider: geminiProvider,
                apiKey: "AIza-test",
                providerName: "gemini");

            var ex = await Assert.ThrowsAsync<NotSupportedException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest
                {
                    Provider = "gemini",
                    Prompt = "hi"
                }));

            Assert.Contains("Gemini", ex.Message);
            Assert.Contains("KI-110b", ex.Message);
        }

        [Fact]
        public async Task CompleteAsync_UnknownFormat_ThrowsInvalidOperation()
        {
            var unknownProvider = AnthropicProviderOptions();
            // Не задаём ни один из известных форматов.
            unknownProvider.Format = (ProviderFormat)999;

            var client = Create(
                (req, ct) => throw new InvalidOperationException("HTTP-запрос не должен выполняться"),
                provider: unknownProvider,
                apiKey: "sk-test",
                providerName: "unknown");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(UserId, new ExternalLlmRequest
                {
                    Provider = "unknown",
                    Prompt = "hi"
                }));

            Assert.Contains("999", ex.Message);
        }
    }
}