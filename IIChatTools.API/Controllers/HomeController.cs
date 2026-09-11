using System;
using System.Diagnostics;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Контроллер Razor-страниц административного интерфейса.
    /// </summary>
    public class HomeController : Controller
    {
        private readonly IStatusService _statusService;
        private readonly ILogger<HomeController> _logger;
        private readonly IStringLocalizer<HomeController> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="statusService">Сервис статистики</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        public HomeController(
            IStatusService statusService,
            ILogger<HomeController> logger,
            IStringLocalizer<HomeController> localizer)
        {
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Главная страница админки.
        /// </summary>
        /// <returns>Razor-представление</returns>
        [HttpGet("/")]
        [HttpGet("/home")]
        public IActionResult Index()
        {
            ViewData["Title"] = _localizer["Главная"];
            return View();
        }

        /// <summary>
        /// Страница статуса системы и статистики.
        /// </summary>
        /// <returns>Razor-представление</returns>
        [HttpGet("/status")]
        public IActionResult Status()
        {
            ViewData["Title"] = _localizer["Статус"];
            return View();
        }

        /// <summary>
        /// Страница администрирования (только для роли Admin).
        /// </summary>
        /// <returns>Razor-представление</returns>
        [Authorize(Policy = "AdminOnly")]
        [HttpGet("/admin")]
        public IActionResult Admin()
        {
            ViewData["Title"] = _localizer["Админка"];
            return View();
        }

        /// <summary>
        /// Страница тестирования инструментов (только для Admin).
        /// </summary>
        /// <returns>Razor-представление</returns>
        [Authorize(Policy = "AdminOnly")]
        [HttpGet("/test")]
        public IActionResult Test()
        {
            ViewData["Title"] = _localizer["Тест"];
            return View();
        }

        /// <summary>
        /// Страница ошибки.
        /// </summary>
        /// <returns>Razor-представление</returns>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [HttpGet("/error")]
        public IActionResult Error()
        {
            ViewData["Title"] = _localizer["Ошибка"];
            ViewData["RequestId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            return View();
        }
    }
}