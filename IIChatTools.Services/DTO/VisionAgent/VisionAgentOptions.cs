using System.Collections.Generic;

namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Корневая конфигурация Vision Agent (секция <c>VisionAgent</c> в appsettings.json).
    /// Связывает: backend (3 варианта), 2 LLM (Vision + Planner), лимиты,
    /// whitelist доменов и процессов, валидацию действий, privacy.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См.
    /// <c>docs/development/v1.12/DESIGN_VISION_AGENT.md</c> § 5.1.
    /// </remarks>
    public class VisionAgentOptions
    {
        /// <summary>Включён ли Vision Agent (default: false).</summary>
        public bool Enabled { get; set; }

        /// <summary>Показывать ли вкладку «Vision Agent» в /admin (default: true).</summary>
        public bool AdminUiEnabled { get; set; } = true;

        /// <summary>Конфигурация backend'а (Local / Sandbox / RemoteVnc).</summary>
        public VisionBackendOptions Backend { get; set; } = new VisionBackendOptions();

        /// <summary>Конфигурация Vision LLM (описание UI со скриншота).</summary>
        public VisionLlmOptions VisionLlm { get; set; } = new VisionLlmOptions();

        /// <summary>Конфигурация Planner LLM (планирование действий).</summary>
        public PlannerLlmOptions PlannerLlm { get; set; } = new PlannerLlmOptions();

        /// <summary>Лимиты шагов, времени, размера скриншота, rate limiting.</summary>
        public VisionLimitsOptions Limits { get; set; } = new VisionLimitsOptions();

        /// <summary>Whitelist / blacklist доменов.</summary>
        public VisionWhitelistOptions Whitelist { get; set; } = new VisionWhitelistOptions();

        /// <summary>Валидация действий (запрет hotkey'ев, лимиты длины).</summary>
        public VisionActionValidationOptions ActionValidation { get; set; } = new VisionActionValidationOptions();

        /// <summary>Privacy: сохранение скриншотов, маскирование URL-bar.</summary>
        public VisionPrivacyOptions Privacy { get; set; } = new VisionPrivacyOptions();

        /// <summary>
        /// Coordinate-then-Verify (KI-162): уточнение координат через
        /// второй VL-вызов на кропе. См. <see cref="VisionVerifyOptions"/>.
        /// </summary>
        public VisionVerifyOptions Verify { get; set; } = new VisionVerifyOptions();

        /// <summary>
        /// Провайдер координат (KI-161): DOM через CDP или VL-fallback.
        /// См. <see cref="VisionCoordinateProviderOptions"/>.
        /// </summary>
        /// <remarks>
        /// v1.13.x (KI-161). Секция <c>VisionAgent:CoordinateProvider</c>
        /// в appsettings.
        /// </remarks>
        public VisionCoordinateProviderOptions CoordinateProvider { get; set; }
            = new VisionCoordinateProviderOptions();

        /// <summary>
        /// Конфигурация OCR-fallback (KI-137): full-res PNG для OCR,
        /// merge OCR-слов в <c>ui_elements</c>, триггеры A/B.
        /// </summary>
        public VisionOcrOptions Ocr { get; set; } = new VisionOcrOptions();
    }

    /// <summary>
    /// Лимиты выполнения задач Vision Agent.
    /// </summary>
    public class VisionLimitsOptions
    {
        /// <summary>Максимальное число шагов в одной задаче (default: 30).</summary>
        public int MaxSteps { get; set; } = 30;

        /// <summary>Максимальное время выполнения задачи, сек (default: 300).</summary>
        public int MaxTaskSeconds { get; set; } = 300;

        /// <summary>Максимальный размер скриншота, байт (default: 2 MB).</summary>
        public long MaxScreenshotBytes { get; set; } = 2_097_152;

        /// <summary>Rate limit: задач на пользователя за 5 минут (default: 5).</summary>
        public int MaxTasksPerUserPer5Min { get; set; } = 5;

        /// <summary>Пауза после каждого действия, мс (default: 500).</summary>
        public int ActionDelayMs { get; set; } = 500;

        /// <summary>Ожидание стабилизации страницы после действия, мс (default: 500).</summary>
        public int PageStabilityCheckMs { get; set; } = 500;

        /// <summary>
        /// Пауза перед retry при пустом <c>ui_elements</c> на первом кадре
        /// (KI-192), мс. Default: 2000 (2 сек).
        /// <para>
        /// Вынесено в отдельную опцию, чтобы unit-тесты могли выставить
        /// минимальное значение (иначе 3 retry × 2000 мс = 6 сек на каждый
        /// тест с пустым <c>DefaultResponse</c>).
        /// </para>
        /// </summary>
        public int EmptyUiElementsRetryDelayMs { get; set; } = 2000;
    }

    /// <summary>
    /// Whitelist / blacklist доменов для browser-режима.
    /// </summary>
    public class VisionWhitelistOptions
    {
        /// <summary>Разрешённые домены (wildcards допустимы: <c>*.rzd.ru</c>).</summary>
        public List<string> Domains { get; set; } = new List<string>();

        /// <summary>Запрещённые домены. Приоритет выше <see cref="Domains"/>.</summary>
        public List<string> DeniedDomains { get; set; } = new List<string>();

        /// <summary>Разрешить любой домен (default: false — только из <see cref="Domains"/>).</summary>
        public bool AllowAnyDomain { get; set; }
    }

    /// <summary>
    /// Валидация действий Vision Agent (защита от опасных hotkey'ев).
    /// </summary>
    public class VisionActionValidationOptions
    {
        /// <summary>Запрещённые одиночные клавиши (F12, Alt+F4, ...).</summary>
        public List<string> BlockedKeys { get; set; } = new List<string>();

        /// <summary>Запрещённые комбинации клавиш (Ctrl+Alt+Delete, Alt+Tab, ...).</summary>
        public List<List<string>> BlockedHotkeys { get; set; } = new List<List<string>>();

        /// <summary>Максимальная длина текста для ввода (default: 2000).</summary>
        public int MaxTextLength { get; set; } = 2000;

        /// <summary>Максимальный delta прокрутки (default: 2000).</summary>
        public int MaxScrollDelta { get; set; } = 2000;
    }

    /// <summary>
    /// Privacy-настройки Vision Agent (сохранение скриншотов, маскирование).
    /// </summary>
    public class VisionPrivacyOptions
    {
        /// <summary>
        /// Сохранять ли скриншоты в <c>ChatMessage.MetadataJson</c> (default: false).
        /// При <c>true</c> — утечка PII через историю чата.
        /// </summary>
        public bool PersistScreenshots { get; set; }

        /// <summary>
        /// Сохранять ли скриншоты в workspace на время задачи
        /// (<c>workspace/screenshots/{taskId}/</c>). Default: true.
        /// </summary>
        public bool SaveToWorkspace { get; set; } = true;

        /// <summary>TTL скриншотов в workspace, часы (default: 1).</summary>
        public int WorkspaceRetentionHours { get; set; } = 1;

        /// <summary>
        /// Обрезать ли top-40px в browser-режиме (URL-bar с токенами). Default: true.
        /// </summary>
        public bool MaskUrlBar { get; set; } = true;
    }
}