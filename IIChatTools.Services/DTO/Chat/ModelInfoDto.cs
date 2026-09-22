namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Информация о модели LM Studio для UI-селектора / настроек чата.
    /// </summary>
    public class ModelInfoDto
    {
        /// <summary>
        /// Идентификатор модели (для API LM Studio, например <c>qwen/qwen3-4b-2507</c>).
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Отображаемое имя модели (в текущей реализации совпадает с <see cref="Id"/>).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Признак модели по умолчанию (значение из <c>LmStudio:Model</c> в конфиге).
        /// В списке всегда ровно одна модель с <c>IsDefault == true</c>.
        /// </summary>
        public bool IsDefault { get; set; }
    }
}