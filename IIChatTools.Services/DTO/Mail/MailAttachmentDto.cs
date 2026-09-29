namespace IIChatTools.Services.DTO.Mail
{
    /// <summary>
    /// Вложение письма.
    /// v1.8.0 (KI-107).
    /// </summary>
    public class MailAttachmentDto
    {
        /// <summary>Оригинальное имя файла (из заголовка Content-Disposition).</summary>
        public string FileName { get; set; }

        /// <summary>MIME-тип (application/pdf, image/png, ...).</summary>
        public string ContentType { get; set; }

        /// <summary>Размер в байтах.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Относительный путь в workspace (mail-attachments/{uid}/{guid}.ext).</summary>
        public string StoragePath { get; set; }
    }
}