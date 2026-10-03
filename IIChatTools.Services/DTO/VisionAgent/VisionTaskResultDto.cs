using System.Collections.Generic;

namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Результат выполнения задачи Vision Agent — используется в ответе
    /// <c>VisionAgentTool</c> (action = <c>run_task</c>).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 4.1, Приложение B.
    /// </remarks>
    public class VisionTaskResultDto
    {
        /// <summary>Исходная задача пользователя (для UI и audit).</summary>
        public string Task { get; set; }

        /// <summary>Успешно ли завершена задача (достигнут <c>done</c> или maxSteps).</summary>
        public bool Success { get; set; }

        /// <summary>Использованный backend (<c>local-harness</c> / <c>sandbox</c> / <c>remote-vnc</c>).</summary>
        public string Backend { get; set; }

        /// <summary>
        /// Список выполненных шагов (с действиями и пояснениями LLM).
        /// Ограничивается в UI (последние N), но в JSON отдаётся полностью.
        /// </summary>
        public List<VisionStepDto> Steps { get; set; } = new List<VisionStepDto>();

        /// <summary>
        /// Итоговое резюме от Planner LLM (когда <c>action = "done"</c>).
        /// Пример: «Найдены билеты, купе доступно».
        /// </summary>
        public string Summary { get; set; }

        /// <summary>
        /// Ошибка, если задача завершилась неудачей.
        /// Пример: «Исчерпан лимит шагов», «Домен не в whitelist».
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Путь к финальному скриншоту в workspace
        /// (например, <c>workspace/screenshots/vt_8f2a/step-012.png</c>).
        /// <c>null</c>, если privacy-настройки запрещают сохранение.
        /// </summary>
        public string FinalScreenshotPath { get; set; }

        /// <summary>
        /// Общая длительность задачи, мс.
        /// </summary>
        public long TotalDurationMs { get; set; }
    }
}