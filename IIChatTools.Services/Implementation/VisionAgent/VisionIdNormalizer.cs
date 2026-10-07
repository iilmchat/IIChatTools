using System;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// KI-193: нормализация семантических id UI-элементов.
    /// <para>
    /// VL-модель (Qwen2.5-VL-7B) нестабильна в именовании — одна и та же
    /// кнопка на соседних кадрах может быть <c>search_btn</c> и
    /// <c>search_button</c>. Planner сгенерирует разные <c>click</c>-actions
    /// с этими target'ами, а KI-187 (детектор цикла) их не поймает.
    /// </para>
    /// <para>
    /// Нормализация приводит типовые сокращения к единому виду. Применяется
    /// в <see cref="ScreenDescriptionParser.ParseUiElement"/> (нормализует id
    /// в <c>ui_elements</c>) и в <c>VisionAgentService.DetectPlannerCycle</c>
    /// (сравнивает нормализованные target'ы).
    /// </para>
    /// </summary>
    public static class VisionIdNormalizer
    {
        /// <summary>
        /// Нормализует id, приводя типовые сокращения к единой форме:
        /// <list type="bullet">
        ///   <item><c>search_btn</c> → <c>search_button</c>;</item>
        ///   <item><c>login_lnk</c> → <c>login_link</c>;</item>
        ///   <item><c>email_field</c> → <c>email_input</c>;</item>
        ///   <item><c>remember_chk</c> → <c>remember_checkbox</c>;</item>
        /// </list>
        /// Суффиксы заменяются только в конце id (самый частый случай).
        /// Регистр префикса сохраняется.
        /// </summary>
        /// <param name="id">Исходный id (может быть null/пустой).</param>
        /// <returns>Нормализованный id, либо исходный, если суффикс не подошёл.</returns>
        public static string Normalize(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;

            // Пары (сокращённый суффикс, полная форма). Порядок — от длинного
            // к короткому (иначе "_btn" поглотит "_btn_"). Все — с ведущим "_".
            var suffixes = new (string Short, string Long)[]
            {
                ("_button", "_button"),
                ("_butt",   "_button"),
                ("_btn",    "_button"),
                ("_link",   "_link"),
                ("_lnk",    "_link"),
                ("_input",  "_input"),
                ("_field",  "_input"),
                ("_inp",    "_input"),
                ("_tb",     "_input"),
                ("_text",   "_text"),
                ("_txt",    "_text"),
                ("_checkbox", "_checkbox"),
                ("_chk",    "_checkbox"),
                ("_cb",     "_checkbox"),
                ("_radio",  "_radio"),
                ("_dropdown", "_dropdown"),
                ("_dd",     "_dropdown"),
                ("_sel",    "_dropdown"),
                ("_option", "_option"),
                ("_opt",    "_option"),
            };

            foreach (var (shortForm, longForm) in suffixes)
            {
                if (id.EndsWith(shortForm, StringComparison.OrdinalIgnoreCase))
                {
                    if (shortForm == longForm) return id;
                    return id.Substring(0, id.Length - shortForm.Length) + longForm;
                }
            }

            return id;
        }
    }
}