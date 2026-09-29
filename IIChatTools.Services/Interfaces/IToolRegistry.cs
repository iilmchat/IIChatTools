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
        /// Возвращает сам экземпляр инструмента по имени (v1.7.0, KI-101).
        /// <para>
        /// Нужен, чтобы вызвать <see cref="ITool.RequiresApprovalForCall"/>
        /// из <c>ChatStreamService</c> до выполнения.
        /// </para>
        /// </summary>
        /// <param name="name">Имя инструмента</param>
        /// <returns>Инструмент или <c>null</c>, если не найден</returns>
        ITool GetTool(string name);

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