using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Ответ LM Studio на запрос chat completion.
    /// </summary>
    public class ChatCompletionResponse
    {
        /// <summary>
        /// Содержимое ответа (может быть null при tool_calls).
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Список вызовов инструментов (может быть пустым).
        /// </summary>
        public JArray ToolCalls { get; set; }

        /// <summary>
        /// Причина завершения: "stop", "tool_calls", "length".
        /// </summary>
        public string FinishReason { get; set; }
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
        /// <param name="tools">Список инструментов (JArray, формат OpenAI Function Calling)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Ответ модели</returns>
        Task<ChatCompletionResponse> CompleteAsync(
            JArray messages,
            JArray tools,
            CancellationToken cancellationToken);
    }
}