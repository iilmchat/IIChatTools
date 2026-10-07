using System;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// DTO вложения чата (v1.5.0, KI-083, Шаг 6A; OCR-прогресс — v1.13.x, KI-204).
    /// </summary>
    public class ChatAttachmentDto
    {
        /// <summary>Идентификатор вложения.</summary>
        public int Id { get; set; }

        /// <summary>Идентификатор чата.</summary>
        public int ChatId { get; set; }

        /// <summary>Идентификатор пользователя-владельца.</summary>
        public int UserId { get; set; }

        /// <summary>Имя файла (для UI).</summary>
        public string FileName { get; set; }

        /// <summary>MIME-тип.</summary>
        public string ContentType { get; set; }

        /// <summary>Размер в байтах.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Количество проиндексированных чанков.</summary>
        public int ChunksCount { get; set; }

        /// <summary>Дата загрузки (UTC).</summary>
        public DateTime UploadedAt { get; set; }

        /// <summary>
        /// v1.13.x (KI-204): прогресс OCR-обработки скана PDF.
        /// <c>null</c> — OCR не применялся (обычный PDF с текстовым слоем)
        /// или обработка уже завершена и запись TTL-очищена.
        /// <para>
        /// Заполняется <c>ChatAttachmentsController.GetListAsync</c> из
        /// <c>IOcrProgressTracker</c>. Клиент polling'ом раз в 500 мс
        /// получает обновления во время upload'а.
        /// </para>
        /// </summary>
        public OcrProgressDto OcrProgress { get; set; }
    }
}