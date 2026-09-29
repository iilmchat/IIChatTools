using System;

namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Запрос поиска писем (для search_emails).
    /// v1.8.0 (KI-107).
    /// </summary>
    public class SearchMailRequest
    {
        /// <summary>Папка поиска (INBOX по умолчанию).</summary>
        public string Mailbox { get; set; } = "INBOX";

        /// <summary>Фильтр по отправителю (substring, case-insensitive).</summary>
        public string From { get; set; }

        /// <summary>Фильтр по теме (substring, case-insensitive).</summary>
        public string Subject { get; set; }

        /// <summary>Письма после даты (включительно, UTC).</summary>
        public DateTime? Since { get; set; }

        /// <summary>Письма до даты (включительно, UTC).</summary>
        public DateTime? Before { get; set; }

        /// <summary>Только непрочитанные.</summary>
        public bool UnseenOnly { get; set; }

        /// <summary>Лимит результатов (0 = DefaultLimit).</summary>
        public int Limit { get; set; }
    }
}