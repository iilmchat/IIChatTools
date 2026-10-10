using System;
using System.Collections.Generic;
using System.Threading;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ChatLlm
{
    /// <summary>
    /// Chat LLM через внешний OpenAI-совместимый провайдер
    /// (RouterAI, YandexGPT, DeepSeek, OpenAI, Groq и т.п.)
    /// — v1.13.10 (KI-224, Фаза C).
    ///
    /// <para>
    /// Делегирует в <see cref="IExternalLlmClient.ChatStreamAsync"/>.
    /// Провайдер берётся из <c>ChatLlm:Provider</c> в appsettings
    /// (формат <c>"external:&lt;name&gt;"</c>, например <c>"external:routerai"</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Требования:</b>
    /// <list type="bullet">
    ///   <item><c>ExternalLlm:Enabled = true</c>;</item>
    ///   <item>провайдер настроен в <c>ExternalLlm:Providers</c>;</item>
    ///   <item>API-ключ в User Secrets;</item>
    ///   <item>провайдер имеет <c>Format = "OpenAI"</c> (для SSE; Anthropic / Gemini — v1.14).</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class ExternalChatLlmClient : IChatLlmClient
    {
        private readonly IExternalLlmClient _externalClient;
        private readonly string _providerName;
        private readonly ILogger<ExternalChatLlmClient> _logger;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="externalClient">Клиент внешних LLM (Singleton).</param>
        /// <param name="configuration">Конфигурация (для <c>ChatLlm:Provider</c>).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Если <c>ChatLlm:Provider</c> не в формате <c>"external:&lt;name&gt;"</c>.
        /// </exception>
        public ExternalChatLlmClient(
            IExternalLlmClient externalClient,
            IConfiguration configuration,
            ILogger<ExternalChatLlmClient> logger)
        {
            _externalClient = externalClient ?? throw new ArgumentNullException(nameof(externalClient));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var raw = configuration["ChatLlm:Provider"] ?? "lmstudio";
            const string prefix = "external:";

            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"ExternalChatLlmClient: ChatLlm:Provider='{raw}' не в формате " +
                    $"'external:<provider>'. Пример: 'external:routerai'.");
            }

            _providerName = raw.Substring(prefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(_providerName))
            {
                throw new InvalidOperationException(
                    "ExternalChatLlmClient: ChatLlm:Provider='external:' " +
                    "не содержит имя провайдера.");
            }

            _logger.LogInformation(
                "ExternalChatLlmClient: инициализирован для провайдера '{Provider}'",
                _providerName);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Всегда <c>true</c>: реальная доступность проверяется при первом
        /// <see cref="ChatStreamAsync"/> (Timeout / HttpRequestException).
        /// </remarks>
        public bool IsReady => true;

        /// <inheritdoc />
        public IAsyncEnumerable<ChatCompletionChunk> ChatStreamAsync(
            int userId,
            JArray messages,
            JArray tools,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug(
                "ExternalChatLlmClient: ChatStreamAsync → provider={Provider}, userId={UserId}, " +
                "messages={MsgCount}, tools={ToolCount}",
                _providerName, userId, messages?.Count ?? 0, tools?.Count ?? 0);

            return _externalClient.ChatStreamAsync(
                userId: userId,
                providerName: _providerName,
                messages: messages,
                tools: tools,
                temperature: null,
                maxTokens: null,
                ct: cancellationToken);
        }
    }
}