using System.ComponentModel.DataAnnotations;

namespace IIChatTools.API.ViewModels
{
    /// <summary>
    /// Модель представления для входа в систему.
    /// </summary>
    public class LoginViewModel
    {
        /// <summary>
        /// Электронная почта (логин).
        /// </summary>
        [Required(ErrorMessage = "Обязательное поле")]
        [EmailAddress(ErrorMessage = "Некорректный адрес электронной почты")]
        [Display(Name = "Электронная почта")]
        public string Email { get; set; }

        /// <summary>
        /// Пароль.
        /// </summary>
        [Required(ErrorMessage = "Обязательное поле")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; }

        /// <summary>
        /// Флаг «запомнить меня».
        /// </summary>
        [Display(Name = "Запомнить меня")]
        public bool RememberMe { get; set; }
    }
}