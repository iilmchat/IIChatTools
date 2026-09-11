namespace IIChatTools.Services.DTO.SubAgent
{
    /// <summary>
    /// Один шаг выполнения задачи суб-агентом (для сохранения снимка в БД).
    /// </summary>
    public class SubAgentStep
    {
        /// <summary>
        /// Номер шага (1-индексация).
        /// </summary>
        public int Step { get; set; }

        /// <summary>
        /// Тип шага: "assistant_text", "tool_call", "tool_result", "system".
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Имя инструмента (для tool_call/tool_result).
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Содержимое шага (текст или JSON).
        /// </summary>
        public string Content { get; set; }
    }
}