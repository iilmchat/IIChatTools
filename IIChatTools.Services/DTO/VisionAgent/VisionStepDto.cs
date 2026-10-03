namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Один шаг выполнения задачи Vision Agent: действие + результат + метаданные.
    /// Собирается в массив <c>steps[]</c> в <c>VisionTaskResultDto</c>.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 4.1.
    /// </remarks>
    public class VisionStepDto
    {
        /// <summary>
        /// Порядковый номер шага (1-based).
        /// </summary>
        public int StepIndex { get; set; }

        /// <summary>
        /// Тип действия (<c>click</c>, <c>type</c>, <c>press_key</c>, ...).
        /// Дублирует <c>VisionActionDto.Action</c> для компактности UI.
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// ID целевого UI-элемента (если был выбран по <c>target</c>).
        /// Может быть <c>null</c> для действий по координатам.
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// Текст, введённый на шаге (для <c>action = "type"</c>).
        /// Может быть <c>null</c> для других действий.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Пояснение Planner LLM — почему выбрано это действие.
        /// Используется в UI (детальный рендер дебатов) и audit.
        /// </summary>
        public string LlmReason { get; set; }

        /// <summary>
        /// Длительность шага (от «начало действия» до «страница стабильна»), мс.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Ошибка выполнения шага (если была). <c>null</c> при успехе.
        /// </summary>
        public string Error { get; set; }
    }
}