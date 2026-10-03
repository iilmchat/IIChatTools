using System.Collections.Generic;

namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Действие, которое Planner LLM предлагает выполнить на текущем экране.
    /// Возвращается из <c>IPlannerLlmClient.PlanNextAsync</c>.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 4.3, § 4.5.
    /// </remarks>
    public class VisionActionDto
    {
        /// <summary>
        /// Тип действия:
        /// <c>click</c> / <c>double_click</c> / <c>right_click</c> /
        /// <c>move_mouse</c> / <c>type</c> / <c>press_key</c> / <c>hotkey</c> /
        /// <c>scroll</c> / <c>wait</c> / <c>done</c> / <c>fail</c>.
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// ID целевого элемента из <c>ui_elements[]</c> последнего описания экрана.
        /// Имеет приоритет над <see cref="X"/> / <see cref="Y"/>.
        /// </summary>
        public string Target { get; set; }

        /// <summary>Координата X (fallback, если <see cref="Target"/> не задан).</summary>
        public int? X { get; set; }

        /// <summary>Координата Y (fallback, если <see cref="Target"/> не задан).</summary>
        public int? Y { get; set; }

        /// <summary>Текст для <c>action = "type"</c>.</summary>
        public string Text { get; set; }

        /// <summary>Одиночная клавиша для <c>action = "press_key"</c> (Enter, Tab, Escape, ...).</summary>
        public string Key { get; set; }

        /// <summary>Комбинация клавиш для <c>action = "hotkey"</c> (["Ctrl", "C"]).</summary>
        public List<string> Keys { get; set; }

        /// <summary>Delta прокрутки для <c>action = "scroll"</c> (+вниз / -вверх).</summary>
        public int? DeltaY { get; set; }

        /// <summary>
        /// Пояснение от LLM (для UI и audit) — почему выбрано именно это действие.
        /// </summary>
        public string Reason { get; set; }
    }
}