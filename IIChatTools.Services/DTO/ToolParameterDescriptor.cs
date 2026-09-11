namespace IIChatTools.Services.DTO
{
    /// <summary>
    /// Описание параметра инструмента (для схемы, передаваемой LLM).
    /// </summary>
    public class ToolParameterDescriptor
    {
        /// <summary>
        /// Имя параметра (латиница, snake_case).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Тип параметра: string, integer, boolean, array, object.
        /// </summary>
        public string Type { get; set; } = "string";

        /// <summary>
        /// Описание параметра для LLM.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Обязателен ли параметр.
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// Значение по умолчанию (опционально).
        /// </summary>
        public object Default { get; set; }
    }
}