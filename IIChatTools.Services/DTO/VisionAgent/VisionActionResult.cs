namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Результат выполнения одного действия (после валидатора + backend).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 6.2.
    /// </remarks>
    public class VisionActionResult
    {
        /// <summary>Успешно ли действие.</summary>
        public bool Success { get; set; }

        /// <summary>
        /// Ошибка (заполнена, если <see cref="Success"/> = false).
        /// Пример: «Клавиша F12 запрещена», «Координата вне вьюпорта».
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Санитизированное действие (например, если <c>deltaY</c> был закламплен
        /// или <c>text</c> обрезан). Может быть <c>null</c>, если действие
        /// не изменилось.
        /// </summary>
        public VisionActionDto SanitizedAction { get; set; }
    }
}