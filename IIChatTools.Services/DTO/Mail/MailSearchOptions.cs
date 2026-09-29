namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Настройки поиска писем.
    /// v1.8.0 (KI-107).
    /// </summary>
    public class MailSearchOptions
    {
        /// <summary>Лимит результатов по умолчанию.</summary>
        public int DefaultLimit { get; set; } = 20;

        /// <summary>Максимальный лимит результатов.</summary>
        public int MaxLimit { get; set; } = 100;
    }
}