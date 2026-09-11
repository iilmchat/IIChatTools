namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Запись белого списка инструментов, не требующих подтверждения.
    /// </summary>
    public class WhitelistEntryDto
    {
        /// <summary>
        /// Имя инструмента.
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Описание инструмента (для UI).
        /// </summary>
        public string Description { get; set; }
    }
}