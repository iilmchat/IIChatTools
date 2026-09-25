using System;

namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// DTO чанка для пагинированного просмотра в админке
    /// (v1.5.0, KI-083, Шаг 7A).
    /// </summary>
    public class RagChunkDto
    {
        /// <summary>ID чанка (<c>DocumentChunk.Id</c>).</summary>
        public int Id { get; set; }

        /// <summary>Имя индекса.</summary>
        public string IndexName { get; set; }

        /// <summary>Путь документа (или URL).</summary>
        public string DocumentPath { get; set; }

        /// <summary>ID чата (для <c>my_rag_docs</c> / <c>chat_history</c>), иначе null.</summary>
        public int? ChatId { get; set; }

        /// <summary>ID пользователя-владельца (0 — глобальный чанк).</summary>
        public int UserId { get; set; }

        /// <summary>Позиция чанка в документе (0-based).</summary>
        public int ChunkIndex { get; set; }

        /// <summary>Количество токенов.</summary>
        public int Tokens { get; set; }

        /// <summary>Превью текста (первые ~120 символов).</summary>
        public string TextPreview { get; set; }

        /// <summary>Дата создания (UTC).</summary>
        public DateTime CreatedAt { get; set; }
    }
}