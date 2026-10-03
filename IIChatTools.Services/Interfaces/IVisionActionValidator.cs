using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Валидатор действий Vision Agent. Проверяет:
    /// существование target в ui_elements, координаты в пределах вьюпорта,
    /// блокировку опасных клавиш (F12, Alt+Tab, Ctrl+Alt+Del, ...),
    /// ограничения длины текста и delta прокрутки.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 6.2.
    /// </remarks>
    public interface IVisionActionValidator
    {
        /// <summary>
        /// Проверяет действие на соответствие политике безопасности.
        /// </summary>
        /// <param name="action">Действие от Planner LLM.</param>
        /// <param name="screen">
        /// Описание текущего экрана. Используется для проверки существования
        /// <c>target</c> в <c>ui_elements[]</c> и границ координат.
        /// </param>
        /// <returns>
        /// <c>Success = true</c> — действие разрешено (возможно, с
        /// санитизацией — см. <c>SanitizedAction</c>).
        /// <c>Success = false</c> — действие отклонено, причина в <c>Error</c>.
        /// </returns>
        VisionActionResult Validate(VisionActionDto action, ScreenDescriptionDto screen);
    }
}