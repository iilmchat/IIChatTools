using System;

namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// DTO одного векторного индекса для админки
    /// (v1.5.0, KI-083, Шаг 7A).
    /// </summary>
    public class RagIndexDto
    {
        /// <summary>
        /// Имя индекса (<c>project_docs</c>, <c>my_rag_docs</c>,
        /// <c>chat_history</c>, <c>workspace</c>).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Человекочитаемое описание (для UI).
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Количество чанков в индексе.
        /// </summary>
        public int ChunkCount { get; set; }

        /// <summary>
        /// Количество уникальных документов (по <c>DocumentPath</c>).
        /// </summary>
        public int DocumentCount { get; set; }

        /// <summary>
        /// Дата последней индексации (UTC). <c>null</c>, если чанков нет.
        /// </summary>
        public DateTime? LastIndexedAt { get; set; }
    }
}