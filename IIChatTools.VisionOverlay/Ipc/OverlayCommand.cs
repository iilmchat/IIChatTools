using System.Text.Json.Serialization;

namespace IIChatTools.VisionOverlay.Ipc
{
    /// <summary>
    /// Базовая команда IPC overlay. Полиморфизм — через поле <see cref="Type"/>:
    /// <c>progress</c> / <c>final_status</c> / <c>close</c> / <c>stop</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.7). См. DESIGN § 4.6.
    /// </para>
    /// <para>
    /// <b>Направления:</b>
    /// <list type="bullet">
    ///   <item><description>API → overlay: <c>progress</c>, <c>final_status</c>, <c>close</c>.</description></item>
    ///   <item><description>overlay → API: <c>stop</c> (пользователь нажал STOP / ESC).</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    internal sealed class OverlayCommand
    {
        /// <summary>
        /// Тип команды: <c>progress</c> / <c>final_status</c> / <c>close</c> / <c>stop</c>.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>Номер шага (1-based) — только для <c>progress</c>.</summary>
        [JsonPropertyName("stepIndex")]
        public int? StepIndex { get; set; }

        /// <summary>Всего шагов — только для <c>progress</c>.</summary>
        [JsonPropertyName("maxSteps")]
        public int? MaxSteps { get; set; }

        /// <summary>Описание действия — только для <c>progress</c>.</summary>
        [JsonPropertyName("action")]
        public string Action { get; set; }

        /// <summary>Финальный текст («Готово» / «Ошибка») — только для <c>final_status</c>.</summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        /// <summary>Успех / неуспех — только для <c>final_status</c>.</summary>
        [JsonPropertyName("success")]
        public bool? Success { get; set; }
    }
}