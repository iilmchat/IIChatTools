using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Клиент Planner LLM — модели, которая решает следующее действие
    /// на основе задачи, истории и описания экрана.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 3.2, § 4.5.
    /// </remarks>
    public interface IPlannerLlmClient
    {
        /// <summary>
        /// Определяет следующее действие для достижения <paramref name="task"/>.
        /// </summary>
        /// <param name="task">Задача пользователя естественным языком.</param>
        /// <param name="history">
        /// История выполненных шагов (ограничивается
        /// <c>PlannerLlmOptions.MaxHistorySteps</c>).
        /// </param>
        /// <param name="screen">Описание текущего экрана от Vision LLM.</param>
        /// <param name="plan">
        /// Текущий список подзадач (может быть пустым или перепланирован LLM).
        /// </param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// Следующее действие. <c>action = "done"</c> — задача выполнена;
        /// <c>action = "fail"</c> — не удалось.
        /// </returns>
        Task<VisionActionDto> PlanNextAsync(
            string task,
            IReadOnlyList<VisionStepDto> history,
            ScreenDescriptionDto screen,
            IReadOnlyList<string> plan,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Готов ли клиент к работе.
        /// </summary>
        bool IsReady { get; }
    }
}