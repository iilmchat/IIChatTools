using System.Collections.Generic;

namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Конфигурация backend'а Vision Agent — абстракции «поверхности»,
    /// на которой выполняется задача (LocalHarness / Sandbox / RemoteVnc).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 3.1, § 5.1.
    /// </remarks>
    public class VisionBackendOptions
    {
        /// <summary>
        /// Режим: <c>local-harness</c> | <c>sandbox</c> | <c>remote-vnc</c>.
        /// Default: <c>local-harness</c>.
        /// </summary>
        public string Mode { get; set; } = "local-harness";

        /// <summary>Конфигурация LocalHarnessVisionBackend.</summary>
        public LocalHarnessBackendOptions Local { get; set; } = new LocalHarnessBackendOptions();

        /// <summary>Конфигурация SandboxVisionBackend.</summary>
        public SandboxBackendOptions Sandbox { get; set; } = new SandboxBackendOptions();

        /// <summary>Конфигурация VncMcpVisionBackend.</summary>
        public RemoteVncBackendOptions RemoteVnc { get; set; } = new RemoteVncBackendOptions();
    }

    /// <summary>
    /// Опции локального backend'а (SystemHarness.Windows на текущей машине).
    /// </summary>
    public class LocalHarnessBackendOptions
    {
        /// <summary>Способ захвата экрана: <c>gdi</c> | <c>windows-graphics-capture</c>.</summary>
        public string CaptureMode { get; set; } = "gdi";

        /// <summary>
        /// Разрешить управление не-browser процессами (Outlook, Excel).
        /// Default: <c>false</c> (только Chrome/Edge/Firefox).
        /// </summary>
        public bool AllowNonBrowserProcesses { get; set; }

        /// <summary>Whitelist процессов (по имени exe, без расширения).</summary>
        public List<string> AllowedProcesses { get; set; } = new List<string>
        {
            "chrome", "msedge", "firefox"
        };

        /// <summary>
        /// Использовать свежий профиль Chrome (<c>--user-data-dir</c> в TEMP).
        /// Без сохранённых паролей, cookies, истории. Default: <c>true</c>.
        /// </summary>
        public bool ChromeFreshProfile { get; set; } = true;

        /// <summary>
        /// Показывать on-screen indicator (<c>VisionOverlay.exe</c>) во время задачи.
        /// Обязательно <c>true</c> для local-harness (DESIGN § 6.4).
        /// </summary>
        public bool ShowOverlay { get; set; } = true;
    }

    /// <summary>
    /// Опции Sandbox-backend'а (Windows Sandbox + TightVNC).
    /// </summary>
    public class SandboxBackendOptions
    {
        /// <summary>Путь к <c>.wsb</c>-конфигу Windows Sandbox.</summary>
        public string ConfigPath { get; set; } = "VisionAgent/sandbox.wsb";

        /// <summary>Таймаут запуска Sandbox, сек (default: 60).</summary>
        public int StartupTimeoutSeconds { get; set; } = 60;

        /// <summary>Порт VNC внутри Sandbox (default: 5901).</summary>
        public int VncPort { get; set; } = 5901;

        /// <summary>
        /// Пароль VNC. Только User Secrets / env, не в appsettings.json.
        /// </summary>
        public string VncPassword { get; set; } = "CHANGE_ME_VIA_USER_SECRETS";

        /// <summary>Автоматически закрывать Sandbox после завершения задачи.</summary>
        public bool AutoShutdownAfterTask { get; set; } = true;

        /// <summary>Shared folder для обмена файлами (внутри Sandbox → C:\Vision).</summary>
        public string MappingFolder { get; set; } = "%USERPROFILE%\\IIChatToolsVision";
    }

    /// <summary>
    /// Опции удалённого backend'а (MCP-клиент для vnc-mcp-server).
    /// </summary>
    public class RemoteVncBackendOptions
    {
        /// <summary>HTTP endpoint MCP-сервера (default: http://127.0.0.1:8765).</summary>
        public string McpEndpoint { get; set; } = "http://127.0.0.1:8765";

        /// <summary>API-ключ MCP. Только User Secrets.</summary>
        public string McpApiKey { get; set; } = "CHANGE_ME_VIA_USER_SECRETS";

        /// <summary>Хост удалённой машины с VNC-сервером.</summary>
        public string VncHost { get; set; } = "192.168.1.50";

        /// <summary>Порт VNC (default: 5900).</summary>
        public int VncPort { get; set; } = 5900;

        /// <summary>Пароль VNC. Только User Secrets.</summary>
        public string VncPassword { get; set; } = "CHANGE_ME_VIA_USER_SECRETS";

        /// <summary>Таймаут подключения, сек (default: 15).</summary>
        public int ConnectionTimeoutSeconds { get; set; } = 15;
    }
}