using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Базовый класс для всех сущностей с общими полями
    /// </summary>
    public abstract class BaseEntity
    {
        /// <summary>
        /// Уникальный идентификатор
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Дата создания записи
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Дата последнего обновления
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}