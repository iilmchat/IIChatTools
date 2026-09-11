using System;
using Microsoft.EntityFrameworkCore;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Элемент долговременной памяти пользователя.
    /// </summary>
    public class MemoryEntry : BaseEntity
    {
        /// <summary>
        /// Идентификатор пользователя.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Навигационное свойство пользователя.
        /// </summary>
        public virtual ApplicationUser User { get; set; }

        /// <summary>
        /// Ключ памяти.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Значение (JSON или строка).
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Тип данных (string, json и т.д.).
        /// </summary>
        public string Type { get; set; }

        // Примечание: свойства CreatedAt и UpdatedAt наследуются от BaseEntity.
    }
}