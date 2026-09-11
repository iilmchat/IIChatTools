using System;

namespace IIChatTools.Services.DTO.Browser
{
    /// <summary>
    /// Публичная информация о сессии браузера (для возврата LLM).
    /// </summary>
    public class BrowserSessionInfo
    {
        /// <summary>
        /// Уникальный идентификатор сессии (GUID).
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Идентификатор пользователя, открывшего сессию.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Текущий URL (если установлен).
        /// </summary>
        public string CurrentUrl { get; set; }

        /// <summary>
        /// Заголовок текущей страницы.
        /// </summary>
        public string CurrentTitle { get; set; }

        /// <summary>
        /// Время создания сессии (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Время последней активности (UTC).
        /// </summary>
        public DateTime LastActivityAt { get; set; }
    }
}