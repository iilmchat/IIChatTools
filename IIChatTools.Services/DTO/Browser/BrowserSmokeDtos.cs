using System;
using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Browser
{
    /// <summary>
    /// Один шаг декларативного smoke-сценария браузера
    /// (команда <c>browser_session_control:run_smoke</c>, v2.0.3, KI-256).
    /// </summary>
    public sealed class BrowserSmokeStep
    {
        /// <summary>Номер шага (1-based) — для сообщений об ошибках.</summary>
        public int Number { get; set; }

        /// <summary>Действие: goto, click, click_by_text, type, press_key, wait_for_selector, assert_text, assert_selector, screenshot, wait.</summary>
        public string Action { get; set; }

        /// <summary>URL (для <c>goto</c>).</summary>
        public string Url { get; set; }

        /// <summary>CSS-селектор (для click / type / wait_for_selector / assert_text / assert_selector).</summary>
        public string Selector { get; set; }

        /// <summary>Текст (для click_by_text / type).</summary>
        public string Text { get; set; }

        /// <summary>Клавиша (для press_key), например <c>Enter</c>.</summary>
        public string Key { get; set; }

        /// <summary>Относительный путь в workspace (для screenshot).</summary>
        public string Path { get; set; }

        /// <summary>Подстрока, которую должен содержать текст элемента (для assert_text).</summary>
        public string Contains { get; set; }

        /// <summary>Таймаут шага в миллисекундах; 0 — использовать таймаут по умолчанию.</summary>
        public int TimeoutMs { get; set; }

        /// <summary>Пауза в миллисекундах (для wait).</summary>
        public int Ms { get; set; }
    }

    /// <summary>
    /// Разобранный и провалидированный smoke-сценарий.
    /// </summary>
    public sealed class BrowserSmokePlan
    {
        /// <summary>
        /// Создаёт план.
        /// </summary>
        /// <param name="steps">Шаги сценария (не пустой список).</param>
        public BrowserSmokePlan(IReadOnlyList<BrowserSmokeStep> steps)
        {
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        }

        /// <summary>Шаги сценария.</summary>
        public IReadOnlyList<BrowserSmokeStep> Steps { get; }
    }

    /// <summary>
    /// Результат выполнения smoke-сценария.
    /// </summary>
    public sealed class BrowserSmokeResult
    {
        /// <summary>Сценарий пройден полностью.</summary>
        public bool Passed { get; set; }

        /// <summary>Номер упавшего шага (1-based) или <c>null</c>.</summary>
        public int? FailedStep { get; set; }

        /// <summary>Длительность выполнения, мс.</summary>
        public long DurationMs { get; set; }

        /// <summary>Сохранённые скриншоты (относительные пути workspace).</summary>
        public IReadOnlyList<string> Screenshots { get; set; } = Array.Empty<string>();

        /// <summary>Текст ошибки или <c>null</c>.</summary>
        public string Error { get; set; }

        /// <summary>Всего шагов в сценарии.</summary>
        public int TotalSteps { get; set; }
    }
}
