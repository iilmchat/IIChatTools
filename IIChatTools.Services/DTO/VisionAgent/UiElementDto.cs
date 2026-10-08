namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Один UI-элемент, распознанный Vision LLM на скриншоте.
    /// Часть <c>ScreenDescriptionDto</c>.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 4.4 (JSON-схема).
    /// </remarks>
    public class UiElementDto
    {
        /// <summary>
        /// Уникальный ID элемента в рамках одного описания экрана.
        /// Пример: <c>from_input</c>, <c>search_btn</c>.
        /// Используется в <c>VisionActionDto.Target</c>.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Тип элемента: <c>text_input</c> / <c>button</c> / <c>link</c> /
        /// <c>checkbox</c> / <c>radio</c> / <c>dropdown</c> / <c>image</c> /
        /// <c>text</c> / <c>other</c>.
        /// </summary>
        public string Type { get; set; }

        /// <summary>Видимая надпись / placeholder / label.</summary>
        public string Label { get; set; }

        /// <summary>Текущее значение (для text_input, dropdown и т.п.). Может быть null.</summary>
        public string Value { get; set; }

        /// <summary>
        /// Активен ли элемент (для кнопок — не disabled).
        /// <c>null</c> = неизвестно.
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>
        /// Источник данных об этом элементе (KI-137):
        /// <list type="bullet">
        ///   <item><c>"vl"</c> (default) — только Vision LLM;</item>
        ///   <item><c>"ocr"</c> — label добавлен OCR (у VL был пустой label);</item>
        ///   <item><c>"merged"</c> — label обновлён OCR (у VL был короткий label).</item>
        /// </list>
        /// Устанавливается в <c>OcrVlMergeHelper.Merge</c>.
        /// </summary>
        public string Source { get; set; } = "vl";

        /// <summary>Прямоугольник элемента на скриншоте (для валидации координат).</summary>
        public UiElementBoundsDto Bounds { get; set; }

        /// <summary>Центр элемента — то, куда кликать.</summary>
        public UiElementCenterDto Center { get; set; }
    }

    /// <summary>
    /// Прямоугольник UI-элемента на скриншоте.
    /// </summary>
    public class UiElementBoundsDto
    {
        /// <summary>Левая граница, px.</summary>
        public int X { get; set; }

        /// <summary>Верхняя граница, px.</summary>
        public int Y { get; set; }

        /// <summary>Ширина, px.</summary>
        public int W { get; set; }

        /// <summary>Высота, px.</summary>
        public int H { get; set; }
    }

    /// <summary>
    /// Координаты центра UI-элемента.
    /// </summary>
    public class UiElementCenterDto
    {
        /// <summary>X-координата центра, px.</summary>
        public int X { get; set; }

        /// <summary>Y-координата центра, px.</summary>
        public int Y { get; set; }
    }
}