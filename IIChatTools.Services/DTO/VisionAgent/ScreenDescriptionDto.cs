using System.Collections.Generic;

namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Описание текущего состояния экрана, возвращённое Vision LLM.
    /// На вход Planner LLM — вместе с задачей и историей действий.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 4.4 (JSON-схема).
    /// </remarks>
    public class ScreenDescriptionDto
    {
        /// <summary>
        /// Свободное текстовое описание экрана от VL-модели.
        /// Пример: «Страница поиска РЖД. Поля: Откуда, Куда, Дата.
        /// Кнопка «Найти» неактивна.»
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Список распознанных UI-элементов (с координатами).
        /// Planner LLM выбирает <c>target</c> из этих ID.
        /// </summary>
        public List<UiElementDto> UiElements { get; set; } = new List<UiElementDto>();
    }
}