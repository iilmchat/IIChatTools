namespace IIChatTools.API.RateLimiting
{
    /// <summary>
    /// Конфигурация системы rate-limiting.
    /// Читается из секции <c>RateLimiting</c> в appsettings.json.
    /// </summary>
    public class RateLimitingOptions
    {
        /// <summary>Глобальный переключатель. При false лимиты не применяются.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Политика для анонимных запросов (fallback по IP).</summary>
        public RateLimitPolicyOptions Global { get; set; } = new RateLimitPolicyOptions { PermitLimit = 300, WindowSeconds = 60, QueueLimit = 0 };

        /// <summary>Политика для авторизованных пользователей (по UserId).</summary>
        public RateLimitPolicyOptions PerUser { get; set; } = new RateLimitPolicyOptions { PermitLimit = 100, WindowSeconds = 60, QueueLimit = 5 };

        /// <summary>Политика для тяжёлых операций /api/tools/execute.</summary>
        public RateLimitPolicyOptions ToolsExecute { get; set; } = new RateLimitPolicyOptions { PermitLimit = 30, WindowSeconds = 60, QueueLimit = 0 };

        /// <summary>Политика для /auth/* (защита от brute-force).</summary>
        public RateLimitPolicyOptions Auth { get; set; } = new RateLimitPolicyOptions { PermitLimit = 5, WindowSeconds = 60, QueueLimit = 0 };

        /// <summary>
        /// Политика для page-viewer (KI-212):
        /// <c>/api/chat/*/attachments/*/pages/*</c> — высокочастотные запросы
        /// (thumbnails PNG, OCR JSON, экспорт ZIP/PDF). Отдельная политика,
        /// чтобы не упираться в общий per-user лимит (100/min) при открытии
        /// 100+ страничного PDF.
        /// </summary>
        public RateLimitPolicyOptions PagesViewer { get; set; } = new RateLimitPolicyOptions { PermitLimit = 300, WindowSeconds = 60, QueueLimit = 0 };
    }

    /// <summary>Параметры одной политики rate-limiting.</summary>
    public class RateLimitPolicyOptions
    {
        public int PermitLimit { get; set; }
        public int WindowSeconds { get; set; }
        public int QueueLimit { get; set; }
    }
}