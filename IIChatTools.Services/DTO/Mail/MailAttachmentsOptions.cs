namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Настройки работы с вложениями Mail Agent.
    /// v1.8.0 (KI-107).
    /// </summary>
    public class MailAttachmentsOptions
    {
        /// <summary>Максимальный размер одного вложения (10 MB по умолчанию).</summary>
        public long MaxFileSizeBytes { get; set; } = 10_485_760;

        /// <summary>Максимальный суммарный размер вложений в одном письме.</summary>
        public long MaxTotalSizeBytes { get; set; } = 10_485_760;

        /// <summary>Максимум вложений в одном письме.</summary>
        public int MaxFilesPerMessage { get; set; } = 5;

        /// <summary>Подпапка в workspace для входящих вложений.</summary>
        public string StorageSubfolder { get; set; } = "mail-attachments";

        /// <summary>Максимум байт в час на пользователя (защита от утечки).</summary>
        public long MaxHourlyBytesPerUser { get; set; } = 52_428_800;
    }
}