namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Решение пользователя по запросу подтверждения tool call в чате.
    /// </summary>
    public enum ChatApprovalDecision
    {
        /// <summary>Подтверждено — инструмент можно выполнять.</summary>
        Approved = 1,

        /// <summary>Отклонено — инструмент не выполняется.</summary>
        Rejected = 2,

        /// <summary>Истёк таймаут ожидания (5 минут).</summary>
        Expired = 3
    }
}