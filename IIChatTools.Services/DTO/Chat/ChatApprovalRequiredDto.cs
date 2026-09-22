using System;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Payload SSE-события <c>tool_approval_required</c> — клиенту нужно
    /// показать модалку подтверждения для вызова инструмента.
    /// </summary>
    public class ChatApprovalRequiredDto
    {
        /// <summary>
        /// Идентификатор вызова инструмента (от LM Studio).
        /// Используется для approve/reject.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Имя инструмента, требующего подтверждения.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Аргументы вызова (для отображения в модалке).
        /// </summary>
        public JObject Arguments { get; set; }

        /// <summary>
        /// Момент времени, когда подтверждение истечёт (UTC).
        /// Клиент может показать countdown.
        /// </summary>
        public DateTime ExpiresAt { get; set; }
    }
}