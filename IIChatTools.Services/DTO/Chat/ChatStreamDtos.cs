using System;

namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Запрос на стриминг ответа LLM.
    /// </summary>
    public class ChatStreamRequest
    {
        /// <summary>Идентификатор чата.</summary>
        public int ChatId { get; set; }

        /// <summary>Текст сообщения пользователя.</summary>
        public string Message { get; set; }

        /// <summary>
        /// Использовать ли tool calling.
        /// В v1.3 Фазе 1.5 игнорируется (всегда false) — включим в Фазе 1.6.
        /// </summary>
        public bool UseTools { get; set; }

        /// <summary>
        /// Режим «Regenerate» (Фаза 2.1.2): удалить последний assistant-exchange
        /// (assistant + tool) и заново сгенерировать ответ от последнего user-сообщения.
        /// При <c>true</c> поле <see cref="Message"/> игнорируется, новое user-сообщение не сохраняется.
        /// </summary>
        public bool Regenerate { get; set; }
    }

    /// <summary>
    /// Одно SSE-событие стрима.
    /// Формат сериализации: <c>event: {type}\ndata: {payload}\n\n</c>.
    /// </summary>
    public class ChatStreamEvent
    {
        /// <summary>
        /// Тип события:
        /// <list type="bullet">
        ///   <item><c>start</c> — стрим начался, содержит ID user message</item>
        ///   <item><c>delta</c> — приращение текста от LLM</item>
        ///   <item><c>done</c> — стрим завершён, содержит ID assistant message</item>
        ///   <item><c>error</c> — ошибка, содержит message</item>
        /// </list>
        /// </summary>
        public string Type { get; set; }

        /// <summary>Данные события (сериализуются в JSON).</summary>
        public object Data { get; set; }

        /// <summary>
        /// Создаёт событие <c>start</c>.
        /// </summary>
        /// <param name="userMessageId">ID сохранённого user message</param>
        /// <param name="chatId">ID чата</param>
        /// <param name="userTokens">
        /// KI-084b: количество токенов user-сообщения (tiktoken).
        /// <c>null</c> — не подсчитано. Позволяет UI показать токены сразу,
        /// не дожидаясь F5 + <c>GET /api/chats/{id}</c>.
        /// </param>
        /// <returns>Событие стрима</returns>
        public static ChatStreamEvent Start(int userMessageId, int chatId, int? userTokens = null)
            => new ChatStreamEvent
            {
                Type = "start",
                Data = new { userMessageId, chatId, userTokens }
            };

        /// <summary>
        /// Создаёт событие <c>delta</c>.
        /// </summary>
        /// <param name="text">Приращение текста</param>
        /// <returns>Событие стрима</returns>
        public static ChatStreamEvent Delta(string text)
            => new ChatStreamEvent
            {
                Type = "delta",
                Data = new { text }
            };

        /// <summary>
        /// Создаёт событие <c>done</c>.
        /// </summary>
        /// <param name="assistantMessageId">ID сохранённого assistant message</param>
        /// <param name="tokensIn">Токенов prompt (опционально)</param>
        /// <param name="tokensOut">Токенов completion (опционально)</param>
        /// <param name="durationMs">
        /// KI-084a: общая длительность генерации (мс). <c>null</c> — не измерялась.
        /// </param>
        /// <param name="firstTokenMs">
        /// KI-084a: время до первого delta (мс). <c>null</c> — delta не было.
        /// </param>
        /// <param name="finishReason">
        /// KI-084a: причина завершения (<c>stop</c>, <c>length</c>, <c>tool_calls</c>).
        /// </param>
        /// <returns>Событие стрима</returns>
        public static ChatStreamEvent Done(
            int assistantMessageId,
            int? tokensIn = null,
            int? tokensOut = null,
            long? durationMs = null,
            long? firstTokenMs = null,
            string finishReason = null)
            => new ChatStreamEvent
            {
                Type = "done",
                Data = new { assistantMessageId, tokensIn, tokensOut, durationMs, firstTokenMs, finishReason }
            };

        /// <summary>
        /// Создаёт событие <c>error</c>.
        /// </summary>
        /// <param name="message">Текст ошибки</param>
        /// <returns>Событие стрима</returns>
        public static ChatStreamEvent Error(string message)
            => new ChatStreamEvent
            {
                Type = "error",
                Data = new { message }
            };


        /// <summary>
        /// Создаёт событие <c>tool_call</c> — LLM вызывает инструмент.
        /// </summary>
        /// <param name="dto">Информация о вызове</param>
        /// <returns>Событие стрима</returns>
        public static ChatStreamEvent ToolCall(ChatToolCallDto dto)
            => new ChatStreamEvent
            {
                Type = "tool_call",
                Data = dto
            };

        /// <summary>
        /// Создаёт событие <c>tool_result</c> — результат выполнения инструмента.
        /// </summary>
        /// <param name="dto">Результат вызова</param>
        /// <returns>Событие стрима</returns>
        public static ChatStreamEvent ToolResult(ChatToolResultDto dto)
            => new ChatStreamEvent
            {
                Type = "tool_result",
                Data = dto
            };     


        /// <summary>
        /// Создаёт событие <c>tool_approval_required</c> — инструмент требует подтверждения.
        /// </summary>
        /// <param name="dto">Информация о запросе подтверждения</param>
        /// <returns>Событие стрима</returns>
        public static ChatStreamEvent ToolApprovalRequired(ChatApprovalRequiredDto dto)
            => new ChatStreamEvent
            {
                Type = "tool_approval_required",
                Data = dto
            };

        /// <summary>
        /// Создаёт событие <c>tool_approval_resolved</c> — решение принято.
        /// </summary>
        /// <param name="dto">Решение пользователя</param>
        /// <returns>Событие стрима</returns>
        public static ChatStreamEvent ToolApprovalResolved(ChatApprovalResolvedDto dto)
            => new ChatStreamEvent
            {
                Type = "tool_approval_resolved",
                Data = dto
            };                   
    }
}