using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Информация о вызове инструмента (payload SSE-события <c>tool_call</c>).
    /// </summary>
    public class ChatToolCallDto
    {
        /// <summary>
        /// Уникальный идентификатор вызова (от LM Studio, например <c>call_abc123</c>).
        /// Используется для корреляции с <c>tool_result</c>.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Имя вызванного инструмента.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Аргументы вызова (распарсенный JSON).
        /// </summary>
        public JObject Arguments { get; set; }

        /// <summary>
        /// Признак «требуется подтверждение пользователя».
        /// Если <c>true</c>, инструмент не будет выполнен автоматически
        /// (см. Фазу 1.7) — LLM получит <c>ToolResult.Fail(...)</c>.
        /// </summary>
        public bool RequiresApproval { get; set; }
    }
}