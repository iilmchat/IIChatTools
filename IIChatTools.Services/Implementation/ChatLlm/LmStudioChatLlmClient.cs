using System;
using System.Collections.Generic;
using System.Threading;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ChatLlm
{
    /// <summary>
    /// Chat LLM на базе LM Studio — тонкая обёртка над
    /// <see cref="ILmStudioClient"/>. Делегирует <c>ChatStreamAsync</c>
    /// без изменений (v1.13.10, KI-224, Фаза A).
    ///
    /// <para>
    /// Регистрируется как реализация <see cref="IChatLlmClient"/>
    /// при <c>ChatLlm:Provider = "lmstudio"</c> (по умолчанию).
    /// </para>
    /// </summary>
    public sealed class LmStudioChatLlmClient : IChatLlmClient
    {
        private readonly ILmStudioClient _inner;

        /// <summary>
        /// Создаёт обёртку.
        /// </summary>
        /// <param name="inner">Внутренний клиент LM Studio (Singleton).</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="inner"/> равен <c>null</c>.</exception>
        public LmStudioChatLlmClient(ILmStudioClient inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc />
        /// <remarks>
        /// LM Studio — локальный провайдер. Всегда «готов попробовать»;
        /// реальная доступность проверяется в <c>ChatStreamAsync</c>
        /// (TimeoutException / HttpRequestException).
        /// </remarks>
        public bool IsReady => true;

        /// <inheritdoc />
        public IAsyncEnumerable<ChatCompletionChunk> ChatStreamAsync(
            int userId,
            JArray messages,
            JArray tools,
            CancellationToken cancellationToken)
        {
            // userId игнорируется: LM Studio — локальный провайдер, без бюджета.
            _ = userId;
            return _inner.ChatStreamAsync(messages, tools, cancellationToken);
        }
    }
}