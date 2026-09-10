using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Элемент долговременной памяти пользователя
    /// </summary>
    public class MemoryEntry : BaseEntity
    {
        /// <summary>
        /// Идентификатор пользователя
        /// </summary>
        public int UserId { get; set; }

        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Ключ памяти
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Значение (JSON)
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Тип данных (строка, число, объект и т.д.)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Дата создания
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Дата последнего обновления
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}