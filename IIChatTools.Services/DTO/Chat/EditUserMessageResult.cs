namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Результат редактирования user-сообщения (Фаза 2.2.6).
    /// Возвращается из <c>IChatService.EditUserMessageAsync</c>.
    /// </summary>
    public class EditUserMessageResult
    {
        /// <summary>
        /// Признак успеха. <c>false</c>, если сообщение не найдено,
        /// не принадлежит пользователю или имеет role != "user".
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Текст ошибки (если <see cref="Success"/> == <c>false</c>).
        /// Не содержит деталей владения — обобщённый ответ.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Количество удалённых сообщений после отредактированного
        /// (ассистент + tool + последующие user/assistant).
        /// </summary>
        public int DeletedCount { get; set; }

        /// <summary>
        /// Идентификатор отредактированного сообщения (для корреляции на клиенте).
        /// </summary>
        public int MessageId { get; set; }
    }
}