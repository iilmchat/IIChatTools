using Newtonsoft.Json.Linq;

namespace IIChatTools.API.DTO
{
    /// <summary>
    /// Тело запроса на выполнение инструмента.
    /// </summary>
    public class ExecuteToolRequest
    {
        /// <summary>
        /// Имя инструмента.
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Аргументы вызова (объект JSON).
        /// </summary>
        public JObject Arguments { get; set; }

        /// <summary>
        /// Идентификатор ранее подтверждённого действия (если требуется подтверждение).
        /// </summary>
        public int? ApprovalId { get; set; }
    }
}