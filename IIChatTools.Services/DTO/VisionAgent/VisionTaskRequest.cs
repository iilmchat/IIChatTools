namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Запрос на выполнение задачи Vision Agent.
    /// Приходит из <c>VisionAgentTool</c> (action = <c>run_task</c>).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 4.3.
    /// </remarks>
    public class VisionTaskRequest
    {
        /// <summary>
        /// Задача пользователя естественным языком.
        /// Пример: «Купи билет РЖД Москва → Петропавловск-Камчатский, купе, нижняя полка».
        /// </summary>
        public string Task { get; set; }

        /// <summary>
        /// Стартовый URL (для browser-режима). Может быть <c>null</c> для desktop-задачи.
        /// Домен проверяется по whitelist из <c>VisionWhitelistOptions</c>.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Override максимального числа шагов для этой задачи.
        /// <c>null</c> → используется <c>VisionAgentOptions.Limits.MaxSteps</c>.
        /// </summary>
        public int? MaxSteps { get; set; }

        /// <summary>
        /// Идентификатор сессии для correlation (используется в логах и audit).
        /// Генерируется в <c>VisionAgentService</c>, если не задан.
        /// </summary>
        public string TaskId { get; set; }
    }
}