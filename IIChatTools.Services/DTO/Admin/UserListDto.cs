using System;
using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Краткая информация о пользователе для списка.
    /// </summary>
    public class UserListDto
    {
        /// <summary>
        /// Идентификатор пользователя.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Электронная почта.
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Полное имя.
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// Признак активности.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Дата регистрации.
        /// </summary>
        public DateTime RegisteredAt { get; set; }

        /// <summary>
        /// Список ролей пользователя.
        /// </summary>
        public IReadOnlyList<string> Roles { get; set; }
    }
}