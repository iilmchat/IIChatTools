namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Запрос на редактирование user-сообщения (Фаза 2.2.6).
    /// Используется endpoint'ом <c>POST /api/chat/messages/{id}/edit</c>.
    /// </summary>
    public class EditUserMessageRequest
    {
        /// <summary>
        /// Новое содержимое сообщения. Пробелы обрезаются. Пустое значение отклоняется.
        /// </summary>
        public string Content { get; set; }
    }
}