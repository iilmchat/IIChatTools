using System.Collections.Generic;

namespace IIChatTools.Services.DTO
{
    /// <summary>
    /// Описание инструмента для LLM и API-клиентов.
    /// </summary>
    public class ToolDescriptor
    {
        /// <summary>
        /// Уникальное имя инструмента (snake_case).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Описание для LLM.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Флаг «требует подтверждения пользователя по умолчанию».
        /// Может быть переопределён белым списком в настройках.
        /// </summary>
        public bool RequiresApprovalByDefault { get; set; }

        /// <summary>
        /// Список параметров.
        /// </summary>
        public IReadOnlyList<ToolParameterDescriptor> Parameters { get; set; }
    }
}