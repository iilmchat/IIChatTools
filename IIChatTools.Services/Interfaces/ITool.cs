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
        /// <para>
        /// Значение используется как fallback, если <see cref="RequiresApprovalForCall"/>
        /// не переопределён наследником.
        /// </para>
        /// </summary>
        bool RequiresApprovalByDefault { get; }

        /// <summary>
        /// Требует ли конкретный вызов инструмента подтверждения пользователя
        /// (v1.7.0, KI-101).
        /// <para>
        /// Default-реализация возвращает <see cref="RequiresApprovalByDefault"/> —
        /// существующие 46 инструментов продолжают работать без изменений.
        /// Инструменты с per-action approval (например, <c>database_agent</c>:
        /// метаданные — без approval, execute_query — с approval) переопределяют
        /// этот метод.
        /// </para>
        /// <para>
        /// Вызывается <c>ChatStreamService</c> <b>до</b> выполнения инструмента,
        /// чтобы решить, показывать ли модалку approval.
        /// </para>
        /// </summary>
        /// <param name="arguments">Аргументы вызова (уже распарсенный JObject)</param>
        /// <returns>true, если вызов требует подтверждения</returns>
        bool RequiresApprovalForCall(Newtonsoft.Json.Linq.JObject arguments)
            => RequiresApprovalByDefault;

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