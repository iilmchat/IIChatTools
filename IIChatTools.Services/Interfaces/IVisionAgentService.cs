using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Оркестратор loop'а Vision Agent: screenshot → describe → plan → act → repeat.
    /// Координирует <see cref="IVisionBackend"/>, <see cref="IVisionLlmClient"/>,
    /// <see cref="IPlannerLlmClient"/>, <see cref="IVisionActionValidator"/>.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 3.3.
    /// </remarks>
    public interface IVisionAgentService
    {
        /// <summary>
        /// Выполняет задачу Vision Agent до <c>done</c> / <c>fail</c> /
        /// исчерпания лимитов. Эмитит промежуточные события (SSE, on-screen overlay)
        /// через <c>ToolExecutionContext.EventWriter</c> (реализация — Фаза 6).
        /// </summary>
        /// <param name="request">Параметры задачи (task, url?, maxSteps?, taskId?).</param>
        /// <param name="userId">Владелец задачи (для rate-limit и audit).</param>
        /// <param name="cancellationToken">
        /// Токен отмены. Срабатывает по: ESC на overlay, timeout MaxTaskSeconds,
        /// Stop в чате, отмене HTTP-запроса.
        /// </param>
        /// <returns>Результат с шагами, summary и финальным скриншотом.</returns>
        Task<VisionTaskResultDto> RunTaskAsync(
            VisionTaskRequest request,
            int userId,
            CancellationToken cancellationToken = default);
    }
}