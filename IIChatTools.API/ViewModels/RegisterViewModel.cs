using System.ComponentModel.DataAnnotations;

namespace IIChatTools.API.ViewModels
{
    /// <summary>
    /// Модель представления для регистрации пользователя.
    /// </summary>
    public class RegisterViewModel
    {
        /// <summary>
        /// Электронная почта пользователя (используется как логин).
        /// </summary>
        [Required(ErrorMessage = "Обязательное поле")]
        [EmailAddress(ErrorMessage = "Некорректный адрес электронной почты")]
        [Display(Name = "Электронная почта")]
        public string Email { get; set; }

        /// <summary>
        /// Полное имя пользователя.
        /// </summary>
        [Required(ErrorMessage = "Обязательное поле")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Длина от 2 до 200 символов")]
        [Display(Name = "Полное имя")]
        public string FullName { get; set; }

        /// <summary>
        /// Пароль.
        /// </summary>
        [Required(ErrorMessage = "Обязательное поле")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Длина пароля от 6 до 100 символов")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; }

        /// <summary>
        /// Подтверждение пароля.
        /// </summary>
        [DataType(DataType.Password)]
        [Display(Name = "Подтверждение пароля")]
        [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают.")]
        public string ConfirmPassword { get; set; }
    }
}