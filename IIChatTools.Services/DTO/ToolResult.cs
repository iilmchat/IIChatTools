namespace IIChatTools.Services.DTO
{
    /// <summary>
    /// Результат выполнения инструмента.
    /// </summary>
    public class ToolResult
    {
        /// <summary>
        /// Признак успешного выполнения.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Полезные данные (объект, сериализуемый в JSON).
        /// </summary>
        public object Data { get; set; }

        /// <summary>
        /// Сообщение для LLM (описание результата или ошибки).
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Создаёт успешный результат.
        /// </summary>
        /// <param name="data">Данные</param>
        /// <param name="message">Сообщение</param>
        /// <returns>Успешный результат</returns>
        public static ToolResult Ok(object data = null, string message = null)
            => new ToolResult { Success = true, Data = data, Message = message };

        /// <summary>
        /// Создаёт результат-ошибку.
        /// </summary>
        /// <param name="message">Сообщение об ошибке</param>
        /// <returns>Результат-ошибка</returns>
        public static ToolResult Fail(string message)
            => new ToolResult { Success = false, Message = message };
    }
}