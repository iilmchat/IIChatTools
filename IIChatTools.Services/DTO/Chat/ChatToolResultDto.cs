namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Результат выполнения инструмента (payload SSE-события <c>tool_result</c>).
    /// </summary>
    public class ChatToolResultDto
    {
        /// <summary>
        /// Идентификатор вызова (совпадает с <see cref="ChatToolCallDto.Id"/>).
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Имя вызванного инструмента.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Признак успешного выполнения.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Полезные данные (соответствует <c>ToolResult.Data</c>). Может быть null.
        /// </summary>
        public object Content { get; set; }

        /// <summary>
        /// Сообщение (соответствует <c>ToolResult.Message</c>). Может быть null.
        /// </summary>
        public string Message { get; set; }
    }
}