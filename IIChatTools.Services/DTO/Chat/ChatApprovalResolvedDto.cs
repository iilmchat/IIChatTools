namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Payload SSE-события <c>tool_approval_resolved</c> — пользователь
    /// принял решение (или таймаут). Клиент закрывает модалку.
    /// </summary>
    public class ChatApprovalResolvedDto
    {
        /// <summary>
        /// Идентификатор вызова инструмента.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Решение: <c>approved</c>, <c>rejected</c>, <c>expired</c>.
        /// </summary>
        public string Decision { get; set; }
    }
}