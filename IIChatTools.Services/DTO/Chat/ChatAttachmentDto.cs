using System;

namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// DTO вложения чата (v1.5.0, KI-083, Шаг 6A).
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
    }
}