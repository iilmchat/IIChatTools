using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SubAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Оркестратор цикла суб-агента.
    /// </summary>
    public interface ISubAgentService
    {
        /// <summary>
        /// Выполняет задачу суб-агентом в изолированном цикле.
        /// </summary>
        /// <param name="context">Контекст выполнения (UserId, Workspace, CancellationToken)</param>
        /// <param name="request">Запрос задачи</param>
        /// <returns>Результат выполнения</returns>
        Task<SubAgentTaskResult> ExecuteTaskAsync(ToolExecutionContext context, SubAgentTaskRequest request);
    }
}