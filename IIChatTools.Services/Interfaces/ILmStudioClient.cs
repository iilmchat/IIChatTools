using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Информация о расходе токенов при вызове модели.
    /// </summary>
    public class ChatCompletionUsage
    {
        /// <summary>
        /// Количество токенов во входных сообщениях (prompt).
        /// </summary>
        public int PromptTokens { get; set; }

        /// <summary>
        /// Количество сгенерированных токенов (включая reasoning).
        /// </summary>
        public int CompletionTokens { get; set; }

        /// <summary>
        /// Общее количество токенов.
        /// </summary>
        public int TotalTokens { get; set; }

        /// <summary>
        /// Из общего числа completion-токенов — сколько ушло на reasoning.
        /// 0 для обычных (не-reasoning) моделей.
        /// </summary>
        public int ReasoningTokens { get; set; }
    }

    /// <summary>
    /// Ответ LM Studio на запрос chat completion.
    /// </summary>
    public class ChatCompletionResponse
    {
        /// <summary>
        /// Финальный ответ модели (может быть null при чистом tool_calls
        /// или пустой строкой, если reasoning-модель не успела закончить).
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Содержимое «размышлений» reasoning-модели (DeepSeek-R1, gemma-reasoning и др.).
        /// Для обычных моделей — null.
        /// </summary>
        public string ReasoningContent { get; set; }

        /// <summary>
        /// Список вызовов инструментов или null, если их нет.
        /// Пустой массив нормализуется в null.
        /// </summary>
        public JArray ToolCalls { get; set; }

        /// <summary>
        /// Причина завершения: "stop", "tool_calls", "length".
        /// </summary>
        public string FinishReason { get; set; }

        /// <summary>
        /// Информация о расходе токенов (может быть null, если LM Studio не вернул).
        /// </summary>
        public ChatCompletionUsage Usage { get; set; }
    }

    /// <summary>
    /// Клиент LM Studio (OpenAI-совместимый API /v1/chat/completions).
    /// </summary>
    public interface ILmStudioClient
    {
        /// <summary>
        /// Отправляет запрос chat completion в LM Studio.
        /// </summary>
        /// <param name="messages">История сообщений (JArray, формат OpenAI)</param>
        /// <param name="tools">Список инструментов (JArray, формат OpenAI Function Calling) или null</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Ответ модели</returns>
        /// <exception cref="System.ArgumentNullException">Если messages равен null</exception>
        /// <exception cref="System.TimeoutException">Если LM Studio не ответил за отведённое время</exception>
        /// <exception cref="System.InvalidOperationException">Если LM Studio вернул ошибку</exception>
        Task<ChatCompletionResponse> CompleteAsync(
            JArray messages,
            JArray tools,
            CancellationToken cancellationToken);

        /// <summary>
        /// Возвращает список идентификаторов моделей, доступных в LM Studio (/v1/models).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список идентификаторов моделей</returns>
        Task<IReadOnlyList<string>> GetModelIdsAsync(CancellationToken cancellationToken);
    }
}