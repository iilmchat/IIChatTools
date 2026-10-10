using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Абстракция Chat LLM — провайдер, который генерирует SSE-стрим ответа
    /// на основе истории сообщений и tools (v1.13.10, KI-224).
    ///
    /// <para>
    /// Позволяет использовать любой OpenAI-совместимый провайдер (LM Studio,
    /// RouterAI, YandexGPT, DeepSeek) как основной Chat LLM. Переключение —
    /// через <c>ChatLlm:Provider</c> в appsettings.
    /// </para>
    ///
    /// <para>
    /// <b>Отличия от <see cref="ILmStudioClient"/>:</b> этот интерфейс
    /// минимальный (только SSE-стрим chat completion), не привязан
    /// к LM Studio. Эмбеддинги и /v1/models остаются в
    /// <see cref="ILmStudioClient"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Реализации:</b>
    /// <list type="bullet">
    ///   <item><c>LmStudioChatLlmClient</c> — делегирует в <see cref="ILmStudioClient"/>;</item>
    ///   <item><c>ExternalChatLlmClient</c> — через <see cref="IExternalLlmClient"/>
    ///   (Фаза C KI-224, требует SSE-стриминг в ExternalLlmClient).</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IChatLlmClient
    {
        /// <summary>
        /// Готов ли провайдер к работе (например, LM Studio доступен).
        /// <c>false</c> — не блокирует вызов <see cref="ChatStreamAsync"/>
        /// (внешние провайдеры могут стать доступны через секунду), но
        /// используется вызывающим кодом для graceful-fallback.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Стримит chat completion через SSE.
        /// </summary>
        /// <param name="userId">Пользователь-инициатор (для budget tracker).</param>
        /// <param name="messages">История сообщений (JArray, формат OpenAI).</param>
        /// <param name="tools">Список инструментов (JArray, OpenAI Function Calling) или <c>null</c>.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Асинхронный поток чанков.</returns>
        /// <exception cref="System.ArgumentNullException">Если <paramref name="messages"/> равен null.</exception>
        /// <exception cref="System.TimeoutException">Провайдер не ответил за отведённое время.</exception>
        /// <exception cref="System.InvalidOperationException">Провайдер вернул ошибку.</exception>
        /// <remarks>
        /// v1.13.10 (KI-224, Фаза B-fix): добавлен <paramref name="userId"/>
        /// — для ExternalChatLlmClient (per-user budget tracker).
        /// LmStudioChatLlmClient игнорирует (локальный провайдер, без бюджета).
        /// </remarks>
        IAsyncEnumerable<ChatCompletionChunk> ChatStreamAsync(
            int userId,
            JArray messages,
            JArray tools,
            CancellationToken cancellationToken);
    }
}