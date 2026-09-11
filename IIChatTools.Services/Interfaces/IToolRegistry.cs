using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Реестр инструментов: обнаружение, описание, выполнение.
    /// </summary>
    public interface IToolRegistry
    {
        /// <summary>
        /// Возвращает описания всех зарегистрированных инструментов.
        /// </summary>
        /// <returns>Список описаний</returns>
        IReadOnlyList<ToolDescriptor> GetAllDescriptors();

        /// <summary>
        /// Возвращает описание инструмента по имени или null.
        /// </summary>
        /// <param name="name">Имя инструмента</param>
        /// <returns>Описание или null</returns>
        ToolDescriptor GetDescriptor(string name);

        /// <summary>
        /// Выполняет инструмент по имени.
        /// </summary>
        /// <param name="toolName">Имя инструмента</param>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="arguments">Аргументы вызова</param>
        /// <returns>Результат выполнения</returns>
        Task<ToolResult> ExecuteAsync(string toolName, ToolExecutionContext context, JObject arguments);
    }
}