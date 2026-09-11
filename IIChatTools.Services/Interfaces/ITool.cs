using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Базовый контракт инструмента, доступного LLM.
    /// Каждый инструмент реализуется отдельным классом и регистрируется в DI.
    /// </summary>
    public interface ITool
    {
        /// <summary>
        /// Уникальное имя инструмента (snake_case).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Описание инструмента для LLM.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Требует ли инструмент подтверждения пользователя по умолчанию.
        /// </summary>
        bool RequiresApprovalByDefault { get; }

        /// <summary>
        /// Параметры инструмента.
        /// </summary>
        IReadOnlyList<ToolParameterDescriptor> Parameters { get; }

        /// <summary>
        /// Выполняет инструмент с заданными аргументами.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="arguments">Аргументы вызова</param>
        /// <returns>Результат выполнения</returns>
        Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments);
    }
}