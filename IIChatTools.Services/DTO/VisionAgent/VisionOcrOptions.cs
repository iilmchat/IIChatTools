namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Настройки OCR-fallback в Vision Agent (секция <c>VisionAgent:Ocr</c>
    /// в appsettings.json).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-137). См.
    /// <c>docs/development/v1.13/DESIGN_VISION_OCR.md</c> § 4.6.
    /// </para>
    /// <para>
    /// <b>Не путать с <c>Rag:Ingestion:Ocr</c></b> (KI-203): там
    /// <c>RenderDpi</c> и <c>MinTextCharsPerPage</c> относятся к PDF-страницам.
    /// Здесь — семантика другая: OCR скриншотов в Vision Agent loop'е.
    /// </para>
    /// </remarks>
    public class VisionOcrOptions
    {
        /// <summary>
        /// Включён ли OCR-fallback в Vision Agent.
        /// Default в appsettings: <c>true</c> в dev, <c>false</c> в prod.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Путь к tessdata (относительный — от ContentRoot, резолвится
        /// через <c>IAppPathProvider</c>). Содержит <c>rus.traineddata</c>
        /// и <c>eng.traineddata</c>.
        /// </summary>
        public string TessDataPath { get; set; } = "tools/tessdata";

        /// <summary>
        /// Языки распознавания (Tesseract-формат: <c>rus+eng</c>).
        /// </summary>
        public string Languages { get; set; } = "rus+eng";

        /// <summary>
        /// Trigger A — проактивный. Запускать OCR, если VL вернул пустой
        /// <c>ui_elements</c> или хотя бы один label пустой / короче
        /// <see cref="ShortLabelThreshold"/>. Default: <c>true</c>.
        /// </summary>
        public bool TriggerA { get; set; } = true;

        /// <summary>
        /// Trigger B — реактивный. Запускать OCR, если Planner LLM вернул
        /// <c>action="fail"</c> (один раз за задачу). Реализован через флаг
        /// <c>forceOcrOnNextDescribe</c> — OCR делается на следующей итерации
        /// loop'а, не в том же шаге. Default: <c>true</c>.
        /// </summary>
        public bool TriggerB { get; set; } = true;

        /// <summary>
        /// Порог «короткого label», символов. Если VL вернул label короче —
        /// срабатывает Trigger A. Default: <c>3</c>.
        /// </summary>
        public int ShortLabelThreshold { get; set; } = 3;

        /// <summary>
        /// Максимальное расстояние (px в screenshot-space) между центром
        /// OCR-слова и центром VL-элемента для merge. Больше — слово
        /// не привязывается к элементу (но остаётся в <c>OcrText</c>).
        /// Default: <c>30</c>.
        /// </summary>
        public int MergeMaxDistancePx { get; set; } = 30;
    }
}