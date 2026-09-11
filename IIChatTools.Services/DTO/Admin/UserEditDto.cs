namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Данные для создания или редактирования пользователя.
    /// </summary>
    public class UserEditDto
    {
        /// <summary>
        /// Идентификатор (0 — создание).
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
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Роль: Admin или User.
        /// </summary>
        public string Role { get; set; } = "User";

        /// <summary>
        /// Новый пароль (только при создании или сбросе).
        /// </summary>
        public string Password { get; set; }
    }
}